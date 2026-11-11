using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MikeShift.Core.Editing;
using Windows.System;

namespace MikeShift.App.Editors;

/// <summary>One line of the Merge order list: its place, the file name and, when the files come from several folders, the folder.</summary>
public sealed class OrderRow : INotifyPropertyChanged
{
    private int _number;

    public OrderRow(string fullPath, bool showFolder)
    {
        FullPath = fullPath;
        Name = System.IO.Path.GetFileName(fullPath);
        Folder = System.IO.Path.GetDirectoryName(fullPath) ?? string.Empty;
        FolderVisibility = showFolder && Folder.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FullPath { get; }

    public string Name { get; }

    public string Folder { get; }

    public Visibility FolderVisibility { get; }

    /// <summary>Its place in the merged file, from 1.</summary>
    public int Number
    {
        get => _number;
        set
        {
            if (_number == value) return;
            _number = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Number)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
        }
    }

    /// <summary>What a screen reader says for the row: "2. report.pdf".</summary>
    public string Description => $"{Number}. {Name}";
}

/// <summary>
/// The Merge order dialog: the selected files as a list to drag into the order they should be joined in (or Move up / Move down, or Alt+Up / Alt+Down),
/// Sort by name, Cancel and Merge. Esc cancels, Enter merges. The rules live in <see cref="FileOrderModel"/>; use <see cref="ShowAsync"/> on the UI thread.
/// </summary>
public sealed partial class MergeOrderDialog : Window
{
    private const int ClientWidth = 540;
    private const int ClientHeight = 460;

    private readonly FileOrderModel _model;
    private readonly ObservableCollection<OrderRow> _rows = [];
    private IReadOnlyList<string>? _result;

    private MergeOrderDialog(IReadOnlyList<string> paths)
    {
        InitializeComponent();
        EditorWindows.Setup(this, "Merge order", ClientWidth, ClientHeight, resizable: true, 420, 340);

        // The shortcuts are hooked on the outermost element, which also holds the title bar: a window that opens with focus on the title bar
        // (the first thing in the tab order) would otherwise ignore Enter, Esc and Alt+arrows until something inside was clicked.
        if (Content is UIElement outermost) outermost.PreviewKeyDown += OnPreviewKeyDown;

        _model = new FileOrderModel(paths);
        var showFolder = paths.Select(p => System.IO.Path.GetDirectoryName(p) ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        foreach (var path in paths) _rows.Add(new OrderRow(path, showFolder));
        FileList.ItemsSource = _rows;
        _model.Changed += SyncRows;

        InfoText.Text = "Drag the files into the order they should have in the merged file, or use Move up and Move down.";
        Renumber();
        FileList.SelectedIndex = _rows.Count > 0 ? 0 : -1;
        UpdateButtons();
        Activated += OnFirstActivated;
    }

    /// <summary>
    /// Opens the dialog for <paramref name="paths"/> and completes when it closes: the same files in the order chosen, or null when the user
    /// cancelled (Esc, Cancel, the close button) or <paramref name="ct"/> was cancelled. No file is opened or read. UI thread only.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> ShowAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (ct.IsCancellationRequested) return null;

        var window = new MergeOrderDialog(paths);
        var done = new TaskCompletionSource<IReadOnlyList<string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => done.TrySetResult(window._result);
        var queue = window.DispatcherQueue;
        using var registration = ct.Register(() => queue.TryEnqueue(window.Close));
        EditorWindows.ShowInForeground(window);
        return await done.Task;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated) return;
        Activated -= OnFirstActivated;

        // Focus the list once the layout has run; asked for at once, the title bar keeps the focus.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (FileList.ContainerFromIndex(Math.Max(0, FileList.SelectedIndex)) is Control row) row.Focus(FocusState.Programmatic);
            else FileList.Focus(FocusState.Programmatic);
        });
    }

    // ---- Model -> list ----

    /// <summary>Makes the list show the model's order (moving rows, so the selection stays on its file), then numbers it.</summary>
    private void SyncRows()
    {
        var selected = (FileList.SelectedItem as OrderRow)?.FullPath;

        for (var i = 0; i < _model.Count; i++)
        {
            var at = IndexOfPath(_model.Paths[i], from: i);
            if (at > i) _rows.Move(at, i);
        }

        Renumber();
        if (selected is not null && _rows.FirstOrDefault(r => string.Equals(r.FullPath, selected, StringComparison.Ordinal)) is { } row)
        {
            FileList.SelectedItem = row;
            FileList.ScrollIntoView(row);
        }
        UpdateButtons();
    }

    private int IndexOfPath(string path, int from)
    {
        for (var i = from; i < _rows.Count; i++)
            if (string.Equals(_rows[i].FullPath, path, StringComparison.Ordinal)) return i;
        return -1;
    }

    private void Renumber()
    {
        for (var i = 0; i < _rows.Count; i++) _rows[i].Number = i + 1;
    }

    private void UpdateButtons()
    {
        var at = FileList.SelectedIndex;
        UpButton.IsEnabled = _model.CanMoveUp(at);
        DownButton.IsEnabled = _model.CanMoveDown(at);
        SortButton.IsEnabled = _model.Count > 1;
    }

    // ---- List -> model ----

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    /// <summary>The list reordered itself while an item was dragged: hand its new order to the model.</summary>
    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _model.SetOrder([.. _rows.Select(r => r.FullPath)]);
        Renumber();
        UpdateButtons();
    }

    // ---- Buttons and keys ----

    private void OnUpClicked(object sender, RoutedEventArgs e)
    {
        MoveSelected(-1);
        if (!UpButton.IsEnabled) FileList.Focus(FocusState.Programmatic);
    }

    private void OnDownClicked(object sender, RoutedEventArgs e)
    {
        MoveSelected(1);
        if (!DownButton.IsEnabled) FileList.Focus(FocusState.Programmatic);
    }

    private void OnSortClicked(object sender, RoutedEventArgs e) => _model.SortByName();

    private void MoveSelected(int by)
    {
        var at = FileList.SelectedIndex;
        if (at >= 0) _model.Move(at, at + by);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Close();

    private void OnMergeClicked(object sender, RoutedEventArgs e) => Merge();

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;
                Close();
                break;

            case VirtualKey.Up or VirtualKey.Down when IsAltDown():
                e.Handled = true;
                MoveSelected(e.Key == VirtualKey.Up ? -1 : 1);
                break;

            case VirtualKey.Enter when e.OriginalSource is not Button:
                e.Handled = true;
                Merge();
                break;
        }
    }

    private static bool IsAltDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>Closes with the order as it is now.</summary>
    private void Merge()
    {
        _result = [.. _model.Paths];
        Close();
    }
}
