using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace DragHarness;

internal static partial class Anger
{
    public static AutomationElement? FindTop(string nameContains, int timeoutMs = 15000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
                {
                    string n; try { n = w.Current.Name; } catch (Exception) { continue; }
                    if (n.Contains(nameContains, StringComparison.OrdinalIgnoreCase)) return w;
                }
            }
            catch (Exception) { }
            Thread.Sleep(200);
        }
        return null;
    }

    private static Rectangle RectOf(IntPtr hwnd)
    {
        AngerNative.GetWindowRect(hwnd, out var r);
        return new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private static void RegisterTests4(Dictionary<string, Action> d)
    {
        d["x4a"] = () => Case("x4a-notepad-text", "Text dragged out of a real Notepad window with Shift: no tiles, wheel hides, Notepad unharmed",
            dir => File.WriteAllText(Path.Combine(dir, "x4a.txt"), "Shiftvert anger test: some selected text to drag out of Notepad, long enough to press on."), s =>
            {
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Path.Combine(s.Work, "x4a.txt") + "\"") { UseShellExecute = true });
                var np = FindTop("x4a.txt - Notepad");
                if (!Rec.Check("Notepad window opened on the temp file", np is not null)) return;
                var hwnd = new IntPtr(np!.Current.NativeWindowHandle);
                try
                {
                    Thread.Sleep(600);
                    Native.SetForegroundWindow(hwnd);
                    Thread.Sleep(300);
                    var doc = np.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document))
                              ?? np.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                    if (!Rec.Check("found Notepad's text area through UI Automation", doc is not null)) return;
                    var r = doc!.Current.BoundingRectangle;
                    Pointer.Glide((int)r.Left + 300, (int)r.Top + 40, 14, 3);
                    Pointer.LeftDown(); Pointer.LeftUp();
                    Thread.Sleep(100);
                    Pointer.KeyDown(Native.VK_LCONTROL); Pointer.KeyDown(0x41); Pointer.KeyUp(0x41); Pointer.KeyUp(Native.VK_LCONTROL);
                    Thread.Sleep(500);
                    var press = new Point((int)r.Left + 60, (int)r.Top + 12);
                    // Where the first characters are: find them via the text pattern if available.
                    try
                    {
                        if (doc.TryGetCurrentPattern(TextPattern.Pattern, out var tp))
                        {
                            var sel = ((TextPattern)tp).GetSelection();
                            if (sel.Length > 0)
                            {
                                var rects = sel[0].GetBoundingRectangles();
                                if (rects.Length > 0) press = new Point((int)(rects[0].Left + 40), (int)(rects[0].Top + rects[0].Height / 2));
                            }
                        }
                    }
                    catch (Exception) { }
                    Rec.Note($"pressing on the selected text at {press}");
                    var topCls = DesktopKit.RootClassAt(press);
                    if (topCls != np.Current.ClassName)
                    {
                        Rec.Note($"NOT POSSIBLE HERE: another window ({topCls}) covers Notepad's text; the user's windows are not moved");
                        return;
                    }
                    s.LogMark = s.App.LogLength();
                    var overExplorer = new Point(s.Ex.Win.Current.BoundingRectangle is var br ? (int)(br.Left + br.Width / 2) : 1800, (int)(s.Ex.Win.Current.BoundingRectangle.Top + 500));
                    Pointer.Glide(press.X, press.Y, 14, 3); Thread.Sleep(200);
                    Pointer.LeftDown(); Thread.Sleep(400);
                    Pointer.Glide(press.X + 300, press.Y - 40, 8, 6); Thread.Sleep(200);
                    var sw = Stopwatch.StartNew();
                    Pointer.KeyDown(Native.VK_LSHIFT);
                    var seen = Sess.Wait(WheelInfo.Visible, 1500);
                    Rec.Note("pending disc visible: " + seen + (seen ? $" after {sw.ElapsedMilliseconds} ms" : ""));
                    var gone = Sess.Wait(() => !WheelInfo.Visible(), 2500);
                    Rec.Check("the wheel is gone within 2.5 s while the text drag continues", gone);
                    var l = s.LogNew;
                    Rec.Check("log: DragEnter classified the data as non-file or no DragEnter at all (never tiles)", !l.Contains("tiles:"), Regex.Match(l, @"DragEnter.*").Value);
                    Shot(s, "x4a-notepad", press, 330);
                    Pointer.Glide(overExplorer.X, overExplorer.Y, 12, 4); Thread.Sleep(200);
                    Pointer.LeftUp(); Thread.Sleep(30); Pointer.KeyUp(Native.VK_LSHIFT);
                    Thread.Sleep(800);
                    Rec.Check("Notepad is still alive", AngerNative.IsWindow(hwnd));
                    Rec.Check("no conversion job was started", !s.Log.Contains("job "));
                }
                finally
                {
                    Pointer.LeftUp(); Pointer.KeyUp(Native.VK_LSHIFT);
                    // Notepad is one window with tabs and may hold the user's own tabs: never move or close the window, close only OUR tab (Ctrl+W
                    // on the active tab, and only when the active tab is ours).
                    Native.SetForegroundWindow(hwnd); Thread.Sleep(400);
                    var title = FindTop("x4a.txt - Notepad", 1500);
                    if (title is not null && AngerNative.GetForegroundWindow() == hwnd)
                    {
                        Pointer.KeyDown(Native.VK_LCONTROL); Pointer.KeyDown(0x57); Pointer.KeyUp(0x57); Pointer.KeyUp(Native.VK_LCONTROL);
                        Thread.Sleep(800);
                    }
                }
            });

        d["x4b"] = () => Case("x4b-titlebar", "Dragging a real window's title bar with Shift held mid-drag: the window keeps moving, the disc hides, nothing else happens",
            dir => FileKit.MakeJpg(Path.Combine(dir, "photo.jpg")), s =>
            {
                var before = RectOf(s.Ex.Hwnd);
                var grab = new Point(before.Left + before.Width - 330, before.Top + 16);
                Pointer.Glide(grab.X, grab.Y, 14, 3); Thread.Sleep(200);
                s.LogMark = s.App.LogLength();
                Pointer.LeftDown(); Thread.Sleep(250);
                Pointer.Glide(grab.X - 120, grab.Y + 60, 6, 6);
                var sw = Stopwatch.StartNew();
                Pointer.KeyDown(Native.VK_LSHIFT);
                var seen = Sess.Wait(WheelInfo.Visible, 800);
                Rec.Note("pending disc seen: " + seen + (seen ? $" ({sw.ElapsedMilliseconds} ms)" : ""));
                var mid = RectOf(s.Ex.Hwnd);
                Pointer.Glide(grab.X - 300, grab.Y + 140, 6, 8);
                Rec.Check("the disc hid itself within 600 ms of arming", Sess.Wait(() => !WheelInfo.Visible(), 600 + 800));
                Thread.Sleep(300);
                var after = RectOf(s.Ex.Hwnd);
                Pointer.LeftUp(); Thread.Sleep(30); Pointer.KeyUp(Native.VK_LSHIFT);
                Thread.Sleep(500);
                var final = RectOf(s.Ex.Hwnd);
                Rec.Check("the window moved with the pointer before and AFTER the disc appeared", mid.X < before.X && after.X < mid.X, $"x: {before.X} -> {mid.X} -> {after.X} (final {final.X})");
                Rec.Check("log: HideSilently, no DragEnter", s.LogNew.Contains("HideSilently") && !s.LogNew.Contains("DragEnter at"));
                Rec.Check("the window is the same size (not resized/snapped by the pending disc)", final.Size == before.Size, $"{before.Size} vs {final.Size}");
                s.Ex.Place(BoxOn(Monitors()[0]));
            });

        d["x4c"] = () => Case("x4c-rubberband", "Rubber-band selection in Explorer with Shift pressed mid-drag: selection still works, the disc hides",
            dir => { foreach (var n in new[] { "r1.jpg", "r2.jpg", "r3.jpg" }) FileKit.MakeJpg(Path.Combine(dir, n)); }, s =>
            {
                var r1 = s.Ex.ItemRect("r1.jpg"); var r3 = s.Ex.ItemRect("r3.jpg");
                var win = s.Ex.Win.Current.BoundingRectangle;
                var start = new Point(Math.Min((int)win.Right - 60, r3.Right + 160), r1.Top - 5 > win.Top + 200 ? r1.Top - 5 : r1.Top + 4);
                Rec.Note($"rubber band from {start} over r1..r3 ({r1}, {r3})");
                Pointer.Glide(start.X, start.Y, 14, 3); Thread.Sleep(200);
                s.LogMark = s.App.LogLength();
                Pointer.LeftDown(); Thread.Sleep(200);
                Pointer.Glide(start.X - 60, start.Y + 40, 6, 6);
                Pointer.KeyDown(Native.VK_LSHIFT);
                var seen = Sess.Wait(WheelInfo.Visible, 800);
                Rec.Note("pending disc seen: " + seen);
                Pointer.Glide(r1.Left + 10, r3.Bottom - 5, 6, 8);
                Rec.Check("the disc hid", Sess.Wait(() => !WheelInfo.Visible(), 1500));
                Pointer.LeftUp(); Thread.Sleep(30); Pointer.KeyUp(Native.VK_LSHIFT);
                Thread.Sleep(600);
                var sel = s.Ex.Selected();
                Rec.Check("the rubber band selected the items", sel.Length >= 2, string.Join(",", sel));
                Rec.Check("log: HideSilently, no DragEnter", s.LogNew.Contains("HideSilently") && !s.LogNew.Contains("DragEnter at"));
                Rec.Check("nothing was converted", !Directory.EnumerateFiles(s.Work, "*.png").Any());
            });

        d["x4d"] = () =>
        {
            Rec.Begin("x4d-zip", "A file inside a .zip opened in Explorer -> 'Save the file first', nothing converted");
            var work = FileKit.Fresh(Root, "x4d-zip");
            var zip = Path.Combine(work, "zp" + Path.GetFileName(work)[^6..] + ".zip");
            var jpg = Path.Combine(work, "inner.jpg");
            FileKit.MakeJpg(jpg);
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) z.CreateEntryFromFile(jpg, "inner.jpg");
            File.Delete(jpg);
            var hash = FileKit.Sha(zip);
            var app = AngerApp.Start(AppExe, Path.Combine(Root, "x4d.log"));
            ExplorerWin? ex = null;
            try
            {
                ex = ExplorerWin.Open(zip, PrimaryBox, Path.GetFileNameWithoutExtension(zip));
                var s = new Sess { App = app, Ex = ex, Work = work };
                Rec.Check("Explorer shows inner.jpg inside the zip", ex.HasItem("inner.jpg"));
                var seen = s.Start("inner.jpg");
                if (!Rec.Check("wheel visible", seen)) { s.Abort(); return; }
                Thread.Sleep(900);
                var l = s.LogNew;
                Rec.Check("log: classified as virtual files", l.Contains("kind=VirtualFiles"), Regex.Match(l, @"DragEnter.*").Value);
                var c = WheelInfo.Centre(s.Log)!.Value;
                Pointer.Glide(c.X + 170, c.Y + 150, 6, 8); Thread.Sleep(400);
                Shot(s, "x4d-zip-save-first", c, 300);
                s.Release();
                Thread.Sleep(1500);
                Rec.Check("no job, no output next to the zip", !s.Log.Contains("job ") && Directory.GetFileSystemEntries(work).Length == 1, string.Join(",", Directory.GetFileSystemEntries(work).Select(Path.GetFileName)));
                Rec.Check("zip byte-identical", FileKit.Sha(zip) == hash);
                Rec.Check("no [ERROR]", !s.Log.Contains("[ERROR]"));
                Rec.Check("wheel hidden at the end", Sess.Wait(() => !WheelInfo.Visible(), 3000));
            }
            finally
            {
                Pointer.LeftUp(); Sess.Keys("shift+ctrl", false);
                try { ex?.Close(); } catch (Exception) { }
                app.Kill();
            }
        };
    }
}
