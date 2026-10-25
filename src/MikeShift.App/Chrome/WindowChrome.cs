using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using WinRT.Interop;
using WinUIEx;
using TitleBar = Microsoft.UI.Xaml.Controls.TitleBar;

namespace MikeShift.App.Chrome;

/// <summary>
/// The one chrome treatment every MikeShift XAML window shares: Mica, the MikeShift icon, and a title bar
/// that is part of the window (extended into the content, the WinUI <see cref="TitleBar"/> control with the icon and the title) so
/// the caption follows the app theme instead of staying light over dark content. Placement clamps to the work area.
/// </summary>
internal static class WindowChrome
{
    /// <summary>Dev switch: <c>MIKESHIFT_THEME=light|dark</c> forces the XAML theme (never changes the OS theme).</summary>
    public static ApplicationTheme? ForcedTheme => DevSwitches.Theme switch
    {
        "light" => ApplicationTheme.Light,
        "dark" => ApplicationTheme.Dark,
        _ => null,
    };

    /// <summary>Applies <see cref="ForcedTheme"/> to the application. Only valid before the first window exists (call from the App constructor).</summary>
    public static void ApplyForcedTheme(Application app)
    {
        if (ForcedTheme is { } theme) app.RequestedTheme = theme;
    }

    /// <summary>The MikeShift icon file next to the exe (packaged and unpackaged).</summary>
    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "MikeShift.ico");

    /// <summary>Path of a bundled asset.</summary>
    public static string AssetPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Assets", fileName);

    /// <summary>
    /// Mica, the window icon and a themed, extended title bar showing <paramref name="title"/>. The window's existing content moves
    /// under the title bar. Returns the title bar so callers can add to it. Call once, after <c>InitializeComponent()</c>.
    /// </summary>
    public static TitleBar Apply(Window window, string title)
    {
        window.Title = title;
        window.SystemBackdrop = new MicaBackdrop();
        SetIcon(window);

        var titleBar = new TitleBar
        {
            Title = title,
            IconSource = new ImageIconSource { ImageSource = new BitmapImage(new Uri(AssetPath("Square44x44Logo.targetsize-32_altform-unplated.png"))) },
        };

        var content = window.Content as UIElement;
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(titleBar);
        if (content is not null)
        {
            window.Content = null;
            Grid.SetRow((FrameworkElement)content, 1);
            grid.Children.Add(content);
        }
        window.Content = grid;

        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);
        return titleBar;
    }

    /// <summary>Sets the title-bar, taskbar and Alt+Tab icon.</summary>
    public static void SetIcon(Window window)
    {
        try
        {
            if (File.Exists(IconPath)) window.AppWindow.SetIcon(IconPath);
        }
        catch (Exception)
        {
            // cosmetic
        }
    }

    /// <summary>The work area of the monitor under the cursor (the monitor the user is working on).</summary>
    public static RectInt32 WorkAreaUnderCursor()
    {
        PInvoke.GetCursorPos(out var cursor);
        return DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest).WorkArea;
    }

    /// <summary>
    /// Sizes the window's client area to <paramref name="clientWidthDip"/> x <paramref name="clientHeightDip"/> device-independent
    /// pixels (the title bar is part of the client area) and centres it on the monitor under the cursor; shrinks to the work area on
    /// small screens (the content scrolls). <paramref name="minWidthDip"/>/<paramref name="minHeightDip"/> set the smallest size the
    /// user can drag the window to (0 = not resizable by the caller's presenter settings).
    /// </summary>
    public static void PlaceCentered(Window window, int clientWidthDip, int clientHeightDip, int minWidthDip = 0, int minHeightDip = 0)
    {
        var work = WorkAreaUnderCursor();

        // Move onto the target monitor first so GetDpiForWindow reports that monitor's scale.
        window.AppWindow.Move(new PointInt32(work.X, work.Y));
        var hwnd = new HWND(WindowNative.GetWindowHandle(window));
        var dpi = PInvoke.GetDpiForWindow(hwnd);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;

        window.AppWindow.ResizeClient(new SizeInt32((int)Math.Round(clientWidthDip * scale), (int)Math.Round(clientHeightDip * scale)));

        var outer = window.AppWindow.Size;
        var width = Math.Min(outer.Width, work.Width);
        var height = Math.Min(outer.Height, work.Height);
        if (width != outer.Width || height != outer.Height)
        {
            window.AppWindow.Resize(new SizeInt32(width, height));
            outer = window.AppWindow.Size;
        }

        window.AppWindow.Move(new PointInt32(
            work.X + Math.Max(0, (work.Width - outer.Width) / 2),
            work.Y + Math.Max(0, (work.Height - outer.Height) / 2)));

        if (minWidthDip > 0 || minHeightDip > 0)
        {
            var manager = WindowManager.Get(window);
            if (minWidthDip > 0) manager.MinWidth = minWidthDip;
            if (minHeightDip > 0) manager.MinHeight = minHeightDip;
        }
    }

    /// <summary>A fixed-size window: no maximize, no resize (minimize stays so it can be set aside).</summary>
    public static void MakeFixedSize(Window window)
    {
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }
    }

    /// <summary>
    /// Shows the window and takes the foreground even when the app was not the active one (a tray app is rarely the active app).
    /// </summary>
    public static void ShowInForeground(Window window) => Editors.EditorWindows.ShowInForeground(window);
}
