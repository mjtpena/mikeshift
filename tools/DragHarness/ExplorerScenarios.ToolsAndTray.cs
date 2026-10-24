using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace DragHarness;

/// <summary>Scenarios for the tools tiles from Explorer, the wheel callout, a sweep along the taskbar, latency, and the real tray flyout and toast.</summary>
internal static partial class ExplorerScenarios
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, System.Text.StringBuilder sb, int max);

    /// <summary>Screen rectangles of the visible top-level windows of class <paramref name="cls"/> (the shell's drag image is one).</summary>
    private static List<Rectangle> WindowsOfClass(string cls)
    {
        var result = new List<Rectangle>();
        ExplorerNative.EnumWindows((h, _) =>
        {
            var sb = new System.Text.StringBuilder(128);
            GetClassNameW(h, sb, 128);
            if (sb.ToString() == cls && Native.IsWindowVisible(h)) result.Add(RectOf(h));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>A large, detailed JPEG at maximum quality so that Compress has something to save.</summary>
    private static void MakeBigJpg(string path, int w = 2400, int h = 1600)
    {
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            using var b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(255, 69, 0), Color.RoyalBlue, 35f);
            g.FillRectangle(b, 0, 0, w, h);
            var rnd = new Random(7);
            for (var i = 0; i < 3000; i++)
            {
                using var br = new SolidBrush(Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)));
                g.FillEllipse(br, rnd.Next(w), rnd.Next(h), rnd.Next(10, 90), rnd.Next(10, 90));
            }
        }
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ps = new EncoderParameters(1);
        ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 100L);
        bmp.Save(path, codec, ps);
    }

    private static string[] NewEntries(string dir, HashSet<string> before) =>
        Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).Where(n => n is not null && !before.Contains(n) && !n!.StartsWith('.')).Select(n => n!).ToArray();

    private static Rectangle Union(IEnumerable<Rectangle> rects) => rects.Aggregate(Rectangle.Union);

    private static void RegisterToolsAndTray(Dictionary<string, Action> d)
    {
        // ---- item 1: tools page from a real Explorer drag (Ctrl -> Compress, Resize 50%)
        d["tools-page"] = () => Case("tools-page", "Explorer: Shift-drag a big JPG, Ctrl -> Tools page: drop on Compress; then again on Resize 50%",
            dir => MakeBigJpg(Path.Combine(dir, "photo.jpg")), s =>
            {
                foreach (var tile in new[] { "Compress", "Resize 50%" })
                {
                    var before = Directory.GetFileSystemEntries(s.Work).Select(Path.GetFileName).Select(n => n!).ToHashSet();
                    var seen = s.Start("photo.jpg");
                    if (!Rec.Check($"[{tile}] wheel visible", seen)) { s.Abort(); return; }
                    s.WaitTiles(); Thread.Sleep(300);
                    Pointer.KeyDown(Native.VK_LCONTROL); Thread.Sleep(40); Pointer.KeyUp(Native.VK_LCONTROL);
                    Rec.Check($"[{tile}] Ctrl switched to the Tools page", s.App.WaitLog(t => t[s.LogMark..].Contains("page = Tool"), 1500));
                    Thread.Sleep(450);
                    var hit = s.ToTile(tile, true);
                    Rec.Check($"[{tile}] pointer on the tile highlighted it", hit, s.App.LastLine("highlight:"));
                    if (tile == "Compress") Shot(s, "tools-page-compress", null, 260);
                    s.Release();
                    var got = Sess.Wait(() => NewEntries(s.Work, before).Length > 0, 20000);
                    var made = NewEntries(s.Work, before);
                    Rec.Check($"[{tile}] an output appeared next to the original", got, string.Join(", ", made.Select(m => m + " (" + new FileInfo(Path.Combine(s.Work, m)).Length + " bytes)")));
                    Rec.Check($"[{tile}] log: Drop effect COPY", Regex.IsMatch(s.LogNew, @"Drop on " + Regex.Escape(tile) + @" .*effect=COPY"), Regex.Match(s.LogNew, @"Drop on.*").Value);
                    Rec.Check($"[{tile}] photo.jpg still listed by Explorer", s.Ex.HasItem("photo.jpg", 3000));
                    Sess.Wait(() => !WheelInfo.Visible(), 3000); Thread.Sleep(1500);
                }
            });

        // ---- item 2: callout pill with the real drag thumbnail, both pages, both halves
        d["callout"] = () => Case("callout", "Wheel callout pill with Explorer's real drag thumbnail: highlighted tile on Formats and Tools, pointer in the top and in the bottom half",
            dir => FileKit.MakeJpg(Path.Combine(dir, "photo.jpg")), s =>
            {
                var seen = s.Start("photo.jpg");
                if (!Rec.Check("wheel visible", seen)) { s.Abort(); return; }
                s.WaitTiles(); Thread.Sleep(500);
                var rect = WheelInfo.LastRect(s.Log)!.Value;
                void Probe(string label, string tile, bool tools, string shot, bool expectPillBelow)
                {
                    var hit = s.ToTile(tile, tools);
                    Rec.Check($"[{label}] pointer on '{tile}' highlighted it", hit);
                    Thread.Sleep(350);
                    var wheel = rect.Rect;
                    var sc = rect.Scale;
                    var pillY = expectPillBelow ? wheel.Top + (380 - 20) * sc : wheel.Top + 20 * sc;
                    var pill = new Rectangle((int)(wheel.Left + 60 * sc), (int)(pillY - 13 * sc), (int)(260 * sc), (int)(26 * sc));
                    var thumbs = WindowsOfClass("SysDragImage");
                    var overlap = thumbs.Where(t => t.IntersectsWith(pill)).ToList();
                    Rec.Check($"[{label}] callout pill is on the {(expectPillBelow ? "bottom" : "top")} margin and Explorer's drag thumbnail does not cover it",
                        thumbs.Count > 0 && overlap.Count == 0,
                        $"pill zone {pill}; drag-image windows: {(thumbs.Count == 0 ? "none found (judge from the screenshot)" : string.Join(" ", thumbs))}");
                    Shot(s, shot, WheelInfo.Centre(s.Log), 230);
                }
                Probe("formats, top half", "PNG", false, "callout-formats-top", true);
                Probe("formats, bottom half", "BMP", false, "callout-formats-bottom", false);
                Pointer.KeyDown(Native.VK_LCONTROL); Thread.Sleep(40); Pointer.KeyUp(Native.VK_LCONTROL);
                Rec.Check("Ctrl switched to the Tools page", s.App.WaitLog(t => t[s.LogMark..].Contains("page = Tool"), 1500));
                Thread.Sleep(500);
                Probe("tools, top half", "Compress", true, "callout-tools-top", true);
                Probe("tools, bottom half", "Strip EXIF", true, "callout-tools-bottom", false);
                s.Abort();
            });

        // ---- item 3: taskbar sweep on the primary monitor
        d["taskbar"] = () =>
        {
            var prim = Monitors()[0];
            var W = prim.WorkingArea; var B = prim.Bounds;
            if (B.Bottom == W.Bottom) { Rec.Begin("taskbar", "no bottom taskbar on the primary monitor"); Rec.Check("a bottom taskbar exists", false); return; }
            var y = B.Bottom - 12;
            var xs = new[] { 150, 700, 1250, 1720, 2300, 2900, 3300 }.Select(x => B.Left + Math.Min(x, B.Width - 40)).ToArray();
            Case("taskbar", $"Shift started with the pointer over the taskbar (y={y}) at several x on the primary monitor: tile ring clear of the taskbar, drop on PNG works",
                dir => FileKit.MakeJpg(Path.Combine(dir, "photo.jpg")), s =>
                {
                    foreach (var x in xs)
                    {
                        var png = Path.Combine(s.Work, "photo.png");
                        if (File.Exists(png)) File.Delete(png);
                        var seen = s.Start("photo.jpg", dest: new Point(x, y));
                        if (!Rec.Check($"[x={x}] wheel visible", seen)) { s.Abort(); Thread.Sleep(1200); continue; }
                        s.WaitTiles();
                        var r = WheelInfo.LastRect(s.Log)!.Value;
                        var rect = r.Rect; var scale = r.Scale;
                        var ring = (40 + 11) * scale;
                        var clear = rect.Left + ring >= W.Left - 1 && rect.Top + ring >= W.Top - 1 && rect.Right - ring <= W.Right + 1 && rect.Bottom - ring <= W.Bottom + 1;
                        Rec.Check($"[x={x}] tile ring entirely above the taskbar (inside the work area)", clear, $"rect {rect} (disc bottom {rect.Bottom - ring:0}), work area bottom {W.Bottom}, pointer ({x},{y}) {(rect.Contains(x, y) ? "inside the window" : "OUTSIDE the window")}");
                        Thread.Sleep(450);
                        var hit = s.ToTile("PNG");
                        Rec.Check($"[x={x}] PNG highlighted", hit);
                        var bottomHit = s.ToTile("BMP");
                        Rec.Check($"[x={x}] the bottom tile (BMP) can be hit too", bottomHit);
                        if (x == xs[0] || x == xs[3] || x == xs[^1]) Shot(s, $"taskbar-{x}", WheelInfo.Centre(s.Log), 260);
                        s.ToTile("PNG");
                        s.Release();
                        Rec.Check($"[x={x}] PNG created", s.WaitFile(png, 10000));
                        Sess.Wait(() => !WheelInfo.Visible(), 2500);
                        Thread.Sleep(400);
                    }
                }, BoxOn(prim));
        };

        // ---- latency (log timestamps) on each monitor and idle cost
        foreach (var which in new[] { "primary", "left" })
        {
            var name = "latency-" + which;
            d[name] = () =>
            {
                var mon = which == "primary" ? Monitors()[0] : Monitors().First(m => !m.Primary);
                var fromLog = new List<double>(); var fromPoll = new List<long>();
                Case(name, $"Latency: 20 Shift-drags from a real Explorer on the {which} monitor; Shift SendInput to 'wheel shown pending' in the log", dir => FileKit.MakeJpg(Path.Combine(dir, "photo.jpg")), s =>
                {
                    for (var i = 0; i < 21; i++)
                    {
                        var seen = s.Start("photo.jpg", settleMs: 100);
                        if (!seen) { Rec.Check($"drag {i}: wheel visible", false); s.Abort(); Thread.Sleep(1500); continue; }
                        var line = Sess.Wait(() => s.LogNew.Contains("wheel shown pending"), 1000) ? s.LogNew.Split('\n').Last(l => l.Contains("wheel shown pending")) : "";
                        var m = Regex.Match(line, @"^(\d\d):(\d\d):(\d\d)\.(\d\d\d)");
                        if (m.Success)
                        {
                            var logT = new TimeSpan(0, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value));
                            var ms = (logT - s.ShiftAt.TimeOfDay).TotalMilliseconds;
                            if (i > 0) { fromLog.Add(ms); fromPoll.Add(s.LatencyMs); }
                            else Rec.Note($"first drag after start (cold): log {ms:0} ms, polled {s.LatencyMs} ms");
                        }
                        s.Abort();
                        Sess.Wait(() => !WheelInfo.Visible(), 3000);
                        Thread.Sleep(900);
                    }
                    fromLog.Sort(); var poll = fromPoll.OrderBy(x => x).ToList();
                    string Stats(IList<double> v) => v.Count == 0 ? "n/a" : $"min {v[0]:0}  median {v[v.Count / 2]:0}  p95 {v[(int)(v.Count * 0.95)]:0}  max {v[^1]:0} ms (n={v.Count})";
                    Rec.Note("Shift -> 'wheel shown pending' (log timestamps): " + Stats(fromLog));
                    Rec.Note("Shift -> window visible (2 ms polling): " + Stats(poll.Select(x => (double)x).ToList()));
                    Rec.Check("20 drags: every wheel shown within 100 ms of Shift (log)", fromLog.Count == 20 && fromLog[^1] < 100, Stats(fromLog));

                    if (which == "primary")
                    {
                        // idle cost: after the drags, then 2 minutes of nothing
                        var p = s.App.Proc;
                        Rec.Note("idle measurement starts: 120 s without touching the app");
                        Thread.Sleep(120_000);
                        p.Refresh();
                        var cpu0 = p.TotalProcessorTime; var t0 = Stopwatch.StartNew();
                        Thread.Sleep(30_000);
                        p.Refresh();
                        var cpu = (p.TotalProcessorTime - cpu0).TotalSeconds / t0.Elapsed.TotalSeconds;
                        double priv = -1;
                        try
                        {
                            var cat = new PerformanceCounterCategory("Process V2");
                            var inst = cat.GetInstanceNames().FirstOrDefault(n => n.EndsWith(":" + p.Id, StringComparison.Ordinal));
                            if (inst is not null) priv = new PerformanceCounter("Process V2", "Working Set - Private", inst, true).NextValue();
                        }
                        catch (Exception) { }
                        Rec.Note($"IDLE after 2 min: working set {p.WorkingSet64 / 1048576.0:0.0} MB, private working set {(priv < 0 ? "n/a" : (priv / 1048576.0).ToString("0.0") + " MB")}, CPU over 30 s {cpu * 100:0.00} % of one core, threads {p.Threads.Count}, handles {p.HandleCount}");
                        Rec.Check("idle CPU below 1 % of one core", cpu < 0.01, $"{cpu * 100:0.00} %");
                    }
                }, BoxOn(mon));
            };
        }

        // ---- item 7: real tray, Fluent flyout, left click -> picker -> click-mode wheel -> toast -> Show in folder
        d["tray-flyout"] = () =>
        {
            var pickDir = Path.Combine(Root, "pickdir");
            if (Directory.Exists(pickDir)) Directory.Delete(pickDir, true);
            Directory.CreateDirectory(pickDir);
            FileKit.MakeJpg(Path.Combine(pickDir, "pick1.jpg"), text: "pick1");
            FileKit.MakeJpg(Path.Combine(pickDir, "pick2.jpg"), text: "pick2");
            var hashes = Directory.GetFiles(pickDir).ToDictionary(f => f, FileKit.Sha);
            Case("tray-flyout", "Real tray icon: right click -> Fluent flyout; left click -> Convert files picker (own folder) -> click-mode wheel -> Enter -> toast -> Show in folder",
                dir => { }, s =>
                {
                    var (icon, where) = TrayKit.FindIcon();
                    if (!Rec.Check("tray icon found through UI Automation", icon is not null, where)) return;
                    var r = icon!.Current.BoundingRectangle;
                    var c = new Point((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2));
                    Pointer.Glide(c.X, c.Y, 20, 3); Thread.Sleep(250);
                    Pointer.RightDown(); Thread.Sleep(40); Pointer.RightUp();
                    var items = new List<AutomationElement>();
                    var ok = Sess.Wait(() =>
                    {
                        items = AppElements(s.App.Proc.Id, ControlType.MenuItem);
                        return items.Count >= 4;
                    }, 5000);
                    var names = items.Select(i => { try { return i.Current.Name; } catch (Exception) { return ""; } }).Where(n => n.Length > 0).ToList();
                    Rec.Check("right click opened the flyout with menu items", ok, string.Join(" | ", names));
                    if (ok)
                    {
                        Thread.Sleep(500);
                        var rects = items.Select(i => { var b = i.Current.BoundingRectangle; return new Rectangle((int)b.Left, (int)b.Top, (int)b.Width, (int)b.Height); }).Where(b => b.Width > 0).ToList();
                        var u = Union(rects); u.Inflate(14, 14);
                        // the header row ("MikeShift - Shift-drag files to convert") sits above the first item
                        u = Rectangle.Union(u, new Rectangle(u.Left, u.Top - 70, u.Width, 70));
                        u.Intersect(Screen.FromPoint(c).WorkingArea);
                        DragHarness.Shot.Region(u, Path.Combine(ShotDir, "tray-flyout-right-click.png"));
                        Rec.Note($"screenshot: {Path.Combine(ShotDir, "tray-flyout-right-click.png")} (region {u})");
                        foreach (var want in new[] { "Convert files", "Settings", "Exit" })
                            Rec.Check($"flyout has '{want}'", names.Any(n => n.Contains(want, StringComparison.OrdinalIgnoreCase)));
                        Pointer.KeyDown(Native.VK_ESCAPE); Thread.Sleep(30); Pointer.KeyUp(Native.VK_ESCAPE);
                        var closed = Sess.Wait(() => AppElements(s.App.Proc.Id, ControlType.MenuItem).Count == 0, 3000);
                        Rec.Check("Esc closed the flyout", closed, closed ? "" : $"{AppElements(s.App.Proc.Id, ControlType.MenuItem).Count} items still reported; foreground window class: {FgClass()}");
                        if (!closed) { Thread.Sleep(800); DragHarness.Shot.Region(u, Path.Combine(ShotDir, "tray-flyout-after-esc.png")); }
                    }
                    TrayKit.CloseOverflow();
                    Thread.Sleep(500);

                    // left click -> picker
                    s.LogMark = s.App.LogLength();
                    var file = Path.Combine(pickDir, "pick1.jpg");
                    var done = TrayKit.ConvertViaTray(s.App, file, out var detail);
                    Rec.Check("left click on the tray icon opened the Convert files picker; picked pick1.jpg", done, detail);
                    if (!done) return;
                    var shown = s.App.WaitLog(t => t.Length > s.LogMark && t[s.LogMark..].Contains("click-mode wheel shown"), 10000);
                    Rec.Check("click-mode wheel opened", shown, s.App.LastLine("click-mode wheel shown"));
                    if (!shown) return;
                    Thread.Sleep(900);
                    Shot(s, "tray-flyout-click-wheel", WheelInfo.LastRect(s.Log, click: true) is { } cr ? new Point(cr.Rect.Left + cr.Rect.Width / 2, cr.Rect.Top + cr.Rect.Height / 2) : null, 260);
                    Pointer.KeyDown(0x27); Thread.Sleep(30); Pointer.KeyUp(0x27); Thread.Sleep(500);
                    Rec.Check("Right arrow highlighted PNG", LastHl(s.LogNew) == "PNG", LastHl(s.LogNew) ?? "none");
                    var pickT = Stopwatch.StartNew();
                    Pointer.KeyDown(0x0D); Thread.Sleep(30); Pointer.KeyUp(0x0D);
                    var png = Path.Combine(pickDir, "pick1.png");
                    Rec.Check("Enter converted: pick1.png next to the original", s.WaitFile(png, 15000), $"{pickT.ElapsedMilliseconds} ms");
                    Rec.Check("original byte-identical", FileKit.Sha(file) == hashes[file]);

                    // toast
                    AutomationElement? show = null;
                    var toast = Sess.Wait(() => (show = AppElements(s.App.Proc.Id, ControlType.Button).FirstOrDefault(b => { try { return b.Current.Name.Contains("Show in folder"); } catch (Exception) { return false; } })) is not null, 8000);
                    Rec.Check("toast with a 'Show in folder' button is on screen", toast, string.Join(" | ", s.ToastTexts(100)));
                    if (!toast) return;
                    Thread.Sleep(500);
                    var texts = s.ToastTexts(300);
                    Rec.Note("toast text: " + string.Join(" | ", texts));
                    var tw = TopLevelOf(show!);
                    var tr = tw.Current.BoundingRectangle;
                    var shotRect = new Rectangle((int)tr.Left - 12, (int)tr.Top - 12, (int)tr.Width + 24, (int)tr.Height + 24);
                    DragHarness.Shot.Region(shotRect, Path.Combine(ShotDir, "tray-flyout-toast.png"));
                    Rec.Note($"screenshot: {Path.Combine(ShotDir, "tray-flyout-toast.png")} (region {shotRect})");
                    var br = show!.Current.BoundingRectangle;
                    Pointer.Glide((int)(br.Left + br.Width / 2), (int)(br.Top + br.Height / 2), 20, 3); Thread.Sleep(150);
                    Pointer.LeftDown(); Thread.Sleep(40); Pointer.LeftUp();
                    // Explorer opens on the folder with the output selected
                    var leaf = Path.GetFileName(pickDir);
                    AutomationElement? win = null;
                    var opened = Sess.Wait(() =>
                    {
                        foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ClassNameProperty, "CabinetWClass")))
                        { try { if (w.Current.Name.Contains(leaf, StringComparison.OrdinalIgnoreCase)) { win = w; return true; } } catch (Exception) { } }
                        return false;
                    }, 8000);
                    Rec.Check("'Show in folder' opened a real Explorer window on the output's folder", opened, leaf);
                    if (opened)
                    {
                        var ex = new ExplorerWin { Win = win!, Hwnd = new IntPtr(win!.Current.NativeWindowHandle), Folder = pickDir };
                        ex.HasItem("pick1.png", 8000);
                        var okSel = Sess.Wait(() => ex.Selected().Contains("pick1.png"), 6000);
                        var sel = ex.Selected();
                        Rec.Check("the output pick1.png is selected in that window", okSel, "selected: " + string.Join(",", sel) + "; items: " + string.Join(",", ex.VisibleItems()));
                        ex.Close();
                    }
                }, openExplorer: false, skipOriginals: true, env: new Dictionary<string, string> { ["MIKESHIFT_PICKER_FOLDER"] = pickDir });
            try { Directory.Delete(pickDir, true); } catch (IOException) { }
        };
    }

    private static uint FgPid() { ExplorerNative.GetWindowThreadProcessId(ExplorerNative.GetForegroundWindow(), out var p); return p; }

    private static string FgClass()
    {
        var sb = new System.Text.StringBuilder(128);
        GetClassNameW(ExplorerNative.GetForegroundWindow(), sb, 128);
        return sb.ToString();
    }

    private static List<AutomationElement> AppElements(int pid, ControlType type)
    {
        var result = new List<AutomationElement>();
        try
        {
            foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid)))
            {
                string cls; try { cls = w.Current.ClassName; } catch (Exception) { continue; }
                if (cls == "MikeShiftWheel") continue;
                foreach (AutomationElement e in w.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, type)))
                {
                    try { if (!e.Current.IsOffscreen) result.Add(e); } catch (Exception) { }
                }
            }
        }
        catch (Exception) { }
        return result;
    }

    private static AutomationElement TopLevelOf(AutomationElement e)
    {
        var walker = TreeWalker.ControlViewWalker;
        var cur = e;
        while (true)
        {
            var parent = walker.GetParent(cur);
            if (parent is null || parent == AutomationElement.RootElement) return cur;
            cur = parent;
        }
    }
}
