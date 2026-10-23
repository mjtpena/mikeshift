using System.Diagnostics;
using System.Windows.Automation;

namespace DragHarness;

internal static partial class Anger
{
    private static List<AutomationElement> AppElementsPublic(int pid) => AppElements(pid, ControlType.MenuItem);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder sb, int max);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassNameW2(IntPtr h, System.Text.StringBuilder sb, int max);

    private static void RegisterTests6(Dictionary<string, Action> d)
    {
        d["xdbg"] = () => Case("xdbg", "debug tray", dir => { }, s =>
        {
            var (btn, where) = TrayKit.FindIcon();
            Rec.Note("icon: " + where + " " + (btn?.Current.BoundingRectangle.ToString() ?? "null"));
            if (btn is null) return;
            if (Environment.GetEnvironmentVariable("XDBG") == "esc")
            {
                var r = btn.Current.BoundingRectangle;
                Pointer.Glide((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2), 20, 3); Thread.Sleep(250);
                Pointer.RightDown(); Thread.Sleep(40); Pointer.RightUp(); Thread.Sleep(1200);
                Rec.Note("items after right click: " + AppElementsPublic(s.App.Proc.Id).Count + ", fg=" + FgClass() + " pid=" + FgPid() + " app pid=" + s.App.Proc.Id);
                Pointer.KeyDown(0x28); Pointer.KeyUp(0x28); Thread.Sleep(500);
                Shot(s, "xdbg-down", new Point(3300, 1000), 300);
                Pointer.KeyDown(Native.VK_ESCAPE); Pointer.KeyUp(Native.VK_ESCAPE); Thread.Sleep(1200);
                Rec.Note("items after Down+Esc: " + AppElementsPublic(s.App.Proc.Id).Count);
                return;
            }
            if (Environment.GetEnvironmentVariable("XDBG") == "right")
            {
                var r = btn.Current.BoundingRectangle;
                Pointer.Glide((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2), 20, 3); Thread.Sleep(250);
                Pointer.RightDown(); Thread.Sleep(40); Pointer.RightUp(); Thread.Sleep(1200);
                var mi = AppElementsPublic(s.App.Proc.Id).FirstOrDefault(i => i.Current.Name.StartsWith("Convert files"));
                Rec.Note("menu item: " + (mi is null ? "null" : mi.Current.BoundingRectangle.ToString()));
                if (mi is not null) TrayKit.Click(mi);
            }
            else TrayKit.Click(btn);
            var swd = Stopwatch.StartNew();
            var dlg = FindTop("Choose files to convert", 5000);
            for (var i = 0; i < 10; i++)
            {
                AngerNative.EnumWindows((h, _) =>
                {
                    var t = new System.Text.StringBuilder(256); GetWindowTextW(h, t, 256);
                    var c = new System.Text.StringBuilder(128); GetClassNameW2(h, c, 128);
                    AngerNative.GetWindowThreadProcessId(h, out var pid);
                    if (t.ToString().Contains("Choose") || (c.ToString() == "#32770")) Rec.Note($"enum: hwnd={h} class={c} title={t} pid={pid} visible={Native.IsWindowVisible(h)}");
                    return true;
                }, IntPtr.Zero);
                Thread.Sleep(500);
            }
            Rec.Note("dialog: " + (dlg is null ? "NOT FOUND" : $"after {swd.ElapsedMilliseconds} ms class={dlg.Current.ClassName} pid={dlg.Current.ProcessId} app pid={s.App.Proc.Id} rect={dlg.Current.BoundingRectangle}"));
            Thread.Sleep(500);
            foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
            {
                try { Rec.Note($"top: class={w.Current.ClassName} name={w.Current.Name} rect={w.Current.BoundingRectangle} pid={w.Current.ProcessId}"); } catch (Exception) { }
            }
            Shot(s, "xdbg-tray", new Point(3000, 1200), 400); Shot(s, "xdbg-full", new Point(1720, 700), 700);
            Pointer.KeyDown(Native.VK_ESCAPE); Pointer.KeyUp(Native.VK_ESCAPE);
        }, openExplorer: false);
    }
}
