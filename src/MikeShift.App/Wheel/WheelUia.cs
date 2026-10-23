using System.Runtime.InteropServices;
using MikeShift.Core.Interaction;
using Windows.Win32.Foundation;

namespace MikeShift.App.Wheel;

/// <summary>
/// UI Automation for the wheel (PLAN §3.2, UX review W-15): the window is a <c>List</c> (Selection pattern: single selection, none required)
/// whose tiles are <c>ListItem</c>s (SelectionItem pattern: <c>IsSelected</c> = highlighted; Invoke picks in click mode) named exactly like the
/// shell's drop description ("Convert to PNG", "Compress"), with <c>PositionInSet</c>/<c>SizeOfSet</c>, <c>HelpText</c> and a
/// <c>FullDescription</c> sentence. The highlighted tile is the focused element in click mode and every highlight change raises a focus event or a
/// notification so Narrator announces it; a page change announces "Tools page, 5 items". Providers read the immutable <see cref="WheelSnapshot"/>,
/// so UIA threads never touch wheel-thread state.
/// </summary>
internal sealed class WheelAutomation
{
    private readonly WheelController _controller;
    private readonly RootProvider _root;
    private TileProvider? _focusedTile;

    public WheelAutomation(WheelController controller)
    {
        _controller = controller;
        _root = new RootProvider(this);
    }

    internal WheelSnapshot Snapshot => _controller.Snapshot;

    /// <summary>Handles <c>WM_GETOBJECT</c>; returns null for any object id other than the UIA root.</summary>
    public LRESULT? HandleGetObject(WPARAM wParam, LPARAM lParam)
    {
        if ((int)lParam.Value != UiaNative.UiaRootObjectId) return null;
        var result = UiaNative.UiaReturnRawElementProvider(_controller.Window.Hwnd, (IntPtr)wParam.Value, (IntPtr)lParam.Value, _root);
        return new LRESULT(result);
    }

    public void OnShown()
    {
        var s = Snapshot;
        Announce(WheelText.OpenAnnouncement(s.Titles.Count, s.HasBothPages));
    }

    public void OnHidden() => _focusedTile = null;

    /// <summary>Tells UI Automation the window's providers are going away (window destruction).</summary>
    public void Disconnect()
    {
        try { UiaNative.UiaReturnRawElementProvider(_controller.Window.Hwnd, IntPtr.Zero, IntPtr.Zero, null); }
        catch (Exception ex) { WheelLog.Error("UIA disconnect failed", ex); }
    }

    public void OnHighlightChanged()
    {
        try
        {
            if (!UiaNative.UiaClientsAreListening()) return;
            var snapshot = Snapshot;
            if (snapshot.Highlighted is not { } index || index >= snapshot.Titles.Count) return;

            var tile = new TileProvider(this, index, snapshot);
            _focusedTile = tile;
            if (_controller.Window.IsForeground)
                UiaNative.UiaRaiseAutomationEvent(tile, UiaNative.UIA_AutomationFocusChangedEventId);
            else
                Announce(snapshot.Titles[index]);
        }
        catch (Exception ex)
        {
            WheelLog.Error("UIA highlight notification failed", ex);
        }
    }

    /// <summary>The page changed (Formats ↔ Tools): focus moves back to the list and "Tools page, 5 items" is announced.</summary>
    public void OnPageChanged()
    {
        try
        {
            if (!UiaNative.UiaClientsAreListening()) return;
            var s = Snapshot;
            _focusedTile = null;
            if (s.ClickMode && _controller.Window.IsForeground)
                UiaNative.UiaRaiseAutomationEvent(_root, UiaNative.UIA_AutomationFocusChangedEventId);
            Announce(WheelText.PageAnnouncement(s.Page, s.Titles.Count));
        }
        catch (Exception ex)
        {
            WheelLog.Error("UIA page notification failed", ex);
        }
    }

    /// <summary>Raises a UIA notification (read out by Narrator) with <paramref name="text"/>.</summary>
    internal void Announce(string text)
    {
        try
        {
            if (!UiaNative.UiaClientsAreListening()) return;
            UiaNative.UiaRaiseNotificationEvent(_root, UiaNative.NotificationKindOther, UiaNative.NotificationProcessingImportantMostRecent, text, "MikeShiftWheel");
        }
        catch (Exception ex)
        {
            WheelLog.Error("UIA notification failed", ex);
        }
    }

    internal void InvokeTile(int index) => _controller.InvokeTile(index);

    internal void InvokeHighlight(int index) => _controller.HighlightTile(index);

    private static UiaRect Rect(double left, double top, double width, double height) => new() { Left = left, Top = top, Width = width, Height = height };

    // ------------------------------------------------------------------ root

    [ComVisible(true)]
    private sealed class RootProvider : IRawElementProviderSimple, IRawElementProviderFragment, IRawElementProviderFragmentRoot, ISelectionProvider
    {
        private readonly WheelAutomation _owner;

        public RootProvider(WheelAutomation owner) => _owner = owner;

        public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider;

        public object? GetPatternProvider(int patternId) => patternId == UiaNative.SelectionPattern ? this : null;

        public object? GetPropertyValue(int propertyId)
        {
            var s = _owner.Snapshot;
            return propertyId switch
            {
                UiaNative.ControlType => UiaNative.List,
                UiaNative.Name => WheelText.ListName(s.Visible, s.Page),
                UiaNative.HelpText => s.Visible ? WheelText.Help(s.ClickMode, s.HasBothPages, s.Page) : null,
                UiaNative.FullDescription => !s.Visible ? null : s.VirtualFiles ? WheelText.SaveFirst : WheelText.PageAnnouncement(s.Page, s.Titles.Count),
                UiaNative.ClassName => WheelWindow.ClassName,
                UiaNative.AutomationId => "MikeShiftWheel",
                UiaNative.FrameworkId => "MikeShift",
                UiaNative.IsEnabled => true,
                UiaNative.IsControlElement => true,
                UiaNative.IsContentElement => true,
                UiaNative.IsKeyboardFocusable => s.ClickMode,
                UiaNative.HasKeyboardFocus => false,
                UiaNative.IsOffscreen => !s.Visible,
                UiaNative.ProcessId => Environment.ProcessId,
                UiaNative.SelectionCanSelectMultiple => false,
                UiaNative.SelectionIsSelectionRequired => false,
                _ => null,
            };
        }

        public IRawElementProviderSimple? HostRawElementProvider
        {
            get
            {
                var hwnd = _owner._controller.Window.Hwnd;
                return UiaNative.UiaHostProviderFromHwnd(hwnd, out var host) == 0 ? host : null;
            }
        }

        public IRawElementProviderFragment? Navigate(NavigateDirection direction)
        {
            var s = _owner.Snapshot;
            return direction switch
            {
                NavigateDirection.FirstChild when s.Titles.Count > 0 => new TileProvider(_owner, 0, s),
                NavigateDirection.LastChild when s.Titles.Count > 0 => new TileProvider(_owner, s.Titles.Count - 1, s),
                _ => null,
            };
        }

        public int[]? GetRuntimeId() => null;   // the HWND host provides the root's runtime id

        public UiaRect BoundingRectangle
        {
            get
            {
                var s = _owner.Snapshot;
                return Rect(s.Rect.Left, s.Rect.Top, s.Rect.Width, s.Rect.Height);
            }
        }

        public object[]? GetEmbeddedFragmentRoots() => null;

        public void SetFocus() => _owner._controller.FocusWindow();

        public IRawElementProviderFragmentRoot? FragmentRoot => this;

        public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y)
        {
            var s = _owner.Snapshot;
            if (!s.Visible || s.Titles.Count == 0) return null;
            var centre = s.Rect.Centre;
            var index = WheelGeometry.HitTest(x, y, centre.X, centre.Y, s.Titles.Count, s.DpiScale);
            return index is { } i ? new TileProvider(_owner, i, s) : null;
        }

        public IRawElementProviderFragment? GetFocus()
        {
            var s = _owner.Snapshot;
            return s.ClickMode && s.Highlighted is { } i && i < s.Titles.Count ? new TileProvider(_owner, i, s) : null;
        }

        // Selection pattern: at most one tile (the highlighted one), none required.
        public object[] GetSelection()
        {
            var s = _owner.Snapshot;
            return s.Highlighted is { } i && i < s.Titles.Count ? [new TileProvider(_owner, i, s)] : [];
        }

        public bool CanSelectMultiple => false;

        public bool IsSelectionRequired => false;
    }

    // ------------------------------------------------------------------ tiles

    [ComVisible(true)]
    private sealed class TileProvider : IRawElementProviderSimple, IRawElementProviderFragment, IInvokeProvider, ISelectionItemProvider
    {
        private readonly WheelAutomation _owner;
        private readonly int _index;
        private readonly WheelSnapshot _snapshot;

        public TileProvider(WheelAutomation owner, int index, WheelSnapshot snapshot)
        {
            _owner = owner;
            _index = index;
            _snapshot = snapshot;
        }

        public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider;

        public object? GetPatternProvider(int patternId) => patternId switch
        {
            UiaNative.InvokePattern when _snapshot.ClickMode => this,
            UiaNative.SelectionItemPattern => this,
            _ => null,
        };

        public object? GetPropertyValue(int propertyId) => propertyId switch
        {
            UiaNative.ControlType => UiaNative.ListItem,
            UiaNative.Name => _snapshot.Titles[_index],
            UiaNative.HelpText => WheelText.TileHelp(_snapshot.ClickMode),
            UiaNative.FullDescription => _index < _snapshot.Descriptions.Count ? _snapshot.Descriptions[_index] : null,
            UiaNative.PositionInSet => _index + 1,
            UiaNative.SizeOfSet => _snapshot.Titles.Count,
            UiaNative.AutomationId => $"WheelTile{_index}",
            UiaNative.ClassName => "MikeShiftWheelTile",
            UiaNative.FrameworkId => "MikeShift",
            UiaNative.IsEnabled => true,
            UiaNative.IsControlElement => true,
            UiaNative.IsContentElement => true,
            UiaNative.IsKeyboardFocusable => _snapshot.ClickMode,
            UiaNative.HasKeyboardFocus => _snapshot.ClickMode && _snapshot.Highlighted == _index,
            UiaNative.IsOffscreen => !_snapshot.Visible,
            UiaNative.ProcessId => Environment.ProcessId,
            UiaNative.SelectionItemIsSelected => _snapshot.Highlighted == _index,
            _ => null,
        };

        public IRawElementProviderSimple? HostRawElementProvider => null;

        public IRawElementProviderFragment? Navigate(NavigateDirection direction)
        {
            var n = _snapshot.Titles.Count;
            return direction switch
            {
                NavigateDirection.Parent => _owner._root,
                NavigateDirection.NextSibling when _index + 1 < n => new TileProvider(_owner, _index + 1, _snapshot),
                NavigateDirection.PreviousSibling when _index > 0 => new TileProvider(_owner, _index - 1, _snapshot),
                _ => null,
            };
        }

        public int[]? GetRuntimeId() =>
            [UiaNative.AppendRuntimeId, (_snapshot.Page == MikeShift.Core.Actions.WheelPage.Tool ? 1000 : 0) + _index + 1];

        public UiaRect BoundingRectangle
        {
            get
            {
                var n = _snapshot.Titles.Count;
                var centre = _snapshot.Rect.Centre;
                var scale = _snapshot.DpiScale;
                if (n == 1)
                {
                    // A single tile is the hub itself.
                    var d = 2 * WheelGeometry.SingleHubRadius * scale;
                    return Rect(centre.X - d / 2, centre.Y - d / 2, d, d);
                }
                var label = WheelGeometry.LabelCentre(_index, n);
                var w = WheelGeometry.LabelWidth * scale;
                var h = WheelGeometry.LabelHeight * scale;
                return Rect(centre.X + label.X * scale - w / 2, centre.Y + label.Y * scale - h / 2, w, h);
            }
        }

        public object[]? GetEmbeddedFragmentRoots() => null;

        public void SetFocus() => _owner.InvokeHighlight(_index);

        public IRawElementProviderFragmentRoot? FragmentRoot => _owner._root;

        public void Invoke() => _owner.InvokeTile(_index);

        // SelectionItem pattern: selecting = highlighting (click mode); drag mode ignores it (the pointer owns the highlight).
        public void Select() => _owner.InvokeHighlight(_index);

        public void AddToSelection() => _owner.InvokeHighlight(_index);

        public void RemoveFromSelection()
        {
            if (_snapshot.Highlighted == _index) _owner.InvokeHighlight(-1);
        }

        public bool IsSelected => _snapshot.Highlighted == _index;

        public IRawElementProviderSimple? SelectionContainer => _owner._root;
    }
}
