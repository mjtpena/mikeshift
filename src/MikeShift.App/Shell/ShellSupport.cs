using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Principal;
using System.Text;
using MikeShift.Core;
using MikeShift.Core.Shell;

namespace MikeShift.App.Shell;

/// <summary>Names and locations shared by the Explorer extension process and the tray app.</summary>
internal static class ShellNames
{
    /// <summary>The command-line switch COM adds (LocalServer32 arguments in the manifest) when it starts the extension server.</summary>
    public const string EmbeddingSwitch = ShellIds.EmbeddingSwitch;

    /// <summary>CLSID of the IExplorerCommand (Windows 11 modern menu; Windows 10's menu). A test checks it against <c>Package.appxmanifest</c>.</summary>
    public static readonly Guid ModernCommandClsid = ShellIds.CommandClsid;

    /// <summary>The per-user pipe the tray app listens on.</summary>
    public static string PipeName()
    {
        string sid;
        try { sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName; }
        catch (Exception) { sid = Environment.UserName; }
        return ShellIds.PipeNameFor(sid);
    }

    /// <summary>
    /// The folder both processes use for the shared files (the capability cache; the extension's own log). The REAL package data folder when
    /// packaged (the tray app writes there through <c>AppData</c>), so no virtualisation is involved; <c>%LOCALAPPDATA%\MikeShift</c> otherwise.
    /// </summary>
    public static string DataFolder()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var family = PackageFamilyName();
        return family is null
            ? Path.Combine(local, "MikeShift")
            : Path.Combine(local, "Packages", family, "LocalCache", "Local", "MikeShift");
    }

    /// <summary>The package family name of this process, or null when it is not packaged.</summary>
    public static unsafe string? PackageFamilyName()
    {
        uint length = 0;
        const int AppModelErrorNoPackage = 15700, InsufficientBuffer = 122;
        var rc = Native.GetCurrentPackageFamilyName(ref length, null);
        if (rc == AppModelErrorNoPackage || rc != InsufficientBuffer || length == 0) return null;
        var buffer = new char[length];
        fixed (char* p = buffer)
        {
            if (Native.GetCurrentPackageFamilyName(ref length, p) != 0) return null;
        }
        return new string(buffer, 0, (int)length - 1);
    }
}

/// <summary>A tiny append-only log of the Explorer extension process (<c>shell-log.txt</c> in the data folder); never throws.</summary>
internal static class ShellLog
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("info ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var path = Path.Combine(ShellNames.DataFolder(), "shell-log.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 256 * 1024) info.Delete(); // small and disposable: start over rather than grow
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} [{level}] [pid {Environment.ProcessId}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // best effort
        }
    }
}

/// <summary>
/// Keeps the extension server alive while it is being used. COM starts it on the first right-click; it serves the following ones from
/// the same process and exits after <see cref="IdleLimit"/> without a call, so MikeShift does not keep a second process around all day.
/// </summary>
internal static class ServerLifetime
{
    /// <summary>How long the server stays after its last activity.</summary>
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(10);

    private static long s_lastActivity = Environment.TickCount64;
    private static int s_active;

    /// <summary>Marks activity; the idle clock restarts.</summary>
    public static void Touch() => Interlocked.Exchange(ref s_lastActivity, Environment.TickCount64);

    /// <summary>Work (a call, a background send) that must finish before the process may exit.</summary>
    public static IDisposable Begin()
    {
        Interlocked.Increment(ref s_active);
        Touch();
        return new Scope();
    }

    /// <summary>True when nothing is in flight and nothing has happened for <see cref="IdleLimit"/>.</summary>
    public static bool IsIdle =>
        Volatile.Read(ref s_active) == 0 && Environment.TickCount64 - Interlocked.Read(ref s_lastActivity) > IdleLimit.TotalMilliseconds;

    private sealed class Scope : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            Interlocked.Decrement(ref s_active);
            Touch();
        }
    }
}

/// <summary>What Explorer selected, as the file-system paths the menu works with.</summary>
/// <param name="Count">Items in the selection.</param>
/// <param name="Paths">The paths read (at most the requested limit); null when an item is not a file-system item (a virtual folder).</param>
/// <param name="Folders">The paths among them that are folders.</param>
internal sealed record ShellSelection(int Count, IReadOnlyList<string>? Paths, IReadOnlySet<string> Folders)
{
    private const uint SigdnFileSysPath = 0x80058000;
    private const uint SfgaoFolder = 0x20000000, SfgaoStream = 0x00400000;

    /// <summary>
    /// Reads up to <paramref name="limit"/> items. A selection larger than the limit only has its first item checked (it has to be a file-system
    /// item) and reports <see cref="Paths"/> as an empty list: the menu for such a selection needs the count only.
    /// </summary>
    public static ShellSelection Read(IShellItemArray? items, int limit)
    {
        if (items is null || items.GetCount(out var count) != Hr.Ok || count == 0) return new ShellSelection(0, [], new HashSet<string>());
        var n = (int)Math.Min(count, int.MaxValue);
        var take = n > limit ? 1 : n;
        var anyFolder = items.GetAttributes(2 /* SIATTRIBFLAGS_OR */, SfgaoFolder, out var attrs) == Hr.Ok && (attrs & SfgaoFolder) != 0;

        var paths = new List<string>(take);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (uint i = 0; i < take; i++)
        {
            if (items.GetItemAt(i, out var item) != Hr.Ok || item is null) return new ShellSelection(n, null, folders);
            if (item.GetDisplayName(SigdnFileSysPath, out var raw) != Hr.Ok) return new ShellSelection(n, null, folders);
            var path = Native.TakeString(raw);
            if (string.IsNullOrEmpty(path)) return new ShellSelection(n, null, folders);
            paths.Add(path);
            // A .zip is a folder to the shell too (SFGAO_STREAM marks it as a file); only real folders are told apart from files.
            if (anyFolder && item.GetAttributes(SfgaoFolder | SfgaoStream, out var itemAttrs) == Hr.Ok
                && (itemAttrs & SfgaoFolder) != 0 && (itemAttrs & SfgaoStream) == 0)
                folders.Add(path);
        }
        return new ShellSelection(n, n > limit ? [] : paths, folders);
    }

    /// <summary>The plan for this selection (hidden when it is empty or contains virtual items).</summary>
    public ContextMenuPlan ToPlan(Capabilities caps)
    {
        if (Count == 0 || Paths is null) return ContextMenuPlan.Hidden;
        if (Count > ContextMenuPlanner.LargeSelection) return new ContextMenuPlan(true, [new ContextMenuEntry("Zip", "Zip")], true);
        return ContextMenuPlanner.Build(Paths, caps, p => Folders.Contains(p) ? Category.Other : Kind.ByExtension(p));
    }
}

/// <summary>The cached capabilities of the tray app, re-read when the file changes (never probes anything itself).</summary>
internal static class CachedCapabilities
{
    private static DateTime s_stamp;
    private static Capabilities s_value = Capabilities.Quick();

    public static Capabilities Get()
    {
        try
        {
            var path = Path.Combine(ShellNames.DataFolder(), CapabilityCache.FileName);
            var stamp = File.GetLastWriteTimeUtc(path);
            if (stamp == s_stamp) return s_value;
            s_value = CapabilityCache.Parse(File.ReadAllText(path));
            s_stamp = stamp;
        }
        catch (Exception)
        {
            // No file yet (the tray app has not finished its first probe): the conservative snapshot stays.
        }
        return s_value;
    }
}

/// <summary>The one place that turns COM pointers into managed objects and back for this folder.</summary>
internal static unsafe class ComBridge
{
    public static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>A new reference (ownership goes to the caller) to <paramref name="command"/> as an IExplorerCommand pointer.</summary>
    public static nint ToPointer(IExplorerCommand command) => (nint)ComInterfaceMarshaller<IExplorerCommand>.ConvertToUnmanaged(command);
}
