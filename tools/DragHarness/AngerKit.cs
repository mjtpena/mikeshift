using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace DragHarness;

/// <summary>
/// Building blocks for the "test in anger" runs (--anger): the real File Explorer is always the OLE drag source. Nothing in here
/// uses the harness's own window as a source. Everything runs on a plain background thread.
/// </summary>
internal static class AngerNative
{
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Native.RECT rect);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hwnd, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] public static extern int GetKeyboardLayoutList(int n, IntPtr[]? list);
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    public const uint SWP_NOZORDER = 4, SWP_SHOWWINDOW = 0x40;

    /// <summary>
    /// Brings a window to the front. A process that was not started from the foreground (a harness started by a script, or another agent) is
    /// refused by <c>SetForegroundWindow</c> and the window stays behind whatever is on top; a tap of an unassigned key first makes this process the
    /// one that generated the last input, which Windows accepts as the right to take the foreground.
    /// </summary>
    public static void Front(IntPtr hwnd)
    {
        Pointer.KeyDown(0xE8); Pointer.KeyUp(0xE8);
        Native.SetForegroundWindow(hwnd);
        Thread.Sleep(150);
        if (GetForegroundWindow() != hwnd) { Native.SetForegroundWindow(hwnd); Thread.Sleep(150); }
        // Never go on to click and drag over whatever else is on top of the screen.
        if (GetForegroundWindow() != hwnd) throw new InvalidOperationException("could not bring the Explorer window to the front; no input was sent to it");
    }
}

/// <summary>A started MikeShift.exe with its own log (unpackaged), or an attached one (packaged).</summary>
internal sealed class AngerApp : IDisposable
{
    public Process Proc = null!;
    public string LogPath = "";
    public string Exe = "";

    public static AngerApp Start(string exe, string logPath, IDictionary<string, string>? env = null, params string[] args)
    {
        var app = new AngerApp { Exe = exe, LogPath = logPath };
        try { File.Delete(logPath); } catch (IOException) { }
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        psi.ArgumentList.Add("--dev"); // the MIKESHIFT_* switches below are honoured only in Debug builds or with --dev (see DevSwitches)
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["MIKESHIFT_LOG"] = logPath;
        psi.Environment["MIKESHIFT_TRACE"] = "1";
        psi.Environment["MIKESHIFT_ACCEPT_INJECTED"] = "1";
        if (env is not null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
        app.Proc = Process.Start(psi)!;
        if (!app.WaitLog(t => t.Contains("hooks installed") && t.Contains("wheel ready"), 25000))
            throw new InvalidOperationException("app did not start; log: " + app.Log());
        return app;
    }

    /// <summary>Attaches to an already running process (packaged app) whose log file is <paramref name="logPath"/>.</summary>
    public static AngerApp Attach(Process p, string logPath) => new() { Proc = p, LogPath = logPath };

    public string Log()
    {
        try
        {
            using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            return r.ReadToEnd();
        }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    public int LogLength() => Log().Length;

    /// <summary>Log text written after <paramref name="offset"/> characters.</summary>
    public string LogSince(int offset) { var l = Log(); return offset >= l.Length ? "" : l[offset..]; }

    public bool WaitLog(Func<string, bool> predicate, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (predicate(Log())) return true;
            Thread.Sleep(15);
        }
        return predicate(Log());
    }

    public string LastLine(string contains) => Log().Split('\n').LastOrDefault(l => l.Contains(contains))?.Trim() ?? "";

    public void Kill()
    {
        try { if (Proc is { HasExited: false }) { Proc.Kill(true); Proc.WaitForExit(3000); } }
        catch (Exception) { }
    }

    public void Dispose() => Kill();
}

/// <summary>A real Explorer window, opened by us on a folder of ours.</summary>
internal sealed class ExplorerWin
{
    public IntPtr Hwnd;
    public AutomationElement Win = null!;
    public string Folder = "";

    public static ExplorerWin Open(string folder, Rectangle bounds, string? match = null)
    {
        var leaf = match ?? Path.GetFileName(folder.TrimEnd('\\'));
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
        AutomationElement? win = null;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20000 && win is null)
        {
            var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ClassNameProperty, "CabinetWClass"));
            win = windows.Cast<AutomationElement>().FirstOrDefault(w =>
            {
                try { return w.Current.Name.Equals(leaf, StringComparison.OrdinalIgnoreCase) || w.Current.Name.Contains(leaf, StringComparison.OrdinalIgnoreCase); }
                catch (ElementNotAvailableException) { return false; }
            });
            if (win is null) Thread.Sleep(100);
        }
        if (win is null) throw new InvalidOperationException("no Explorer window for " + folder);
        var e = new ExplorerWin { Win = win, Folder = folder, Hwnd = new IntPtr(win.Current.NativeWindowHandle) };
        e.Place(bounds);
        return e;
    }

    public void Place(Rectangle b)
    {
        AngerNative.SetWindowPos(Hwnd, IntPtr.Zero, b.X, b.Y, b.Width, b.Height, AngerNative.SWP_NOZORDER | AngerNative.SWP_SHOWWINDOW);
        Thread.Sleep(400);
        AngerNative.Front(Hwnd);
        Thread.Sleep(300);
    }

    public void Close()
    {
        if (AngerNative.IsWindow(Hwnd)) Native.PostMessageW(Hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
    }

    public AutomationElement? FindItem(string name, int timeoutMs = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var item = Win.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.NameProperty, name),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)));
                if (item is not null) return item;
                // Details view may expose the item as a DataItem; fall back to any element with the name inside the list.
                item = Win.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));
                if (item is not null && item.Current.ControlType != ControlType.Edit && item.Current.ControlType != ControlType.Text) return item;
            }
            catch (ElementNotAvailableException) { }
            if (sw.ElapsedMilliseconds > timeoutMs) return null;
            Thread.Sleep(100);
        }
    }

    public bool HasItem(string name, int timeoutMs = 8000) => FindItem(name, timeoutMs) is not null;

    /// <summary>Point on the item's icon (where a real user presses to drag).</summary>
    public Point IconPoint(string name)
    {
        var item = FindItem(name) ?? throw new InvalidOperationException("Explorer shows no item " + name);
        var r = item.Current.BoundingRectangle;
        if (r.Width > r.Height * 2) return new Point((int)(r.Left + Math.Min(26, r.Width / 3)), (int)(r.Top + r.Height / 2));
        return new Point((int)(r.Left + r.Width / 2), (int)(r.Top + Math.Min(r.Height * 0.35, 45)));
    }

    public Rectangle ItemRect(string name)
    {
        var r = (FindItem(name) ?? throw new InvalidOperationException(name)).Current.BoundingRectangle;
        return new Rectangle((int)r.Left, (int)r.Top, (int)r.Width, (int)r.Height);
    }

    public string[] VisibleItems()
    {
        try
        {
            return Win.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                .Cast<AutomationElement>().Select(i => i.Current.Name).ToArray();
        }
        catch (ElementNotAvailableException) { return []; }
    }

    /// <summary>Left-click the first item, Ctrl-click the others (a real multi-selection).</summary>
    public void Select(params string[] names)
    {
        AngerNative.Front(Hwnd);
        for (var i = 0; i < names.Length; i++)
        {
            var p = IconPoint(names[i]);
            Pointer.Glide(p.X, p.Y, 14, 3);
            Thread.Sleep(80);
            if (i > 0) { Pointer.KeyDown(Native.VK_LCONTROL); Thread.Sleep(30); }
            Pointer.LeftDown(); Thread.Sleep(25); Pointer.LeftUp();
            if (i > 0) { Thread.Sleep(30); Pointer.KeyUp(Native.VK_LCONTROL); }
            Thread.Sleep(120);
        }
        Thread.Sleep(650); // longer than the double-click time, or the next press on the same item opens it in Photos
        {
        }
    }

    public void SelectAll()
    {
        AngerNative.Front(Hwnd);
        Pointer.KeyDown(Native.VK_LCONTROL); Pointer.KeyDown(0x41); Pointer.KeyUp(0x41); Pointer.KeyUp(Native.VK_LCONTROL);
        Thread.Sleep(400);
    }

    /// <summary>Names Explorer currently reports as selected (SelectionItemPattern).</summary>
    public string[] Selected()
    {
        try
        {
            return Win.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                .Cast<AutomationElement>().Where(i =>
                {
                    try { return i.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p) && ((SelectionItemPattern)p).Current.IsSelected; }
                    catch (Exception) { return false; }
                }).Select(i => i.Current.Name).ToArray();
        }
        catch (ElementNotAvailableException) { return []; }
    }
}

internal static class Shot
{
    public static void Region(Rectangle rect, string path)
    {
        using var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            var screen = Native.GetDC(IntPtr.Zero);
            var dest = g.GetHdc();
            Native.BitBlt(dest, 0, 0, rect.Width, rect.Height, screen, rect.X, rect.Y, 0x00CC0020u | 0x40000000u);
            g.ReleaseHdc(dest);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        bmp.Save(path, ImageFormat.Png);
    }

    public static void Around(Point centre, int half, string path) =>
        Region(new Rectangle(centre.X - half, centre.Y - half, half * 2, half * 2), path);
}

/// <summary>Result recorder: prints and appends to results.txt next to the work folders.</summary>
internal static class Rec
{
    public static string File = "";
    public static string Current = "";
    public static int Failures;

    public static void Begin(string id, string desc)
    {
        Current = id;
        Console.WriteLine($"=== {id}: {desc}");
        Append($"## {id}: {desc}");
    }

    public static bool Check(string name, bool ok, string detail = "")
    {
        if (!ok) Failures++;
        var line = $"  [{(ok ? "ok" : "FAIL")}] {name}" + (detail.Length > 0 ? " - " + detail : "");
        Console.WriteLine(line);
        Append(line);
        return ok;
    }

    public static void Note(string s) { Console.WriteLine("  " + s); Append("  " + s); }

    private static void Append(string line)
    {
        try { System.IO.File.AppendAllText(File, line + Environment.NewLine); } catch (IOException) { }
    }
}

internal static class FileKit
{
    public static string Sha(string path) => Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(path)));

    public static string ShaDir(string path)
    {
        using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            all.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(path, f)));
            all.AppendData(System.IO.File.ReadAllBytes(f));
        }
        return Convert.ToHexString(all.GetHashAndReset());
    }

    public static string HashAny(string p) => Directory.Exists(p) ? ShaDir(p) : Sha(p);

    public static void MakeJpg(string path, int w = 640, int h = 400, string text = "anger")
    {
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            using var b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, w, h), Color.OrangeRed, Color.RoyalBlue, 35f);
            g.FillRectangle(b, 0, 0, w, h);
            g.DrawString(text, new Font("Segoe UI", Math.Max(12, h / 10)), Brushes.White, 20, h / 3);
        }
        bmp.Save(path, path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ImageFormat.Png : ImageFormat.Jpeg);
    }

    public static string Fresh(string root, string name)
    {
        var d = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(d);
        return d;
    }
}

/// <summary>Wheel facts parsed from the app's trace log.</summary>
internal static class WheelInfo
{
    public static (Rectangle Rect, double Scale)? LastRect(string log, bool click = false)
    {
        var m = Regex.Matches(log, click
            ? @"click-mode wheel shown gen=\d+ rect=\((-?\d+),(-?\d+),(\d+)\)"
            : @"wheel shown pending gen=\d+ at \(-?\d+,-?\d+\) rect=\((-?\d+),(-?\d+),(\d+)\)").LastOrDefault();
        if (m is null) return null;
        var l = int.Parse(m.Groups[1].Value); var t = int.Parse(m.Groups[2].Value); var s = int.Parse(m.Groups[3].Value);
        return (new Rectangle(l, t, s, s), s / 380.0);
    }

    public static (List<string> Formats, List<string> Tools)? LastTiles(string log)
    {
        var line = log.Split('\n').LastOrDefault(l => l.Contains("tiles:") && !l.Contains("catalog"));
        if (line is null) return null;
        var body = line[(line.IndexOf("tiles:", StringComparison.Ordinal) + 6)..].Trim();
        if (body.StartsWith("(refreshed):")) body = body["(refreshed):".Length..].Trim();
        var parts = body.Split('|');
        List<string> Split(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return (Split(parts[0]), parts.Length > 1 ? Split(parts[1]) : []);
    }

    /// <summary>Screen centre of tile <paramref name="title"/> (radius 100 px logical), or null if absent.</summary>
    public static Point? TileCentre(string log, string title, bool tools)
    {
        var rect = LastRect(log); var tiles = LastTiles(log);
        if (rect is null || tiles is null) return null;
        var list = tools ? tiles.Value.Tools : tiles.Value.Formats;
        var i = list.IndexOf(title);
        if (i < 0) return null;
        var angle = (i + 0.5) * 2 * Math.PI / list.Count;
        var c = new Point(rect.Value.Rect.Left + rect.Value.Rect.Width / 2, rect.Value.Rect.Top + rect.Value.Rect.Height / 2);
        var r = (list.Count == 1 ? 40 : 100) * rect.Value.Scale;   // a single tile is the hub button (radius 64), not a ring
        return new Point((int)Math.Round(c.X + r * Math.Sin(angle)), (int)Math.Round(c.Y - r * Math.Cos(angle)));
    }

    public static Point? Centre(string log)
    {
        var rect = LastRect(log);
        return rect is null ? null : new Point(rect.Value.Rect.Left + rect.Value.Rect.Width / 2, rect.Value.Rect.Top + rect.Value.Rect.Height / 2);
    }

    public static bool Visible()
    {
        var hwnd = Native.FindWindowW("MikeShiftWheel", null);
        return hwnd != IntPtr.Zero && Native.IsWindowVisible(hwnd);
    }
}

internal static class CpuLoad
{
    private static PerformanceCounter? _c;
    public static double Now()
    {
        try
        {
            _c ??= new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");
            _c.NextValue(); Thread.Sleep(250);
            return Math.Round(_c.NextValue(), 0);
        }
        catch (Exception) { return -1; }
    }
}
