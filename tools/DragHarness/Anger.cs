using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace DragHarness;

/// <summary>One scripted gesture with the real Explorer as the OLE drag source.</summary>
internal sealed class Sess
{
    public AngerApp App = null!;
    public ExplorerWin Ex = null!;
    public string Work = "";
    public Point Dest;
    public long LatencyMs = -1;
    public int LogMark;
    public DateTime ShiftAt;

    public static int KeyOf(string k) => k switch { "shift" => Native.VK_LSHIFT, "ctrl" => Native.VK_LCONTROL, "alt" => 0xA4, _ => throw new ArgumentException(k) };

    public static void Keys(string mods, bool down)
    {
        foreach (var m in mods.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            if (down) Pointer.KeyDown(KeyOf(m)); else Pointer.KeyUp(KeyOf(m));
            Thread.Sleep(down ? 40 : 10);
        }
    }

    public string Log => App.Log();
    public string LogNew => App.LogSince(LogMark);

    /// <summary>
    /// Presses on <paramref name="item"/> in Explorer (optionally with <paramref name="pre"/> modifiers already down), moves past the threshold
    /// toward <paramref name="dest"/>, then presses <paramref name="during"/> (default Shift). Waits for the wheel to be visible.
    /// </summary>
    public bool Start(string item, string pre = "", string during = "shift", Point? dest = null, bool expectWheel = true, int settleMs = 200)
    {
        var p = Ex.IconPoint(item);
        var scr = Screen.FromPoint(p).WorkingArea;
        Dest = dest ?? new Point(Math.Clamp(p.X + 420, scr.Left + 260, scr.Right - 260), Math.Clamp(p.Y + 40, scr.Top + 260, scr.Bottom - 260));
        LogMark = App.LogLength();
        Pointer.Glide(p.X, p.Y, 14, 3);
        Thread.Sleep(150);
        if (pre.Length > 0) Keys(pre, true);
        Pointer.LeftDown();
        Thread.Sleep(300);
        // Watch for the wheel on the side: with Shift already down it arms during the glide, long before the glide ends.
        var firstSeen = new long[] { -1 };
        var armedAt = Stopwatch.StartNew();
        var stopWatch = false;
        var watcher = new Thread(() =>
        {
            while (!stopWatch) { if (firstSeen[0] < 0 && WheelInfo.Visible()) firstSeen[0] = armedAt.ElapsedMilliseconds; Thread.Sleep(2); }
        }) { IsBackground = true };
        watcher.Start();
        var shiftPre = pre.Contains("shift");
        Pointer.Glide(Dest.X, Dest.Y, 8, 6);
        var sw = Stopwatch.StartNew();
        ShiftAt = DateTime.Now;
        if (during.Length > 0) Keys(during, true);
        var seen = Wait(() => firstSeen[0] >= 0, 2500);
        LatencyMs = shiftPre ? 0 : sw.ElapsedMilliseconds;
        stopWatch = true;
        if (expectWheel && seen) Thread.Sleep(settleMs);
        return seen;
    }

    public static bool Wait(Func<bool> cond, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; Thread.Sleep(5); }
        return cond();
    }

    public bool WaitTiles(int ms = 5000) => App.WaitLog(t => t.Length > LogMark && t[LogMark..].Contains("tiles:"), ms) ;

    public string[] Tiles(bool tools)
    {
        var t = WheelInfo.LastTiles(Log);
        return t is null ? [] : (tools ? t.Value.Tools : t.Value.Formats).ToArray();
    }

    /// <summary>Glides onto a tile (fresh geometry from the log) and waits for the highlight.</summary>
    public bool ToTile(string title, bool tools = false)
    {
        var c = WheelInfo.TileCentre(Log, title, tools);
        if (c is null) return false;
        Pointer.Glide(c.Value.X, c.Value.Y, 4, 10);
        var ok = App.WaitLog(t => t.Length > LogMark && t[LogMark..].Contains("highlight: " + title + " "), 2000);
        Thread.Sleep(250);
        return ok;
    }

    public void Release(string mods = "shift")
    {
        Pointer.LeftUp();
        Thread.Sleep(30);
        if (mods.Length > 0) Keys(mods, false);
        // belt and braces
        Pointer.KeyUp(Native.VK_LSHIFT); Pointer.KeyUp(Native.VK_LCONTROL);
    }

    /// <summary>Cancels the Explorer drag (Esc) and lets go: nothing gets dropped on whatever is under the pointer.</summary>
    public void Abort()
    {
        Esc(); Thread.Sleep(250); Release();
    }

    public void Esc()
    {
        Pointer.KeyDown(Native.VK_ESCAPE); Thread.Sleep(30); Pointer.KeyUp(Native.VK_ESCAPE);
    }

    public bool WaitFile(string path, int ms = 15000) => Wait(() => File.Exists(path) || Directory.Exists(path), ms);

    /// <summary>Reads the HUD toast text through UI Automation, polling until <paramref name="ms"/>; returns every distinct text seen.</summary>
    public List<string> ToastTexts(int ms)
    {
        var seen = new List<string>();
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            foreach (var t in ReadAppTexts())
                if (!seen.Contains(t)) seen.Add(t);
            Thread.Sleep(60);
        }
        return seen;
    }

    /// <summary>Texts of the app's visible top-level non-wheel windows (toast, dialogs).</summary>
    public IEnumerable<string> ReadAppTexts()
    {
        var result = new List<string>();
        try
        {
            var wins = AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, App.Proc.Id));
            foreach (AutomationElement w in wins)
            {
                string cls;
                try { cls = w.Current.ClassName; } catch (Exception) { continue; }
                if (cls == "MikeShiftWheel" || cls == "H.NotifyIcon.TaskbarIcon" || w.Current.IsOffscreen) continue;
                var texts = w.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                foreach (AutomationElement t in texts)
                {
                    var n = t.Current.Name;
                    if (!string.IsNullOrWhiteSpace(n)) result.Add(n);
                }
            }
        }
        catch (Exception) { }
        return result;
    }
}

internal static partial class Anger
{
    public static string Root = "";
    public static string AppExe = "";
    public static string RepoRoot = "";
    public static string ShotDir = "";

    public static Rectangle PrimaryBox => new(1500, 100, 1100, 760);
    public static Rectangle LeftBox
    {
        get
        {
            var s = Screen.AllScreens.FirstOrDefault(x => !x.Primary) ?? Screen.PrimaryScreen!;
            return new Rectangle(s.WorkingArea.Left + 60, s.WorkingArea.Top + 60, 1100, 760);
        }
    }

    public static int Run(string[] args, string appExe, string repo)
    {
        AppExe = appExe;
        RepoRoot = repo;
        Root = Path.GetFullPath(Environment.GetEnvironmentVariable("MIKESHIFT_ANGER_ROOT") is { Length: > 0 } r ? r : Path.Combine(Path.GetTempPath(), "mikeshift-anger"));
        Directory.CreateDirectory(Root);
        MakeFixtures();
        ShotDir = Path.Combine(Root, "shots");
        Directory.CreateDirectory(ShotDir);
        Rec.File = Path.Combine(Root, "results.txt");

        var wanted = args[Array.IndexOf(args, "--anger") + 1].Split(',');
        var all = Tests();
        var run = wanted.Contains("all") ? all.Keys.ToArray() : wanted;
        foreach (var name in run)
        {
            if (!all.TryGetValue(name, out var test)) { Console.Error.WriteLine("unknown anger test " + name + "; known: " + string.Join(", ", all.Keys)); return 2; }
            var before = Rec.Failures;
            try { test(); }
            catch (Exception ex) { Rec.Check("test ran without exceptions", false, ex.ToString()); }
            Console.WriteLine($"=== {name}: {(Rec.Failures == before ? "PASS" : "FAIL")}");
        }
        Console.WriteLine(Rec.Failures == 0 ? "ANGER ALL PASS" : $"{Rec.Failures} check(s) FAILED");
        return Rec.Failures == 0 ? 0 : 1;
    }

    /// <summary>Runs <paramref name="body"/> with a fresh folder, a started app and an Explorer window on the folder.</summary>
    public static void Case(string id, string desc, Action<string> files, Action<Sess> body, Rectangle? box = null,
        Dictionary<string, string>? env = null, bool openExplorer = true, bool skipOriginals = false)
    {
        Rec.Begin(id, desc);
        var work = FileKit.Fresh(Root, id);
        files(work);
        var inputs = Directory.EnumerateFileSystemEntries(work).ToDictionary(p => p, FileKit.HashAny);
        var s = new Sess { Work = work };
        try
        {
            s.App = AngerApp.Start(AppExe, Path.Combine(Root, id + ".log"), env);
            Thread.Sleep(500);
            if (openExplorer) s.Ex = ExplorerWin.Open(work, box ?? PrimaryBox);
            body(s);
            if (skipOriginals) { }
            else if (inputs.Count > 20)
            {
                var bad = inputs.Where(kv => !(File.Exists(kv.Key) || Directory.Exists(kv.Key)) || FileKit.HashAny(kv.Key) != kv.Value).Select(kv => Path.GetFileName(kv.Key)).ToList();
                Rec.Check($"all {inputs.Count} originals in place and byte-identical", bad.Count == 0, string.Join(",", bad.Take(5)));
            }
            else foreach (var kv in inputs)
            {
                var exists = File.Exists(kv.Key) || Directory.Exists(kv.Key);
                if (!exists) { Rec.Check("original still in place: " + Path.GetFileName(kv.Key), false, "MISSING (moved or deleted)"); continue; }
                Rec.Check("original byte-identical: " + Path.GetFileName(kv.Key), FileKit.HashAny(kv.Key) == kv.Value);
            }
            Rec.Check("no [ERROR] in the app log", !s.Log.Contains("[ERROR]"), s.Log.Split('\n').FirstOrDefault(l => l.Contains("[ERROR]"))?.Trim() ?? "");
            Rec.Check("wheel hidden at the end", Sess.Wait(() => !WheelInfo.Visible(), 3000));
        }
        finally
        {
            Sess.Keys("shift+ctrl", false);
            TrayKit.CloseOverflow();
            Pointer.LeftUp();
            try { s.Ex?.Close(); } catch (Exception) { }
            s.App?.Kill();
            if (!Directory.Exists(work)) { }
            Thread.Sleep(300);
        }
    }

    public static void Shot(Sess s, string name, Point? at = null, int half = 330)
    {
        var path = Path.Combine(ShotDir, name + ".png");
        Point c = at ?? WheelInfo.Centre(s.Log) ?? new Point(Pointer.Position.X, Pointer.Position.Y);
        Shot2(path, c, half);
        Rec.Note("screenshot: " + path);
    }

    private static void Shot2(string path, Point c, int half) => DragHarness.Shot.Around(c, half, path);

    /// <summary>A minimal valid one-page PDF at Root\fx\minimal.pdf (used by the image + PDF scenarios).</summary>
    private static void MakeFixtures()
    {
        var fx = Path.Combine(Root, "fx");
        Directory.CreateDirectory(fx);
        var pdf = Path.Combine(fx, "minimal.pdf");
        if (File.Exists(pdf)) return;
        var objs = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream",
        };
        var sb = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objs.Length; i++) { offsets.Add(sb.Length); sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
        var xref = sb.Length;
        sb.Append($"xref\n0 {objs.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:0000000000} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objs.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(pdf, System.Text.Encoding.ASCII.GetBytes(sb.ToString()));
    }

    public static Dictionary<string, Action> Tests()
    {
        var d = new Dictionary<string, Action>();
        RegisterTests(d);
        RegisterTests2(d);
        RegisterTests3(d);
        RegisterTests4(d);
        RegisterTests5(d);
        RegisterTests6(d);
        RegisterTests7(d);
        RegisterTests8(d);
        RegisterTests8b(d);
        return d;
    }
}
