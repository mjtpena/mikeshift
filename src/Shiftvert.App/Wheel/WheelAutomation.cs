using System.Runtime.InteropServices;
using Shiftvert.Core.Interaction;
using Windows.Win32.Foundation;

namespace Shiftvert.App.Wheel;

// ---------------------------------------------------------------------------------------------------------------------
// UI Automation provider interfaces (UIAutomationCore.h), declared by hand: classic COM interop, no extra packages.
// ---------------------------------------------------------------------------------------------------------------------

[Flags]
internal enum ProviderOptions
{
    ClientSideProvider = 0x1,
    ServerSideProvider = 0x2,
    NonClientAreaProvider = 0x4,
    OverrideProvider = 0x8,
    ProviderOwnsSetFocus = 0x10,
    UseComThreading = 0x20,
}

internal enum NavigateDirection
{
    Parent = 0,
    NextSibling = 1,
    PreviousSibling = 2,
    FirstChild = 3,
    LastChild = 4,
}

[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
}

[ComImport, Guid("D6DD68D1-86FD-4332-8666-9ABEDEA2D24C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderSimple
{
    ProviderOptions ProviderOptions { get; }

    [return: MarshalAs(UnmanagedType.IUnknown)]
    object? GetPatternProvider(int patternId);

    object? GetPropertyValue(int propertyId);

    IRawElementProviderSimple? HostRawElementProvider { get; }
}

[ComImport, Guid("F7063DA8-8359-439C-9297-BBC5299A7D87"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderFragment
{
    [return: MarshalAs(UnmanagedType.Interface)]
    IRawElementProviderFragment? Navigate(NavigateDirection direction);

    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)]
    int[]? GetRuntimeId();

    UiaRect BoundingRectangle { get; }

    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UNKNOWN)]
    object[]? GetEmbeddedFragmentRoots();

    void SetFocus();

    IRawElementProviderFragmentRoot? FragmentRoot { get; }
}

[ComImport, Guid("620CE2A5-AB8F-40A9-86CB-DE3C75599B58"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderFragmentRoot
{
    [return: MarshalAs(UnmanagedType.Interface)]
    IRawElementProviderFragment? ElementProviderFromPoint(double x, double y);

    [return: MarshalAs(UnmanagedType.Interface)]
    IRawElementProviderFragment? GetFocus();
}

[ComImport, Guid("54FCB24B-E18E-47A2-B4D3-ECCBE77599A2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInvokeProvider
{
    void Invoke();
}

internal static class UiaNative
{
    public const int UiaRootObjectId = -25;
    public const int UIA_AutomationFocusChangedEventId = 20005;

    // Property ids
    public const int BoundingRectangle = 30001, ProcessId = 30002, ControlType = 30003, LocalizedControlType = 30004, Name = 30005,
        HasKeyboardFocus = 30008, IsKeyboardFocusable = 30009, IsEnabled = 30010, AutomationId = 30011, ClassName = 30012,
        IsControlElement = 30016, IsContentElement = 30017, NativeWindowHandle = 30020, IsOffscreen = 30022, FrameworkId = 30024;

    // Control types
    public const int ListItem = 50007, List = 50008;

    // Pattern ids
    public const int InvokePattern = 10000;

    public const int AppendRuntimeId = 0x2A; // UiaAppendRuntimeId

    [DllImport("uiautomationcore.dll")]
    public static extern IntPtr UiaReturnRawElementProvider(IntPtr hwnd, IntPtr wParam, IntPtr lParam, IRawElementProviderSimple? provider);

    [DllImport("uiautomationcore.dll")]
    public static extern int UiaHostProviderFromHwnd(IntPtr hwnd, out IRawElementProviderSimple provider);

    [DllImport("uiautomationcore.dll")]
    public static extern int UiaRaiseAutomationEvent(IRawElementProviderSimple provider, int eventId);

    [DllImport("uiautomationcore.dll", CharSet = CharSet.Unicode)]
    public static extern int UiaRaiseNotificationEvent(
        IRawElementProviderSimple provider, int notificationKind, int notificationProcessing,
        [MarshalAs(UnmanagedType.BStr)] string displayString, [MarshalAs(UnmanagedType.BStr)] string activityId);

    [DllImport("uiautomationcore.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UiaClientsAreListening();

    public const int NotificationKindOther = 4;
    public const int NotificationProcessingImportantMostRecent = 1;
}

/// <summary>
/// UI Automation for the wheel (PLAN §3.2): the window is a <c>List</c> whose tiles are <c>ListItem</c>s named like their drop description
/// ("Convert to PNG", "Compress"); the highlighted tile is the focused element in click mode, and every highlight change raises a focus
/// event and a notification so Narrator announces it. Providers read the immutable <see cref="WheelSnapshot"/>, so UIA threads never touch
/// wheel-thread state. Tiles support <c>Invoke</c> (picks the tile in click mode).
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
        Announce($"Shiftvert wheel, {Snapshot.Titles.Count} options. Arrow keys move, Enter chooses, Escape cancels.");
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

    private void Announce(string text)
    {
        try
        {
            if (!UiaNative.UiaClientsAreListening()) return;
            UiaNative.UiaRaiseNotificationEvent(_root, UiaNative.NotificationKindOther, UiaNative.NotificationProcessingImportantMostRecent, text, "ShiftvertWheel");
        }
        catch (Exception ex)
        {
            WheelLog.Error("UIA notification failed", ex);
        }
    }

    internal void InvokeTile(int index) => _controller.InvokeTile(index);

    private static UiaRect Rect(double left, double top, double width, double height) => new() { Left = left, Top = top, Width = width, Height = height };

    // ------------------------------------------------------------------ root

    [ComVisible(true)]
    private sealed class RootProvider : IRawElementProviderSimple, IRawElementProviderFragment, IRawElementProviderFragmentRoot
    {
        private readonly WheelAutomation _owner;

        public RootProvider(WheelAutomation owner) => _owner = owner;

        public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider;

        public object? GetPatternProvider(int patternId) => null;

        public object? GetPropertyValue(int propertyId)
        {
            var s = _owner.Snapshot;
            return propertyId switch
            {
                UiaNative.ControlType => UiaNative.List,
                UiaNative.Name => s.Visible ? $"Shiftvert wheel: {(s.Page == Shiftvert.Core.Actions.WheelPage.Format ? "convert to" : "tools")}" : "Shiftvert wheel",
                UiaNative.ClassName => WheelWindow.ClassName,
                UiaNative.AutomationId => "ShiftvertWheel",
                UiaNative.FrameworkId => "Shiftvert",
                UiaNative.IsEnabled => true,
                UiaNative.IsControlElement => true,
                UiaNative.IsContentElement => true,
                UiaNative.IsKeyboardFocusable => s.ClickMode,
                UiaNative.HasKeyboardFocus => false,
                UiaNative.IsOffscreen => !s.Visible,
                UiaNative.ProcessId => Environment.ProcessId,
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
            var index = Shiftvert.Core.Interaction.WheelGeometry.HitTest(x, y, centre.X, centre.Y, s.Titles.Count, s.DpiScale);
            return index is { } i ? new TileProvider(_owner, i, s) : null;
        }

        public IRawElementProviderFragment? GetFocus()
        {
            var s = _owner.Snapshot;
            return s.ClickMode && s.Highlighted is { } i && i < s.Titles.Count ? new TileProvider(_owner, i, s) : null;
        }
    }

    // ------------------------------------------------------------------ tiles

    [ComVisible(true)]
    private sealed class TileProvider : IRawElementProviderSimple, IRawElementProviderFragment, IInvokeProvider
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

        public object? GetPatternProvider(int patternId) => patternId == UiaNative.InvokePattern && _snapshot.ClickMode ? this : null;

        public object? GetPropertyValue(int propertyId) => propertyId switch
        {
            UiaNative.ControlType => UiaNative.ListItem,
            UiaNative.Name => _snapshot.Titles[_index],
            UiaNative.AutomationId => $"WheelTile{_index}",
            UiaNative.ClassName => "ShiftvertWheelTile",
            UiaNative.FrameworkId => "Shiftvert",
            UiaNative.IsEnabled => true,
            UiaNative.IsControlElement => true,
            UiaNative.IsContentElement => true,
            UiaNative.IsKeyboardFocusable => _snapshot.ClickMode,
            UiaNative.HasKeyboardFocus => _snapshot.ClickMode && _snapshot.Highlighted == _index,
            UiaNative.IsOffscreen => !_snapshot.Visible,
            UiaNative.ProcessId => Environment.ProcessId,
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
            [UiaNative.AppendRuntimeId, (_snapshot.Page == Shiftvert.Core.Actions.WheelPage.Tool ? 1000 : 0) + _index + 1];

        public UiaRect BoundingRectangle
        {
            get
            {
                var n = _snapshot.Titles.Count;
                var centre = _snapshot.Rect.Centre;
                var label = n == 1 ? new PointD(0, -WheelGeometry.LabelRadius) : WheelGeometry.LabelCentre(_index, n);
                var scale = _snapshot.DpiScale;
                var w = WheelGeometry.LabelWidth * scale;
                var h = WheelGeometry.LabelHeight * scale;
                return Rect(centre.X + label.X * scale - w / 2, centre.Y + label.Y * scale - h / 2, w, h);
            }
        }

        public object[]? GetEmbeddedFragmentRoots() => null;

        public void SetFocus() => _owner.InvokeHighlight(_index);

        public IRawElementProviderFragmentRoot? FragmentRoot => _owner._root;

        public void Invoke() => _owner.InvokeTile(_index);
    }

    internal void InvokeHighlight(int index) => _controller.HighlightTile(index);
}
