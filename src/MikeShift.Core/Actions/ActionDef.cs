namespace MikeShift.Core.Actions;

/// <summary>Runs an action over a whole selection. Must not modify the inputs; writes outputs via <c>Naming.CommitAsync</c>.</summary>
public delegate Task<ActionResult> ActionRun(IReadOnlyList<string> paths, ActionContext ctx, CancellationToken ct);

/// <summary>Converts a single input and returns the produced path. Used with <see cref="ActionDef.PerFile"/>.</summary>
public delegate Task<string> FileConvert(string input, ActionContext ctx, CancellationToken ct);

/// <summary>
/// One wheel tile: what it is called, where it lives, when it is visible and how it runs.
/// Instances are immutable and created once, in the per-category files under <c>Actions/Catalog</c>.
/// </summary>
public sealed record ActionDef
{
    private static readonly IReadOnlySet<string> NoExtensions = new HashSet<string>();

    public ActionDef(string title, WheelPage page, ActionRun run)
    {
        Title = title;
        Page = page;
        Run = run;
    }

    /// <summary>Display and CLI name: "PNG", "Compress", "Resize 50%", "Merge PDF", "Zip". Unique within a category list.</summary>
    public string Title { get; }

    /// <summary>The wheel page this tile belongs to.</summary>
    public WheelPage Page { get; }

    /// <summary>Executes the action.</summary>
    public ActionRun Run { get; }

    /// <summary>For tools: a Segoe Fluent Icons code point as a one-character string (e.g. ""). Null for format tiles.</summary>
    public string? Glyph { get; init; }

    /// <summary>
    /// Lower-case extensions (no dot) already in this action's output format. The action is hidden when EVERY selected
    /// file has one of these; in a mixed selection matching files are skipped and counted ("· 1 already PNG").
    /// </summary>
    public IReadOnlySet<string> HideFor { get; init; } = NoExtensions;

    /// <summary>True for actions that need user input first (Crop, Trim): the HUD toast waits until the editor closes.</summary>
    public bool Interactive { get; init; }

    /// <summary>Capability gate (e.g. <c>c =&gt; c.HeifEncode</c>). Null = always available.</summary>
    public Func<Capabilities, bool>? Requires { get; init; }

    /// <summary>Selection gate (e.g. "at least 2 items"). Receives the selected paths and the capabilities. Null = always applicable.</summary>
    public Func<IReadOnlyList<string>, Capabilities, bool>? AppliesTo { get; init; }

    /// <summary>
    /// The text for Explorer's drop description while this tile is highlighted:
    /// "Convert to PNG" for formats, the title for tools.
    /// </summary>
    public string DropDescription => Page == WheelPage.Format ? $"Convert to {Title}" : Title;

    /// <summary>
    /// True if this action should be offered for <paramref name="paths"/> given <paramref name="caps"/>:
    /// capability available, selection predicate satisfied, and not every selected file is already in the target format.
    /// </summary>
    public bool IsVisibleFor(IReadOnlyList<string> paths, Capabilities caps)
    {
        if (Requires is not null && !Requires(caps)) return false;
        if (AppliesTo is not null && !AppliesTo(paths, caps)) return false;
        if (HideFor.Count > 0 && paths.Count > 0 && paths.All(p => HideFor.Contains(Kind.ExtensionOf(p)))) return false;
        return true;
    }

    /// <summary>
    /// Creates an action that converts each selected file independently through <see cref="Each"/>
    /// (sequential, failures isolated, "already in format" skipping driven by <paramref name="hideFor"/>, labelled with the title).
    /// </summary>
    public static ActionDef PerFile(
        string title,
        WheelPage page,
        FileConvert convert,
        string? glyph = null,
        string[]? hideFor = null,
        bool interactive = false,
        Func<Capabilities, bool>? requires = null,
        Func<IReadOnlyList<string>, Capabilities, bool>? appliesTo = null)
    {
        var skip = hideFor is { Length: > 0 } ? new HashSet<string>(hideFor, StringComparer.Ordinal) : NoExtensions;
        return new ActionDef(title, page,
            (paths, ctx, ct) => Each.RunAsync(paths, ctx, (input, token) => convert(input, ctx, token), ct, skip, title))
        {
            Glyph = glyph,
            HideFor = skip,
            Interactive = interactive,
            Requires = requires,
            AppliesTo = appliesTo,
        };
    }

    /// <summary>
    /// Creates an action that consumes the whole selection at once (Merge, Zip) through <paramref name="run"/>.
    /// </summary>
    public static ActionDef Batch(
        string title,
        WheelPage page,
        ActionRun run,
        string? glyph = null,
        Func<Capabilities, bool>? requires = null,
        Func<IReadOnlyList<string>, Capabilities, bool>? appliesTo = null) =>
        new(title, page, run) { Glyph = glyph, Requires = requires, AppliesTo = appliesTo };

    /// <summary>
    /// Creates an action that consumes the whole selection and produces ONE output (Merge PDF, Merge, Zip): <paramref name="convert"/>
    /// returns the output path. Cancellation, <see cref="ConversionException"/> and unexpected exceptions become a proper
    /// <see cref="ActionResult"/>; the Downloads fallback is inferred against the first input.
    /// </summary>
    public static ActionDef SingleOutput(
        string title,
        WheelPage page,
        Func<IReadOnlyList<string>, ActionContext, CancellationToken, Task<string>> convert,
        string? glyph = null,
        Func<Capabilities, bool>? requires = null,
        Func<IReadOnlyList<string>, Capabilities, bool>? appliesTo = null) =>
        Batch(title, page, async (paths, ctx, ct) =>
        {
            if (ct.IsCancellationRequested) return new ActionResult { Total = paths.Count, Cancelled = true };
            ctx.Progress?.Report(title);
            try
            {
                var output = await convert(paths, ctx, ct).ConfigureAwait(false);
                return new ActionResult
                {
                    Total = paths.Count,
                    Outputs = [new ActionOutput(paths[0], output, Each.UsedFallback(paths[0], output))],
                };
            }
            catch (OperationCanceledException)
            {
                return new ActionResult { Total = paths.Count, Cancelled = true };
            }
            catch (ConversionException ex)
            {
                return ActionResult.Failed(paths.Count, paths[0], ex.Message);
            }
            catch (Exception ex)
            {
                return ActionResult.Failed(paths.Count, paths[0], Each.DescribeUnexpected(ex));
            }
        }, glyph, requires, appliesTo);

    /// <summary>An <c>appliesTo</c> predicate: at least <paramref name="n"/> selected items.</summary>
    public static Func<IReadOnlyList<string>, Capabilities, bool> AtLeast(int n) => (paths, _) => paths.Count >= n;
}
