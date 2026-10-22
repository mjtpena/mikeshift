namespace Shiftvert.Core.Actions;

/// <summary>
/// The per-file batch loop shared by every non-batch action (PLAN §1.1 Batch):
/// sequential; one failure does not stop the rest; files already in the target format are skipped and counted
/// separately; cancellation keeps earlier outputs; the Downloads fallback is detected per output.
/// </summary>
public static class Each
{
    /// <summary>Converts one input and returns the path of what it produced (file, or folder for pages/split).</summary>
    public delegate Task<string> FileStep(string input, CancellationToken ct);

    /// <summary>
    /// Runs <paramref name="convert"/> over <paramref name="paths"/> in order.
    /// </summary>
    /// <param name="paths">Selected inputs.</param>
    /// <param name="ctx">Used for progress reporting only.</param>
    /// <param name="convert">Converter for a single input. Throw <see cref="ConversionException"/> for expected failures,
    /// <see cref="OperationCanceledException"/> to stop the batch (user cancelled a dialog).</param>
    /// <param name="ct">Cancellation: stops before the next file (and flows into <paramref name="convert"/>).</param>
    /// <param name="skipExtensions">Lower-case extensions (no dot) that are already in the target format; matching inputs are
    /// skipped and counted in <see cref="ActionResult.AlreadyInFormat"/>.</param>
    /// <param name="alreadyLabel">Label for the skip note, e.g. "PNG" gives "· 2 already PNG".</param>
    public static async Task<ActionResult> RunAsync(
        IReadOnlyList<string> paths,
        ActionContext ctx,
        FileStep convert,
        CancellationToken ct,
        IReadOnlySet<string>? skipExtensions = null,
        string? alreadyLabel = null)
    {
        var outputs = new List<ActionOutput>();
        var failures = new List<ActionFailure>();
        var already = 0;
        var cancelled = false;

        foreach (var input in paths)
        {
            if (ct.IsCancellationRequested) { cancelled = true; break; }

            if (skipExtensions is { Count: > 0 } && skipExtensions.Contains(Kind.ExtensionOf(input)))
            {
                already++;
                continue;
            }

            ctx.Progress?.Report(Path.GetFileName(input));
            try
            {
                var output = await convert(input, ct).ConfigureAwait(false);
                outputs.Add(new ActionOutput(input, output, UsedFallback(input, output)));
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
            catch (ConversionException ex)
            {
                failures.Add(new ActionFailure(input, ex.Message));
            }
            catch (Exception ex)
            {
                failures.Add(new ActionFailure(input, DescribeUnexpected(ex)));
            }
        }

        return new ActionResult
        {
            Total = paths.Count,
            Outputs = outputs,
            Failures = failures,
            AlreadyInFormat = already,
            AlreadyLabel = alreadyLabel,
            Cancelled = cancelled,
        };
    }

    /// <summary>
    /// Converters return only a path, so the Downloads fallback is inferred: the output's parent directory differs from
    /// the input's parent directory. (Outputs always sit beside their input unless <c>Naming.CommitAsync</c> fell back.
    /// Merge uses the first input as the reference.)
    /// </summary>
    internal static bool UsedFallback(string input, string output)
    {
        try
        {
            var inDir = Path.GetDirectoryName(Path.GetFullPath(input).TrimEnd('\\', '/'));
            var outDir = Path.GetDirectoryName(Path.GetFullPath(output).TrimEnd('\\', '/'));
            return !string.Equals(inDir, outDir, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static string DescribeUnexpected(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Access to the file was denied.",
        FileNotFoundException or DirectoryNotFoundException => "The file could not be found.",
        NotImplementedException => "This conversion is not implemented yet (" + ex.Message + ").",
        _ => ex.Message,
    };
}
