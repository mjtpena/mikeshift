using System.Runtime.InteropServices;
using MikeShift.Core.Interaction;
using Windows.Win32.Foundation;

namespace MikeShift.App.Wheel;

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

[ComImport, Guid("FB8B03AF-3BDF-48D4-BD36-1A65793BE168"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISelectionProvider
{
    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UNKNOWN)]
    object[] GetSelection();

    bool CanSelectMultiple { [return: MarshalAs(UnmanagedType.Bool)] get; }

    bool IsSelectionRequired { [return: MarshalAs(UnmanagedType.Bool)] get; }
}

[ComImport, Guid("2ACAD808-B2D4-452D-A407-91FF1AD167B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISelectionItemProvider
{
    void Select();

    void AddToSelection();

    void RemoveFromSelection();

    bool IsSelected { [return: MarshalAs(UnmanagedType.Bool)] get; }

    IRawElementProviderSimple? SelectionContainer { get; }
}

internal static class UiaNative
{
    public const int UiaRootObjectId = -25;
    public const int UIA_AutomationFocusChangedEventId = 20005;

    // Property ids
    public const int BoundingRectangle = 30001, ProcessId = 30002, ControlType = 30003, LocalizedControlType = 30004, Name = 30005,
        HasKeyboardFocus = 30008, IsKeyboardFocusable = 30009, IsEnabled = 30010, AutomationId = 30011, ClassName = 30012,
        IsControlElement = 30016, IsContentElement = 30017, NativeWindowHandle = 30020, IsOffscreen = 30022, FrameworkId = 30024;

    // Further properties: help text, full description, set position, and the Selection / SelectionItem pattern properties
    public const int HelpText = 30013, SelectionSelection = 30059, SelectionCanSelectMultiple = 30060, SelectionIsSelectionRequired = 30061,
        SelectionItemIsSelected = 30079, SelectionItemSelectionContainer = 30080, PositionInSet = 30152, SizeOfSet = 30153, FullDescription = 30159;

    // Control types
    public const int ListItem = 50007, List = 50008;

    // Pattern ids
    public const int InvokePattern = 10000, SelectionPattern = 10001, SelectionItemPattern = 10010;

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

