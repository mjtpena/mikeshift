using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Shiftvert.App.Tray;

/// <summary>
/// The Win32 <c>IFileOpenDialog</c> behind "Convert Files…": multi-select, real file-system paths only.
/// <para>
/// Limitation (PLAN §1.1 asks for files AND folders): the common item dialog cannot select files and folders in one
/// go (<c>FOS_PICKFOLDERS</c> turns files off), so Convert Files… offers files only. Folders are converted/zipped by
/// Shift-dragging them in Explorer.
/// </para>
/// </summary>
internal static unsafe class FilePicker
{
    private const int HResultCancelled = unchecked((int)0x800704C7); // HRESULT_FROM_WIN32(ERROR_CANCELLED)

    /// <summary>Shows the dialog and returns the chosen file paths (empty if cancelled or on failure).</summary>
    public static IReadOnlyList<string> PickFiles(nint owner = 0)
    {
        try
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();
            dialog.GetOptions(out var options);
            dialog.SetOptions(options
                | FILEOPENDIALOGOPTIONS.FOS_ALLOWMULTISELECT
                | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM
                | FILEOPENDIALOGOPTIONS.FOS_PATHMUSTEXIST
                | FILEOPENDIALOGOPTIONS.FOS_FILEMUSTEXIST);
            dialog.SetTitle("Convert Files");

            try
            {
                dialog.Show(new HWND(owner));
            }
            catch (COMException ex) when (ex.HResult == HResultCancelled)
            {
                return [];
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
        catch (COMException)
        {
            return [];
        }
    }
}
