using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MikeShift.Core.Actions;
using MikeShift.Core.Editing;
using Windows.Storage;
using Windows.System;

namespace MikeShift.App.Editors;

/// <summary>
/// The trim dialog (PLAN §3.5): start/end <c>m:ss.f</c> fields, a two-thumb range slider over a strip of frames, the
/// selected length and Cancel/Trim. Esc cancels, Enter trims. Use <see cref="ShowAsync"/> on the UI thread.
/// </summary>
public sealed partial class TrimDialog : Window
{
    private const int ClientWidth = 640;
    private const int ClientHeight = 268;
    private const int ThumbnailCount = 8;

    private readonly TimeRangeModel? _model;
    private readonly RangeSlider? _slider;
    private readonly CancellationTokenSource _closed = new();
    private TimeRange? _result;

    private TrimDialog(string path, StorageFile? file, TimeSpan duration, string? error)
    {
        InitializeComponent();
        EditorWindows.Setup(this, $"Trim — {Path.GetFileName(path)}", ClientWidth, ClientHeight);
        Closed += (_, _) => _closed.Cancel();

        if (file is null || duration < TimeRangeModel.MinLength)
        {
            ErrorText.Text = error ?? "MikeShift could not read the length of this file.";
            ErrorText.Visibility = Visibility.Visible;
            InfoText.Text = string.Empty;
            TrimButton.IsEnabled = false;
            StartBox.IsEnabled = false;
            EndBox.IsEnabled = false;
            LengthText.Text = string.Empty;
            TotalText.Text = string.Empty;
            return;
        }

        _model = new TimeRangeModel(duration);
        _slider = new RangeSlider();
        _slider.Attach(_model);
        SliderHost.Children.Add(_slider);

        InfoText.Text = "Drag the handles, or type the times.";
        _model.Changed += SyncTexts;
        SyncTexts();
        Activated += OnFirstActivated;
        _ = LoadThumbnailsAsync(file, duration);
    }

    /// <summary>
    /// Opens the dialog for the media file at <paramref name="path"/> and completes when it closes: the range, or null when
    /// the user cancelled (Esc, Cancel, the close button) or <paramref name="ct"/> was cancelled. UI thread only.
    /// </summary>
    public static async Task<TimeRange?> ShowAsync(string path, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return null;

        StorageFile? file = null;
        var duration = TimeSpan.Zero;
        string? error = null;
        try
        {
            file = await StorageFile.GetFileFromPathAsync(path);
            duration = await MediaProbe.GetDurationAsync(file);
        }
        catch (Exception ex)
        {
            error = $"This file could not be opened: {ex.Message}";
        }

        if (ct.IsCancellationRequested) return null;

        var window = new TrimDialog(path, file, duration, error);
        var done = new TaskCompletionSource<TimeRange?>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => done.TrySetResult(window._result);
        var queue = window.DispatcherQueue;
        using var registration = ct.Register(() => queue.TryEnqueue(window.Close));
        EditorWindows.ShowInForeground(window);
        return await done.Task;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        StartBox.Focus(FocusState.Programmatic);
    }

    private async Task LoadThumbnailsAsync(StorageFile file, TimeSpan duration)
    {
        try
        {
            var frames = await MediaProbe.GetThumbnailsAsync(file, duration, ThumbnailCount, _closed.Token);
            if (_closed.IsCancellationRequested || _slider is null) return;
            _slider.SetThumbnails(frames);
            if (frames.Count == 0)
                InfoText.Text = "Drag the handles, or type the times. (No preview frames for this file.)";
        }
        catch (OperationCanceledException)
        {
            // dialog closed meanwhile
        }
    }

    // ---- Model -> controls ----

    private void SyncTexts()
    {
        if (_model is null) return;
        // Do not overwrite what the user is typing in the focused box.
        if (!StartBox.FocusState.IsFocused()) StartBox.Text = TimeRangeModel.Format(_model.Start);
        if (!EndBox.FocusState.IsFocused()) EndBox.Text = TimeRangeModel.Format(_model.End);
        LengthText.Text = $"Selected {TimeRangeModel.Format(_model.Length)}";
        TotalText.Text = $"of {TimeRangeModel.Format(_model.Duration)}";
    }

    // ---- Text boxes ----

    private bool CommitStart()
    {
        if (_model is null) return false;
        var ok = _model.TrySetStartText(StartBox.Text);
        StartBox.Text = TimeRangeModel.Format(_model.Start);
        return ok;
    }

    private bool CommitEnd()
    {
        if (_model is null) return false;
        var ok = _model.TrySetEndText(EndBox.Text);
        EndBox.Text = TimeRangeModel.Format(_model.End);
        return ok;
    }

    private void OnStartLostFocus(object sender, RoutedEventArgs e) => CommitStart();

    private void OnEndLostFocus(object sender, RoutedEventArgs e) => CommitEnd();

    // ---- Buttons and keys ----

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Close();

    private void OnTrimClicked(object sender, RoutedEventArgs e) => Trim();

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
                Trim();
                break;
        }
    }

    /// <summary>Commits pending text, then closes with the range; beeps and stays open if a field is not a valid time.</summary>
    private void Trim()
    {
        if (_model is null)
        {
            EditorWindows.Beep();
            return;
        }

        var startOk = CommitStart();
        var endOk = CommitEnd();
        if (!startOk || !endOk || _model.ToRange() is not { } range)
        {
            EditorWindows.Beep();
            return;
        }

        _result = range;
        Close();
    }
}

internal static class FocusStateExtensions
{
    public static bool IsFocused(this FocusState state) => state != FocusState.Unfocused;
}
