using MikeShift.Core.Editing;

namespace MikeShift.Core.Actions;

/// <summary>An axis-aligned pixel rectangle with a top-left origin (x grows right, y grows down).</summary>
public readonly record struct RectPx(int X, int Y, int W, int H);

/// <summary>
/// The user's crop decision.
/// </summary>
/// <param name="Rect">The source region to keep, in source-image pixels, top-left origin, in the coordinates of the image
/// as displayed upright (EXIF orientation already applied).</param>
/// <param name="OutputWidth">Output width in pixels (equals <c>Rect.W</c> when not scaled).</param>
/// <param name="OutputHeight">Output height in pixels (equals <c>Rect.H</c> when not scaled).</param>
public sealed record CropChoice(RectPx Rect, int OutputWidth, int OutputHeight);

/// <summary>A time range inside a media file; <c>Start &lt; End</c>.</summary>
public readonly record struct TimeRange(TimeSpan Start, TimeSpan End)
{
    /// <summary><c>End - Start</c>.</summary>
    public TimeSpan Duration => End - Start;
}

/// <summary>
/// How an interactive action (Crop, Trim, the order of a Merge) asks the user for input. The App shows editor windows; the CLI answers
/// from <c>--crop</c>/<c>--range</c> and takes the files in the order given; tests use a fake. Returning null means the user cancelled,
/// which stops the batch but keeps outputs already written.
/// </summary>
public interface IInteraction
{
    /// <summary>Asks for a crop of the image at <paramref name="path"/>. Null = cancelled.</summary>
    Task<CropChoice?> ChooseCropAsync(string path, CancellationToken ct);

    /// <summary>Asks for a time range of the media file at <paramref name="path"/>. Null = cancelled.</summary>
    Task<TimeRange?> ChooseRangeAsync(string path, CancellationToken ct);

    /// <summary>
    /// Asks in which order <paramref name="paths"/> are to be joined (Merge, Merge PDF). The answer holds exactly the same files, in the
    /// chosen order (<see cref="MikeShift.Core.Editing.FileOrderModel.IsPermutation"/>). Null = cancelled.
    /// </summary>
    Task<IReadOnlyList<string>?> ChooseOrderAsync(IReadOnlyList<string> paths, CancellationToken ct);
}

/// <summary>
/// An <see cref="IInteraction"/> for runs without anyone to ask. It cancels a crop or a trim, which have no sensible default, and keeps
/// the order of a merge as it was given.
/// </summary>
public sealed class NoInteraction : IInteraction
{
    public static NoInteraction Instance { get; } = new();

    public Task<CropChoice?> ChooseCropAsync(string path, CancellationToken ct) => Task.FromResult<CropChoice?>(null);

    public Task<TimeRange?> ChooseRangeAsync(string path, CancellationToken ct) => Task.FromResult<TimeRange?>(null);

    public Task<IReadOnlyList<string>?> ChooseOrderAsync(IReadOnlyList<string> paths, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>?>(paths);
}

/// <summary>Everything an action run needs besides the file list.</summary>
public sealed class ActionContext
{
    public ActionContext(Capabilities capabilities, IInteraction? interaction = null, IProgress<string>? progress = null, ConversionOptions? options = null)
    {
        Capabilities = capabilities;
        Interaction = interaction ?? NoInteraction.Instance;
        Progress = progress;
        Options = options ?? ConversionOptions.Default;
    }

    /// <summary>What this machine can do (also what the catalog was filtered with).</summary>
    public Capabilities Capabilities { get; }

    /// <summary>Source of crop/range decisions.</summary>
    public IInteraction Interaction { get; }

    /// <summary>The user's conversion settings (quality, Compress strength, Resize percentage, GIF length, output folder); <see cref="ConversionOptions.Default"/> unless the host passed some.</summary>
    public ConversionOptions Options { get; }

    /// <summary>Optional status sink; <c>Each</c> reports the file name it is starting.</summary>
    public IProgress<string>? Progress { get; }

    /// <summary>Asks for a crop; throws <see cref="OperationCanceledException"/> if the user cancelled.</summary>
    public async Task<CropChoice> RequireCropAsync(string path, CancellationToken ct) =>
        await Interaction.ChooseCropAsync(path, ct).ConfigureAwait(false)
        ?? throw new OperationCanceledException("Crop cancelled.");

    /// <summary>Asks for a range; throws <see cref="OperationCanceledException"/> if the user cancelled.</summary>
    public async Task<TimeRange> RequireRangeAsync(string path, CancellationToken ct) =>
        await Interaction.ChooseRangeAsync(path, ct).ConfigureAwait(false)
        ?? throw new OperationCanceledException("Trim cancelled.");

    /// <summary>
    /// Asks in which order to join <paramref name="paths"/>; throws <see cref="OperationCanceledException"/> if the user cancelled and
    /// <see cref="InvalidOperationException"/> if the answer is not the same files (a merge must never silently drop or add one).
    /// </summary>
    public async Task<IReadOnlyList<string>> RequireOrderAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        var ordered = await Interaction.ChooseOrderAsync(paths, ct).ConfigureAwait(false)
            ?? throw new OperationCanceledException("Merge cancelled.");
        if (!FileOrderModel.IsPermutation(paths, ordered))
            throw new InvalidOperationException("The chosen order does not hold the files that were selected.");
        return ordered;
    }
}
