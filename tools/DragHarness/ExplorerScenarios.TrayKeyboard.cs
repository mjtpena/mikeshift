using System.Windows.Automation;

namespace DragHarness;

/// <summary>The tray flyout is driven by the keyboard (real tray icon, real input).</summary>
internal static partial class ExplorerScenarios
{
    private static string[] MenuNames(Sess s) =>
        AppElements(s.App.Proc.Id, ControlType.MenuItem).Select(i => { try { return i.Current.Name; } catch (Exception) { return ""; } }).Where(n => n.Length > 0 && n != "System").ToArray(); // "System" is the title-bar menu of another window of the app (Settings)

    private static string FocusedName()
    {
        try { return AutomationElement.FocusedElement?.Current.Name ?? "(none)"; } catch (Exception) { return "(error)"; }
    }

    /// <summary>The names of the app's top-level windows (UI Automation), e.g. "Settings".</summary>
    private static string[] AppWindowNames(int pid)
    {
        var names = new List<string>();
        try
        {
            foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid)))
                try { names.Add(w.Current.Name); } catch (Exception) { }
        }
        catch (Exception) { }
        return names.ToArray();
    }

    /// <summary>Right click on the real tray icon; true when the flyout's menu items are on screen.</summary>
    private static bool OpenTrayFlyout(Sess s)
    {
        var (icon, where) = TrayKit.FindIcon();
        if (!Rec.Check("tray icon found through UI Automation", icon is not null, where)) return false;
        var r = icon!.Current.BoundingRectangle;
        Pointer.Glide((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2), 20, 3); Thread.Sleep(250);
        Pointer.RightDown(); Thread.Sleep(40); Pointer.RightUp();
        var ok = Sess.Wait(() => MenuNames(s).Length >= 4, 5000);
        Thread.Sleep(500);
        return Rec.Check("right click opened the flyout", ok, string.Join(" | ", MenuNames(s)));
    }

    private static void FlyoutShot(Sess s, string name)
    {
        var rects = AppElements(s.App.Proc.Id, ControlType.MenuItem).Select(i => i.Current.BoundingRectangle).Where(b => !b.IsEmpty && b.Width > 0).Select(b => new Rectangle((int)b.Left, (int)b.Top, (int)b.Width, (int)b.Height)).ToList();
        if (rects.Count == 0) return;
        var u = Union(rects); u.Inflate(14, 14);
        u = Rectangle.Union(u, new Rectangle(u.Left, u.Top - 70, u.Width, 70));
        u.Intersect(Screen.FromPoint(new Point(u.Left, u.Top)).WorkingArea);
        DragHarness.Shot.Region(u, Path.Combine(ShotDir, name + ".png"));
        Rec.Note($"screenshot: {Path.Combine(ShotDir, name + ".png")}");
    }

    private static void Tap(int vk, int after = 400, bool shift = false)
    {
        if (shift) Pointer.KeyDown(Native.VK_LSHIFT);
        Pointer.KeyDown(vk); Thread.Sleep(30); Pointer.KeyUp(vk);
        if (shift) Pointer.KeyUp(Native.VK_LSHIFT);
        Thread.Sleep(after);
    }

    private static void RegisterTrayKeyboard(Dictionary<string, Action> d)
    {
        d["tray-keyboard"] = () => Case("tray-keyboard", "Real tray icon: the flyout takes the keyboard (Down, Esc, Enter on Settings, focus loss)", dir => { }, s =>
        {
            const int VK_DOWN = 0x28, VK_UP = 0x26, VK_RETURN = 0x0D;
            var pid = s.App.Proc.Id;

            // 1. Esc closes it
            if (!OpenTrayFlyout(s)) return;
            var start = FocusedName();
            Rec.Note("focus right after opening: " + start);
            Tap(Native.VK_ESCAPE, 100);
            var closed = Sess.Wait(() => MenuNames(s).Length == 0, 3000);
            Rec.Check("Esc closed the flyout", closed, closed ? "" : $"still open; foreground class {FgClass()}");
            Thread.Sleep(300);
            Rec.Check("focus went back to the shell, not to the hidden host window", FgClass() != "WinUIDesktopWin32WindowClass", FgClass());
            TrayKit.CloseOverflow(); Thread.Sleep(500);

            // 2. Down moves focus item by item, Up goes back
            if (!OpenTrayFlyout(s)) return;
            FlyoutShot(s, "tray-keyboard-opened-by-mouse");
            Tap(VK_DOWN); var f1 = FocusedName();
            FlyoutShot(s, "tray-keyboard-after-down");
            Tap(VK_DOWN); var f2 = FocusedName();
            Tap(VK_DOWN); var f3 = FocusedName();
            Rec.Note($"Down, Down, Down -> focus: {f1} | {f2} | {f3}");
            Rec.Check("first Down focuses a menu item", MenuNames(s).Contains(f1), f1);
            Rec.Check("second Down moves focus to the next item", MenuNames(s).Contains(f2) && f2 != f1, f2);
            Rec.Check("third Down moves on again", MenuNames(s).Contains(f3) && f3 != f2, f3);
            Tap(VK_UP); var f4 = FocusedName();
            Rec.Check("Up moves focus back", f4 == f2, f4);
            Tap(Native.VK_ESCAPE, 100);
            Sess.Wait(() => MenuNames(s).Length == 0, 3000);
            TrayKit.CloseOverflow(); Thread.Sleep(500);

            // 3. Down to Settings, Enter opens it
            if (!OpenTrayFlyout(s)) return;
            string cur = "";
            for (var i = 0; i < 12 && !cur.StartsWith("Settings", StringComparison.Ordinal); i++) { Tap(VK_DOWN, 250); cur = FocusedName(); }
            Rec.Check("Down reaches 'Settings'", cur.StartsWith("Settings", StringComparison.Ordinal), cur);
            Tap(VK_RETURN, 100);
            var opened = Sess.Wait(() => AppWindowNames(pid).Any(n => n == "Settings"), 6000);
            Rec.Check("Enter on 'Settings' opened the Settings window", opened, string.Join(" | ", AppWindowNames(pid)));
            var shut = Sess.Wait(() => MenuNames(s).Length == 0, 3000); Rec.Check("flyout closed after Enter", shut, string.Join(" | ", MenuNames(s)));
            TrayKit.CloseOverflow(); Thread.Sleep(500);

            // 4. Another window becoming active closes it: switching apps (SetForegroundWindow from outside, like Alt+Tab) ...
            if (!OpenTrayFlyout(s)) return;
            Native.SetForegroundWindow(s.Ex.Hwnd);
            var gone = Sess.Wait(() => MenuNames(s).Length == 0, 3000);
            Rec.Check("activating another window closed the flyout", gone, gone ? "" : $"still open: {string.Join(" | ", MenuNames(s))}; foreground class {FgClass()}");
            TrayKit.CloseOverflow(); Thread.Sleep(500);

            // ... and a real click on another window's title bar
            if (!OpenTrayFlyout(s)) return;
            var wr = s.Ex.Win.Current.BoundingRectangle;
            Pointer.Glide((int)(wr.Left + wr.Width / 2), (int)wr.Top + 14, 20, 3); Thread.Sleep(200);
            Pointer.LeftDown(); Thread.Sleep(40); Pointer.LeftUp();
            var gone2 = Sess.Wait(() => MenuNames(s).Length == 0, 3000);
            Rec.Check("clicking another window closed the flyout", gone2, gone2 ? "" : $"still open: {string.Join(" | ", MenuNames(s))}; foreground class {FgClass()}");
            TrayKit.CloseOverflow();
        });
    }
}

internal static partial class ExplorerScenarios
{
    private static void RegisterTrayKeyboardOpen(Dictionary<string, Action> d)
    {
        // Opening the menu from the keyboard: UI Automation focus on the tray button, then the Menu key (VK_APPS) opens its context menu.
        d["tray-keyboard-open"] = () => Case("tray-keyboard-open", "Real tray icon: opened with the keyboard (focus the button, Menu key); the first item is focused and Esc closes it", dir => { }, s =>
        {
            var (icon, where) = TrayKit.FindIcon();
            if (!Rec.Check("tray icon found through UI Automation", icon is not null, where)) return;
            icon!.SetFocus(); Thread.Sleep(400);
            Tap(0x5D, 100); // VK_APPS
            var ok = Sess.Wait(() => MenuNames(s).Length >= 4, 3000);
            if (!ok) { Tap(0x79, 100, shift: true); ok = Sess.Wait(() => MenuNames(s).Length >= 4, 3000); } // Shift+F10
            Rec.Check("the Menu key / Shift+F10 on the focused tray button opened the flyout", ok, string.Join(" | ", MenuNames(s)));
            if (!ok) return;
            Thread.Sleep(500);
            FlyoutShot(s, "tray-keyboard-opened");
            Rec.Note("focus right after opening: " + FocusedName());
            Tap(Native.VK_ESCAPE, 100);
            Rec.Check("Esc closed it", Sess.Wait(() => MenuNames(s).Length == 0, 3000));
            TrayKit.CloseOverflow();
        }, openExplorer: false);
    }
}
