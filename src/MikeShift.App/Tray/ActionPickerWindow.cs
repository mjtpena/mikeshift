using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MikeShift.Core.Actions;
using WinUIEx;

namespace MikeShift.App.Tray;

/// <summary>
/// TEMPORARY action picker (W2f): shown by Convert Files… when nothing handles
/// <see cref="TrayController.ConvertFilesRequested"/>. Lists every action for the selection as a button so the whole
/// pipeline (catalog, job runner, toast, dialogs) is testable end to end. W3's wheel replaces it by attaching a handler.
/// </summary>
internal sealed class ActionPickerWindow : Window
{
    private ActionPickerWindow(AppServices services, IReadOnlyList<string> paths)
    {

        var actions = ActionCatalog.For(paths, services.Capabilities);

        var panel = new StackPanel { Spacing = 6, Padding = new Thickness(20) };
        var names = string.Join(", ", paths.Take(3).Select(Path.GetFileName));
        if (paths.Count > 3) names += $" and {paths.Count - 3} more";
        panel.Children.Add(new TextBlock
        {
            Text = paths.Count == 1 ? names : $"{paths.Count} items: {names}",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 0, 0, 8),
        });

        AddGroup(panel, "Convert to", actions.Where(a => a.Page == WheelPage.Format), services, paths);
        AddGroup(panel, "Tools", actions.Where(a => a.Page == WheelPage.Tool), services, paths);

        Content = new ScrollViewer { Content = panel };
        Chrome.WindowChrome.Apply(this, "Choose an action");
        Chrome.WindowChrome.PlaceCentered(this, 420, 592, minWidthDip: 360, minHeightDip: 320);
    }

    /// <summary>Shows the picker for <paramref name="paths"/>.</summary>
    public static void Show(AppServices services, IReadOnlyList<string> paths)
    {
        var window = new ActionPickerWindow(services, paths);
        window.Activate();
        WinUIEx.WindowExtensions.SetForegroundWindow(window);
    }

    private void AddGroup(StackPanel panel, string header, IEnumerable<ActionDef> actions, AppServices services, IReadOnlyList<string> paths)
    {
        var list = actions.ToList();
        if (list.Count == 0) return;

        panel.Children.Add(new TextBlock
        {
            Text = header,
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });

        foreach (var action in list)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            if (action.Glyph is { } glyph)
                row.Children.Add(new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16 });
            row.Children.Add(new TextBlock { Text = action.Title });

            var button = new Button
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, action.DropDescription);
            button.Click += (_, _) =>
            {
                Close();
                services.Jobs.Enqueue(action, paths);
            };
            panel.Children.Add(button);
        }
    }
}
