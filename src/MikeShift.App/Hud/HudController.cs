using Microsoft.UI.Dispatching;
using MikeShift.Core.Feedback;

namespace MikeShift.App.Hud;

/// <summary>What the rest of the app uses to put a message on the HUD toast. Safe to call from any thread.</summary>
public interface IHud
{
    /// <summary>Shows (or updates in place) the toast with <paramref name="content"/>; it hides itself after <see cref="ToastContent.Lifetime"/>.</summary>
    void Show(ToastContent content);

    /// <summary>Hides the toast immediately.</summary>
    void Hide();
}

/// <summary>Convenience overloads of <see cref="IHud"/>.</summary>
public static class HudExtensions
{
    /// <summary>Shows plain <paramref name="text"/> (no status glyph, no action).</summary>
    public static void Show(this IHud hud, string text) => hud.Show(new ToastContent(text));
}

/// <summary>
/// Owns the single <see cref="ToastWindow"/> and marshals calls onto the UI thread. The window is created ahead of the first toast
/// (<see cref="Prewarm"/>, a few seconds after start-up) so the first result does not wait for XAML to initialise; it stays cloaked
/// until its first frame is composed, so it never flashes black either way.
/// </summary>
public sealed class HudController : IHud
{
    private readonly DispatcherQueue _dispatcher;
    private ToastWindow? _window;

    public HudController(DispatcherQueue dispatcher) => _dispatcher = dispatcher;

    /// <summary>True when the toast is currently on screen (UI thread only).</summary>
    public bool IsVisible => _window?.IsToastVisible == true;

    /// <summary>The toast window if it exists (UI thread only; tests and the capture harness).</summary>
    internal ToastWindow? Window => _window;

    public void Show(ToastContent content) => _dispatcher.TryEnqueue(() =>
    {
        try
        {
            (_window ??= new ToastWindow()).ShowContent(content);
        }
        catch (Exception ex)
        {
            // A toast is never worth a crash; the log has the reason.
            Wheel.WheelLog.Error("toast failed", ex);
        }
    });

    /// <summary>Shows plain <paramref name="text"/> (no status glyph, no action).</summary>
    public void Show(string text) => Show(new ToastContent(text));

    public void Hide() => _dispatcher.TryEnqueue(() => _window?.HideToast());

    /// <summary>Creates the (hidden) toast window now. UI thread only; a no-op when it exists.</summary>
    public void Prewarm()
    {
        try
        {
            _window ??= new ToastWindow();
        }
        catch (Exception)
        {
            // created on first use instead
        }
    }

    /// <summary>Closes the window (app exit).</summary>
    public void Dispose() => _dispatcher.TryEnqueue(() =>
    {
        _window?.Close();
        _window = null;
    });
}
