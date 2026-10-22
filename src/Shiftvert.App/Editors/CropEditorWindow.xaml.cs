using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Shiftvert.Core.Actions;
using Shiftvert.Core.Editing;
using Windows.System;

namespace Shiftvert.App.Editors;

/// <summary>
/// The crop editor (PLAN §3.5): a ~640 px floating window with the picture, a draggable crop box, aspect presets, a scale
/// slider and output W/H boxes. Esc cancels, Enter saves. Create and use on the UI thread via <see cref="ShowAsync"/>.
/// </summary>
public sealed partial class CropEditorWindow : Window
{
    private const int ClientWidth = 640;
    private const int ClientHeight = 640;
    private const double MaxSurfaceWidth = 592;
    private const double MaxSurfaceHeight = 358;

    private readonly CropBoxModel? _model;
    private bool _syncing;
    private CropChoice? _result;

    private CropEditorWindow(string path, PreviewImage? preview, string? error)
    {
        InitializeComponent();
        EditorWindows.Setup(this, $"Crop — {Path.GetFileName(path)}", ClientWidth, ClientHeight);

        if (preview is null)
        {
            ErrorText.Text = error ?? "This image could not be opened.";
            ErrorText.Visibility = Visibility.Visible;
            SaveButton.IsEnabled = false;
            foreach (var control in new Control[] { ScaleSlider, WidthBox, HeightBox, ResetButton }) control.IsEnabled = false;
            foreach (ToggleButton b in PresetButtons.Children) b.IsEnabled = false;
            SelectionText.Text = string.Empty;
            return;
        }

        _model = new CropBoxModel(preview.ImageWidth, preview.ImageHeight);

        // Fit the picture into the preview area (small images may be magnified up to 2x).
        var fit = Math.Min(2.0, Math.Min(MaxSurfaceWidth / preview.ImageWidth, MaxSurfaceHeight / preview.ImageHeight));
        var surface = new CropSurface();
        surface.Attach(preview.Source, _model, Math.Max(1, Math.Round(preview.ImageWidth * fit)), Math.Max(1, Math.Round(preview.ImageHeight * fit)));
        SurfaceHost.Children.Add(surface);
        Surface = surface;

        _model.Changed += SyncControls;
        SyncControls();
        Activated += OnFirstActivated;
    }

    private CropSurface? Surface { get; }

    /// <summary>
    /// Opens the editor for the image at <paramref name="path"/> and completes when it closes: the crop, or null when the
    /// user cancelled (Esc, Cancel, the close button) or <paramref name="ct"/> was cancelled. UI thread only.
    /// </summary>
    public static async Task<CropChoice?> ShowAsync(string path, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return null;

        PreviewImage? preview = null;
        string? error = null;
        try
        {
            preview = await PreviewLoader.LoadAsync(path);
        }
        catch (Exception ex)
        {
            error = $"This image could not be opened: {ex.Message}";
        }

        if (ct.IsCancellationRequested) return null;

        var window = new CropEditorWindow(path, preview, error);
        var done = new TaskCompletionSource<CropChoice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => done.TrySetResult(window._result);
        var queue = window.DispatcherQueue;
        using var registration = ct.Register(() => queue.TryEnqueue(window.Close));
        EditorWindows.ShowInForeground(window);
        return await done.Task;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        Surface?.Focus(FocusState.Programmatic);
    }

    // ---- Model -> controls ----

    private void SyncControls()
    {
        if (_model is null) return;
        _syncing = true;
        try
        {
            ScaleSlider.Value = _model.SliderPercent;
            WidthBox.Value = _model.OutputWidth;
            HeightBox.Value = _model.OutputHeight;
            var percent = _model.ScalePercent;
            PercentText.Text = percent >= 10 || percent == Math.Floor(percent)
                ? string.Create(CultureInfo.CurrentCulture, $"{Math.Round(percent):0} %")
                : string.Create(CultureInfo.CurrentCulture, $"{percent:0.0} %");
            var r = _model.Rect;
            SelectionText.Text = $"Selection {r.W} × {r.H} px at ({r.X}, {r.Y})  →  output {_model.OutputWidth} × {_model.OutputHeight} px";

            foreach (ToggleButton b in PresetButtons.Children)
                b.IsChecked = string.Equals((string)b.Tag, _model.Aspect.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            _syncing = false;
        }
    }

    // ---- Controls -> model ----

    private void OnPresetClicked(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        if (sender is ToggleButton { Tag: string tag } && Enum.TryParse<CropAspect>(tag, out var aspect))
            _model.SetAspect(aspect);
        SyncControls(); // a clicked toggle flips itself: show the model's truth (one selected, never none)
    }

    private void OnScaleChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncing || _model is null) return;
        _model.SetScalePercent(e.NewValue);
    }

    private void OnWidthChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing || _model is null) return;
        if (!_model.TrySetOutputWidth(args.NewValue)) SyncControls(); // out of range or empty: show the real value again
    }

    private void OnHeightChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing || _model is null) return;
        if (!_model.TrySetOutputHeight(args.NewValue)) SyncControls();
    }

    private void OnResetClicked(object sender, RoutedEventArgs e)
    {
        _model?.Reset();
        Surface?.Focus(FocusState.Programmatic);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Close();

    private void OnSaveClicked(object sender, RoutedEventArgs e) => Save();

    // ---- Keys ----

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;
                Close();
                break;

            case VirtualKey.Enter when e.OriginalSource is not Button:
                e.Handled = true;
                Save();
                break;
        }
    }

    /// <summary>Commits pending NumberBox text, then closes with the crop; beeps and stays open when it is not valid.</summary>
    private void Save()
    {
        if (_model is null || !CommitPendingText() || _model.ToChoice() is not { } choice)
        {
            EditorWindows.Beep();
            return;
        }

        _result = choice;
        Close();
    }

    /// <summary>The TextBox inside a NumberBox's template: the only place the not-yet-committed text lives.</summary>
    private static TextBox? InnerTextBox(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox tb) return tb;
            if (InnerTextBox(child) is { } found) return found;
        }

        return null;
    }

    /// <summary>
    /// A NumberBox only applies typed text on Enter/blur, and Enter reaches us first: read the text ourselves.
    /// False if a box holds something that is not a valid size.
    /// </summary>
    private bool CommitPendingText()
    {
        if (_model is null) return false;
        var ok = Commit(WidthBox, _model.OutputWidth, _model.TrySetOutputWidth);
        ok = Commit(HeightBox, _model.OutputHeight, _model.TrySetOutputHeight) && ok;
        return ok;

        bool Commit(NumberBox box, int current, Func<double, bool> set)
        {
            var text = (InnerTextBox(box)?.Text ?? box.Text)?.Trim();
            if (string.IsNullOrEmpty(text)) return false;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v)
                && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return false;
            return Math.Round(v) == current || set(v); // equal = nothing pending
        }
    }
}
