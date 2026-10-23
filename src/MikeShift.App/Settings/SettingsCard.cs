using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MikeShift.App.Settings;

/// <summary>
/// A Windows 11 settings card (UX review S-01): an optional icon, a title and a description on the left and the control on the right,
/// on a 4 px rounded card surface. Hand-built (the template is the implicit style in <c>Styles/SettingsStyles.xaml</c>) so the app does not
/// need the CommunityToolkit settings controls, which are built for Windows App SDK 1.x. The control inside is the card's
/// <see cref="ContentControl.Content"/>.
/// </summary>
public sealed class SettingsCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty HeaderIconProperty =
        DependencyProperty.Register(nameof(HeaderIcon), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty, OnTextChanged));

    private TextBlock? _description;
    private FrameworkElement? _icon;

    public SettingsCard() => DefaultStyleKey = typeof(SettingsCard);

    /// <summary>The setting's name.</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>One line (or two) saying what it does or what its state is.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>A Segoe Fluent Icons glyph shown before the title, or empty.</summary>
    public string HeaderIcon
    {
        get => (string)GetValue(HeaderIconProperty);
        set => SetValue(HeaderIconProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _description = GetTemplateChild("DescriptionText") as TextBlock;
        _icon = GetTemplateChild("IconHost") as FrameworkElement;
        UpdateVisibility();
        UpdateName();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (SettingsCard)d;
        card.UpdateVisibility();
        card.UpdateName();
    }

    private void UpdateVisibility()
    {
        if (_description is not null) _description.Visibility = string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
        if (_icon is not null) _icon.Visibility = string.IsNullOrEmpty(HeaderIcon) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The card's accessible name is its title, and its help text its description.</summary>
    private void UpdateName()
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, Header);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(this, Description);
    }
}
