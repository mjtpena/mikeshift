using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ContextMenuHarness;

[ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerCommand
{
    [PreserveSig] int GetTitle([MarshalAs(UnmanagedType.Interface)] IShellItemArray? items, [MarshalAs(UnmanagedType.LPWStr)] out string? name);
    [PreserveSig] int GetIcon([MarshalAs(UnmanagedType.Interface)] IShellItemArray? items, [MarshalAs(UnmanagedType.LPWStr)] out string? icon);
    [PreserveSig] int GetToolTip([MarshalAs(UnmanagedType.Interface)] IShellItemArray? items, [MarshalAs(UnmanagedType.LPWStr)] out string? tip);
    [PreserveSig] int GetCanonicalName(out Guid name);
    [PreserveSig] int GetState([MarshalAs(UnmanagedType.Interface)] IShellItemArray? items, int okToBeSlow, out int state);
    [PreserveSig] int Invoke([MarshalAs(UnmanagedType.Interface)] IShellItemArray? items, IntPtr bindContext);
    [PreserveSig] int GetFlags(out int flags);
    [PreserveSig] int EnumSubCommands([MarshalAs(UnmanagedType.Interface)] out IEnumExplorerCommand? commands);
}

[ComImport, Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumExplorerCommand
{
    [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IExplorerCommand? command, out uint fetched);
    [PreserveSig] int Skip(uint count);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumExplorerCommand? clone);
}

[ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemArray
{
    [PreserveSig] int BindToHandler(IntPtr bc, ref Guid handler, ref Guid iid, out IntPtr result);
    [PreserveSig] int GetPropertyStore(int flags, ref Guid iid, out IntPtr result);
    [PreserveSig] int GetPropertyDescriptionList(IntPtr key, ref Guid iid, out IntPtr result);
    [PreserveSig] int GetAttributes(int flags, uint mask, out uint attributes);
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemAt(uint index, [MarshalAs(UnmanagedType.Interface)] out object item);
    [PreserveSig] int EnumItems(out IntPtr result);
}


internal static class ShellNative
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint attrsIn, out uint attrsOut);

    [DllImport("shell32.dll")]
    public static extern int SHCreateShellItemArrayFromIDLists(uint count, IntPtr[] pidls, [MarshalAs(UnmanagedType.Interface)] out IShellItemArray array);

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object instance);
}

internal static class ComProbe
{
    public static readonly Guid ModernClsid = new("02dcb673-3099-4372-86cb-c7f4fad75db8");
    private const uint ClsctxLocalServer = 4, ClsctxInprocServer = 1;

    public static IShellItemArray ItemArray(IEnumerable<string> paths)
    {
        var pidls = paths.Select(p =>
        {
            var hr = ShellNative.SHParseDisplayName(Path.GetFullPath(p), IntPtr.Zero, out var pidl, 0, out _);
            Marshal.ThrowExceptionForHR(hr);
            return pidl;
        }).ToArray();
        Marshal.ThrowExceptionForHR(ShellNative.SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out var array));
        return array;
    }

    private static string Flags(int f) => f switch { 0 => "", 1 => " [has submenu]", 8 => " [separator]", _ => $" [flags {f}]" };

    public static int Run(string[] args)
    {
        string? invoke = null;
        var repeat = 1;
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--invoke") invoke = args[++i];
            else if (args[i] == "--repeat") repeat = int.Parse(args[++i]);
            else if (args[i] == "--folder-count") files.AddRange(Directory.EnumerateFileSystemEntries(args[++i]));
            else files.Add(args[i]);
        }
        if (files.Count == 0) { Console.Error.WriteLine("usage: probe [--invoke label] [--repeat n] [--folder-count dir] file..."); return 2; }

        var array = ItemArray(files);
        Console.WriteLine($"selection: {files.Count} item(s)");
        for (var round = 1; round <= repeat; round++)
        {
            var sw = Stopwatch.StartNew();
            var clsid = ModernClsid;
            var iid = typeof(IExplorerCommand).GUID;
            var hr = ShellNative.CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxLocalServer | ClsctxInprocServer, ref iid, out var instance);
            var tActivate = sw.Elapsed.TotalMilliseconds;
            if (hr != 0) { Console.WriteLine($"CoCreateInstance failed: 0x{hr:X8}"); return 1; }
            var command = (IExplorerCommand)instance;

            sw.Restart();
            command.GetState(array, 0, out var state);
            var tState = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            command.GetTitle(array, out var title);
            command.GetIcon(array, out var icon);
            command.GetFlags(out var flags);
            var tTitle = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var hrEnum = command.EnumSubCommands(out var sub);
            var items = new List<(IExplorerCommand Cmd, string Title, int Flags)>();
            if (hrEnum == 0 && sub is not null)
            {
                while (sub.Next(1, out var next, out var fetched) == 0 && fetched == 1 && next is not null)
                {
                    next.GetTitle(array, out var t);
                    next.GetFlags(out var f);
                    items.Add((next, t ?? "", f));
                }
            }
            var tEnum = sw.Elapsed.TotalMilliseconds;

            Console.WriteLine($"round {round}: activate {tActivate:F1} ms, GetState {tState:F1} ms (state {state}), GetTitle/Icon/Flags {tTitle:F1} ms, EnumSubCommands {tEnum:F1} ms  =>  menu ready {tActivate + tState + tTitle + tEnum:F1} ms");
            if (round == 1)
            {
                Console.WriteLine($"top: \"{title}\"{Flags(flags)}  icon: {icon}");
                foreach (var (_, t, f) in items) Console.WriteLine($"   - {(f == 8 ? "----------" : t)}{Flags(f)}");
            }
            if (invoke is not null && round == repeat)
            {
                var target = items.FirstOrDefault(x => x.Title == invoke);
                if (target.Cmd is null) { Console.WriteLine($"no sub-command named \"{invoke}\""); return 1; }
                sw.Restart();
                var rc = target.Cmd.Invoke(array, IntPtr.Zero);
                Console.WriteLine($"Invoke(\"{invoke}\") -> 0x{rc:X8} in {sw.Elapsed.TotalMilliseconds:F1} ms");
            }
        }
        return 0;
    }
}
