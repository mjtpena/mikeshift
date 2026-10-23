using Microsoft.UI.Dispatching;
using MikeShift.App.Hud;
using MikeShift.Core.Actions;

namespace MikeShift.App.Jobs;

/// <summary>
/// The App's <see cref="IInteraction"/>: how Crop and Trim ask the user for input.
/// <para>
/// W2f ships a placeholder: until an editor is attached the methods return null (cancelled) after showing a
/// "not available yet" toast. W4a plugs the real editors in by setting <see cref="CropEditor"/> and
/// <see cref="TrimEditor"/> (from App startup code). The handlers are called on a thread-pool thread because actions run
/// there; use <see cref="OnUiThreadAsync{T}"/> to open XAML windows.
/// </para>
/// </summary>
public sealed class UiInteraction : IInteraction
{
    private readonly DispatcherQueue _dispatcher;
    private readonly IHud _hud;

    public UiInteraction(DispatcherQueue dispatcher, IHud hud)
    {
        _dispatcher = dispatcher;
        _hud = hud;
    }

    /// <summary>Shows the crop editor for an image path and returns the decision (null = cancelled). Set by W4a.</summary>
    public Func<string, CancellationToken, Task<CropChoice?>>? CropEditor { get; set; }

    /// <summary>Shows the trim dialog for a media path and returns the range (null = cancelled). Set by W4a.</summary>
    public Func<string, CancellationToken, Task<TimeRange?>>? TrimEditor { get; set; }

    public Task<CropChoice?> ChooseCropAsync(string path, CancellationToken ct)
    {
        if (CropEditor is { } editor) return editor(path, ct);
        _hud.Show("Crop is not available yet");
        return Task.FromResult<CropChoice?>(null);
    }

    public Task<TimeRange?> ChooseRangeAsync(string path, CancellationToken ct)
    {
        if (TrimEditor is { } editor) return editor(path, ct);
        _hud.Show("Trim is not available yet");
        return Task.FromResult<TimeRange?>(null);
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the UI thread and returns its result, so an editor handler called from the
    /// thread pool can create and await a XAML window.
    /// </summary>
    public Task<T> OnUiThreadAsync<T>(Func<Task<T>> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () =>
        {
            try { tcs.SetResult(await work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
        {
            tcs.SetException(new InvalidOperationException("The UI thread is not available."));
        }
        return tcs.Task;
    }
}

/// <summary>
/// Wraps an <see cref="IInteraction"/> and reports when the user accepted an editor, so the runner can show the
/// "working" toast only after the editor closed (PLAN §3.3).
/// </summary>
internal sealed class NotifyingInteraction : IInteraction
{
    private readonly IInteraction _inner;
    private readonly Action _accepted;

    public NotifyingInteraction(IInteraction inner, Action accepted)
    {
        _inner = inner;
        _accepted = accepted;
    }

    public async Task<CropChoice?> ChooseCropAsync(string path, CancellationToken ct)
    {
        var choice = await _inner.ChooseCropAsync(path, ct).ConfigureAwait(false);
        if (choice is not null) _accepted();
        return choice;
    }

    public async Task<TimeRange?> ChooseRangeAsync(string path, CancellationToken ct)
    {
        var range = await _inner.ChooseRangeAsync(path, ct).ConfigureAwait(false);
        if (range is not null) _accepted();
        return range;
    }
}
