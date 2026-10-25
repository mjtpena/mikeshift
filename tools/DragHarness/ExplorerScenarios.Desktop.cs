using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DragHarness;

internal static class DesktopKit
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Native.POINT p);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hwnd, System.Text.StringBuilder sb, int max);

    /// <summary>Class name of the top-level window under a screen point.</summary>
    public static string RootClassAt(Point p)
    {
        var h = WindowFromPoint(new Native.POINT { X = p.X, Y = p.Y });
        if (h == IntPtr.Zero) return "";
        var root = GetAncestor(h, 2);
        var sb = new System.Text.StringBuilder(256);
        GetClassNameW(root == IntPtr.Zero ? h : root, sb, 256);
        return sb.ToString();
    }

    public static System.Windows.Automation.AutomationElement? FindDesktopItem(string name)
    {
        foreach (var cls in new[] { "Progman", "WorkerW" })
        {
            var tops = System.Windows.Automation.AutomationElement.RootElement.FindAll(System.Windows.Automation.TreeScope.Children,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ClassNameProperty, cls));
            foreach (System.Windows.Automation.AutomationElement t in tops)
            {
                var item = t.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, name));
                if (item is not null) return item;
            }
        }
        return null;
    }
}

internal static partial class ExplorerScenarios
{
    private static void RegisterDesktop(Dictionary<string, Action> d)
    {
        d["desktop"] = () =>
        {
            Rec.Begin("desktop", "The real Desktop as the drag source (temp file created on the Desktop and deleted afterwards)");
            var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var name = "mikeshift-scenario-desktop.jpg";
            var file = Path.Combine(desk, name);
            var png = Path.Combine(desk, "mikeshift-scenario-desktop.png");
            AppProcess? app = null;
            try
            {
                if (File.Exists(file)) { Rec.Check("no clash with an existing file", false, file); return; }
                FileKit.MakeJpg(file, text: "desktop");
                var hash = FileKit.Sha(file);
                app = AppProcess.Start(AppExe, Path.Combine(Root, "desktop.log"));
                System.Windows.Automation.AutomationElement? item = null;
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 15000 && (item = DesktopKit.FindDesktopItem(name)) is null) Thread.Sleep(250);
                if (!Rec.Check("UI Automation found the file's icon on the Desktop", item is not null)) return;
                var r = item!.Current.BoundingRectangle;
                var p = new Point((int)(r.Left + r.Width / 2), (int)(r.Top + Math.Min(r.Height * 0.35, 40)));
                var cls = DesktopKit.RootClassAt(p);
                Rec.Note($"icon at {p}, window under it: {cls}");
                if (cls is not ("Progman" or "WorkerW"))
                {
                    Rec.Note("NOT POSSIBLE HERE: the icon is covered by a window of class " + cls + "; the user's windows are not touched");
                    return;
                }
                var s = new Sess { App = app, Work = desk };
                var dest = new Point(p.X + 380, p.Y + 120);
                Pointer.Glide(p.X, p.Y, 14, 3); Thread.Sleep(150);
                s.LogMark = app.LogLength();
                Pointer.LeftDown(); Thread.Sleep(300);
                Pointer.Glide(dest.X, dest.Y, 8, 6);
                Pointer.KeyDown(Native.VK_LSHIFT);
                var seen = Sess.Wait(WheelInfo.Visible, 2500);
                if (!Rec.Check("wheel visible (Desktop source)", seen)) { s.Abort(); return; }
                s.WaitTiles(); Thread.Sleep(450);
                var hit = s.ToTile("PNG");
                Rec.Check("PNG tile highlighted", hit);
                Shot(s, "desktop", WheelInfo.Centre(s.Log), 300);
                if (!hit) { s.Abort(); return; }
                s.Release();
                Rec.Check("desktop PNG created", s.WaitFile(png));
                Rec.Check("original on the Desktop byte-identical and still there", File.Exists(file) && FileKit.Sha(file) == hash);
                Rec.Check("log: Drop effect COPY", Regex.IsMatch(s.Log, @"Drop on PNG .*effect=COPY"));
            }
            finally
            {
                Pointer.LeftUp(); Sess.Keys("shift+ctrl", false);
                app?.Kill();
                Thread.Sleep(300);
                foreach (var f in new[] { file, png }) { try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { } }
                Rec.Check("Desktop test files deleted", !File.Exists(file) && !File.Exists(png));
            }
        };
    }
}
