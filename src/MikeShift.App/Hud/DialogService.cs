using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MikeShift.App.Hud;

/// <summary>Modal dialogs for failures and warnings.</summary>
public interface IDialogService
{
    /// <summary>Shows a modal error/warning dialog. Blocks (with a message pump) until dismissed; UI thread only.</summary>
    void ShowError(string title, string body);
}

/// <summary>
/// Native <c>MessageBox</c>: needs no XAML window host (the app has no main window), is always topmost and keeps the
/// system look. Used for "None of the N files could be converted…", the partial-failure list and the elevation warning.
/// </summary>
public sealed class Win32DialogService : IDialogService
{
    public void ShowError(string title, string body) => Show(title, body, MESSAGEBOX_STYLE.MB_ICONWARNING);

    /// <summary>Shows an informational dialog.</summary>
    public void ShowInfo(string title, string body) => Show(title, body, MESSAGEBOX_STYLE.MB_ICONINFORMATION);

    private static void Show(string title, string body, MESSAGEBOX_STYLE icon) =>
        PInvoke.MessageBox(HWND.Null, body, title,
            MESSAGEBOX_STYLE.MB_OK | icon | MESSAGEBOX_STYLE.MB_SETFOREGROUND | MESSAGEBOX_STYLE.MB_TOPMOST);
}
