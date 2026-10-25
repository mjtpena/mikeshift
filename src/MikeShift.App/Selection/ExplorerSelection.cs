using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using MikeShift.App.Wheel;
using MikeShift.Core.Hotkeys;
using MikeShift.Core.Interaction;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using IShellServiceProvider = Windows.Win32.System.Com.IServiceProvider;

namespace MikeShift.App.Selection;

/// <summary>What the foreground window turned out to be.</summary>
internal enum ExplorerSelectionStatus
{
    /// <summary>The foreground window is not a File Explorer window or the desktop.</summary>
    NotExplorer,
    /// <summary>It is, but nothing file-system is selected (or the window could not be read).</summary>
    NoSelection,
    /// <summary>One or more files or folders are selected.</summary>
    Selected,
}

/// <summary>The selection of the foreground Explorer window and where it is on screen (physical px), for placing the wheel.</summary>
internal readonly record struct ExplorerSelectionResult(ExplorerSelectionStatus Status, IReadOnlyList<string> Paths, ScreenRect Window)
{
    public static ExplorerSelectionResult NotExplorer { get; } = new(ExplorerSelectionStatus.NotExplorer, [], default);
}

/// <summary>
/// <c>IShellWindows</c> (ExDisp.h): the collection of open Explorer windows. Hand-written because the generated one needs <c>VARIANT</c> arguments the
/// generator keeps private; <c>object</c> with <c>UnmanagedType.Struct</c> is the classic way to pass them. A dual interface, so the vtable starts
/// with IDispatch's seven slots; the methods before <see cref="FindWindowSW"/> are declared only to keep the slot order right.
/// </summary>
[ComImport]
[Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IShellWindowsCollection
{
    [PreserveSig] int get_Count(out int count);

    [PreserveSig] int Item([In, MarshalAs(UnmanagedType.Struct)] object index, [MarshalAs(UnmanagedType.IDispatch)] out object? folder);

    [PreserveSig] int _NewEnum([MarshalAs(UnmanagedType.IUnknown)] out object? enumerator);

    [PreserveSig] int Register([In, MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass, out int cookie);

    [PreserveSig] int RegisterPending(int threadId, [In, MarshalAs(UnmanagedType.Struct)] ref object loc, [In, MarshalAs(UnmanagedType.Struct)] ref object locRoot, int swClass, out int cookie);

    [PreserveSig] int Revoke(int cookie);

    [PreserveSig] int OnNavigate(int cookie, [In, MarshalAs(UnmanagedType.Struct)] ref object loc);

    [PreserveSig] int OnActivated(int cookie, [MarshalAs(UnmanagedType.VariantBool)] bool active);

    [PreserveSig]
    int FindWindowSW([In, MarshalAs(UnmanagedType.Struct)] ref object loc, [In, MarshalAs(UnmanagedType.Struct)] ref object locRoot,
        int swClass, out int hwnd, int swfwOptions, [MarshalAs(UnmanagedType.IDispatch)] out object? dispatch);
}

/// <summary>
/// The files selected in the foreground File Explorer window (or on the desktop), for the "convert the current selection" hotkey. The shell
/// exposes every open Explorer window through <c>IShellWindows</c>; the one whose frame is the foreground window (and, with tabs, whose tab
/// is the visible one) gives its <c>IShellBrowser</c> -> <c>IShellView</c> -> <c>IFolderView2.GetSelection</c>. The desktop is found with
/// <c>FindWindowSW(CSIDL_DESKTOP)</c>. Items without a file-system path (This PC, Recycle Bin, a phone) are left out.
/// <para>Classic COM interop (the generated shell interfaces and the one above are [ComImport] types in the trim-root assembly, kept whole by the
/// trimmer exactly like the drag code's). Every call here is made on the hotkey thread (an STA of its own), never on the hook or wheel thread: the
/// shell answers across a process boundary.</para>
/// </summary>
internal static unsafe class ExplorerSelection
{
    private static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid IidShellWindows = new("85CB6900-4D95-11CF-960C-0080C7F4EE85");
    private static readonly Guid IidShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    private const int SwcDesktop = 0x8;       // ShellWindowTypeConstants.SWC_DESKTOP
    private const int SwfoNeedDispatch = 0x1; // ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH

    /// <summary>Reads the selection of <paramref name="foreground"/> (call on an STA thread). Never throws.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2050", Justification = TrimJustification.ComInterop)]
    public static ExplorerSelectionResult ReadForeground(HWND foreground)
    {
        try
        {
            if (foreground == HWND.Null) return ExplorerSelectionResult.NotExplorer;
            var className = ClassNameOf(foreground);
            WheelLog.Stage($"explorer selection: foreground window 0x{(nint)foreground.Value:X} is {className}");
            if (!SelectionPaths.IsExplorerWindowClass(className)) return ExplorerSelectionResult.NotExplorer;

            var windows = CreateShellWindows();
            if (windows is null)
            {
                WheelLog.Stage("explorer selection: ShellWindows could not be created");
                return Failed(foreground);
            }

            try
            {
                var desktop = className is "Progman" or "WorkerW";
                var view = desktop ? DesktopView(windows) : ViewOfFrame(windows, foreground);
                if (view is null) return Failed(foreground);
                try
                {
                    var paths = SelectedPaths(view);
                    var rect = RectOf(foreground);
                    return paths.Count == 0
                        ? new ExplorerSelectionResult(ExplorerSelectionStatus.NoSelection, [], rect)
                        : new ExplorerSelectionResult(ExplorerSelectionStatus.Selected, paths, rect);
                }
                finally
                {
                    Marshal.ReleaseComObject(view);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(windows);
            }
        }
        catch (Exception ex)
        {
            WheelLog.Error("reading the Explorer selection failed", ex);
            return Failed(foreground);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid, out IntPtr ppv);

    private const uint ClsctxInprocServer = 0x1, ClsctxLocalServer = 0x4;

    /// <summary>
    /// <c>CLSID_ShellWindows</c>. The collection lives in explorer.exe (a local server), so unlike the in-process shell classes the wheel uses it
    /// needs <c>CLSCTX_LOCAL_SERVER</c>. Created with <c>CoCreateInstance</c> and wrapped by <c>Marshal.GetObjectForIUnknown</c>: nothing the trimmer cannot see.
    /// </summary>
    private static IShellWindowsCollection? CreateShellWindows()
    {
        if (CoCreateInstance(ClsidShellWindows, IntPtr.Zero, ClsctxLocalServer | ClsctxInprocServer, IidShellWindows, out var unknown) < 0 || unknown == IntPtr.Zero)
            return null;
        try { return Marshal.GetObjectForIUnknown(unknown) as IShellWindowsCollection; }
        finally { Marshal.Release(unknown); }
    }

    private static ExplorerSelectionResult Failed(HWND foreground) =>
        new(ExplorerSelectionStatus.NoSelection, [], RectOf(foreground));

    private static string ClassNameOf(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[64];
        fixed (char* p = buffer)
        {
            var n = PInvoke.GetClassName(hwnd, p, buffer.Length);
            return n > 0 ? new string(p, 0, n) : "";
        }
    }

    private static ScreenRect RectOf(HWND hwnd) =>
        PInvoke.GetWindowRect(hwnd, out var r) ? new ScreenRect(r.left, r.top, r.right, r.bottom) : default;

    /// <summary>The desktop's folder view: <c>FindWindowSW(CSIDL_DESKTOP, ..., SWC_DESKTOP, SWFO_NEEDDISPATCH)</c> -> IShellBrowser -> active view.</summary>
    private static IFolderView2? DesktopView(IShellWindowsCollection windows)
    {
        object location = 0; // CSIDL_DESKTOP as a VT_I4
        object root = Type.Missing; // VT_ERROR / DISP_E_PARAMNOTFOUND: no root
        object? dispatch = null;
        if (windows.FindWindowSW(ref location, ref root, SwcDesktop, out _, SwfoNeedDispatch, out dispatch) < 0 || dispatch is null) return null;
        try { return ViewOf(dispatch); }
        finally { Marshal.ReleaseComObject(dispatch); }
    }

    /// <summary>The folder view of the Explorer window whose frame is <paramref name="foreground"/>; with tabs, the tab that is showing.</summary>
    private static IFolderView2? ViewOfFrame(IShellWindowsCollection windows, HWND foreground)
    {
        if (windows.get_Count(out var count) < 0) return null;
        WheelLog.Stage($"explorer selection: {count} shell window(s), foreground frame 0x{(nint)foreground.Value:X}");
        IFolderView2? fallback = null;
        for (var i = 0; i < count; i++)
        {
            object? dispatch = null;
            try
            {
                if (windows.Item(i, out dispatch) < 0 || dispatch is null) continue;
                if (BrowserOf(dispatch) is not { } browser) continue;
                try
                {
                    browser.GetWindow(out var tab);
                    var root = PInvoke.GetAncestor(tab, GET_ANCESTOR_FLAGS.GA_ROOT);
                    WheelLog.Stage($"explorer selection: window {i}: tab 0x{(nint)tab.Value:X}, frame 0x{(nint)root.Value:X}, visible {PInvoke.IsWindowVisible(tab)}");
                    if (root != foreground) continue;
                    if (ViewOf(browser) is not { } view) continue;
                    // Every tab of a window shares the frame; only the visible tab is the one the user is looking at.
                    if (PInvoke.IsWindowVisible(tab))
                    {
                        if (fallback is not null) Marshal.ReleaseComObject(fallback);
                        return view;
                    }
                    if (fallback is null) fallback = view;
                    else Marshal.ReleaseComObject(view);
                }
                finally
                {
                    Marshal.ReleaseComObject(browser);
                }
            }
            catch (COMException)
            {
                // a window that went away between Count and Item
            }
            finally
            {
                if (dispatch is not null && Marshal.IsComObject(dispatch)) Marshal.ReleaseComObject(dispatch);
            }
        }
        return fallback;
    }

    /// <summary><c>IServiceProvider.QueryService(SID_STopLevelBrowser, IID_IShellBrowser)</c> on an Explorer window's dispatch object.</summary>
    private static IShellBrowser? BrowserOf(object dispatch)
    {
        if (dispatch is not IShellServiceProvider provider) return null;
        var service = PInvoke.SID_STopLevelBrowser;
        var iid = IidShellBrowser;
        void* raw;
        provider.QueryService(&service, &iid, out raw);
        if (raw is null) return null;
        try { return Marshal.GetObjectForIUnknown((IntPtr)raw) as IShellBrowser; }
        finally { Marshal.Release((IntPtr)raw); }
    }

    private static IFolderView2? ViewOf(object dispatch)
    {
        var browser = BrowserOf(dispatch);
        if (browser is null) return null;
        try { return ViewOf(browser); }
        finally { Marshal.ReleaseComObject(browser); }
    }

    private static IFolderView2? ViewOf(IShellBrowser browser)
    {
        browser.QueryActiveShellView(out var shellView);
        return shellView as IFolderView2;
    }

    /// <summary>The file-system paths of the view's selected items (<c>IFolderView2.GetSelection</c>), filtered by <see cref="SelectionPaths.Filter"/>.</summary>
    private static IReadOnlyList<string> SelectedPaths(IFolderView2 view)
    {
        IShellItemArray? array;
        try
        {
            view.GetSelection(false, out array);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070490)) // HRESULT_FROM_WIN32(ERROR_NOT_FOUND): nothing is selected
        {
            WheelLog.Stage("explorer selection: nothing is selected");
            return [];
        }
        if (array is null) return [];
        var reported = new List<string?>();
        try
        {
            array.GetCount(out var count);
            for (uint i = 0; i < Math.Min(count, 100_000u); i++)
            {
                array.GetItemAt(i, out var item);
                try
                {
                    item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var name);
                    try { reported.Add(name.ToString()); }
                    finally { Marshal.FreeCoTaskMem((IntPtr)name.Value); }
                }
                catch (COMException)
                {
                    // not a file-system item (This PC, the Recycle Bin...)
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(array);
        }
        var kept = SelectionPaths.Filter(reported, p => File.Exists(p) || Directory.Exists(p));
        WheelLog.Stage($"explorer selection: the view reported {reported.Count} item(s), {kept.Count} are files or folders");
        return kept;
    }
}
