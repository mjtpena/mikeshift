using System.Diagnostics;
using System.Text;
using System.Windows.Automation;

namespace ContextMenuHarness;

/// <summary>Opens and drives a real File Explorer window: select items through the shell view, find their screen position through UI Automation.</summary>
internal sealed class ExplorerDriver
{
    private readonly dynamic _window;
    public IntPtr Hwnd { get; }
    public string Folder { get; }

    private ExplorerDriver(dynamic window, IntPtr hwnd, string folder)
    {
        _window = window;
        Hwnd = hwnd;
        Folder = folder;
    }

    /// <summary>Opens <paramref name="folder"/> in a new Explorer window, places it at a known size and brings it to the front.</summary>
    public static ExplorerDriver Open(string folder, int x = 60, int y = 60, int width = 1180, int height = 760)
    {
        var url = new Uri(folder.TrimEnd('\\') + "\\").AbsoluteUri.TrimEnd('/');
        var existing = FindWindows().Select(w => (IntPtr)(long)w.HWND).ToHashSet();
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var w in FindWindows())
            {
                string? location = null;
                try { location = (string)w.LocationURL; } catch { /* window still starting */ }
                if (location is null || !string.Equals(Uri.UnescapeDataString(location).TrimEnd('/'), Uri.UnescapeDataString(url), StringComparison.OrdinalIgnoreCase)) continue;
                var hwnd = (IntPtr)(long)w.HWND;
                if (existing.Contains(hwnd)) continue;
                var driver = new ExplorerDriver(w, hwnd, folder);
                RealInput.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, 0x4 | 0x40); // NOZORDER | SHOWWINDOW
                Thread.Sleep(400);
                driver.Activate();
                Thread.Sleep(500);
                return driver;
            }
            Thread.Sleep(100);
        }
        throw new TimeoutException("the Explorer window did not open");
    }

    private static IEnumerable<dynamic> FindWindows()
    {
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        dynamic windows = ((dynamic)shell).Windows();
        var count = (int)windows.Count;
        for (var i = 0; i < count; i++)
        {
            dynamic? w = null;
            try { w = windows.Item(i); } catch { /* closed meanwhile */ }
            if (w is null) continue;
            string? name = null;
            try { name = (string)w.FullName; } catch { }
            if (name is not null && name.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase)) yield return w;
        }
    }

    public void Activate()
    {
        RealInput.ShowWindow(Hwnd, 9);
        RealInput.SetForegroundWindow(Hwnd);
    }

    /// <summary>Selects exactly these items (by file name) in the shell view.</summary>
    public void Select(params string[] names)
    {
        dynamic doc = _window.Document;
        dynamic folder = doc.Folder;
        var first = true;
        foreach (var name in names)
        {
            dynamic item = folder.ParseName(name) ?? throw new InvalidOperationException($"{name} is not in {Folder}");
            // SVSI_SELECT 0x1, SVSI_DESELECTOTHERS 0x4, SVSI_ENSUREVISIBLE 0x8, SVSI_FOCUSED 0x10 (the first item replaces the old selection)
            doc.SelectItem(item, first ? (0x1 | 0x4 | 0x8 | 0x10) : (0x1 | 0x8));
            first = false;
        }
        Thread.Sleep(300);
    }

    /// <summary>Selects every item through the shell view and returns how many are selected afterwards.</summary>
    public int SelectAll()
    {
        dynamic doc = _window.Document;
        var first = true;
        foreach (var item in doc.Folder.Items())
        {
            doc.SelectItem(item, first ? (0x1 | 0x4 | 0x8 | 0x10) : 0x1);
            first = false;
        }
        Thread.Sleep(500);
        return (int)doc.SelectedItems().Count;
    }

    /// <summary>The element of an item in the list, found by its displayed name (the extension may be hidden by the user's Explorer settings).</summary>
    public AutomationElement? FindItem(string fileName)
    {
        var root = AutomationElement.FromHandle(Hwnd);
        var bare = Path.GetFileNameWithoutExtension(fileName);
        var items = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        foreach (AutomationElement e in items)
        {
            var n = e.Current.Name;
            if (n == fileName || n == bare) return e;
        }
        return null;
    }

    public (int X, int Y) CenterOf(string fileName)
    {
        var e = FindItem(fileName) ?? throw new InvalidOperationException($"no list item named {fileName}");
        e.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll);
        (scroll as ScrollItemPattern)?.ScrollIntoView();
        var r = e.Current.BoundingRectangle;
        return ((int)(r.X + r.Width / 2), (int)(r.Y + Math.Min(r.Height / 2, 14)));
    }

    public void Close()
    {
        try { _window.Quit(); } catch { /* already gone */ }
    }
}

/// <summary>Finds menu windows and entries of the shell through UI Automation.</summary>
internal static class MenuFinder
{
    /// <summary>Every top-level window that belongs to explorer.exe.</summary>
    public static IEnumerable<AutomationElement> ExplorerTopLevel()
    {
        var pids = Process.GetProcessesByName("explorer").Select(p => p.Id).ToHashSet();
        foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            var mine = false;
            try { mine = pids.Contains(w.Current.ProcessId); }
            catch (ElementNotAvailableException) { }
            if (mine) yield return w;
        }
    }

    /// <summary>The first element with this name inside any explorer.exe top-level window, or null.</summary>
    public static AutomationElement? FindByName(string name)
    {
        var cond = new PropertyCondition(AutomationElement.NameProperty, name);
        foreach (var w in ExplorerTopLevel())
        {
            try
            {
                if (w.Current.Name == name) return w;
                var hit = w.FindFirst(TreeScope.Descendants, cond);
                if (hit is not null) return hit;
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
        }
        return null;
    }

    /// <summary>Waits until an element with this name shows up; returns it and the waiting time in ms measured from <paramref name="since"/>.</summary>
    public static (AutomationElement? Element, double Ms) WaitFor(string name, long since, int timeoutMs = 8000)
    {
        while (RealInput.MsSince(since) < timeoutMs)
        {
            var e = FindByName(name);
            if (e is not null) return (e, RealInput.MsSince(since));
            Thread.Sleep(8);
        }
        return (null, RealInput.MsSince(since));
    }

    private const string PopupClass = "Microsoft.UI.Content.PopupWindowSiteBridge";

    /// <summary>The XAML popups of the modern menu (the command row, the list, the open submenu): panes of this class under an Explorer window.</summary>
    public static List<AutomationElement> Popups()
    {
        var list = new List<AutomationElement>();
        var cond = new PropertyCondition(AutomationElement.ClassNameProperty, PopupClass);
        foreach (var w in ExplorerTopLevel())
        {
            try
            {
                var found = w.FindAll(TreeScope.Children, cond);
                if (found is null) continue;
                foreach (AutomationElement p in found) list.Add(p);
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
        }
        return list;
    }

    public static List<System.Windows.Rect> PopupRects() =>
        Popups().Select(p => { try { return p.Current.BoundingRectangle; } catch (ElementNotAvailableException) { return System.Windows.Rect.Empty; } })
                .Where(r => !r.IsEmpty && r.Width > 20 && r.Height > 20).ToList();

    /// <summary>
    /// The visible body of each popup: the union of its rows' rectangles with a little padding, clipped to the popup (the popup rectangle itself also
    /// covers the drop shadow, which would show whatever is behind the menu).
    /// </summary>
    public static List<System.Windows.Rect> PopupBodies()
    {
        var bodies = new List<System.Windows.Rect>();
        foreach (var p in Popups())
        {
            try
            {
                var outer = p.Current.BoundingRectangle;
                if (outer.IsEmpty || outer.Width < 20) continue;
                var body = System.Windows.Rect.Empty;
                var all = p.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                foreach (AutomationElement e in all)
                {
                    var r = e.Current.BoundingRectangle;
                    if (!r.IsEmpty && outer.Contains(r)) body.Union(r);
                }
                if (body.IsEmpty) continue;
                body.Inflate(4, 3);
                body.Intersect(outer);
                bodies.Add(body);
            }
            catch (ElementNotAvailableException) { }
        }
        return bodies;
    }

    /// <summary>An item with this name inside the modern menu's popups (fast: it never walks the whole Explorer window).</summary>
    public static AutomationElement? FindInPopups(string name)
    {
        var cond = new PropertyCondition(AutomationElement.NameProperty, name);
        foreach (var p in Popups())
        {
            try
            {
                var hit = p.FindFirst(TreeScope.Descendants, cond);
                if (hit is not null && !hit.Current.BoundingRectangle.IsEmpty) return hit;
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
        }
        return null;
    }

    public static (AutomationElement? Element, double Ms) WaitInPopups(string name, long since, int timeoutMs = 8000)
    {
        while (RealInput.MsSince(since) < timeoutMs)
        {
            var e = FindInPopups(name);
            if (e is not null) return (e, RealInput.MsSince(since));
            Thread.Sleep(5);
        }
        return (null, RealInput.MsSince(since));
    }

    /// <summary>The classic Win32 menus (class #32768) of Explorer.</summary>
    public static List<AutomationElement> ClassicMenus()
    {
        var list = new List<AutomationElement>();
        var cond = new PropertyCondition(AutomationElement.ClassNameProperty, "#32768");
        var pids = Process.GetProcessesByName("explorer").Select(p => p.Id).ToHashSet();
        var found = AutomationElement.RootElement.FindAll(TreeScope.Children, cond);
        if (found is null) return list;
        foreach (AutomationElement m in found)
        {
            try { if (pids.Contains(m.Current.ProcessId)) list.Add(m); }
            catch (ElementNotAvailableException) { }
        }
        return list;
    }

    public static List<System.Windows.Rect> ClassicMenuRects() =>
        ClassicMenus().Select(m => { try { return m.Current.BoundingRectangle; } catch (ElementNotAvailableException) { return System.Windows.Rect.Empty; } }).Where(r => !r.IsEmpty).ToList();

    public static AutomationElement? FindClassic(string name, AutomationElement? notThis = null)
    {
        var cond = new PropertyCondition(AutomationElement.NameProperty, name);
        foreach (var m in ClassicMenus())
        {
            try
            {
                var hit = m.FindFirst(TreeScope.Descendants, cond);
                if (hit is not null && !hit.Current.BoundingRectangle.IsEmpty) return hit;
            }
            catch (ElementNotAvailableException) { }
        }
        return null;
    }

    /// <summary>The names of the items next to <paramref name="item"/> (its menu), "----" for a separator.</summary>
    public static List<string> SubmenuTitles(AutomationElement? item, bool classic)
    {
        var titles = new List<string>();
        if (item is null) return titles;
        var parent = TreeWalker.ControlViewWalker.GetParent(item);
        if (parent is null) return titles;
        foreach (AutomationElement e in parent.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            try
            {
                var type = e.Current.ControlType;
                if (type == ControlType.Separator) titles.Add("----");
                else if (type == ControlType.MenuItem) titles.Add(e.Current.Name);
            }
            catch (ElementNotAvailableException) { }
        }
        return titles;
    }

    public static string Dump(int maxDepth = 6)
    {
        var sb = new StringBuilder();
        foreach (var w in ExplorerTopLevel())
        {
            try
            {
                var c = w.Current;
                if (c.IsOffscreen) continue;
                sb.AppendLine($"WINDOW class={c.ClassName} name=\"{c.Name}\" rect={c.BoundingRectangle}");
                Walk(w, 1, maxDepth, sb);
            }
            catch (ElementNotAvailableException) { }
        }
        return sb.ToString();
    }

    private static void Walk(AutomationElement parent, int depth, int max, StringBuilder sb)
    {
        if (depth > max) return;
        var kids = parent.FindAll(TreeScope.Children, Condition.TrueCondition);
        if (kids is null) return;
        foreach (AutomationElement e in kids)
        {
            try
            {
                var c = e.Current;
                sb.AppendLine($"{new string(' ', depth * 2)}{(c.ControlType?.ProgrammaticName ?? "?").Replace("ControlType.", "")} class={c.ClassName} name=\"{c.Name}\" rect={c.BoundingRectangle}{(c.IsOffscreen ? " offscreen" : "")}");
                if (!(depth >= 2 && c.ControlType?.Id == ControlType.ListItem.Id)) Walk(e, depth + 1, max, sb);
            }
            catch (ElementNotAvailableException) { }
        }
    }
}
