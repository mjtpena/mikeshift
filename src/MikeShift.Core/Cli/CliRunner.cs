using System.Globalization;
using MikeShift.Core.Actions;
using MikeShift.Core.Feedback;

namespace MikeShift.Core.Cli;

/// <summary>
/// The <c>mikeshift</c> command line: the same catalog and converters as the wheel, without the UI.
/// <code>
/// mikeshift [--use-word] --list FILES...                                 comma-separated action titles, exit 0
/// mikeshift [--use-word] --run TITLE [--range m:ss-m:ss] [--crop X,Y,W,H[@OWxOH]] FILES...   prints each output path, exit 0
/// </code>
/// Both accept the conversion options <c>--quality N</c> (50-100), <c>--compress light|medium|strong</c>, <c>--resize 25|50|75</c>,
/// <c>--gif-seconds 5|10|15|30</c>, <c>--max-size MB</c> (1-2000, the size the video Fit tool aims for) and <c>--out DIR</c> (an existing folder); each defaults to <see cref="ConversionOptions.Default"/> (the command line
/// never reads the Settings window's choices, so a script gives the same result on every PC). A bad value is a usage error (exit 2).
/// MikeShift is fully local: without <c>--use-word</c> the CLI never starts Microsoft Word (so legacy .doc is not offered and DOCX to
/// PDF uses the built-in converter). <c>--use-word</c> opts in for that run; Word may then connect to Microsoft on its own.
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
        "  mikeshift [--use-word] [OPTIONS] --list FILES...\n" +
        "  mikeshift [--use-word] [OPTIONS] --run TITLE [--range m:ss-m:ss] [--crop X,Y,W,H[@OWxOH]] FILES...\n" +
        "\n" +
        "  --use-word   let this run use Microsoft Word if it is installed (legacy .doc files, Word's own PDF export).\n" +
        "               Off by default: MikeShift is fully local, and Word may connect to Microsoft.\n" +
        "\n" +
        "conversion options (the defaults are what the Settings window starts with; the command line does not read Settings):\n" +
        "  --quality N        JPG, WebP and HEIC quality, 50 to 100 (default 90)\n" +
        "  --compress LEVEL   how hard Compress squeezes a picture: light, medium or strong (default medium)\n" +
        "  --resize P         the Resize percentage: 25, 50 or 75 (default 50; --run \"Resize 25%\" says the same)\n" +
        "  --gif-seconds N    the longest a GIF made from a video runs: 5, 10, 15 or 30 (default 15)\n" +
        "  --max-size MB      the size Fit keeps a video under, in whole megabytes from 1 to 2000\n" +
        "                     (instead of Fit 10 MB and Fit 25 MB; --run \"Fit 8 MB\" says the same)\n" +
        "  --out DIR          put converted files in this existing folder instead of next to the originals\n" +
        "                     (Downloads if it can't be written to)\n";

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

        // Word is opt-in per run: whatever the caller or the machine says, it is only used with --use-word.
        caps = (caps ?? Capabilities.Current).WithWord(cmd.UseWord);

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

        var options = cmd.Options;
        var actions = ActionCatalog.For(paths, caps, options);

        if (cmd.Mode == Mode.List)
        {
            stdout.WriteLine(string.Join(", ", actions.Select(a => a.Title)));
            return ExitOk;
        }

        var action = ActionCatalog.Find(actions, cmd.Title!);
        if (action is null && actions.FirstOrDefault(a => a.OpensUri is not null) is { } getCodec)
        {
            // HEIC/AVIF photos Windows cannot open yet: the one useful answer is which extension to install, not a list of actions.
            var why = await getCodec.Run(paths, new ActionContext(caps, null), ct).ConfigureAwait(false);
            stderr.WriteLine("error: " + (why.AllFailedMessage ?? "Windows can't open these files yet."));
            stderr.WriteLine("note: " + getCodec.OpensUri);
            return ExitError;
        }
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
        var ctx = new ActionContext(caps, interaction, options: options);

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
        foreach (var note in result.Notes)
            stderr.WriteLine($"note: {note}");
        if (result.AnyDownloadsFallback)
            stderr.WriteLine(options.OutputFolder is { } chosen
                ? $"note: saved in Downloads ({chosen} is not writable)"
                : "note: saved in Downloads (the original's folder is not writable)");

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

    private sealed record Command(Mode Mode, string? Title, string? Range, string? Crop, IReadOnlyList<string> Files, bool UseWord = false,
        ConversionOptions? ParsedOptions = null)
    {
        public ConversionOptions Options => ParsedOptions ?? ConversionOptions.Default;
    }

    private static bool TryParse(string[] args, out Command cmd, out string? error)
    {
        cmd = new Command(Mode.List, null, null, null, []);
        error = null;

        Mode? mode = null;
        string? title = null, range = null, crop = null;
        var useWord = false;
        var files = new List<string>();
        var onlyFiles = false;
        var options = ConversionOptions.Default;
        int? resizeFlag = null;
        int? fitFlag = null;

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
                case "--use-word":
                    useWord = true;
                    break;
                case "--range":
                    if (++i >= args.Length) { error = "--range needs a value"; return false; }
                    range = args[i];
                    break;
                case "--crop":
                    if (++i >= args.Length) { error = "--crop needs a value"; return false; }
                    crop = args[i];
                    break;
                case "--quality":
                    if (++i >= args.Length) { error = "--quality needs a number from 50 to 100"; return false; }
                    if (!ConversionOptions.TryParseQuality(args[i], out var quality))
                    {
                        error = $"--quality must be a whole number from {ConversionOptions.MinQuality} to {ConversionOptions.MaxQuality}, not \"{args[i]}\"";
                        return false;
                    }
                    options = options with { Quality = quality };
                    break;
                case "--compress":
                    if (++i >= args.Length) { error = "--compress needs light, medium or strong"; return false; }
                    if (!ConversionOptions.TryParseCompress(args[i], out var strength))
                    {
                        error = $"--compress must be light, medium or strong, not \"{args[i]}\"";
                        return false;
                    }
                    options = options with { Compress = strength };
                    break;
                case "--resize":
                    if (++i >= args.Length) { error = "--resize needs 25, 50 or 75"; return false; }
                    if (!ConversionOptions.TryParseResizePercent(args[i], out var percent))
                    {
                        error = $"--resize must be 25, 50 or 75, not \"{args[i]}\"";
                        return false;
                    }
                    resizeFlag = percent;
                    options = options with { ResizePercent = percent };
                    break;
                case "--gif-seconds":
                    if (++i >= args.Length) { error = "--gif-seconds needs 5, 10, 15 or 30"; return false; }
                    if (!ConversionOptions.TryParseGifSeconds(args[i], out var seconds))
                    {
                        error = $"--gif-seconds must be 5, 10, 15 or 30, not \"{args[i]}\"";
                        return false;
                    }
                    options = options with { GifSeconds = seconds };
                    break;
                case "--max-size":
                    if (++i >= args.Length) { error = $"--max-size needs a number of megabytes from {ConversionOptions.MinFitMegabytes} to {ConversionOptions.MaxFitMegabytes}"; return false; }
                    if (!ConversionOptions.TryParseFitMegabytes(args[i], out var megabytes))
                    {
                        error = $"--max-size must be a whole number of megabytes from {ConversionOptions.MinFitMegabytes} to {ConversionOptions.MaxFitMegabytes}, not \"{args[i]}\"";
                        return false;
                    }
                    fitFlag = megabytes;
                    options = options with { FitMegabytes = megabytes };
                    break;
                case "--out":
                    if (++i >= args.Length) { error = "--out needs a folder"; return false; }
                    if (!TryResolveFolder(args[i], out var folder, out var folderError))
                    {
                        error = "--out: " + folderError;
                        return false;
                    }
                    options = options with { OutputFolder = folder };
                    break;
                default:
                    error = $"unknown option {a}";
                    return false;
            }
        }

        if (mode is null) { error = null; return false; }
        if (files.Count == 0) { error = "no files given"; return false; }
        if (mode == Mode.List && (range is not null || crop is not null)) { error = "--range and --crop only apply to --run"; return false; }

        // --run "Resize 25%" names the percentage itself; it only conflicts with a --resize that says something else.
        if (mode == Mode.Run && ActionCatalog.ResizePercentIn(title!) is { } titled)
        {
            if (resizeFlag is { } flagged && flagged != titled)
            {
                error = $"\"{title}\" and --resize {flagged} disagree";
                return false;
            }
            options = options with { ResizePercent = titled };
        }

        // Likewise --run "Fit 8 MB" names the size; a --max-size that says another one is a mistake, not a choice between them.
        if (mode == Mode.Run && SizeTargetCopy.MegabytesIn(title!) is { } fitTitled)
        {
            if (fitFlag is { } flagged && flagged != fitTitled)
            {
                error = $"\"{title}\" and --max-size {flagged} disagree";
                return false;
            }
            if (fitTitled is < ConversionOptions.MinFitMegabytes or > ConversionOptions.MaxFitMegabytes)
            {
                error = $"\"{title}\": the size must be {ConversionOptions.MinFitMegabytes} to {ConversionOptions.MaxFitMegabytes} MB";
                return false;
            }
            options = options with { FitMegabytes = fitTitled };
            // "Fit 8MB", "fit  8 mb" and "Fit 008 MB" all name the tile the catalog calls "Fit 8 MB".
            title = SizeTargetCopy.Title(fitTitled);
        }

        cmd = new Command(mode.Value, title, range, crop, files, useWord, options);
        return true;
    }

    /// <summary>An <c>--out</c> value: relative to the current folder is fine; it must already exist.</summary>
    private static bool TryResolveFolder(string text, out string? folder, out string error)
    {
        folder = null;
        error = "";
        string full;
        try { full = Path.GetFullPath(text); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"\"{text}\" is not a valid folder name";
            return false;
        }
        folder = ConversionOptions.ValidateOutputFolder(full, out var problem);
        if (folder is null) error = problem ?? $"\"{text}\" can't be used";
        return folder is not null;
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
