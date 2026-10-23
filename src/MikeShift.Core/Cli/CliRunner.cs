using System.Globalization;
using MikeShift.Core.Actions;

namespace MikeShift.Core.Cli;

/// <summary>
/// The <c>mikeshift</c> command line (PLAN §3.6): the same catalog and converters as the wheel, without the UI.
/// <code>
/// mikeshift --list FILES...                                              comma-separated action titles, exit 0
/// mikeshift --run TITLE [--range m:ss-m:ss] [--crop X,Y,W,H[@OWxOH]] FILES...   prints each output path, exit 0
/// </code>
/// Exit codes: 0 success; 1 an error ("error: ..." on stderr; unknown title lists the available actions; Trim/Crop
/// without --range/--crop prints syntax help); 2 usage (any other flag, missing arguments).
/// It lives in Core so tests can call it in-process; <c>MikeShift.Cli</c> is a thin <c>Main</c> around it.
/// </summary>
public static class CliRunner
{
    public const int ExitOk = 0;
    public const int ExitError = 1;
    public const int ExitUsage = 2;

    public const string UsageText =
        "usage:\n" +
        "  mikeshift --list FILES...\n" +
        "  mikeshift --run TITLE [--range m:ss-m:ss] [--crop X,Y,W,H[@OWxOH]] FILES...\n";

    public const string RangeSyntax =
        "--range m:ss-m:ss   start and end of the part to keep, e.g. --range 0:05-0:12.5 (also h:mm:ss, fractions allowed)";

    public const string CropSyntax =
        "--crop X,Y,W,H[@OWxOH]   pixel rectangle (top-left origin) and optional output size, e.g. --crop 100,50,800,600@400x300";

    /// <summary>Runs the CLI. <paramref name="caps"/> defaults to <see cref="Capabilities.Current"/>.</summary>
    public static async Task<int> RunAsync(
        string[] args, TextWriter stdout, TextWriter stderr, Capabilities? caps = null, CancellationToken ct = default)
    {
        if (!TryParse(args, out var cmd, out var parseError))
        {
            if (parseError is not null) stderr.WriteLine("error: " + parseError);
            stderr.Write(UsageText);
            return ExitUsage;
        }

        caps ??= Capabilities.Current;

        var paths = new List<string>();
        foreach (var f in cmd.Files)
        {
            string full;
            try { full = Path.GetFullPath(f); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                stderr.WriteLine($"error: invalid path: {f}");
                return ExitError;
            }
            if (!File.Exists(full) && !Directory.Exists(full))
            {
                stderr.WriteLine($"error: not found: {f}");
                return ExitError;
            }
            paths.Add(full);
        }

        var actions = ActionCatalog.For(paths, caps);

        if (cmd.Mode == Mode.List)
        {
            stdout.WriteLine(string.Join(", ", actions.Select(a => a.Title)));
            return ExitOk;
        }

        var action = ActionCatalog.Find(actions, cmd.Title!);
        if (action is null)
        {
            stderr.WriteLine($"error: unknown action \"{cmd.Title}\" for these files. Available: " +
                             string.Join(", ", actions.Select(a => a.Title)));
            return ExitError;
        }

        RangeArg? range = null;
        CropChoice? crop = null;
        if (cmd.Range is not null && !TryParseRange(cmd.Range, out range, out var rangeErr))
        {
            stderr.WriteLine($"error: {rangeErr}\n{RangeSyntax}");
            return ExitError;
        }
        if (cmd.Crop is not null && !TryParseCrop(cmd.Crop, out crop, out var cropErr))
        {
            stderr.WriteLine($"error: {cropErr}\n{CropSyntax}");
            return ExitError;
        }

        var interaction = new CliInteraction(range is { } r ? new TimeRange(r.Start, r.End) : null, crop);
        var ctx = new ActionContext(caps, interaction);

        ActionResult result;
        try
        {
            result = await action.Run(paths, ctx, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            stderr.WriteLine("error: cancelled");
            return ExitError;
        }

        foreach (var o in result.Outputs)
            stdout.WriteLine(o.Path);

        if (result.AlreadyInFormat > 0)
            stderr.WriteLine($"note: {result.AlreadyInFormat} already {result.AlreadyLabel ?? action.Title}");
        if (result.AnyDownloadsFallback)
            stderr.WriteLine("note: saved in Downloads (the original's folder is not writable)");

        if (result.Cancelled)
        {
            if (interaction.MissingRange)
                stderr.WriteLine($"error: {action.Title} needs a time range.\n{RangeSyntax}");
            else if (interaction.MissingCrop)
                stderr.WriteLine($"error: {action.Title} needs a crop rectangle.\n{CropSyntax}");
            else
                stderr.WriteLine("error: cancelled");
            return ExitError;
        }

        if (result.AllFailed)
        {
            stderr.WriteLine("error: " + result.AllFailedMessage);
            return ExitError;
        }

        foreach (var f in result.Failures)
            stderr.WriteLine($"error: {Path.GetFileName(f.Input)}: {f.Message}");

        return result.Failures.Count == 0 ? ExitOk : ExitError;
    }

    // ------------------------------------------------------------------ parsing

    private enum Mode { List, Run }

    private sealed record Command(Mode Mode, string? Title, string? Range, string? Crop, IReadOnlyList<string> Files);

    private static bool TryParse(string[] args, out Command cmd, out string? error)
    {
        cmd = new Command(Mode.List, null, null, null, []);
        error = null;

        Mode? mode = null;
        string? title = null, range = null, crop = null;
        var files = new List<string>();
        var onlyFiles = false;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (onlyFiles || !a.StartsWith('-') || a.Length == 1)
            {
                files.Add(a);
                continue;
            }

            switch (a)
            {
                case "--":
                    onlyFiles = true;
                    break;
                case "--list":
                    if (mode is not null) { error = "use either --list or --run"; return false; }
                    mode = Mode.List;
                    break;
                case "--run":
                    if (mode is not null) { error = "use either --list or --run"; return false; }
                    mode = Mode.Run;
                    if (++i >= args.Length) { error = "--run needs an action title"; return false; }
                    title = args[i];
                    break;
                case "--range":
                    if (++i >= args.Length) { error = "--range needs a value"; return false; }
                    range = args[i];
                    break;
                case "--crop":
                    if (++i >= args.Length) { error = "--crop needs a value"; return false; }
                    crop = args[i];
                    break;
                default:
                    error = $"unknown option {a}";
                    return false;
            }
        }

        if (mode is null) { error = null; return false; }
        if (files.Count == 0) { error = "no files given"; return false; }
        if (mode == Mode.List && (range is not null || crop is not null)) { error = "--range and --crop only apply to --run"; return false; }

        cmd = new Command(mode.Value, title, range, crop, files);
        return true;
    }

    private readonly record struct RangeArg(TimeSpan Start, TimeSpan End);

    private static bool TryParseRange(string text, out RangeArg? range, out string error)
    {
        range = null;
        error = $"can't read the range \"{text}\"";
        var parts = text.Split('-');
        if (parts.Length != 2 || !TryParseTime(parts[0], out var start) || !TryParseTime(parts[1], out var end)) return false;
        if (end <= start) { error = $"the range \"{text}\" ends before it starts"; return false; }
        range = new RangeArg(start, end);
        return true;
    }

    /// <summary>[h:]m:ss[.fff] or plain seconds.</summary>
    internal static bool TryParseTime(string text, out TimeSpan time)
    {
        time = default;
        var segs = text.Trim().Split(':');
        if (segs.Length is < 1 or > 3) return false;

        double total = 0;
        for (var i = 0; i < segs.Length; i++)
        {
            var isLast = i == segs.Length - 1;
            var style = isLast ? NumberStyles.AllowDecimalPoint : NumberStyles.None;
            if (!double.TryParse(segs[i], style, CultureInfo.InvariantCulture, out var v) || v < 0) return false;
            if (!isLast && v != Math.Floor(v)) return false;
            total = total * 60 + v;
        }
        time = TimeSpan.FromSeconds(total);
        return true;
    }

    private static bool TryParseCrop(string text, out CropChoice? crop, out string error)
    {
        crop = null;
        error = $"can't read the crop \"{text}\"";

        var at = text.IndexOf('@');
        var rectPart = at < 0 ? text : text[..at];
        var rect = rectPart.Split(',');
        if (rect.Length != 4) return false;

        var v = new int[4];
        for (var i = 0; i < 4; i++)
            if (!int.TryParse(rect[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out v[i])) return false;
        if (v[2] <= 0 || v[3] <= 0) { error = "the crop width and height must be positive"; return false; }

        int ow = v[2], oh = v[3];
        if (at >= 0)
        {
            var size = text[(at + 1)..].Split('x', 'X');
            if (size.Length != 2
                || !int.TryParse(size[0], NumberStyles.None, CultureInfo.InvariantCulture, out ow)
                || !int.TryParse(size[1], NumberStyles.None, CultureInfo.InvariantCulture, out oh)) return false;
            if (ow <= 0 || oh <= 0) { error = "the crop output size must be positive"; return false; }
        }

        crop = new CropChoice(new RectPx(v[0], v[1], v[2], v[3]), ow, oh);
        return true;
    }

    /// <summary>Answers interaction requests from the command line; records when an answer was needed but missing.</summary>
    private sealed class CliInteraction(TimeRange? range, CropChoice? crop) : IInteraction
    {
        public bool MissingRange { get; private set; }
        public bool MissingCrop { get; private set; }

        public Task<CropChoice?> ChooseCropAsync(string path, CancellationToken ct)
        {
            if (crop is null) MissingCrop = true;
            return Task.FromResult(crop);
        }

        public Task<TimeRange?> ChooseRangeAsync(string path, CancellationToken ct)
        {
            if (range is null) MissingRange = true;
            return Task.FromResult(range);
        }
    }
}
