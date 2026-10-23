using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.UI.Shell;

namespace MikeShift.App.Wheel;

/// <summary>What a drag carries, as far as the wheel is concerned.</summary>
internal enum DragKind
{
    /// <summary>Real file-system items: the wheel shows its tiles.</summary>
    Files,
    /// <summary>Virtual files, URLs or pictures (Outlook attachments, zip-folder entries, phones, browser images): the wheel says "Save the file first".</summary>
    VirtualFiles,
    /// <summary>Something that is no file at all (selected text...): no wheel.</summary>
    Other,
}

/// <summary>The result of looking at a drag's data object.</summary>
internal sealed record DragPayload(DragKind Kind, IReadOnlyList<string> Paths);

/// <summary>
/// Reads and writes the few shell formats the wheel needs from an <c>IDataObject</c>. In <c>DragEnter</c>/<c>DragOver</c> only
/// cheap formats are fetched (<c>CF_HDROP</c>: a short HGLOBAL; the IDList array through the shell's own helper); nothing
/// heavy (<c>FileContents</c>, bitmaps...) is ever requested, because that would block the source's drag loop.
/// </summary>
internal static unsafe class DataObjectInspector
{
    /// <summary>Classifies the drag and extracts file-system paths when there are any.</summary>
    public static DragPayload Inspect(System.Runtime.InteropServices.ComTypes.IDataObject data)
    {
        IReadOnlyList<string> paths = [];

        if (OleNative.Has(data, OleNative.CF_HDROP))
        {
            paths = ReadHDrop(data);
            if (paths.Count > 0) return new DragPayload(DragKind.Files, paths);
        }

        var hasIdList = OleNative.Has(data, ShellFormats.ShellIdList);
        if (hasIdList)
        {
            paths = ReadShellItems(data);
            if (paths.Count > 0) return new DragPayload(DragKind.Files, paths);
        }

        if (hasIdList
            || OleNative.Has(data, ShellFormats.FileDescriptorW)
            || OleNative.Has(data, ShellFormats.FileDescriptorA)
            || OleNative.Has(data, ShellFormats.UrlW)
            || OleNative.Has(data, ShellFormats.UrlA)
            || OleNative.Has(data, ShellFormats.Png)
            || OleNative.Has(data, ShellFormats.Html)
            || OleNative.Has(data, OleNative.CF_DIB)
            || OleNative.Has(data, OleNative.CF_DIBV5)
            || OleNative.Has(data, OleNative.CF_BITMAP, TYMED.TYMED_GDI))
            return new DragPayload(DragKind.VirtualFiles, []);

        return new DragPayload(DragKind.Other, []);
    }

    /// <summary>The paths in <c>CF_HDROP</c> via <c>DragQueryFileW</c>; empty if the format is absent or not an HGLOBAL.</summary>
    public static IReadOnlyList<string> ReadHDrop(System.Runtime.InteropServices.ComTypes.IDataObject data)
    {
        var etc = OleNative.Etc(OleNative.CF_HDROP, TYMED.TYMED_HGLOBAL);
        STGMEDIUM medium = default;
        try
        {
            data.GetData(ref etc, out medium);
        }
        catch (Exception)
        {
            return [];
        }

        try
        {
            if (medium.tymed != TYMED.TYMED_HGLOBAL || medium.unionmember == IntPtr.Zero) return [];
            var hdrop = new HDROP(medium.unionmember);
            var count = PInvoke.DragQueryFile(hdrop, 0xFFFFFFFF, default(Span<char>));
            var result = new List<string>((int)Math.Min(count, 4096));
            for (uint i = 0; i < count; i++)
            {
                var length = PInvoke.DragQueryFile(hdrop, i, default(Span<char>));
                if (length == 0) continue;
                var buffer = new char[length + 1];
                var copied = PInvoke.DragQueryFile(hdrop, i, buffer);
                if (copied > 0) result.Add(new string(buffer, 0, (int)copied));
            }
            return result;
        }
        finally
        {
            OleNative.ReleaseStgMedium(ref medium);
        }
    }

    /// <summary>File-system paths of the items in the data object's IDList array (Libraries, search results...). Virtual items are skipped.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2050", Justification = TrimJustification.ComInterop)]
    public static IReadOnlyList<string> ReadShellItems(System.Runtime.InteropServices.ComTypes.IDataObject data)
    {
        try
        {
            var iid = OleNative.IidShellItemArray;
            var hr = PInvoke.SHCreateShellItemArrayFromDataObject(data, &iid, out var obj);
            if (hr.Failed || obj is not IShellItemArray array) return [];

            array.GetCount(out var count);
            var result = new List<string>((int)Math.Min(count, 4096));
            for (uint i = 0; i < count; i++)
            {
                array.GetItemAt(i, out var item);
                try
                {
                    item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var name);
                    try
                    {
                        var path = name.ToString();
                        if (!string.IsNullOrEmpty(path)) result.Add(path);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem((IntPtr)name.Value);
                    }
                }
                catch (COMException)
                {
                    // not a file-system item
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            Marshal.ReleaseComObject(array);
            return result;
        }
        catch (Exception)
        {
            return [];
        }
    }

    // ------------------------------------------------------------------ writing

    /// <summary><c>DROPIMAGETYPE</c> values.</summary>
    public const int DropImageInvalid = -1;
    public const int DropImageNone = 0;
    public const int DropImageCopy = 1;

    /// <summary>
    /// Sets <c>CFSTR_DROPDESCRIPTION</c> on the source's data object so Explorer's drag image says what the drop will do
    /// ("Convert to PNG"). Best effort: sources that do not accept it are ignored.
    /// </summary>
    public static bool SetDropDescription(System.Runtime.InteropServices.ComTypes.IDataObject data, int imageType, string message, string insert)
    {
        const int Chars = 260;
        var size = (uint)(sizeof(int) + 2 * Chars * sizeof(char));
        var hglobal = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE | GLOBAL_ALLOC_FLAGS.GMEM_ZEROINIT, size);
        if (hglobal.IsNull) return false;

        var p = (byte*)PInvoke.GlobalLock(hglobal);
        if (p == null)
        {
            PInvoke.GlobalFree(hglobal);
            return false;
        }
        *(int*)p = imageType;
        WriteString((char*)(p + sizeof(int)), message, Chars);
        WriteString((char*)(p + sizeof(int) + Chars * sizeof(char)), insert, Chars);
        PInvoke.GlobalUnlock(hglobal);

        return SetHGlobal(data, ShellFormats.DropDescription, hglobal);
    }

    /// <summary>Sets <c>CFSTR_PERFORMEDDROPEFFECT</c> so a source that deletes its originals after a MOVE sees that this was a COPY.</summary>
    public static bool SetPerformedDropEffect(System.Runtime.InteropServices.ComTypes.IDataObject data, uint effect)
    {
        var hglobal = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE | GLOBAL_ALLOC_FLAGS.GMEM_ZEROINIT, sizeof(uint));
        if (hglobal.IsNull) return false;
        var p = (uint*)PInvoke.GlobalLock(hglobal);
        if (p == null)
        {
            PInvoke.GlobalFree(hglobal);
            return false;
        }
        *p = effect;
        PInvoke.GlobalUnlock(hglobal);
        return SetHGlobal(data, ShellFormats.PerformedDropEffect, hglobal);
    }

    private static bool SetHGlobal(System.Runtime.InteropServices.ComTypes.IDataObject data, string format, HGLOBAL hglobal)
    {
        var etc = OleNative.Etc(OleNative.Format(format), TYMED.TYMED_HGLOBAL);
        var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = (nint)hglobal.Value, pUnkForRelease = null };
        try
        {
            data.SetData(ref etc, ref medium, true); // fRelease: the data object frees the HGLOBAL on success
            return true;
        }
        catch (Exception)
        {
            PInvoke.GlobalFree(hglobal); // refused: we still own it
            return false;
        }
    }

    private static void WriteString(char* destination, string text, int capacity)
    {
        var length = Math.Min(text.Length, capacity - 1);
        for (var i = 0; i < length; i++) destination[i] = text[i];
        destination[length] = '\0';
    }
}
