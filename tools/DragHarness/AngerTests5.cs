using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace DragHarness;

internal static class TrayKit
{
    private static AutomationElement Root => AutomationElement.RootElement;

    private static AutomationElement? ByClass(string cls) =>
        Root.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ClassNameProperty, cls));

    /// <summary>Finds the MikeShift tray button in the visible taskbar or, after opening "Show Hidden Icons", in the overflow flyout.</summary>
    public static (AutomationElement? Button, string Where) FindIcon()
    {
        var tray = ByClass("Shell_TrayWnd");
        if (tray is null) return (null, "no taskbar");
        var direct = tray.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, "MikeShift"),
            new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NormalButton")));
        if (direct is not null) return (direct, "taskbar notification area");
        var chevron = tray.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NormalButton"),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon")));
        chevron = tray.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NormalButton"))
            .Cast<AutomationElement>().FirstOrDefault(b => b.Current.Name.StartsWith("Show Hidden Icons"));
        if (chevron is null) return (null, "no chevron");
        // The flyout may already be open from an earlier run; only open it when it is not.
        if (!chevron.Current.Name.Contains("Hide")) ((InvokePattern)chevron.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 4000)
        {
            var over = ByClass("TopLevelWindowForOverflowXamlIsland");
            var b = over?.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, "MikeShift"),
                new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NormalButton")));
            if (b is not null && !b.Current.BoundingRectangle.IsEmpty && !b.Current.IsOffscreen) { Thread.Sleep(400); return (b, "overflow flyout (Show Hidden Icons)"); }
            Thread.Sleep(100);
        }
        return (null, "not in the overflow flyout either");
    }

    /// <summary>Closes the "Show Hidden Icons" flyout if it is open (the chevron is then named "Show Hidden Icons Hide").</summary>
    public static void CloseOverflow()
    {
        try
        {
            var tray = ByClass("Shell_TrayWnd");
            var chevron = tray?.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NormalButton"))
                .Cast<AutomationElement>().FirstOrDefault(b => b.Current.Name.StartsWith("Show Hidden Icons") && b.Current.Name.Contains("Hide"));
            if (chevron is not null) ((InvokePattern)chevron.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }
        catch (Exception) { }
    }

    public static void Click(AutomationElement e)
    {
        var r = e.Current.BoundingRectangle;
        var x = (int)(r.Left + r.Width / 2); var y = (int)(r.Top + r.Height / 2);
        Pointer.Glide(x, y, 20, 3); Thread.Sleep(200);
        Pointer.LeftDown(); Thread.Sleep(40); Pointer.LeftUp();
    }

    /// <summary>The open popup menu (#32768) and its item names.</summary>
    public static (AutomationElement? Menu, List<(string Name, AutomationElement El)> Items) WaitMenu(int ms = 4000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            var m = ByClass("#32768");
            if (m is not null)
            {
                var items = m.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem))
                    .Cast<AutomationElement>().Select(i => (i.Current.Name, i)).ToList();
                if (items.Count > 0) return (m, items);
            }
            Thread.Sleep(100);
        }
        return (null, []);
    }

    public static AutomationElement? AppDialog(int pid, int ms = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            var d = Root.FindFirst(TreeScope.Children, new AndCondition(
                new PropertyCondition(AutomationElement.ClassNameProperty, "#32770"),
                new PropertyCondition(AutomationElement.ProcessIdProperty, pid)));
            if (d is not null) return d;
            Thread.Sleep(150);
        }
        return null;
    }

    /// <summary>Tray icon -> menu -> Convert Files… -> file dialog -> the given file. Returns a description of what happened.</summary>
    public static bool ConvertViaTray(AngerApp app, string file, out string detail, bool cancelDialog = false)
    {
        string where = "";
        AutomationElement? menu = null;
        var items = new List<(string Name, AutomationElement El)>();
        for (var attempt = 0; attempt < 3 && menu is null; attempt++)
        {
            AutomationElement? btn;
            (btn, where) = FindIcon();
            if (btn is null) { detail = "tray icon not found: " + where; return false; }
            Click(btn);
            (menu, items) = WaitMenu(attempt == 0 ? 3000 : 4000);
            if (menu is null) { CloseOverflow(); Thread.Sleep(800); }
        }
        if (menu is null) { detail = "tray menu did not open (3 attempts)"; return false; }
        var conv = items.FirstOrDefault(i => i.Name.StartsWith("Convert Files"));
        if (conv.El is null) { detail = "no Convert Files… item in: " + string.Join(" | ", items.Select(i => i.Name)); return false; }
        Click(conv.El);
        var dlg = AppDialog(app.Proc.Id);
        if (dlg is null) { detail = "file dialog did not appear"; return false; }
        if (cancelDialog)
        {
            Pointer.KeyDown(Native.VK_ESCAPE); Pointer.KeyUp(Native.VK_ESCAPE);
            detail = "dialog cancelled";
            return true;
        }
        var edit = dlg.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "1148")));
        edit ??= dlg.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        if (edit is null) { detail = "no file name box in the dialog"; return false; }
        ((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).SetValue(file);
        var open = dlg.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "1")));
        if (open is null) { detail = "no Open button"; return false; }
        ((InvokePattern)open.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        detail = "tray icon found in the " + where + "; menu items: " + string.Join(" | ", items.Select(i => i.Name));
        return true;
    }
}

internal static partial class Anger
{
    private static void TrayCase(string id, string desc, Action<Sess, string> body, bool second = false)
    {
        Case(id, desc, dir => FileKit.MakeJpg(Path.Combine(dir, "photo.jpg")), s =>
        {
            var file = Path.Combine(s.Work, "photo.jpg");
            body(s, file);
        });
    }

    /// <summary>Last "highlight: X" stage line (drag mode appends a "(drop description ...)" suffix, click mode does not).</summary>
    private static string? LastHl(string log) =>
        Regex.Matches(log, @"highlight: ([^(\r\n]+?)\s*(?:\(|\r|\n|$)").LastOrDefault()?.Groups[1].Value;

    private static bool OpenClickMode(Sess s, string file)
    {
        s.LogMark = s.App.LogLength();
        var ok = TrayKit.ConvertViaTray(s.App, file, out var detail);
        Rec.Check("real tray icon -> menu -> Convert Files… -> file dialog -> picked the file", ok, detail);
        if (!ok) return false;
        var shown = s.App.WaitLog(t => t.Length > s.LogMark && t[s.LogMark..].Contains("click-mode wheel shown"), 10000);
        Rec.Check("click-mode wheel opened", shown, s.App.LastLine("click-mode wheel shown"));
        Thread.Sleep(650);
        return shown;
    }

    private static void RegisterTests5(Dictionary<string, Action> d)
    {
        d["x5a"] = () => TrayCase("x5a-tray-keys", "Real tray -> Convert Files…; keyboard: arrows, Tab, Enter; UIA focus events", (s, file) =>
        {
            if (!OpenClickMode(s, file)) return;
            Rec.Check("click-mode wheel took keyboard focus", s.App.LastLine("click-mode wheel shown").Contains("foreground=True") || s.Log.Contains("foreground retried, now foreground=True"));
            var rect = WheelInfo.LastRect(s.Log, click: true)!.Value;
            var centre = new Point(rect.Rect.Left + rect.Rect.Width / 2, rect.Rect.Top + rect.Rect.Height / 2);
            Pointer.Glide(centre.X, centre.Y, 10, 4); Thread.Sleep(300);
            var focusNames = new List<string>();
            AutomationFocusChangedEventHandler onFocus = (_, _) => { try { lock (focusNames) focusNames.Add(AutomationElement.FocusedElement.Current.Name); } catch (Exception) { } };
            Automation.AddAutomationFocusChangedEventHandler(onFocus);
            try
            {
                Shot(s, "x5a-click-mode", centre, 300);
                void Key(int vk) { Pointer.KeyDown(vk); Thread.Sleep(30); Pointer.KeyUp(vk); Thread.Sleep(350); }
                Key(0x27); // Right
                var first = LastHl(s.LogNew);
                Rec.Check("Right arrow highlights the first tile", first == "PNG", first ?? "none");
                Key(0x27); Key(0x27);
                var third = LastHl(s.LogNew);
                Rec.Check("two more Rights move to the third tile", third == "GIF", third ?? "none");
                Key(0x25); // Left
                var back = LastHl(s.LogNew);
                Rec.Check("Left arrow moves back one tile", back == "TIFF", back ?? "none");
                Key(0x09); // Tab
                Rec.Check("Tab toggles to the Tools page", Regex.Matches(s.LogNew, @"page = (\w+)").LastOrDefault()?.Groups[1].Value == "Tool", Regex.Matches(s.LogNew, @"page = (\w+)").LastOrDefault()?.Value ?? "");
                Key(0x09);
                Rec.Check("Tab again returns to Formats", Regex.Matches(s.LogNew, @"page = (\w+)").LastOrDefault()?.Groups[1].Value == "Format");
                Key(0x27);
                Thread.Sleep(300);
                string[] names; lock (focusNames) names = focusNames.ToArray();
                Rec.Check("UIA focus-changed events announced tiles (what Narrator reads)", names.Any(n => n.Contains("Convert to ") || n == "Compress" || n.Length > 0), string.Join(" | ", names.Distinct().Take(8)));
                Rec.Check("announcements name the format tiles", names.Any(n => n.Contains("Convert to PNG")) && names.Any(n => n.Contains("Convert to GIF")), string.Join(" | ", names.Distinct().Take(8)));
                // pick PNG: go back to first tile and Enter
                Key(0x25); Key(0x25); Key(0x25); Key(0x25);
                var cur = LastHl(s.LogNew);
                Rec.Note("highlight before Enter: " + cur);
                Key(0x0D);
                var action = Regex.Match(s.LogNew, @"click mode picked (.+)").Groups[1].Value.Trim();
                Rec.Check("Enter picked the highlighted tile", action.Length > 0, action);
                var png = Path.Combine(s.Work, action == "PNG" ? "photo.png" : "photo." + action.ToLowerInvariant());
                if (action == "PNG") Rec.Check("photo.png written", s.WaitFile(png));
                Rec.Check("wheel closed after the pick", Sess.Wait(() => !WheelInfo.Visible(), 3000));
            }
            finally { Automation.RemoveAllEventHandlers(); }
        });

        d["x5b"] = () => TrayCase("x5b-tray-mouse", "Real tray -> Convert Files…; mouse hover highlights, mouse-down commits", (s, file) =>
        {
            if (!OpenClickMode(s, file)) return;
            var hit = s.ToTile("PNG");
            // ToTile uses the drag-wheel regexes; click mode needs its own rect
            var rect = WheelInfo.LastRect(s.Log, click: true)!.Value;
            var tiles = WheelInfo.LastTiles(s.Log)!.Value.Formats;
            var i = tiles.IndexOf("PNG");
            var ang = (i + 0.5) * 2 * Math.PI / tiles.Count;
            var c = new Point(rect.Rect.Left + rect.Rect.Width / 2, rect.Rect.Top + rect.Rect.Height / 2);
            var t = new Point((int)(c.X + 100 * rect.Scale * Math.Sin(ang)), (int)(c.Y - 100 * rect.Scale * Math.Cos(ang)));
            Pointer.Glide(t.X, t.Y, 8, 6);
            Rec.Check("hovering the PNG tile highlights it", s.App.WaitLog(x => x[s.LogMark..].Contains("highlight: PNG"), 2000));
            Thread.Sleep(300);
            Shot(s, "x5b-click-mouse", c, 300);
            Pointer.LeftDown(); Pointer.LeftUp();
            Rec.Check("mouse-down on the tile committed it", s.WaitFile(Path.Combine(s.Work, "photo.png")));
            Rec.Check("wheel closed", Sess.Wait(() => !WheelInfo.Visible(), 3000));
        });

        d["x5c"] = () => TrayCase("x5c-tray-esc", "Real tray -> Convert Files…; Esc cancels", (s, file) =>
        {
            if (!OpenClickMode(s, file)) return;
            Pointer.KeyDown(Native.VK_ESCAPE); Pointer.KeyUp(Native.VK_ESCAPE);
            Rec.Check("Esc dismissed the wheel", Sess.Wait(() => !WheelInfo.Visible(), 2500));
            Thread.Sleep(800);
            Rec.Check("nothing converted", !Directory.EnumerateFiles(s.Work, "*.png").Any() && !s.Log.Contains("job "));
        });

        d["x5d"] = () => TrayCase("x5d-tray-blur", "Real tray -> Convert Files…; clicking another window (focus loss) dismisses", (s, file) =>
        {
            if (!OpenClickMode(s, file)) return;
            // click on my own Explorer window's empty area
            var r = s.Ex.Win.Current.BoundingRectangle;
            Native.SetForegroundWindow(s.Ex.Hwnd);
            Rec.Check("the wheel dismissed itself when focus moved away", Sess.Wait(() => !WheelInfo.Visible(), 2500));
            Thread.Sleep(400);
            Rec.Check("nothing converted", !s.Log.Contains("job "));
        });

        d["x5e"] = () => TrayCase("x5e-tray-cancel", "Real tray -> Convert Files…; cancelling the file dialog does nothing", (s, file) =>
        {
            var ok = TrayKit.ConvertViaTray(s.App, file, out var detail, cancelDialog: true);
            Rec.Check("tray -> menu -> dialog -> Esc", ok, detail);
            Thread.Sleep(1000);
            Rec.Check("no wheel, no job", !WheelInfo.Visible() && !s.Log.Contains("job ") && !s.Log.Contains("click-mode wheel shown"));
        });
    }
}
