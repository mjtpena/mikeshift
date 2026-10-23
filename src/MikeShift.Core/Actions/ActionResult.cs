using System.Text;

namespace MikeShift.Core.Actions;

/// <summary>One file or folder an action produced.</summary>
/// <param name="Input">The input path this output came from (for batch actions such as Merge/Zip: the first input).</param>
/// <param name="Path">The output path (a folder for " pages" / " split" outputs).</param>
/// <param name="UsedDownloadsFallback">The output directory differs from the input's directory because it was not writable.</param>
public sealed record ActionOutput(string Input, string Path, bool UsedDownloadsFallback);

/// <summary>One input that could not be converted.</summary>
/// <param name="Input">The input path.</param>
/// <param name="Message">The user-facing reason.</param>
public sealed record ActionFailure(string Input, string Message);

/// <summary>
/// The outcome of running an action over a selection (PLAN §1.1 Batch, feedback rules).
/// </summary>
public sealed record ActionResult
{
    /// <summary>Maximum failures listed in a partial-failure message (PLAN: "up to 6").</summary>
    public const int MaxListedFailures = 6;

    /// <summary>Number of inputs the action was started with.</summary>
    public int Total { get; init; }

    /// <summary>Everything written, in input order.</summary>
    public IReadOnlyList<ActionOutput> Outputs { get; init; } = [];

    /// <summary>Inputs that failed, in input order.</summary>
    public IReadOnlyList<ActionFailure> Failures { get; init; } = [];

    /// <summary>Inputs skipped because they were already in the target format ("· N already PNG").</summary>
    public int AlreadyInFormat { get; init; }

    /// <summary>Label used for the "already" note, e.g. "PNG".</summary>
    public string? AlreadyLabel { get; init; }

    /// <summary>The user cancelled (or the token fired); earlier outputs were kept and later inputs were not attempted.</summary>
    public bool Cancelled { get; init; }

    /// <summary>True if at least one output went to Downloads instead of next to its input.</summary>
    public bool AnyDownloadsFallback => Outputs.Any(o => o.UsedDownloadsFallback);

    /// <summary>True if nothing was produced and at least one real failure occurred (the "None of the N files..." case).</summary>
    public bool AllFailed => Outputs.Count == 0 && Failures.Count > 0 && !Cancelled;

    /// <summary>True if some outputs exist and some inputs failed (the partial-failure dialog case).</summary>
    public bool PartialFailure => Outputs.Count > 0 && Failures.Count > 0;

    /// <summary>True if no input failed and the run was not cancelled.</summary>
    public bool Succeeded => Failures.Count == 0 && !Cancelled;

    /// <summary>
    /// The error text when <see cref="AllFailed"/>: the reason alone for a single file, otherwise
    /// "None of the N files could be converted: &lt;first reason&gt;". Null if not all failed.
    /// </summary>
    public string? AllFailedMessage
    {
        get
        {
            if (!AllFailed) return null;
            var first = Failures[0].Message;
            return Total <= 1 ? first : $"None of the {Total} files could be converted: {first}";
        }
    }

    /// <summary>
    /// The partial-failure dialog body: one "name: reason" line per failure, at most
    /// <see cref="MaxListedFailures"/>, then "…and N more". Empty if there are no failures.
    /// </summary>
    public IReadOnlyList<string> FailureLines
    {
        get
        {
            var lines = Failures.Take(MaxListedFailures)
                .Select(f => $"{System.IO.Path.GetFileName(f.Input)}: {f.Message}")
                .ToList();
            if (Failures.Count > MaxListedFailures)
                lines.Add($"…and {Failures.Count - MaxListedFailures} more");
            return lines;
        }
    }

    /// <summary>
    /// The HUD toast text: "photo 2.png", "3 files saved", "2 saved, 1 skipped" (skipped = failed), plus
    /// " · 2 already PNG" and " · in Downloads" when applicable. Empty only when nothing happened at all.
    /// </summary>
    public string ToastText
    {
        get
        {
            var sb = new StringBuilder();
            if (Outputs.Count == 1 && Failures.Count == 0)
                sb.Append(System.IO.Path.GetFileName(Outputs[0].Path.TrimEnd('\\', '/')));
            else if (Outputs.Count > 0 && Failures.Count == 0)
                sb.Append(Outputs.Count).Append(" files saved");
            else if (Outputs.Count > 0)
                sb.Append(Outputs.Count).Append(" saved, ").Append(Failures.Count).Append(" skipped");
            else if (Failures.Count > 0)
                sb.Append(AllFailedMessage);
            else if (Cancelled)
                sb.Append("Cancelled");

            if (AlreadyInFormat > 0)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(AlreadyInFormat).Append(" already ").Append(AlreadyLabel ?? "converted");
            }
            if (AnyDownloadsFallback)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append("in Downloads");
            }
            return sb.ToString();
        }
    }

    /// <summary>A result for a batch action that produced exactly one output.</summary>
    public static ActionResult Single(int total, string input, CommitResult commit) => new()
    {
        Total = total,
        Outputs = [new ActionOutput(input, commit.Path, commit.UsedDownloadsFallback)],
    };

    /// <summary>A result for a batch action that failed as a whole.</summary>
    public static ActionResult Failed(int total, string input, string message) => new()
    {
        Total = total,
        Failures = [new ActionFailure(input, message)],
    };
}
