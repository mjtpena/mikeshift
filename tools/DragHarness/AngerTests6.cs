using System.Diagnostics;
using System.Windows.Automation;

namespace DragHarness;

internal static partial class Anger
{
    private static void RegisterTests6(Dictionary<string, Action> d)
    {
        d["xdbg"] = () => Case("xdbg", "debug tray", dir => { }, s =>
        {
            var (btn, where) = TrayKit.FindIcon();
            Rec.Note("icon: " + where + " " + (btn?.Current.BoundingRectangle.ToString() ?? "null"));
            if (btn is null) return;
            TrayKit.Click(btn);
            Thread.Sleep(1500);
            foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
            {
                try { Rec.Note($"top: class={w.Current.ClassName} name={w.Current.Name} rect={w.Current.BoundingRectangle} pid={w.Current.ProcessId}"); } catch (Exception) { }
            }
            Shot(s, "xdbg-tray", new Point(3000, 1200), 400);
            Pointer.KeyDown(Native.VK_ESCAPE); Pointer.KeyUp(Native.VK_ESCAPE);
        }, openExplorer: false);
    }
}
