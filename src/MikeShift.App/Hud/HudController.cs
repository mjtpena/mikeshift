using Microsoft.UI.Dispatching;

namespace MikeShift.App.Hud;

/// <summary>What the rest of the app uses to put text on the HUD toast. Safe to call from any thread.</summary>
public interface IHud
{
    /// <summary>Shows (or updates in place) the toast with <paramref name="text"/>; it hides itself 2.5 s later.</summary>
    void Show(string text);

    /// <summary>Hides the toast immediately.</summary>
    void Hide();
}

/// <summary>Owns the single <see cref="ToastWindow"/> (created lazily on the UI thread) and marshals calls onto it.</summary>
public sealed class HudController : IHud
{
    private readonly DispatcherQueue _dispatcher;
    private ToastWindow? _window;

    public HudController(DispatcherQueue dispatcher) => _dispatcher = dispatcher;

    /// <summary>True when the toast is currently on screen (UI thread only).</summary>
    public bool IsVisible => _window?.IsToastVisible == true;

    public void Show(string text) => _dispatcher.TryEnqueue(() =>
    {
        try
        {
            _window ??= new ToastWindow();
            _window.ShowText(text);
        }
        catch (Exception)
        {
            // A toast is never worth a crash.
        }
    });

    public void Hide() => _dispatcher.TryEnqueue(() => _window?.HideToast());

    /// <summary>Closes the window (app exit).</summary>
    public void Dispose() => _dispatcher.TryEnqueue(() =>
    {
        _window?.Close();
        _window = null;
    });
}
