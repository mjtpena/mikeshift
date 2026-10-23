using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DragHarness;

internal static partial class Anger
{
    private static Screen[] Monitors() => Screen.AllScreens.OrderByDescending(s => s.Primary).ToArray();

    private static Rectangle BoxOn(Screen s) => new(s.WorkingArea.Left + 60, s.WorkingArea.Top + 60, Math.Min(1100, s.WorkingArea.Width - 700), 760);

    /// <summary>Drags photo.jpg from a real Explorer on <paramref name="mon"/> to each edge/corner position and drops on PNG each time.</summary>
    private static void EdgeRun(Screen mon, string tag)
    {
        var W = mon.WorkingArea; var B = mon.Bounds;
        var cx = (W.Left + W.Right) / 2; var cy = (W.Top + W.Bottom) / 2;
        var spots = new List<(string Name, Point P)>
        {
            ("top-left", new(W.Left + 8, W.Top + 8)), ("top", new(cx, W.Top + 8)), ("top-right", new(W.Right - 8, W.Top + 8)),
            ("right", new(W.Right - 8, cy)), ("bottom-right", new(W.Right - 8, W.Bottom - 8)), ("bottom", new(cx, W.Bottom - 8)),
            ("bottom-left", new(W.Left + 8, W.Bottom - 8)), ("left", new(W.Left + 8, cy)),
            ("extreme-top-left", new(B.Left + 1, B.Top + 1)), ("extreme-bottom-right", new(B.Right - 2, B.Bottom - 2)),
        };
        if (B.Bottom > W.Bottom) spots.Add(("taskbar (bottom)", new(cx, B.Bottom - 12)));
        else if (B.Top < W.Top) spots.Add(("taskbar (top)", new(cx, B.Top + 12)));
        else if (B.Left < W.Left) spots.Add(("taskbar (left)", new(B.Left + 12, cy)));
        else if (B.Right > W.Right) spots.Add(("taskbar (right)", new(B.Right - 12, cy)));
        else Rec.Note($"({tag}: no taskbar on this monitor, work area == bounds)");

        Case("x2-edges-" + tag, $"Explorer on {tag} ({B}, work {W}): Shift near every edge/corner of that monitor, tiles hit",
            dir => FileKit.MakeJpg(Path.Combine(dir, "photo.jpg")), s =>
            {
                foreach (var (name, p) in spots)
                {
                    var png = Path.Combine(s.Work, "photo.png");
                    if (File.Exists(png)) File.Delete(png);
                    var seen = s.Start("photo.jpg", dest: p);
                    if (!Rec.Check($"[{name} {p.X},{p.Y}] wheel visible", seen)) { s.Abort(); Thread.Sleep(1200); continue; }
                    s.WaitTiles();
                    var rect = WheelInfo.LastRect(s.Log)!.Value.Rect;
                    var scale = WheelInfo.LastRect(s.Log)!.Value.Scale;
                    var inside = rect.Left >= W.Left - 1 && rect.Top >= W.Top - 1 && rect.Right <= W.Right + 1 && rect.Bottom <= W.Bottom + 1;
                    if (!W.Contains(p)) inside = B.Contains(rect) && rect.Contains(p); // cursor over the taskbar: the wheel must still contain it
                    Rec.Check($"[{name}] wheel window inside the work area", inside, $"rect {rect}, dpi scale {scale:0.##}, latency {s.LatencyMs} ms");
                    Thread.Sleep(420);
                    var hit = s.ToTile("PNG");
                    Rec.Check($"[{name}] tile PNG highlighted by moving onto it", hit);
                    if (name is "top-left" or "bottom-right" or "taskbar (bottom)" or "extreme-top-left") Shot(s, $"x2-{tag}-{name.Replace(' ', '-').Replace("(", "").Replace(")", "")}", WheelInfo.Centre(s.Log), 260);
                    s.Release();
                    var ok = s.WaitFile(png, 10000);
                    Rec.Check($"[{name}] PNG created", ok);
                    Sess.Wait(() => !WheelInfo.Visible(), 2500);
                    Thread.Sleep(300);
                }
            }, BoxOn(mon));
    }

    private static void RegisterTests2(Dictionary<string, Action> d)
    {
        d["x2-primary"] = () => EdgeRun(Monitors()[0], "primary");
        d["x2-left"] = () => EdgeRun(Monitors().First(m => !m.Primary), "left");

        d["x2-cross"] = () =>
        {
            var left = Monitors().First(m => !m.Primary); var prim = Monitors()[0];
            Case("x2-cross", "Explorer on the left monitor: drag across the boundary to the primary, Shift there; and Shift 10 px before the boundary, wheel clamped, pointer crosses out and back",
                dir => FileKit.MakeJpg(Path.Combine(dir, "photo.jpg")), s =>
                {
                    var y = left.WorkingArea.Top + 500;
                    // (a) Shift pressed on the other monitor
                    var seen = s.Start("photo.jpg", dest: new Point(prim.WorkingArea.Left + 500, y));
                    Rec.Check("(a) wheel visible on the primary monitor after crossing the boundary", seen);
                    if (!seen) { s.Abort(); return; }
                    s.WaitTiles();
                    var r = WheelInfo.LastRect(s.Log)!.Value.Rect;
                    Rec.Check("(a) wheel is on the primary monitor", prim.Bounds.Contains(r), r.ToString());
                    Thread.Sleep(420);
                    DropOn(s, "PNG", false, "x2-cross-a", "photo.png");
                    File.Delete(Path.Combine(s.Work, "photo.png"));
                    Sess.Wait(() => !WheelInfo.Visible(), 2500); Thread.Sleep(600);

                    // (b) Shift 10 px before the boundary on the left monitor, then out across the boundary and back
                    seen = s.Start("photo.jpg", dest: new Point(left.WorkingArea.Right - 10, y));
                    Rec.Check("(b) wheel visible 10 px from the boundary", seen);
                    if (!seen) { s.Abort(); return; }
                    s.WaitTiles();
                    r = WheelInfo.LastRect(s.Log)!.Value.Rect;
                    Rec.Check("(b) wheel clamped inside the left monitor", left.WorkingArea.Contains(r), r.ToString());
                    Thread.Sleep(420);
                    Pointer.Glide(prim.WorkingArea.Left + 400, y, 10, 4);
                    Thread.Sleep(300);
                    Rec.Check("(b) DragLeave when the pointer left the wheel across the boundary", s.LogNew.Contains("DragLeave"));
                    Rec.Check("(b) wheel stays while the button is down", WheelInfo.Visible());
                    Shot(s, "x2-cross-b-out", new Point(0, y), 500);
                    DropOn(s, "PNG", false, "", "photo.png");
                });
        };

        // ---- item 3: page toggles and endings
        d["x3a"] = () => Case("x3a-toggles", "Explorer drag: Ctrl -> Tools, Shift -> Formats, wheel ticks, Ctrl held (autorepeat) inside and outside the wheel",
            dir => FileKit.MakeJpg(Path.Combine(dir, "photo.jpg")), s =>
            {
                var seen = s.Start("photo.jpg");
                if (!Rec.Check("wheel visible", seen)) { s.Abort(); return; }
                s.WaitTiles(); Thread.Sleep(450);
                int PageChanges() => Regex.Matches(s.LogNew, @"page = (Tool|Format)").Count;
                string LastPage() => Regex.Matches(s.LogNew, @"page = (Tool|Format)").LastOrDefault()?.Groups[1].Value ?? "Format(start)";
                var wait = () => Thread.Sleep(500);

                Pointer.KeyDown(Native.VK_LCONTROL); Thread.Sleep(40); Pointer.KeyUp(Native.VK_LCONTROL); wait();
                Rec.Check("Ctrl press -> Tools", LastPage() == "Tool", LastPage());
                // a fresh Shift press: release, press again
                Pointer.KeyUp(Native.VK_LSHIFT); Thread.Sleep(60); Pointer.KeyDown(Native.VK_LSHIFT); wait();
                Rec.Check("fresh Shift press -> Formats", LastPage() == "Format", LastPage());
                Pointer.Wheel(-120); wait();
                Rec.Check("wheel tick -> Tools", LastPage() == "Tool", LastPage());
                Pointer.Wheel(120); wait();
                Rec.Check("wheel tick -> Formats", LastPage() == "Format", LastPage());
                Pointer.Wheel(-30); Pointer.Wheel(-30); Pointer.Wheel(-30); wait();
                Rec.Check("sub-notch wheel deltas stay put until a full notch", LastPage() == "Format", LastPage());
                Pointer.Wheel(-30); wait();
                Rec.Check("four 30-unit deltas make one notch -> Tools", LastPage() == "Tool", LastPage());
                Pointer.Wheel(120); wait();

                // Ctrl held with typematic autorepeat while the pointer sits still INSIDE the wheel (on the PNG tile)
                s.ToTile("PNG");
                var before = PageChanges();
                for (var i = 0; i < 25; i++) { Pointer.KeyDown(Native.VK_LCONTROL); Thread.Sleep(40); }
                Pointer.KeyUp(Native.VK_LCONTROL); wait();
                Rec.Check("Ctrl autorepeat inside the wheel toggles exactly once", PageChanges() - before == 1, $"{PageChanges() - before} toggle(s), now {LastPage()}");
                // OUTSIDE the wheel (pointer 300 px away, still dragging)
                var c = WheelInfo.Centre(s.Log)!.Value;
                Pointer.Glide(c.X + 330, c.Y + 20, 10, 5); Thread.Sleep(300);
                Pointer.Wheel(120); wait(); // back to Formats so that Ctrl has something to change
                var page0 = LastPage(); before = PageChanges();
                for (var i = 0; i < 25; i++) { Pointer.KeyDown(Native.VK_LCONTROL); Thread.Sleep(40); }
                Pointer.KeyUp(Native.VK_LCONTROL); wait();
                Rec.Check("Ctrl autorepeat outside the wheel toggles exactly once", PageChanges() - before == 1, $"{PageChanges() - before} toggle(s), {page0} -> {LastPage()}");
                Rec.Check("the wheel is still shown after all that", WheelInfo.Visible());
                Shot(s, "x3a-toggles", c, 300);
                Pointer.Glide(c.X, c.Y, 10, 5);
                s.Esc(); Thread.Sleep(300); s.Release();
            });

        d["x3b"] = () => Case("x3b-release-empty", "Release over empty space in ANOTHER real Explorer folder: Explorer's own drop proceeds, nothing converted by us",
            dir => FileKit.MakeJpg(Path.Combine(dir, "photo.jpg")), s =>
            {
                var dest = FileKit.Fresh(Root, "x3b-dest");
                var other = ExplorerWin.Open(dest, new Rectangle(2650, 100, 700, 800));
                try
                {
                    Native.SetForegroundWindow(s.Ex.Hwnd);
                    var oc = new Point(2650 + 350, 100 + 400);
                    var seen = s.Start("photo.jpg", dest: new Point(oc.X - 160, oc.Y));
                    if (!Rec.Check("wheel visible over the other Explorer window", seen)) { s.Abort(); return; }
                    s.WaitTiles(); Thread.Sleep(450);
                    Pointer.Glide(oc.X + 300, oc.Y + 200, 10, 5); Thread.Sleep(300);
                    Shot(s, "x3b-over-other-explorer", oc, 420);
                    s.Release();
                    var moved = Path.Combine(dest, "photo.jpg");
                    var ok = s.WaitFile(moved, 8000);
                    Rec.Check("Explorer moved photo.jpg into the other folder (Shift = move, Explorer's rule)", ok);
                    Rec.Check("the original left the source folder, as Explorer's move does", !File.Exists(Path.Combine(s.Work, "photo.jpg")));
                    Thread.Sleep(2500);
                    Rec.Check("Shiftvert converted nothing (no png in either folder)", !Directory.EnumerateFiles(dest, "*.png").Any() && !Directory.EnumerateFiles(s.Work, "*.png").Any());
                    Rec.Check("log: no job was enqueued", !s.Log.Contains("job "));
                    Rec.Check("log: released over nothing, dismissed", s.Log.Contains("released over nothing") || s.Log.Contains("DragLeave"));
                }
                finally { other.Close(); try { Directory.Delete(dest, true); } catch (IOException) { } }
            }, skipOriginals: true);

        d["x3c"] = () => Case("x3c-esc", "Esc during a wheel: dismissed, Explorer's drag cancelled, nothing converted, file stays",
            dir => FileKit.MakeJpg(Path.Combine(dir, "photo.jpg")), s =>
            {
                var seen = s.Start("photo.jpg");
                if (!Rec.Check("wheel visible", seen)) { s.Abort(); return; }
                s.WaitTiles(); Thread.Sleep(450);
                s.ToTile("PNG");
                s.Esc();
                Rec.Check("wheel hidden after Esc", Sess.Wait(() => !WheelInfo.Visible(), 2500));
                Thread.Sleep(300); s.Release();
                Thread.Sleep(1500);
                Rec.Check("nothing converted", !Directory.EnumerateFiles(s.Work, "*.png").Any());
                Rec.Check("log: no job", !s.Log.Contains("job "));
            });

        d["x3d"] = () => Case("x3d-hub", "Release on the hub: nothing converted",
            dir => FileKit.MakeJpg(Path.Combine(dir, "photo.jpg")), s =>
            {
                var seen = s.Start("photo.jpg");
                if (!Rec.Check("wheel visible", seen)) { s.Abort(); return; }
                s.WaitTiles(); Thread.Sleep(450);
                var c = WheelInfo.Centre(s.Log)!.Value;
                Pointer.Glide(c.X + 60, c.Y, 4, 10); Pointer.Glide(c.X, c.Y, 4, 10); Thread.Sleep(300);
                Shot(s, "x3d-hub", c, 300);
                s.Release();
                Sess.Wait(() => !WheelInfo.Visible(), 3000);
                Thread.Sleep(1500);
                Rec.Check("nothing converted", !Directory.EnumerateFiles(s.Work, "*.png").Any());
                Rec.Check("log: released on the hub without a tile, dismissed", s.Log.Contains("released on the wheel without a tile") || s.Log.Contains("Drop on no tile"), s.App.LastLine("dismiss"));
            });

        d["x3e"] = () => Case("x3e-shiftclick", "Shift-click rule: after a conversion, a Shift-click selection including the produced file drops it",
            dir => { FileKit.MakeJpg(Path.Combine(dir, "a.jpg")); FileKit.MakeJpg(Path.Combine(dir, "b.jpg")); }, s =>
            {
                var seen = s.Start("a.jpg");
                if (!Rec.Check("first drag: wheel visible", seen)) { s.Abort(); return; }
                DropOn(s, "PNG", false, "", "a.png");
                Sess.Wait(() => !WheelInfo.Visible(), 2500);
                Thread.Sleep(700);
                // Explorer order: a.jpg, a.png, b.jpg. Select b.jpg, then Shift-click a.png (range) and drag with Shift down at mouse-down.
                s.Ex.Select("b.jpg");
                s.Ex.HasItem("a.png");
                seen = s.Start("a.png", pre: "shift", during: "");
                if (!Rec.Check("second drag: wheel visible", seen)) { s.Abort(); return; }
                s.WaitTiles(); Thread.Sleep(450);
                var m = Regex.Match(s.LogNew, @"DragEnter .*paths=(\d+)");
                var rule = s.LogNew.Contains("Shift-click rule applied");
                Rec.Check("Explorer dragged 2 items (a.png + b.jpg)", m.Success && m.Groups[1].Value == "2", m.Value);
                Rec.Check("Shift-click rule dropped the produced a.png from the selection", rule, Regex.Match(s.LogNew, @"Shift-click rule.*").Value);
                s.ToTile("PNG");
                s.Release();
                Rec.Check("b.png written", s.WaitFile(Path.Combine(s.Work, "b.png")));
                Thread.Sleep(1500);
                Rec.Check("a.png was not re-converted (no 'a 2.png')", !File.Exists(Path.Combine(s.Work, "a 2.png")));
            });
    }
}
