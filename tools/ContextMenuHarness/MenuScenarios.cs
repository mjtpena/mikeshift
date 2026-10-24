using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows.Automation;

namespace ContextMenuHarness;

/// <summary>
/// Scripted scenarios against the REAL File Explorer: open a window on a temp folder, select items, right-click with SendInput, read the menu through
/// UI Automation, pick entries with real clicks, then check what landed on disk. Each scenario prints PASS/FAIL lines and the time from the
/// right-click to the menu showing the entry. Screenshots are cropped to the menu popups (HARNESS_SHOTS names the folder).
/// </summary>
internal static class MenuScenarios
{
    private static string Work = "";
    private static string? ShotsDir;
    private static int s_failures;

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: menu <workfolder> dump|all|<scenario>... (scenarios: jpg-png multi-merge folder-zip mixed heic big600 classic more)"); return 2; }
        Work = Path.GetFullPath(args[0]);
        ShotsDir = Environment.GetEnvironmentVariable("HARNESS_SHOTS");
        if (ShotsDir is not null) Directory.CreateDirectory(ShotsDir);

        var wanted = args[1..];
        if (wanted is ["dump"]) return Dump();
        if (wanted is ["timing", ..]) return Timing(wanted.Length > 1 ? int.Parse(wanted[1]) : 6);
        if (wanted is ["all"]) wanted = ["jpg-png", "multi-merge", "folder-zip", "mixed", "heic", "classic", "more", "big600"];
        if (Environment.GetEnvironmentVariable("HARNESS_NO_TRAY") != "1") PrepareTray();
        foreach (var name in wanted)
        {
            Console.WriteLine($"=== {name}");
            try
            {
                switch (name)
                {
                    case "jpg-png": JpgToPng(); break;
                    case "multi-merge": MultiMerge(); break;
                    case "folder-zip": FolderZip(); break;
                    case "mixed": Mixed(); break;
                    case "heic": Heic(); break;
                    case "big600": Big600(); break;
                    case "classic": Classic(); break;
                    case "more": MoreOptions(); break;
                    default: Console.WriteLine("unknown scenario"); s_failures++; break;
                }
            }
            catch (Exception ex)
            {
                Check(false, "scenario threw " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                CloseMenus();
            }
        }
        Console.WriteLine(s_failures == 0 ? "ALL PASS" : $"{s_failures} FAILURE(S)");
        return s_failures == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
        if (!ok) s_failures++;
    }

    // ------------------------------------------------------------------ scenarios

    private static void JpgToPng()
    {
        var original = Path.Combine(Work, "photo1.jpg");
        var before = Hash(original);
        using var session = OpenFolder(Work);
        var (ms, entry) = RightClickAndFindEntry(session, ["photo1.jpg"], "photo1.jpg");
        Check(entry is not null, $"modern menu shows \"Convert with MikeShift\" for a JPG ({ms:F0} ms after the right-click)");
        if (entry is null) return;
        var items = OpenSubmenu(entry, "Convert to PNG");
        Console.WriteLine("submenu: " + string.Join(" | ", items));
        Check(items.Contains("Convert to PNG") && items.Contains("Zip") && items.Contains("More options…"), "submenu offers Convert to PNG, Zip and More options…");
        Check(!items.Contains("Convert to JPG"), "a JPG is not offered Convert to JPG");
        Shot("modern-menu-jpg.png");
        ClickName("Convert to PNG");
        var output = WaitForFile(Path.Combine(Work, "photo1.png"));
        Check(output is not null, "photo1.png appeared next to the original");
        Check(Hash(original) == before, "the original JPG is byte-identical");
        Thread.Sleep(900);
        ShotToast("toast-after-convert.png");
        Cleanup();
    }

    private static void MultiMerge()
    {
        using var session = OpenFolder(Work);
        var (ms, entry) = RightClickAndFindEntry(session, ["photo1.jpg", "photo2.jpg", "photo3.jpg"], "photo2.jpg");
        Check(entry is not null, $"menu for 3 images ({ms:F0} ms)");
        if (entry is null) return;
        var items = OpenSubmenu(entry, "Merge PDF");
        Console.WriteLine("submenu: " + string.Join(" | ", items));
        Check(items.Contains("Merge PDF"), "submenu offers Merge PDF for 3 images");
        Shot("modern-menu-multi-images.png");
        ClickName("Merge PDF");
        var output = WaitForFile(Path.Combine(Work, "Merged.pdf"));
        Check(output is not null, "Merged.pdf appeared");
        Cleanup();
    }

    private static void FolderZip()
    {
        using var session = OpenFolder(Work);
        var (ms, entry) = RightClickAndFindEntry(session, ["folder"], "folder");
        Check(entry is not null, $"menu for a folder ({ms:F0} ms)");
        if (entry is null) return;
        var items = OpenSubmenu(entry, "Zip");
        Console.WriteLine("submenu: " + string.Join(" | ", items));
        Check(items.SequenceEqual(["Zip", "----", "More options…"]) || items.SequenceEqual(["Zip", "More options…"]), "folder: Zip and More options… only");
        Shot("modern-menu-folder.png");
        ClickName("Zip");
        var output = WaitForFile(Path.Combine(Work, "folder.zip"));
        Check(output is not null, "folder.zip appeared");
        Cleanup();
    }

    private static void Mixed()
    {
        using var session = OpenFolder(Work);
        var (ms, entry) = RightClickAndFindEntry(session, ["folder", "photo1.jpg"], "photo1.jpg");
        Check(entry is not null, $"menu for a folder plus a file ({ms:F0} ms)");
        if (entry is null) return;
        var items = OpenSubmenu(entry, "Zip");
        Console.WriteLine("submenu: " + string.Join(" | ", items));
        Check(items.Contains("Zip") && !items.Any(i => i.StartsWith("Convert to", StringComparison.Ordinal)), "mixed selection: Zip (and More options…), no format conversions");
        Shot("modern-menu-mixed.png");
    }

    private static void Heic()
    {
        var heic = Path.Combine(Work, "IMG_0001.heic");
        if (!File.Exists(heic)) { Console.WriteLine("SKIP no IMG_0001.heic in the work folder"); return; }
        using var session = OpenFolder(Work);
        var (ms, entry) = RightClickAndFindEntry(session, ["IMG_0001.heic"], "IMG_0001.heic");
        Check(entry is not null, $"menu for a HEIC ({ms:F0} ms)");
        if (entry is null) return;
        var items = OpenSubmenu(entry, "Convert to JPG");
        Console.WriteLine("submenu: " + string.Join(" | ", items));
        Check(items.Contains("Convert to JPG") && items.Contains("Convert to PNG") && items.Contains("Convert to PDF") && items.Contains("Compress"), "HEIC: Convert to JPG, PNG, PDF and Compress");
        Shot("modern-menu-heic.png");
        ClickName("Convert to JPG");
        var output = WaitForFile(Path.Combine(Work, "IMG_0001.jpg"));
        Check(output is not null, "IMG_0001.jpg appeared");
        Cleanup();
    }

    private static void Big600()
    {
        var big = Path.Combine(Work, "big");
        using var session = OpenFolder(big);
        // Reference: one file in the same folder.
        var (single, e1) = RightClickAndFindEntry(session, ["img001.jpg"], "img001.jpg");
        Console.WriteLine($"1 item: entry visible after {single:F0} ms");
        CloseMenus();
        Thread.Sleep(400);

        var selected = session.SelectAll();
        Check(selected == 600, $"{selected} items selected in the folder");
        var (ms, entry) = RightClickAndFindEntry(session, null, "img005.jpg");
        Check(entry is not null, $"menu for 600 selected files appeared ({ms:F0} ms after the right-click; 1 item: {single:F0} ms)");
        Check(ms < 2500, "600-file menu appears within 2.5 s");
        if (entry is null) return;
        var items = OpenSubmenu(entry, "Zip");
        Console.WriteLine("submenu: " + string.Join(" | ", items));
        // Explorer builds the menu from the first 16 items of a big selection (see ARCHITECTURE.md), so the entries are those of 16 JPEGs.
        Check(items.Contains("Convert to PNG") && items.Contains("Merge PDF") && items.Contains("Zip") && items.Contains("More options…"), "600 JPEGs: the image entries, Zip and More options…");
        Shot("modern-menu-600-files.png");

        // What does Invoke receive: the whole selection or only what the menu was built from?
        var before = Directory.EnumerateFiles(big).ToHashSet();
        ClickName("Zip");
        var sw = Stopwatch.StartNew();
        string? zip = null;
        while (sw.ElapsedMilliseconds < 30000 && zip is null)
        {
            zip = Directory.EnumerateFiles(big, "*.zip").FirstOrDefault(f => !before.Contains(f));
            if (zip is null) Thread.Sleep(100);
        }
        Check(zip is not null, "a zip appeared");
        if (zip is not null)
        {
            Thread.Sleep(800);
            using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
            Console.WriteLine($"  {Path.GetFileName(zip)} holds {archive.Entries.Count} entries");
            Check(archive.Entries.Count == 600, "Zip ran on all 600 selected files");
            archive.Dispose();
            File.Delete(zip);
        }
    }

    private static void Classic()
    {
        using var session = OpenFolder(Work);
        var (ms, top) = RightClickAndFindEntry(session, ["photo1.jpg"], "photo1.jpg", shift: true, classic: true);
        Check(top is not null, $"the classic menu (Shift+right-click) lists \"Convert with MikeShift\" ({ms:F0} ms)");
        if (top is null) return;
        var items = OpenSubmenu(top, "Convert to PNG", classic: true);
        Console.WriteLine("submenu: " + string.Join(" | ", items));
        Check(items.Contains("Convert to PNG"), "classic menu submenu has the formats");
        ShotClassic("classic-menu-jpg.png", top);
        CloseMenus();
        Thread.Sleep(500);

        // And through "Show more options" of the modern menu.
        var (ms2, entry) = RightClickAndFindEntry(session, ["photo1.jpg"], "photo1.jpg");
        var more = MenuFinder.WaitFor("Show more options", Stopwatch.GetTimestamp(), 3000).Element;
        Check(more is not null, "modern menu has \"Show more options\"");
        if (more is not null)
        {
            var r = more.Current.BoundingRectangle;
            RealInput.LeftClick((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
            var (el, t) = WaitClassicEntry(Stopwatch.GetTimestamp());
            Check(el is not null, $"\"Show more options\" opens the classic menu with the entry ({t:F0} ms)");
        }
    }

    private static void MoreOptions()
    {
        using var session = OpenFolder(Work);
        var (ms, entry) = RightClickAndFindEntry(session, ["photo1.jpg", "photo2.jpg"], "photo1.jpg");
        Check(entry is not null, $"menu for 2 images ({ms:F0} ms)");
        if (entry is null) return;
        OpenSubmenu(entry, "More options…");
        var stamp = Stopwatch.GetTimestamp();
        ClickName("More options…");
        // The wheel is a MikeShift window with a UI Automation list whose items are the tiles (the welcome window is excluded: it only draws one).
        AutomationElement? tile = null;
        while (RealInput.MsSince(stamp) < 8000 && tile is null)
        {
            tile = FindWheelTile("Convert to PNG");
            if (tile is null) Thread.Sleep(50);
        }
        Check(tile is not null, $"the wheel opened with its tiles ({RealInput.MsSince(stamp):F0} ms after the click)");
        Thread.Sleep(600);
        ShotWheel("wheel-from-more-options.png");
        RealInput.Press(RealInput.VkEscape);
        Thread.Sleep(500);
    }

    /// <summary>
    /// Right-click to "menu on screen" for three selections, several rounds each, measured on a shell-owned item ("Properties") so it works with
    /// and without the extension registered; with it registered the time to our own entry is printed as well.
    /// </summary>
    private static int Timing(int rounds)
    {
        var big = Path.Combine(Work, "big");
        var cases = new (string Label, string Folder, string[]? Select, bool All, string ClickOn)[]
        {
            ("1 JPG", Work, ["photo1.jpg"], false, "photo1.jpg"),
            ("3 images", Work, ["photo1.jpg", "photo2.jpg", "photo3.jpg"], false, "photo2.jpg"),
            ("600 files", big, null, true, "img005.jpg"),
        };
        foreach (var c in cases)
        {
            using var session = OpenFolder(c.Folder);
            if (c.All) session.SelectAll();
            var shell = new List<double>();
            var ours = new List<double>();
            for (var i = 0; i < rounds; i++)
            {
                session.Explorer.Activate();
                if (c.Select is not null) session.Select(c.Select);
                var (x, y) = session.CenterOf(c.ClickOn);
                var stamp = RealInput.RightClick(x, y);
                var (props, ms) = MenuFinder.WaitInPopups("Properties", stamp, 8000);
                if (props is null) { Console.WriteLine($"  {c.Label} round {i + 1}: menu did not appear"); CloseMenus(); continue; }
                shell.Add(ms);
                var entry = MenuFinder.FindInPopups("Convert with MikeShift");
                if (entry is not null) ours.Add(RealInput.MsSince(stamp));
                CloseMenus();
                Thread.Sleep(400);
            }
            Console.WriteLine($"{c.Label,-10} menu on screen: {Summary(shell)}   entry visible: {(ours.Count == 0 ? "(not registered)" : Summary(ours))}");
        }
        return 0;
    }

    private static string Summary(List<double> ms)
    {
        if (ms.Count == 0) return "n/a";
        var sorted = ms.OrderBy(v => v).ToList();
        return $"first {ms[0]:F0} ms, median {sorted[sorted.Count / 2]:F0} ms, min {sorted[0]:F0}, max {sorted[^1]:F0} (n={ms.Count})";
    }

    /// <summary>Makes sure the tray app is running and its one-time welcome window (first run of a fresh install) is closed, so it cannot be mistaken for the wheel.</summary>
    private static void PrepareTray()
    {
        var sw = Stopwatch.StartNew();
        var pipe = "mikeshift-shell-" + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        var up = false;
        for (var attempt = 0; attempt < 2 && !up; attempt++)
        {
            try
            {
                using var client = new System.IO.Pipes.NamedPipeClientStream(".", pipe, System.IO.Pipes.PipeDirection.InOut);
                client.Connect(attempt == 0 ? 500 : 20000);
                using (var w = new BinaryWriter(client, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    w.Write(0x3148534Du); w.Write((byte)3); w.Write(""); w.Write(0); w.Flush();
                }
                up = client.ReadByte() == 1;
            }
            catch (Exception) when (attempt == 0)
            {
                var family = Environment.GetEnvironmentVariable("HARNESS_PFN") ?? "MikeShift_zatzfr9kzh5xt";
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{family}!App") { UseShellExecute = true });
            }
        }
        Console.WriteLine($"  tray app {(up ? "ready" : "NOT reachable")} after {sw.ElapsedMilliseconds} ms");
        Thread.Sleep(2500);
        CloseWelcome();
    }

    private static void CloseWelcome()
    {
        var pids = Process.GetProcessesByName("MikeShift").Select(p => p.Id).ToHashSet();
        foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            try
            {
                if (!pids.Contains(w.Current.ProcessId) || !w.Current.Name.StartsWith("Welcome to MikeShift", StringComparison.Ordinal)) continue;
                if (w.TryGetCurrentPattern(WindowPattern.Pattern, out var p)) { ((WindowPattern)p).Close(); Console.WriteLine("  closed the first-run welcome window"); }
            }
            catch (ElementNotAvailableException) { }
        }
        Thread.Sleep(500);
    }

    private static int Dump()
    {
        using var session = OpenFolder(Work);
        session.Select("photo1.jpg");
        var (x, y) = session.CenterOf("photo1.jpg");
        RealInput.RightClick(x, y);
        Thread.Sleep(1500);
        Console.WriteLine(MenuFinder.Dump(7));
        CloseMenus();
        return 0;
    }

    // ------------------------------------------------------------------ steps

    private sealed class Session : IDisposable
    {
        public required ExplorerDriver Explorer;
        public void Select(params string[] names) => Explorer.Select(names);
        public (int X, int Y) CenterOf(string name) => Explorer.CenterOf(name);
        public int SelectAll()
        {
            Explorer.Activate();
            return Explorer.SelectAll();
        }
        public void Dispose() => Explorer.Close();
    }

    private static Session OpenFolder(string folder) => new() { Explorer = ExplorerDriver.Open(folder) };

    private static (double Ms, AutomationElement? Entry) RightClickAndFindEntry(Session session, string[]? select, string clickOn, bool shift = false, bool classic = false)
    {
        session.Explorer.Activate();
        if (select is not null) session.Select(select);
        var (x, y) = session.CenterOf(clickOn);
        var stamp = shift ? RealInput.ShiftRightClick(x, y) : RealInput.RightClick(x, y);
        if (classic)
        {
            var (el, t) = WaitClassicEntry(stamp);
            return (t, el);
        }
        var (entry, ms) = MenuFinder.WaitInPopups("Convert with MikeShift", stamp, 8000);
        return (ms, entry);
    }

    private static (AutomationElement? Element, double Ms) WaitClassicEntry(long since)
    {
        while (RealInput.MsSince(since) < 8000)
        {
            var e = MenuFinder.FindClassic("Convert with MikeShift");
            if (e is not null) return (e, RealInput.MsSince(since));
            Thread.Sleep(10);
        }
        return (null, RealInput.MsSince(since));
    }

    /// <summary>Clicks the entry (opening its submenu) and returns the titles of the submenu items, in order ("----" for the separator).</summary>
    private static List<string> OpenSubmenu(AutomationElement entry, string waitFor, bool classic = false)
    {
        var r = entry.Current.BoundingRectangle;
        RealInput.LeftClick((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
        var stamp = Stopwatch.GetTimestamp();
        AutomationElement? hit = null;
        while (RealInput.MsSince(stamp) < 4000 && hit is null)
        {
            hit = classic ? MenuFinder.FindClassic(waitFor, entry) : MenuFinder.FindInPopups(waitFor);
            if (hit is null) Thread.Sleep(15);
        }
        Thread.Sleep(250);
        return MenuFinder.SubmenuTitles(hit, classic);
    }

    private static void ClickName(string name)
    {
        var e = MenuFinder.FindInPopups(name) ?? MenuFinder.FindClassic(name) ?? throw new InvalidOperationException($"menu item \"{name}\" not found");
        var r = e.Current.BoundingRectangle;
        RealInput.LeftClick((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
    }

    private static void CloseMenus()
    {
        for (var i = 0; i < 3; i++)
        {
            RealInput.Press(RealInput.VkEscape);
            Thread.Sleep(150);
        }
    }

    private static string? WaitForFile(string path, int timeoutMs = 25000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0) { Console.WriteLine($"  {Path.GetFileName(path)} after {sw.ElapsedMilliseconds} ms"); return path; }
            Thread.Sleep(100);
        }
        return null;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static readonly HashSet<string> Baseline = ["photo1.jpg", "photo2.jpg", "photo3.jpg", "sample.png", "doc.pdf", "letter.docx", "IMG_0001.heic"];

    /// <summary>Removes what the scenario produced (anything in the work folder that is not a baseline file or folder).</summary>
    private static void Cleanup()
    {
        Thread.Sleep(500);
        foreach (var f in Directory.EnumerateFiles(Work))
            if (!Baseline.Contains(Path.GetFileName(f))) { try { File.Delete(f); } catch (IOException) { } }
    }

    // ------------------------------------------------------------------ screenshots

    private static void Shot(string name)
    {
        if (ShotsDir is null) return;
        // Copy only the menu bodies (the rows of each popup plus their padding); whatever lies between two popups, and the shadow around them,
        // stays transparent, so no other window that happens to be behind the menu ends up in the picture.
        var rects = MenuFinder.PopupBodies();
        if (rects.Count == 0) { Console.WriteLine("  (no popup to capture)"); return; }
        var u = Union(rects);
        using var bmp = new Bitmap(u.Width, u.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            foreach (var r in rects)
            {
                var body = new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
                g.CopyFromScreen(body.X, body.Y, body.X - u.X, body.Y - u.Y, body.Size);
            }
        }
        var path = Path.Combine(ShotsDir, name);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"  screenshot {name} ({u.Width}x{u.Height})");
    }

    private static void ShotClassic(string name, AutomationElement top)
    {
        if (ShotsDir is null) return;
        // Crop to the entry's row, the rows below it and the submenu: the rows above can name the user's OneDrive accounts.
        var rects = MenuFinder.ClassicMenuRects();
        if (rects.Count == 0) return;
        foreach (var r in rects) Console.WriteLine($"  classic menu window {r}");
        var row = top.Current.BoundingRectangle;
        var left = (int)rects.Min(r => r.Left);
        var right = (int)rects.Max(r => r.Right);
        var topY = (int)row.Top - 4;
        var bottom = Math.Min((int)rects.Max(r => r.Bottom), topY + 24 * 10);
        // The submenu may extend above/below the row; include it fully if it fits next to the row.
        var sub = rects.Where(r => r.Left > row.Right - 20).ToList();
        if (sub.Count > 0) { bottom = Math.Max(bottom, (int)sub.Max(r => r.Bottom)); topY = Math.Min(topY, (int)sub.Min(r => r.Top)); }
        SaveCrop(Path.Combine(ShotsDir, name), new Rectangle(left, topY, right - left, bottom - topY));
    }

    private static void ShotToast(string name)
    {
        if (ShotsDir is null) return;
        var r = MikeShiftWindowRects().OrderBy(x => x.Width * x.Height).FirstOrDefault(x => x.Width > 120 && x.Height > 30 && x.Width < 900);
        if (r.IsEmpty) { Console.WriteLine("  (no toast window found)"); return; }
        SaveCrop(Path.Combine(ShotsDir, name), r);
    }

    private static void ShotWheel(string name)
    {
        if (ShotsDir is null) return;
        var r = MikeShiftWindowRects().OrderByDescending(x => x.Width * x.Height).FirstOrDefault(x => x.Width >= 300);
        if (r.IsEmpty) { Console.WriteLine("  (no wheel window found)"); return; }
        SaveCrop(Path.Combine(ShotsDir, name), r);
    }

    private static Rectangle Union(IEnumerable<System.Windows.Rect> rects)
    {
        var l = rects.Min(r => r.Left); var t = rects.Min(r => r.Top); var rr = rects.Max(r => r.Right); var b = rects.Max(r => r.Bottom);
        return new Rectangle((int)l, (int)t, (int)(rr - l), (int)(b - t));
    }

    private static void SaveCrop(string path, Rectangle r)
    {
        using var bmp = new Bitmap(r.Width, r.Height);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"  screenshot {Path.GetFileName(path)} ({r.Width}x{r.Height})");
    }

    private static IEnumerable<Rectangle> MikeShiftWindowRects()
    {
        var pids = Process.GetProcessesByName("MikeShift").Select(p => p.Id).ToHashSet();
        foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            Rectangle rect = Rectangle.Empty;
            try
            {
                if (!pids.Contains(w.Current.ProcessId) || w.Current.IsOffscreen) continue;
                var b = w.Current.BoundingRectangle;
                if (b.IsEmpty) continue;
                rect = new Rectangle((int)b.X, (int)b.Y, (int)b.Width, (int)b.Height);
            }
            catch (ElementNotAvailableException) { }
            if (!rect.IsEmpty) yield return rect;
        }
    }

    private static AutomationElement? FindWheelTile(string name)
    {
        var pids = Process.GetProcessesByName("MikeShift").Select(p => p.Id).ToHashSet();
        var cond = new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            try
            {
                if (!pids.Contains(w.Current.ProcessId) || w.Current.Name.StartsWith("Welcome", StringComparison.Ordinal)) continue;
                var hit = w.FindFirst(TreeScope.Descendants, cond);
                if (hit is not null) return hit;
            }
            catch (ElementNotAvailableException) { }
        }
        return null;
    }
}
