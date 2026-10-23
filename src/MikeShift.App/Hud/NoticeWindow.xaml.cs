using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MikeShift.App.Chrome;
using MikeShift.Core.Feedback;
using Windows.Graphics;
using Windows.System;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace MikeShift.App.Hud;

/// <summary>
/// A Fluent failure/warning dialog (UX review D-01 to D-03): a real window laid out like a ContentDialog (severity icon, heading,
/// message, list, footer, button bar). Not modal and not blocking: the job runner shows it and carries on. It is topmost and is shown
/// without taking focus (so a keystroke meant for Explorer never lands on a button) unless the user has been idle for a moment.
/// Esc closes; Enter presses the default (last, accent) button.
/// </summary>
public sealed partial class NoticeWindow : Window
{
    private const int ClientWidthDip = 480;
    private const int MaxClientHeightDip = 640;
    private const uint IdleActivateMs = 2500;
    private static int s_open;

    private readonly DialogContent _content;
    private readonly Action<DialogButton> _onCommand;
    private Button? _defaultButton;
    private bool _fanned;

    public NoticeWindow(DialogContent content, Action<DialogButton> onCommand)
    {
        _content = content;
        _onCommand = onCommand;
        InitializeComponent();
        WindowChrome.Apply(this, FeedbackCopy.AppName);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);

        Fill(content);
        s_open++;
        Closed += (_, _) => s_open--;
    }

    /// <summary>The dialog's buttons, left to right (tests and automation).</summary>
    internal IReadOnlyList<Button> Buttons => [.. ButtonBar.Children.OfType<Button>()];

    /// <summary>The window handle.</summary>
    internal nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// <summary>Shows the dialog without blocking. Takes the foreground only when the user has been idle for a moment.</summary>
    public void ShowDialog()
    {
        // Templated controls (buttons, the list) only have their real size once they are in a live tree, so the window is shown
        // cloaked, sized again from the first layout, and revealed afterwards: no flash at the wrong size.
        var hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this));
        Cloak(hwnd, true);
        PlaceOnScreen();
        var activate = UserIdleMilliseconds() >= IdleActivateMs;
        Root.Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            PlaceOnScreen();
            Cloak(hwnd, false);
            if (activate) _defaultButton?.Focus(FocusState.Programmatic);
        });
        AppWindow.Show(activate);
    }

    private static unsafe void Cloak(HWND hwnd, bool cloak)
    {
        uint value = cloak ? 1u : 0u;
        _ = PInvoke.DwmSetWindowAttribute(hwnd, Windows.Win32.Graphics.Dwm.DWMWINDOWATTRIBUTE.DWMWA_CLOAK, &value, sizeof(uint));
    }

    private void Fill(DialogContent c)
    {
        var error = c.Severity == DialogSeverity.Error;
        SeverityIcon.Glyph = error ? "" : ""; // ErrorBadge / Warning
        SeverityIcon.Foreground = (Brush)Application.Current.Resources[error ? "SystemFillColorCriticalBrush" : "SystemFillColorCautionBrush"];

        TitleText.Text = c.Title;
        if (!string.IsNullOrEmpty(c.Message))
        {
            MessageText.Text = c.Message;
            MessageText.Visibility = Visibility.Visible;
        }
        if (c.Items.Count > 0)
        {
            ItemList.ItemsSource = c.Items;
            ItemsScroll.Visibility = Visibility.Visible;
        }
        if (!string.IsNullOrEmpty(c.Footer))
        {
            FooterText.Text = c.Footer;
            FooterText.Visibility = Visibility.Visible;
        }

        for (var i = 0; i < c.Buttons.Count; i++)
        {
            var spec = c.Buttons[i];
            var button = new Button { Content = spec.Label, MinWidth = 108, Tag = spec };
            if (i == c.Buttons.Count - 1)
            {
                button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
                _defaultButton = button;
            }
            button.Click += (_, _) => Run(spec);
            ButtonBar.Children.Add(button);
        }

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Root, c.Title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetFullDescription(Root, c.PlainText);
    }

    private void Run(DialogButton button)
    {
        try { _onCommand(button); }
        finally { Close(); }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;
                Close();
                break;
            case VirtualKey.Enter when e.OriginalSource is not Button && _content.Buttons.Count > 0:
                e.Handled = true;
                Run(_content.Buttons[^1]);
                break;
        }
    }

    /// <summary>Measures the layout at the fixed width, sizes the client area to it and centres on the monitor under the cursor.</summary>
    private void PlaceOnScreen()
    {
        var grid = (FrameworkElement)Content;
        grid.Measure(new Windows.Foundation.Size(ClientWidthDip, double.PositiveInfinity));
        var height = (int)Math.Ceiling(Math.Min(grid.DesiredSize.Height, MaxClientHeightDip));
        WindowChrome.PlaceCentered(this, ClientWidthDip, Math.Max(height, 160));

        // Several dialogs at once fan out instead of hiding each other.
        if (s_open > 1 && !_fanned)
        {
            _fanned = true;
            var offset = (s_open - 1) % 6 * 32;
            var p = AppWindow.Position;
            AppWindow.Move(new PointInt32(p.X + offset, p.Y + offset));
        }
    }

    private static unsafe uint UserIdleMilliseconds()
    {
        try
        {
            var info = new Windows.Win32.UI.Input.KeyboardAndMouse.LASTINPUTINFO { cbSize = (uint)sizeof(Windows.Win32.UI.Input.KeyboardAndMouse.LASTINPUTINFO) };
            if (!PInvoke.GetLastInputInfo(ref info)) return uint.MaxValue;
            return unchecked((uint)Environment.TickCount - info.dwTime);
        }
        catch (Exception)
        {
            return uint.MaxValue;
        }
    }
}
