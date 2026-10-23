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
/// How an interactive action (Crop, Trim) asks the user for input. The App shows editor windows; the CLI answers
/// from <c>--crop</c>/<c>--range</c>; tests use a fake. Returning null means the user cancelled, which stops the
/// batch but keeps outputs already written.
/// </summary>
public interface IInteraction
{
    /// <summary>Asks for a crop of the image at <paramref name="path"/>. Null = cancelled.</summary>
    Task<CropChoice?> ChooseCropAsync(string path, CancellationToken ct);

    /// <summary>Asks for a time range of the media file at <paramref name="path"/>. Null = cancelled.</summary>
    Task<TimeRange?> ChooseRangeAsync(string path, CancellationToken ct);
}

/// <summary>An <see cref="IInteraction"/> that cancels everything. Used for headless runs without interactive input.</summary>
public sealed class NoInteraction : IInteraction
{
    public static NoInteraction Instance { get; } = new();

    public Task<CropChoice?> ChooseCropAsync(string path, CancellationToken ct) => Task.FromResult<CropChoice?>(null);

    public Task<TimeRange?> ChooseRangeAsync(string path, CancellationToken ct) => Task.FromResult<TimeRange?>(null);
}

/// <summary>Everything an action run needs besides the file list.</summary>
public sealed class ActionContext
{
    public ActionContext(Capabilities capabilities, IInteraction? interaction = null, IProgress<string>? progress = null)
    {
        Capabilities = capabilities;
        Interaction = interaction ?? NoInteraction.Instance;
        Progress = progress;
    }

    /// <summary>What this machine can do (also what the catalog was filtered with).</summary>
    public Capabilities Capabilities { get; }

    /// <summary>Source of crop/range decisions.</summary>
    public IInteraction Interaction { get; }

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
}
