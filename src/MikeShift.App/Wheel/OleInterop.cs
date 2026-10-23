using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace MikeShift.App.Wheel;

/// <summary><c>POINTL</c> / <c>POINT</c>: screen coordinates in physical pixels as OLE delivers them.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OlePoint
{
    public int X;
    public int Y;
}

/// <summary><c>IDropTarget</c> (classic COM interop; the CCW is created when the managed object is handed to <c>RegisterDragDrop</c>).</summary>
[ComImport]
[Guid("00000122-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTarget
{
    [PreserveSig]
    int DragEnter([In, MarshalAs(UnmanagedType.Interface)] System.Runtime.InteropServices.ComTypes.IDataObject pDataObj, uint grfKeyState, OlePoint pt, [In, Out] ref uint pdwEffect);

    [PreserveSig]
    int DragOver(uint grfKeyState, OlePoint pt, [In, Out] ref uint pdwEffect);

    [PreserveSig]
    int DragLeave();

    [PreserveSig]
    int Drop([In, MarshalAs(UnmanagedType.Interface)] System.Runtime.InteropServices.ComTypes.IDataObject pDataObj, uint grfKeyState, OlePoint pt, [In, Out] ref uint pdwEffect);
}

/// <summary><c>IDropTargetHelper</c> (shell32's drag image helper, <c>CLSID_DragDropHelper</c>).</summary>
[ComImport]
[Guid("4657278B-411B-11D2-839A-00C04FD918D0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTargetHelper
{
    [PreserveSig]
    int DragEnter(IntPtr hwndTarget, [In, MarshalAs(UnmanagedType.Interface)] System.Runtime.InteropServices.ComTypes.IDataObject pDataObject, ref OlePoint ppt, uint dwEffect);

    [PreserveSig]
    int DragLeave();

    [PreserveSig]
    int DragOver(ref OlePoint ppt, uint dwEffect);

    [PreserveSig]
    int Drop([In, MarshalAs(UnmanagedType.Interface)] System.Runtime.InteropServices.ComTypes.IDataObject pDataObject, ref OlePoint ppt, uint dwEffect);

    [PreserveSig]
    int Show([MarshalAs(UnmanagedType.Bool)] bool fShow);
}

/// <summary>Drop effects (<c>DROPEFFECT_*</c>). The wheel only ever answers <see cref="None"/> or <see cref="Copy"/>.</summary>
internal static class DropEffects
{
    public const uint None = 0;
    public const uint Copy = 1;
    public const uint Move = 2;
    public const uint Link = 4;
}

/// <summary>Clipboard formats and shell data-object plumbing used by the drop target.</summary>
internal static class OleNative
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_FAIL = unchecked((int)0x80004005);

    public const short CF_TEXT = 1;
    public const short CF_BITMAP = 2;
    public const short CF_DIB = 8;
    public const short CF_HDROP = 15;
    public const short CF_UNICODETEXT = 13;
    public const short CF_DIBV5 = 17;

    public static readonly Guid ClsidDragDropHelper = new("4657278A-411B-11D2-839A-00C04FD918D0");
    public static readonly Guid IidShellItemArray = new("B63EA76D-1F85-456F-A19C-48159EFA858B");

    [DllImport("ole32.dll")]
    public static extern int RegisterDragDrop(IntPtr hwnd, IDropTarget pDropTarget);

    [DllImport("ole32.dll")]
    public static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("ole32.dll")]
    public static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClipboardFormatNameW(uint format, System.Text.StringBuilder name, int max);

    /// <summary>A readable list of the formats a data object advertises (diagnostics only; enumerates, never fetches data).</summary>
    public static string Describe(System.Runtime.InteropServices.ComTypes.IDataObject data)
    {
        try
        {
            var parts = new List<string>();
            var e = data.EnumFormatEtc(DATADIR.DATADIR_GET);
            var one = new FORMATETC[1];
            var fetched = new int[1];
            while (e.Next(1, one, fetched) == S_OK)
            {
                var id = (ushort)one[0].cfFormat;
                string name;
                if (id < 0xC000)
                    name = id switch { 1 => "CF_TEXT", 2 => "CF_BITMAP", 8 => "CF_DIB", 13 => "CF_UNICODETEXT", 15 => "CF_HDROP", 17 => "CF_DIBV5", _ => "cf" + id };
                else
                {
                    var sb = new System.Text.StringBuilder(128);
                    name = GetClipboardFormatNameW(id, sb, sb.Capacity) > 0 ? sb.ToString() : "cf" + id;
                }
                parts.Add($"{name}/{one[0].tymed}");
            }
            return string.Join(", ", parts);
        }
        catch (Exception ex)
        {
            return "(could not enumerate: " + ex.Message + ")";
        }
    }

    private static readonly Dictionary<string, short> FormatCache = [];

    /// <summary>The registered clipboard format id for <paramref name="name"/> (cached).</summary>
    public static short Format(string name)
    {
        lock (FormatCache)
        {
            if (!FormatCache.TryGetValue(name, out var id))
                FormatCache[name] = id = unchecked((short)RegisterClipboardFormatW(name));
            return id;
        }
    }

    public static FORMATETC Etc(short format, TYMED medium = TYMED.TYMED_HGLOBAL) => new()
    {
        cfFormat = format,
        dwAspect = DVASPECT.DVASPECT_CONTENT,
        lindex = -1,
        ptd = IntPtr.Zero,
        tymed = medium,
    };

    /// <summary>True if the data object advertises <paramref name="format"/> (a cheap <c>QueryGetData</c>; never fetches data).</summary>
    public static bool Has(System.Runtime.InteropServices.ComTypes.IDataObject data, short format, TYMED medium = TYMED.TYMED_HGLOBAL)
    {
        try
        {
            var etc = Etc(format, medium);
            return data.QueryGetData(ref etc) == S_OK;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool Has(System.Runtime.InteropServices.ComTypes.IDataObject data, string name, TYMED medium = TYMED.TYMED_HGLOBAL) =>
        Has(data, Format(name), medium);
}

/// <summary>Registered clipboard format names used for drag and drop (shlobj_core.h).</summary>
internal static class ShellFormats
{
    public const string ShellIdList = "Shell IDList Array";           // CFSTR_SHELLIDLIST
    public const string FileDescriptorW = "FileGroupDescriptorW";      // CFSTR_FILEDESCRIPTORW
    public const string FileDescriptorA = "FileGroupDescriptor";       // CFSTR_FILEDESCRIPTORA
    public const string FileContents = "FileContents";                 // CFSTR_FILECONTENTS
    public const string DropDescription = "DropDescription";           // CFSTR_DROPDESCRIPTION
    public const string PerformedDropEffect = "Performed DropEffect";  // CFSTR_PERFORMEDDROPEFFECT
    public const string UrlW = "UniformResourceLocatorW";
    public const string UrlA = "UniformResourceLocator";
    public const string Html = "HTML Format";
    public const string Png = "PNG";
}
