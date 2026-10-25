using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using MikeShift.App.Chrome;
using MikeShift.Core.Feedback;
using MikeShift.Core.Hosting;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MikeShift.App.Tray;

/// <summary>
/// The Win32 <c>IFileOpenDialog</c> behind "Convert files…": multi-select, real file-system paths only.
/// <para>
/// It says what to do ("Choose files to convert", button "Choose"), offers a type filter (supported files, images, videos, audio, PDF and
/// documents, all files), starts in Pictures the first time and then wherever it was last used (a client GUID makes the shell remember
/// the folder), and opens on the monitor the user is working on: its owner is an invisible window over that monitor's work area, so
/// the dialog is centred there instead of on the primary monitor.
/// </para>
/// Limitation: the common item dialog cannot select files and folders in one go
/// (<c>FOS_PICKFOLDERS</c> turns files off), so Convert files… offers files only. Folders are converted/zipped by Shift-dragging them
/// in Explorer.
/// </summary>
internal static unsafe class FilePicker
{
    private const int HResultCancelled = unchecked((int)0x800704C7); // HRESULT_FROM_WIN32(ERROR_CANCELLED)

    /// <summary>Remembers the last folder of this dialog separately from every other open dialog on the PC.</summary>
    private static readonly Guid ClientGuid = new("5b0d1f3e-6c1a-4f43-9d62-3a2b0a8f77c1");

    /// <summary>Shows the dialog and returns the chosen file paths (empty if cancelled or on failure).</summary>
    public static IReadOnlyList<string> PickFiles(nint owner = 0)
    {
        var ownHelper = owner == 0;
        var helper = ownHelper ? CreateOwnerOnCursorMonitor() : HWND.Null;
        var specs = new List<nint>();
        try
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();
            dialog.GetOptions(out var options);
            dialog.SetOptions(options
                | FILEOPENDIALOGOPTIONS.FOS_ALLOWMULTISELECT
                | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM
                | FILEOPENDIALOGOPTIONS.FOS_PATHMUSTEXIST
                | FILEOPENDIALOGOPTIONS.FOS_FILEMUSTEXIST);
            dialog.SetTitle(FileTypeFilters.Title);
            dialog.SetOkButtonLabel(FileTypeFilters.OkLabel);
            dialog.SetClientGuid(ClientGuid);
            SetFileTypes(dialog, specs);
            SetStartFolder(dialog);

            // The common dialog ignores its owner for placement and opens at its remembered spot (the top-left of the primary monitor the
            // first time); the first OnFolderChange, fired while it is being built, moves it to the middle of the monitor in use.
            var events = new CentringEvents();
            uint cookie = 0;
            try { dialog.Advise(events, out cookie); }
            catch (COMException) { /* placement is cosmetic */ }

            try
            {
                dialog.Show(helper != HWND.Null ? helper : new HWND(owner));
            }
            catch (COMException ex) when (ex.HResult == HResultCancelled)
            {
                return [];
            }
            finally
            {
                if (cookie != 0) dialog.Unadvise(cookie);
            }

            dialog.GetResults(out var items);
            items.GetCount(out var count);
            var result = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                items.GetItemAt(i, out var item);
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var name);
                try
                {
                    var path = name.ToString();
                    if (!string.IsNullOrEmpty(path)) result.Add(path);
                }
                finally
                {
                    Marshal.FreeCoTaskMem((nint)name.Value);
                }
            }
            return result;
        }
        catch (Exception ex) when (!UnhandledPolicy.IsFatal(ex))
        {
            // Any failure of the common dialog (a shell extension that throws, a COM error) means "nothing picked", not a dead app.
            Wheel.WheelLog.Error("file picker failed", ex);
            return [];
        }
        finally
        {
            foreach (var p in specs) Marshal.FreeHGlobal(p);
            if (helper != HWND.Null) PInvoke.DestroyWindow(helper);
        }
    }

    /// <summary>Remembers the last folder of the "where converted files go" dialog separately from the file dialog above.</summary>
    private static readonly Guid FolderClientGuid = new("a3e8c2d1-4b7f-4e2a-8c35-6d1f0b9a7e42");

    /// <summary>
    /// Shows the common folder dialog ("Where converted files go", Settings) over <paramref name="owner"/> and returns the chosen file-system
    /// folder, or null if cancelled or on failure. Starts in <paramref name="startFolder"/> when it exists, else wherever Windows last
    /// opened it.
    /// </summary>
    public static string? PickFolder(nint owner, string? startFolder = null)
    {
        try
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();
            dialog.GetOptions(out var options);
            dialog.SetOptions(options
                | FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS
                | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM
                | FILEOPENDIALOGOPTIONS.FOS_PATHMUSTEXIST);
            dialog.SetTitle("Choose a folder for converted files");
            dialog.SetOkButtonLabel("Choose folder");
            dialog.SetClientGuid(FolderClientGuid);
            try
            {
                if (!string.IsNullOrEmpty(startFolder) && Directory.Exists(startFolder)) dialog.SetFolder(ItemFor(startFolder));
            }
            catch (Exception ex)
            {
                Wheel.WheelLog.Error("folder picker start folder", ex); // the dialog opens wherever Windows likes
            }

            try
            {
                dialog.Show(new HWND(owner));
            }
            catch (COMException ex) when (ex.HResult == HResultCancelled)
            {
                return null;
            }

            dialog.GetResult(out var item);
            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var name);
            try
            {
                var path = name.ToString();
                return string.IsNullOrEmpty(path) ? null : path;
            }
            finally
            {
                Marshal.FreeCoTaskMem((nint)name.Value);
            }
        }
        catch (Exception ex) when (!UnhandledPolicy.IsFatal(ex))
        {
            Wheel.WheelLog.Error("folder picker failed", ex);
            return null;
        }
    }

    private static void SetFileTypes(IFileOpenDialog dialog, List<nint> allocations)
    {
        var filters = FileTypeFilters.All;
        var native = new COMDLG_FILTERSPEC[filters.Count];
        for (var i = 0; i < filters.Count; i++)
        {
            var name = Marshal.StringToHGlobalUni(filters[i].Name);
            var spec = Marshal.StringToHGlobalUni(filters[i].Spec);
            allocations.Add(name);
            allocations.Add(spec);
            native[i] = new COMDLG_FILTERSPEC { pszName = (char*)name, pszSpec = (char*)spec };
        }
        fixed (COMDLG_FILTERSPEC* p = native) dialog.SetFileTypes((uint)native.Length, p);
        dialog.SetFileTypeIndex(1); // "Supported files"
    }

    /// <summary>Pictures the first time; the shell restores the last used folder afterwards (see <see cref="ClientGuid"/>).</summary>
    private static void SetStartFolder(IFileOpenDialog dialog)
    {
        try
        {
            var forced = DevSwitches.PickerFolder; // dev switch: screenshots must not show real folders
            if (!string.IsNullOrEmpty(forced) && Directory.Exists(forced))
            {
                dialog.SetFolder(ItemFor(forced));
                return;
            }

            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Directory.Exists(pictures))
            {
                dialog.SetDefaultFolder(ItemFor(pictures));
            }
        }
        catch (Exception ex)
        {
            // the dialog opens wherever Windows likes
            Wheel.WheelLog.Error("picker start folder", ex);
        }
    }

    /// <summary>
    /// Centres the dialog on the monitor under the cursor. It is built hidden at one size (OnFolderChange: moved to that monitor so it
    /// never shows up on another one) and then grown to its remembered size as it appears (first OnSelectionChange: centred for real).
    /// </summary>
    private sealed unsafe class CentringEvents : IFileDialogEvents
    {
        private bool _movedEarly;
        private bool _centred;

        private static void Centre(IFileDialog pfd)
        {
            try
            {
                var window = (Windows.Win32.System.Ole.IOleWindow)pfd;
                window.GetWindow(out var hwnd);
                if (hwnd == HWND.Null) return;
                PInvoke.GetWindowRect(hwnd, out var r);
                var work = WindowChrome.WorkAreaUnderCursor();
                var x = work.X + Math.Max(0, (work.Width - (r.right - r.left)) / 2);
                var y = work.Y + Math.Max(0, (work.Height - (r.bottom - r.top)) / 2);
                PInvoke.SetWindowPos(hwnd, HWND.Null, x, y, 0, 0,
                    SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
            }
            catch (Exception)
            {
                // placement is cosmetic
            }
        }

        public void OnFolderChange(IFileDialog pfd)
        {
            if (_movedEarly) return;
            _movedEarly = true;
            Centre(pfd);
        }

        public void OnSelectionChange(IFileDialog pfd)
        {
            if (_centred) return;
            _centred = true;
            Centre(pfd);
        }

        public void OnFileOk(IFileDialog pfd) { }
        public void OnFolderChanging(IFileDialog pfd, IShellItem psi) { }
        public void OnShareViolation(IFileDialog pfd, IShellItem psi, FDE_SHAREVIOLATION_RESPONSE* pResponse) { }
        public void OnTypeChange(IFileDialog pfd) { }
        public void OnOverwrite(IFileDialog pfd, IShellItem psi, FDE_OVERWRITE_RESPONSE* pResponse) { }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2050", Justification = TrimJustification.ComInterop)]
    private static IShellItem ItemFor(string path)
    {
        var iid = typeof(IShellItem).GUID;
        fixed (char* p = path)
        {
            PInvoke.SHCreateItemFromParsingName(p, null, &iid, out var item).ThrowOnFailure();
            return (IShellItem)item;
        }
    }

    /// <summary>
    /// An invisible, click-through tool window over the work area of the monitor under the cursor. The common dialog centres itself on
    /// its owner, so this is what puts it on that monitor.
    /// </summary>
    private static HWND CreateOwnerOnCursorMonitor()
    {
        try
        {
            var work = WindowChrome.WorkAreaUnderCursor();
            var hwnd = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
                "STATIC", "MikeShift", WINDOW_STYLE.WS_POPUP,
                work.X, work.Y, work.Width, work.Height, HWND.Null, null, null, null);
            if (hwnd == HWND.Null) return HWND.Null;
            PInvoke.SetLayeredWindowAttributes(hwnd, new COLORREF(0), 0, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
            return hwnd;
        }
        catch (Exception)
        {
            return HWND.Null;
        }
    }
}
