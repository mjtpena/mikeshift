using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DragHarness;

/// <summary>What one scripted drag does.</summary>
internal sealed record Scenario(
    string Name,
    string Extension = "jpg",
    int Files = 1,
    string? Tile = "PNG",
    string? ExpectedOutput = "photo.png",
    bool ToolsPage = false,
    bool ShiftBeforeClick = false,
    bool EscapeInstead = false,
    bool ReleaseOnHub = false,
    string Data = "files",
    bool ExpectWheel = true,
    string Monitor = "primary",
    bool Edge = false,
    bool Leave = false,
    bool ReleaseOutside = false,
    bool WheelTick = false,
    string? ClickMode = null,
    bool Explorer = false,
    string Description = "");

internal static class Program
{
    private static readonly Scenario[] All =
    [
        new("copy", Description: "Shift-drag a JPG, drop on PNG: photo.png appears, original untouched, COPY"),
        new("tools", Tile: "Resize 50%", ExpectedOutput: "photo 50%.jpg", ToolsPage: true, Description: "Ctrl switches to Tools, drop on Resize 50%"),
        new("shiftfirst", ShiftBeforeClick: true, Description: "Shift held before the click still arms the wheel"),
        new("multi", Files: 2, Tile: "PNG", ExpectedOutput: "photo.png", Description: "Two JPGs, drop on PNG: two outputs"),
        new("esc", Tile: null, ExpectedOutput: null, EscapeInstead: true, Description: "Esc dismisses the wheel, nothing converted"),
        new("nodrop", Tile: null, ExpectedOutput: null, ReleaseOnHub: true, Description: "Release on the hub: nothing converted, original untouched"),
        new("text", Tile: null, ExpectedOutput: null, Data: "text", ExpectWheel: false, Description: "Text drag: no tiles, wheel hides"),
        new("nodrag", Tile: null, ExpectedOutput: null, Data: "none", ExpectWheel: false, Description: "No OLE drag at all (like a window move): the pending wheel hides silently, never a DragEnter"),
        new("virtual", Tile: null, ExpectedOutput: null, Data: "url", Description: "URL/virtual drag: 'Save the file first', NONE"),
        new("leave", Description: "Pointer leaves the wheel (DragLeave) and comes back: the wheel stays and still converts"),
        new("outside", Tile: null, ExpectedOutput: null, ReleaseOutside: true, Description: "Release outside the wheel: dismissed after the timeout, nothing converted"),
        new("wheeltick", Tile: "Resize 50%", ExpectedOutput: "photo 50%.jpg", ToolsPage: true, WheelTick: true, Description: "A mouse-wheel tick toggles to Tools"),
        new("leftmonitor", Monitor: "left", Description: "Same drag on the second monitor, which sits at negative coordinates"),
        new("edge", Monitor: "left", Edge: true, Description: "Shift pressed within 30 px of the screen edge: wheel clamped to the work area, tiles still hit correctly"),
        new("folder", Extension: "dir", Tile: "Zip", ExpectedOutput: "photo.zip", Description: "A folder: only Zip (single full-ring tile on the Tools page), drop produces photo.zip"),
        new("explorer", Explorer: true, Description: "The real Explorer as drag source: Shift-drag photo.jpg from an Explorer window onto PNG (original must not be moved)"),
        new("click-keys", Tile: "PNG", ClickMode: "keys", Description: "Convert Files... click mode: arrow + Enter picks PNG"),
        new("click-mouse", Tile: "PNG", ClickMode: "mouse", Description: "Click mode: hover highlights, mouse-down commits"),
        new("click-esc", Tile: null, ExpectedOutput: null, ClickMode: "esc", Description: "Click mode: Esc cancels"),
        new("click-blur", Tile: null, ExpectedOutput: null, ClickMode: "blur", Description: "Click mode: losing focus dismisses"),
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Native.OleInitialize(IntPtr.Zero);

        string Arg(string name, string fallback)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        var repo = FindRepoRoot();
        var app = Arg("--app", Path.Combine(repo, "src", "Shiftvert.App", "bin", "x64", "Debug", "net10.0-windows10.0.26100.0", "Shiftvert.exe"));
        var wanted = Arg("--scenario", "copy");
        var screenshot = Arg("--screenshot", "");
        var theme = Arg("--theme", "dark");
        var tileOverride = Arg("--tile", "");

        if (!File.Exists(app))
        {
            Console.Error.WriteLine("App not found: " + app + " (build the solution first, or pass --app)");
            return 2;
        }
        if (Native.FindWindowW("ShiftvertWheel", null) != IntPtr.Zero)
        {
            Console.Error.WriteLine("A Shiftvert process is already running; quit it first (the harness needs its own instance and log).");
            return 2;
        }

        var scenarios = wanted == "all" ? All : All.Where(s => s.Name == wanted).ToArray();
        if (scenarios.Length == 0)
        {
            Console.Error.WriteLine("Unknown scenario. Known: " + string.Join(", ", All.Select(s => s.Name)) + ", all");
            return 2;
        }

        var failures = 0;
        foreach (var s0 in scenarios)
        {
            var s = tileOverride.Length > 0 ? s0 with { Tile = tileOverride } : s0;
            Console.WriteLine($"=== scenario {s.Name}: {s.Description}");
            var run = new ScenarioRun(s, app, theme, wanted == "all" || screenshot.Length == 0 ? "" : screenshot);
            var ok = run.Execute();
            Console.WriteLine(ok ? $"=== {s.Name}: PASS" : $"=== {s.Name}: FAIL");
            if (!ok) failures++;
        }
        Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} scenario(s) FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shiftvert.sln"))) dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}

/// <summary>One scripted drag against a freshly started Shiftvert.exe.</summary>
internal sealed class ScenarioRun
{
    private readonly Scenario _s;
    private readonly string _appPath;
    private readonly string _theme;
    private readonly string _screenshot;
    private readonly string _work;
    private readonly string _logPath;
    private readonly List<(string Name, bool Ok, string Detail)> _checks = [];
    private readonly ManualResetEventSlim _dragStarted = new(false);
    private readonly ManualResetEventSlim _dragFinished = new(false);
    private uint _effect = 0xFFFF;
    private int _dragHr;
    private Process? _process;
    private BackdropForm _form = null!;

    public ScenarioRun(Scenario scenario, string appPath, string theme, string screenshot)
    {
        _s = scenario;
        _appPath = appPath;
        _theme = theme;
        _screenshot = screenshot;
        _work = Path.Combine(Path.GetTempPath(), "shiftvert-harness-" + Guid.NewGuid().ToString("N")[..8]);
        _logPath = Path.Combine(_work, "app-log.txt");
    }

    private void Check(string name, bool ok, string detail = "")
    {
        _checks.Add((name, ok, detail));
        Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {name}" + (detail.Length > 0 ? " - " + detail : ""));
    }

    public bool Execute()
    {
        Directory.CreateDirectory(_work);
        var inputs = CreateInputs();
        var hashes = inputs.ToDictionary(p => p, Sha);
        var sizes = inputs.ToDictionary(p => p, SizeOf);

        try
        {
            if (_s.ClickMode is not null)
            {
                // Something colourful and harmless under the click-mode wheel (it opens at the centre of the primary monitor).
                _form = new BackdropForm("primary");
                _form.Show();
                for (var i = 0; i < 15; i++) { Application.DoEvents(); Thread.Sleep(20); }
            }
            StartApp(_s.ClickMode is null ? null : inputs);
            if (_s.Explorer) RunExplorer(inputs);
            else if (_s.ClickMode is null) RunUi(inputs);
            else ClickScript(inputs);
            VerifyOutputs(inputs, hashes, sizes);
        }
        catch (Exception ex)
        {
            Check("harness ran without exceptions", false, ex.ToString());
        }
        finally
        {
            ReleaseEverything();
            if (_s.ClickMode is not null) { try { _form.Close(); } catch (Exception) { } }
            StopApp();
            try { if (_checks.All(c => c.Ok)) Directory.Delete(_work, true); }
            catch (IOException) { }
        }
        if (_checks.Any(c => !c.Ok)) Console.WriteLine("  work dir kept: " + _work);
        return _checks.All(c => c.Ok);
    }

    // ------------------------------------------------------------------ setup

    private List<string> CreateInputs()
    {
        var list = new List<string>();
        for (var i = 0; i < _s.Files; i++)
        {
            if (_s.Extension == "dir")
            {
                var folder = Path.Combine(_work, "photo");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "a.txt"), "hello from the harness");
                File.WriteAllText(Path.Combine(folder, "b.txt"), "second file");
                list.Add(folder);
                continue;
            }
            var path = Path.Combine(_work, (_s.Files == 1 ? "photo" : "photo" + (i + 1)) + "." + _s.Extension);
            using var bmp = new Bitmap(640, 400);
            using (var g = Graphics.FromImage(bmp))
            {
                using var brush = new LinearGradientBrush(new Rectangle(0, 0, 640, 400), Color.OrangeRed, Color.RoyalBlue, 35f);
                g.FillRectangle(brush, 0, 0, 640, 400);
                g.DrawString("harness " + (i + 1), new Font("Segoe UI", 40), Brushes.White, 30, 150);
            }
            bmp.Save(path, _s.Extension == "png" ? ImageFormat.Png : ImageFormat.Jpeg);
            list.Add(path);
        }
        return list;
    }

    private static string Sha(string path)
    {
        if (!Directory.Exists(path)) return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        // A folder: hash every file's relative name and content.
        using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            all.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(path, f)));
            all.AppendData(File.ReadAllBytes(f));
        }
        return Convert.ToHexString(all.GetHashAndReset());
    }

    private static long SizeOf(string path) =>
        Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : new FileInfo(path).Length;

    private void StartApp(IReadOnlyList<string>? convert)
    {
        var psi = new ProcessStartInfo(_appPath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(_appPath)! };
        if (convert is not null)
        {
            // Dev hook of the app: behaves like Convert Files... with these paths.
            psi.ArgumentList.Add("--convert");
            foreach (var path in convert) psi.ArgumentList.Add(path);
        }
        psi.Environment["SHIFTVERT_LOG"] = _logPath;
        psi.Environment["SHIFTVERT_TRACE"] = "1";
        psi.Environment["SHIFTVERT_ACCEPT_INJECTED"] = "1";
        psi.Environment["SHIFTVERT_THEME"] = _theme;
        if (Environment.GetEnvironmentVariable("SHIFTVERT_ANIMATIONS") is { } animations) psi.Environment["SHIFTVERT_ANIMATIONS"] = animations;
        _process = Process.Start(psi);
        var ready = WaitLog(t => t.Contains("hooks installed") && t.Contains("wheel ready"), 20000);
        Check("app started, wheel ready, hooks installed", ready, ready ? "" : Log());
        if (!ready) throw new InvalidOperationException("app did not start");
    }

    private void StopApp()
    {
        try
        {
            if (_process is { HasExited: false }) { _process.Kill(true); _process.WaitForExit(3000); }
        }
        catch (Exception) { /* already gone */ }
    }

    private string Log()
    {
        try
        {
            using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    private bool WaitLog(Func<string, bool> predicate, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (predicate(Log())) return true;
            Pause(15);
        }
        return predicate(Log());
    }

    /// <summary>
    /// Sleeps; on the STA main thread it keeps pumping messages, because that thread owns the drag source's COM objects (data object,
    /// drop source) and the wheel's proxies must be able to talk to it after the drop.
    /// </summary>
    private static void Pause(int ms)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) { Thread.Sleep(ms); return; }
        var sw = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            Thread.Sleep(Math.Min(5, ms));
        } while (sw.ElapsedMilliseconds < ms);
    }

    private static bool WheelVisible()
    {
        var hwnd = Native.FindWindowW("ShiftvertWheel", null);
        return hwnd != IntPtr.Zero && Native.IsWindowVisible(hwnd);
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            Pause(5);
        }
        return condition();
    }

    // ------------------------------------------------------------------ the UI and the script

    private void RunUi(List<string> inputs)
    {
        _form = new BackdropForm(_s.Monitor, _s.Edge);
        _form.DragRequested += () =>
        {
            if (_s.Data == "none")
            {
                _dragStarted.Set(); // a plain mouse press-move with no DoDragDrop
                return;
            }
            ComIDataObject data = _s.Data switch
            {
                "text" => new TextDataObject("selected text, not a file"),
                "url" => new TextDataObject("https://example.invalid/picture.png", "UniformResourceLocatorW"),
                _ => Native.CreateShellDataObject(inputs),
            };
            Console.WriteLine("  data object formats (source side): " + DescribeFormats(data));
            _dragStarted.Set();
            _dragHr = Native.DoDragDrop(data, new DropSource(), Native.DROPEFFECT_COPY | Native.DROPEFFECT_MOVE | Native.DROPEFFECT_LINK, out _effect);
            _dragFinished.Set();
        };

        var script = new Thread(() =>
        {
            try { Script(inputs); }
            catch (Exception ex) { Check("script ran without exceptions", false, ex.ToString()); }
            finally
            {
                ReleaseEverything();
                try { _form.BeginInvoke(_form.Close); } catch (Exception) { }
            }
        }) { IsBackground = true, Name = "harness script" };

        _form.Shown += (_, _) => script.Start();
        Application.Run(_form);
    }

    private IntPtr _explorerHwnd;

    private void RunExplorer(List<string> inputs)
    {
        try { Script(inputs); }
        catch (Exception ex) { Check("script ran without exceptions", false, ex.ToString()); }
        finally
        {
            ReleaseEverything();
            if (_explorerHwnd != IntPtr.Zero) Native.PostMessageW(_explorerHwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private bool AwaitDragEnd(int ms)
    {
        if (!_s.Explorer) return _dragFinished.Wait(ms);
        Thread.Sleep(Math.Min(ms, 1200)); // Explorer's DoDragDrop is not ours to observe
        return true;
    }

    /// <summary>Opens the temp folder in a real Explorer window and finds photo.jpg's on-screen position through UI Automation.</summary>
    private ((int X, int Y) Start, (int X, int Y) Centre, Rectangle Area) OpenExplorerAndLocate(string file)
    {
        var leaf = Path.GetFileName(_work);
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _work + "\"") { UseShellExecute = true });

        System.Windows.Automation.AutomationElement? win = null;
        var found = WaitFor(() =>
        {
            var windows = System.Windows.Automation.AutomationElement.RootElement.FindAll(System.Windows.Automation.TreeScope.Children,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ClassNameProperty, "CabinetWClass"));
            win = windows.Cast<System.Windows.Automation.AutomationElement>().FirstOrDefault(w => w.Current.Name.Contains(leaf, StringComparison.OrdinalIgnoreCase));
            return win is not null;
        }, 20000);
        Check("an Explorer window opened on the temp folder", found, leaf);
        if (!found) throw new InvalidOperationException("no Explorer window");

        _explorerHwnd = new IntPtr(win!.Current.NativeWindowHandle);
        Native.SetForegroundWindow(_explorerHwnd);

        System.Windows.Automation.AutomationElement? item = null;
        var haveItem = WaitFor(() =>
        {
            item = win.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, Path.GetFileName(file)));
            return item is not null;
        }, 10000);
        Check("UI Automation found the file in the Explorer list", haveItem);
        if (!haveItem) throw new InvalidOperationException("file item not found");

        var r = item!.Current.BoundingRectangle;
        var start = ((int)(r.Left + Math.Min(r.Width / 2, 30)), (int)(r.Top + r.Height / 2));
        Console.WriteLine($"  file item at ({start.Item1},{start.Item2}), item rect {r}");
        var screen = Screen.FromPoint(new Point(start.Item1, start.Item2)).WorkingArea;
        var centre = (Math.Clamp(start.Item1 + 420, screen.Left + 250, screen.Right - 250), Math.Clamp(start.Item2 + 40, screen.Top + 250, screen.Bottom - 250));
        return (start, centre, screen);
    }

    private static void ReleaseEverything()
    {
        try
        {
            Pointer.LeftUp();
            Pointer.KeyUp(Native.VK_LSHIFT);
            Pointer.KeyUp(Native.VK_LCONTROL);
            Pointer.KeyUp(Native.VK_SHIFT);
            Pointer.KeyUp(Native.VK_CONTROL);
        }
        catch (Exception) { }
    }

    private void Script(List<string> inputs)
    {
        Thread.Sleep(600); // let the form settle and the app's first-run window (if any) go
        Rectangle area;
        (int X, int Y) start, centre;
        if (_s.Explorer)
        {
            var located = OpenExplorerAndLocate(inputs[0]);
            (start, centre, area) = (located.Start, located.Centre, located.Area);
        }
        else
        {
            area = _form.Bounds;
            start = (X: area.Left + 110, Y: area.Top + area.Height / 2);
            centre = _s.Edge
                ? (X: area.Left + 30, Y: area.Top + area.Height / 2)
                : (X: area.Left + area.Width / 2, Y: area.Top + area.Height / 2);
            if (_s.Edge) start = (area.Left + 150, start.Y);
        }

        Pointer.Glide(start.X, start.Y);
        Thread.Sleep(150);
        if (_s.ShiftBeforeClick)
        {
            Pointer.KeyDown(Native.VK_LSHIFT);
            Thread.Sleep(80);
        }
        Pointer.LeftDown();
        var began = _s.Explorer || _dragStarted.Wait(3000);
        if (_s.Explorer) Thread.Sleep(300);
        Check(_s.Explorer ? "left button pressed on the Explorer item" : "OLE drag started in the harness window", began);
        if (!began) return;
        Thread.Sleep(200);

        // Move past the drag threshold toward the middle; with Shift already down the wheel arms at the threshold.
        Pointer.Glide(centre.X, centre.Y);
        var shiftAt = Stopwatch.StartNew();
        if (!_s.ShiftBeforeClick) Pointer.KeyDown(Native.VK_LSHIFT);

        var visible = WaitFor(WheelVisible, 2000);
        var shownAfter = shiftAt.ElapsedMilliseconds;
        Check("wheel window became visible", visible, visible ? $"{(_s.ShiftBeforeClick ? "since arming" : "after Shift")}: {shownAfter} ms (PLAN target < 100 ms)" : "");
        if (!visible) { Check("log (for diagnosis)", false, Log()); return; }

        if (_s.Edge)
        {
            // The window can be on screen a few milliseconds before the app has written its "wheel shown" log line.
            WaitFor(() => Regex.IsMatch(Log(), @"wheel shown pending gen=\d+ at \("), 1000);
            var m = Regex.Matches(Log(),@"wheel shown pending gen=\d+ at \((-?\d+),(-?\d+)\) rect=\((-?\d+),(-?\d+),(\d+)\)").LastOrDefault();
            var rectLeft = m is null ? int.MinValue : int.Parse(m.Groups[3].Value);
            var cursorX = m is null ? 0 : int.Parse(m.Groups[1].Value);
            Check("wheel window was clamped to the monitor's work area", m is not null && rectLeft == area.Left && cursorX - 190 < area.Left, $"cursor x={cursorX}, window left={rectLeft}, work area left={area.Left}");
        }

        if (_s.Data == "none")
        {
            var gone = WaitFor(() => !WheelVisible(), 1500);
            Check("the pending wheel hid silently (no DragEnter within 250 ms)", gone);
            var l = Log();
            Check("log: HideSilently, no DragEnter", l.Contains("HideSilently") && !l.Contains("DragEnter at"));
            Pointer.LeftUp();
            Pointer.KeyUp(Native.VK_LSHIFT);
            return;
        }

        // The pending wheel must turn into an OLE DragEnter on its own (no pointer movement from us).
        var entered = WaitLog(t => t.Contains("DragEnter at"), 2000);
        Check("OLE delivered DragEnter to the wheel (stationary pointer, nudge)", entered, entered ? NudgeSummary() : Log());
        if (!entered) return;

        if (!_s.ExpectWheel)
        {
            var hidden = WaitFor(() => !WheelVisible(), 1500);
            Check("wheel hid for a non-file drag", hidden);
            var log0 = Log();
            Check("DragEnter classified the drag as 'Other'", log0.Contains("kind=Other"), "");
            Pointer.LeftUp();
            AwaitDragEnd(3000);
            Pointer.KeyUp(Native.VK_LSHIFT);
            return;
        }

        if (_s.Data == "url")
        {
            Thread.Sleep(400);
            var logU = Log();
            Check("virtual-file drag classified, hint shown", logU.Contains("kind=VirtualFiles"));
            TakeScreenshot(centre);
            Pointer.Glide(centre.X + 40, centre.Y - 70);
            Thread.Sleep(250);
            Pointer.LeftUp();
            Pointer.KeyUp(Native.VK_LSHIFT);
            return;
        }

        var tiled = WaitLog(t => t.Contains("tiles:"), 4000);
        Check("tiles populated from the dragged paths", tiled, tiled ? LastLine("tiles:") : Log());
        if (!tiled) return;
        Thread.Sleep(450); // let the open animation play

        if (_s.EscapeInstead)
        {
            TakeScreenshot(centre);
            Pointer.KeyDown(Native.VK_ESCAPE);
            Thread.Sleep(30);
            Pointer.KeyUp(Native.VK_ESCAPE);
            var done = AwaitDragEnd(3000);
            Check("DoDragDrop ended after Esc", done);
            var gone = WaitFor(() => !WheelVisible(), 1500);
            Check("wheel hidden after Esc", gone);
            Pointer.KeyUp(Native.VK_LSHIFT);
            return;
        }

        if (_s.WheelTick)
        {
            Pointer.Wheel(-120);
            var ticked = WaitLog(t => t.Contains("page = Tool"), 1500);
            Check("a mouse-wheel tick switched the wheel to the Tools page", ticked);
            Thread.Sleep(550);
        }
        else if (_s.ToolsPage)
        {
            Pointer.KeyDown(Native.VK_LCONTROL);
            var toggled = WaitLog(t => t.Contains("page = Tool"), 1500);
            Check("Ctrl switched the wheel to the Tools page", toggled);
            Pointer.KeyUp(Native.VK_LCONTROL);
            Thread.Sleep(550);
        }

        if (_s.ReleaseOnHub)
        {
            TakeScreenshot(centre);
            Pointer.LeftUp();
            Pointer.KeyUp(Native.VK_LSHIFT);
            AwaitDragEnd(3000);
            WaitFor(() => !WheelVisible(), 2500);
            return;
        }

        if (_s.ReleaseOutside)
        {
            var (_, _, wc, sc) = LocateTile("PNG");
            var far = (wc.X + (int)(280 * sc), wc.Y);
            Pointer.Glide(far.Item1, far.Item2);
            Thread.Sleep(300);
            Check("DragLeave reached the wheel when the pointer left it", Log().Contains("DragLeave"));
            Check("the wheel stays while the button is still down", WheelVisible());
            TakeScreenshot(wc);
            Pointer.LeftUp();
            Pointer.KeyUp(Native.VK_LSHIFT);
            AwaitDragEnd(3000);
            var closed = WaitFor(() => !WheelVisible(), 2500);
            Check("released outside: wheel dismissed (400 ms timeout + fade)", closed);
            return;
        }

        var tile = _s.Tile!;
        var (index, count, wheelCentre, scale) = LocateTile(tile);
        if (_s.Leave)
        {
            Pointer.Glide(wheelCentre.X + (int)(280 * scale), wheelCentre.Y);
            Thread.Sleep(400);
            Check("DragLeave reached the wheel when the pointer left it", Log().Contains("DragLeave"));
            Check("the wheel stays while the button is still down", WheelVisible());
        }
        Check($"tile '{tile}' is on the {(_s.ToolsPage ? "tools" : "expected")} page", index >= 0, $"index {index} of {count}");
        if (index < 0) return;

        var angle = (index + 0.5) * 2 * Math.PI / count;
        var radius = 100 * scale;
        var target = ((int)Math.Round(wheelCentre.X + radius * Math.Sin(angle)), (int)Math.Round(wheelCentre.Y - radius * Math.Cos(angle)));
        Pointer.Glide(target.Item1, target.Item2, stepPx: 4, stepMs: 10);
        var highlighted = WaitLog(t => t.Contains("highlight: " + tile + " "), 2000);
        Check($"moving onto the tile highlighted '{tile}'", highlighted, LastLine("highlight:"));
        var described = Log().Contains("highlight: " + tile + " (drop description set: True)");
        Check("Explorer-side drop description accepted by the data object", described, described ? "" : "the data object refused CFSTR_DROPDESCRIPTION (or the helper was not used)");
        Thread.Sleep(350);
        TakeScreenshot(centre);

        Pointer.LeftUp();
        Pointer.KeyUp(Native.VK_LSHIFT);
        var finished = AwaitDragEnd(5000);
        Check("DoDragDrop returned", finished);
    }


    // ------------------------------------------------------------------ click mode (Convert Files...)

    private void ClickScript(List<string> inputs)
    {
        var shown = WaitLog(t => t.Contains("click-mode wheel shown"), 10000);
        Check("click-mode wheel opened for Convert Files...", shown, shown ? LastLine("click-mode wheel shown") : Log());
        if (!shown) return;
        Thread.Sleep(600); // opening animation
        var line = LastLine("click-mode wheel shown");
        Check("wheel window is visible", WheelVisible());
        Check("click-mode wheel took keyboard focus (foreground)", line.Contains("foreground=True") || Log().Contains("foreground retried, now foreground=True"), line);

        var (index, count, wheelCentre, scale) = LocateTile(_s.Tile ?? "PNG");
        UiaCheck(count);
        var area = Screen.PrimaryScreen!.Bounds;

        switch (_s.ClickMode)
        {
            case "keys":
                Pointer.Glide(wheelCentre.X, wheelCentre.Y); // park the pointer on the hub so hover does not interfere with the keys
                Thread.Sleep(300);
                var focusEvents = new List<string>();
                System.Windows.Automation.AutomationFocusChangedEventHandler onFocus = (_, _) =>
                {
                    try { focusEvents.Add(System.Windows.Automation.AutomationElement.FocusedElement.Current.Name); }
                    catch (Exception) { }
                };
                System.Windows.Automation.Automation.AddAutomationFocusChangedEventHandler(onFocus);
                TakeScreenshot(wheelCentre);
                Pointer.KeyDown(0x27); // Right: first tile
                Pointer.KeyUp(0x27);
                Check("Right arrow highlighted the first tile", WaitLog(t => t.Contains("highlight: " + _s.Tile), 2000), LastLine("highlight:"));
                Thread.Sleep(500);
                UiaFocusCheck(_s.Tile!);
                System.Windows.Automation.Automation.RemoveAllEventHandlers();
                lock (focusEvents)
                    Check("UIA: a focus-changed event announced the highlighted tile (what Narrator reads)", focusEvents.Any(n => n.Contains("Convert to " + _s.Tile)), "events: " + string.Join(" | ", focusEvents));
                Pointer.KeyDown(0x0D); // Enter
                Pointer.KeyUp(0x0D);
                break;

            case "mouse":
            {
                var angle = (index + 0.5) * 2 * Math.PI / count;
                var radius = 100 * scale;
                var target = ((int)Math.Round(wheelCentre.X + radius * Math.Sin(angle)), (int)Math.Round(wheelCentre.Y - radius * Math.Cos(angle)));
                Pointer.Glide(target.Item1, target.Item2, stepPx: 8, stepMs: 6);
                Check("hovering the tile highlighted it", WaitLog(t => t.Contains("highlight: " + _s.Tile), 2000), LastLine("highlight:"));
                Thread.Sleep(350);
                TakeScreenshot(wheelCentre);
                Pointer.LeftDown();
                Pointer.LeftUp();
                break;
            }

            case "esc":
                Pointer.KeyDown(Native.VK_ESCAPE);
                Pointer.KeyUp(Native.VK_ESCAPE);
                Check("Esc dismissed the click-mode wheel", WaitFor(() => !WheelVisible(), 2000));
                break;

            case "blur":
            {
                var other = new BackdropForm("primary", small: true);
                other.Show();
                other.Activate();
                for (var i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(20); }
                Check("another window took focus: the wheel dismissed itself", WaitFor(() => !WheelVisible(), 2000));
                other.Close();
                break;
            }
        }

        if (_s.ExpectedOutput is not null) WaitFor(() => File.Exists(Path.Combine(_work, _s.ExpectedOutput)), 15000);
        WaitFor(() => !WheelVisible(), 2500);
    }

    private void UiaCheck(int tileCount)
    {
        try
        {
            var hwnd = Native.FindWindowW("ShiftvertWheel", null);
            var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
            var type = root.Current.ControlType;
            Check("UIA: the wheel window is a List", type == System.Windows.Automation.ControlType.List, $"{type.ProgrammaticName}, name \"{root.Current.Name}\"");
            var children = root.FindAll(System.Windows.Automation.TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
            Check("UIA: tiles are ListItems", children.Count == tileCount && children.Cast<System.Windows.Automation.AutomationElement>().All(c => c.Current.ControlType == System.Windows.Automation.ControlType.ListItem),
                $"{children.Count} children: " + string.Join(", ", children.Cast<System.Windows.Automation.AutomationElement>().Select(c => c.Current.Name)));
        }
        catch (Exception ex)
        {
            Check("UIA: provider reachable", false, ex.Message);
        }
    }

    private void UiaFocusCheck(string expectedTitle)
    {
        try
        {
            var hwnd = Native.FindWindowW("ShiftvertWheel", null);
            var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
            var children = root.FindAll(System.Windows.Automation.TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
            var focused = children.Cast<System.Windows.Automation.AutomationElement>().FirstOrDefault(c => c.Current.HasKeyboardFocus);
            Check("UIA: the highlighted tile reports keyboard focus", focused is not null && focused.Current.Name.Contains(expectedTitle), focused?.Current.Name ?? "(none)");
        }
        catch (Exception ex)
        {
            Check("UIA: focus query", false, ex.Message);
        }
    }

    private static string DescribeFormats(ComIDataObject data)
    {
        try
        {
            var parts = new List<string>();
            var e = data.EnumFormatEtc(System.Runtime.InteropServices.ComTypes.DATADIR.DATADIR_GET);
            var one = new System.Runtime.InteropServices.ComTypes.FORMATETC[1];
            var fetched = new int[1];
            int hr;
            while ((hr = e.Next(1, one, fetched)) == 0) parts.Add($"{(ushort)one[0].cfFormat}/{one[0].tymed}");
            return string.Join(", ", parts) + $" (last hr={hr})";
        }
        catch (Exception ex) { return "error: " + ex.Message; }
    }

    private string NudgeSummary()
    {
        var log = Log();
        var wiggled = log.Contains("wiggling by 1 px");
        var m = Regex.Match(log, @"DragEnter at .*nudgeStage=(\d)");
        return wiggled ? "needed the 1 px wiggle fallback" : "arrived after the zero-move SendInput nudge alone (stage " + (m.Success ? m.Groups[1].Value : "?") + ")";
    }

    private string LastLine(string contains)
    {
        var line = Log().Split('\n').LastOrDefault(l => l.Contains(contains));
        return line?.Trim() ?? "";
    }

    private (int Index, int Count, (int X, int Y) Centre, double Scale) LocateTile(string tile)
    {
        var log = Log();
        var shown = Regex.Matches(log, @"(?:wheel shown pending|click-mode wheel shown) gen=\d+ (?:at \(-?\d+,-?\d+\) )?rect=\((-?\d+),(-?\d+),(\d+)\)").LastOrDefault();
        if (shown is null) return (-1, 0, default, 1);
        var left = int.Parse(shown.Groups[1].Value);
        var top = int.Parse(shown.Groups[2].Value);
        var size = int.Parse(shown.Groups[3].Value);
        var scale = size / 380.0;

        var tilesLine = log.Split('\n').LastOrDefault(l => l.Contains("tiles:"));
        if (tilesLine is null) return (-1, 0, default, scale);
        var body = tilesLine[(tilesLine.IndexOf("tiles:", StringComparison.Ordinal) + 6)..].Trim();
        var parts = body.Split('|');
        var formats = parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var tools = parts.Length > 1 ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() : [];
        var list = _s.ToolsPage || formats.Count == 0 ? tools : formats;
        return (list.IndexOf(tile), list.Count, (left + size / 2, top + size / 2), scale);
    }

    private void TakeScreenshot((int X, int Y) centre)
    {
        if (_screenshot.Length == 0) return;
        try
        {
            var dpi = Native.GetDpiForSystem() / 96.0;
            var half = (int)(300 * dpi);
            var rect = new Rectangle(centre.X - half, centre.Y - half, half * 2, half * 2);
            using var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                var screen = Native.GetDC(IntPtr.Zero);
                var dest = g.GetHdc();
                // SRCCOPY | CAPTUREBLT: include layered windows (the wheel) in the capture.
                Native.BitBlt(dest, 0, 0, rect.Width, rect.Height, screen, rect.X, rect.Y, 0x00CC0020u | 0x40000000u);
                g.ReleaseHdc(dest);
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_screenshot))!);
            bmp.Save(_screenshot, ImageFormat.Png);
            Console.WriteLine("  screenshot: " + Path.GetFullPath(_screenshot));
        }
        catch (Exception ex)
        {
            Console.WriteLine("  screenshot failed: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ assertions

    private void VerifyOutputs(List<string> inputs, Dictionary<string, string> hashes, Dictionary<string, long> sizes)
    {
        if (_s.ClickMode is null && !_s.Explorer && _s.Data != "none") VerifyDragEffect();
        VerifyFiles(inputs, hashes, sizes);
    }

    private void VerifyDragEffect()
    {
        Check("DoDragDrop returned S_OK/DRAGDROP_S_*", _dragHr is 0 or Native.DRAGDROP_S_DROP or Native.DRAGDROP_S_CANCEL, $"hr=0x{_dragHr:X}");

        var effectName = _effect switch { 0 => "NONE", 1 => "COPY", 2 => "MOVE", 4 => "LINK", 0xFFFF => "(never returned)", _ => _effect.ToString() };
        if (_s.Tile is not null && _s.ExpectedOutput is not null)
        {
            Check("DoDragDrop returned DROPEFFECT_COPY (never MOVE)", _effect == Native.DROPEFFECT_COPY, "effect = " + effectName);
        }
        else
        {
            Check("DoDragDrop did not return MOVE", _effect != Native.DROPEFFECT_MOVE, "effect = " + effectName);
            if (_s.Data != "text") Check("DoDragDrop returned NONE (nothing dropped)", _effect == Native.DROPEFFECT_NONE, "effect = " + effectName);
        }
    }

    private void VerifyFiles(List<string> inputs, Dictionary<string, string> hashes, Dictionary<string, long> sizes)
    {
        foreach (var path in inputs)
        {
            var exists = File.Exists(path) || Directory.Exists(path);
            Check("original still exists: " + Path.GetRelativePath(_work, path), exists);
            if (exists)
                Check("original unchanged (SHA-256, size)", Sha(path) == hashes[path] && SizeOf(path) == sizes[path]);
        }

        if (_s.ExpectedOutput is { } expected)
        {
            var outputs = inputs.Count == 1
                ? [Path.Combine(_work, expected)]
                : inputs.Select(p => Path.ChangeExtension(p, Path.GetExtension(expected))).ToList();
            var all = WaitFor(() => outputs.All(File.Exists), 15000);
            Check("converted output(s) appeared next to the input(s): " + expected, all, string.Join(", ", outputs.Select(o => Path.GetRelativePath(_work, o) + (File.Exists(o) ? $" ({new FileInfo(o).Length} bytes)" : " MISSING"))));
            var log = Log();
            if (_s.ClickMode is null)
                Check("log: wheel -> DragEnter -> highlight -> Drop -> job", log.Contains("wheel shown pending") && log.Contains("DragEnter at") && log.Contains("Drop on " + _s.Tile) && Regex.IsMatch(log, @"job \d+ enqueued: " + Regex.Escape(_s.Tile!)));
            else
                Check("log: click-mode wheel -> pick -> job", log.Contains("click-mode wheel shown") && log.Contains("click mode picked " + _s.Tile) && Regex.IsMatch(log, @"job \d+ enqueued: " + Regex.Escape(_s.Tile!)));
            Check("log: nothing logged as ERROR", !log.Contains("[ERROR]"), log.Split('\n').FirstOrDefault(l => l.Contains("[ERROR]"))?.Trim() ?? "");
        }
        else
        {
            Pause(500);
            var stray = Directory.EnumerateFiles(_work, "*", SearchOption.AllDirectories)
                .Where(f => !inputs.Contains(f) && !f.EndsWith("app-log.txt", StringComparison.Ordinal)).ToList();
            Check("no output files were produced", stray.Count == 0, string.Join(", ", stray.Select(s => Path.GetFileName(s))));
            var log = Log();
            Check("log: nothing logged as ERROR", !log.Contains("[ERROR]"), log.Split('\n').FirstOrDefault(l => l.Contains("[ERROR]"))?.Trim() ?? "");
        }
        Check("wheel is hidden at the end", WaitFor(() => !WheelVisible(), 2500));
        Console.WriteLine("  " + LastLine("wheel hidden:"));
    }
}

/// <summary>Borderless topmost window with a colourful gradient: the drag source, and something for the wheel's glass to blur.</summary>
internal sealed class BackdropForm : Form
{
    public event Action? DragRequested;
    private bool _dragged;

    public BackdropForm(string monitor = "primary", bool edge = false, bool small = false)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        var screen = monitor == "left"
            ? Screen.AllScreens.FirstOrDefault(sc => !sc.Primary) ?? Screen.PrimaryScreen!
            : Screen.PrimaryScreen!;
        var wa = screen.WorkingArea;
        if (edge) Bounds = wa;
        else if (small) Bounds = new Rectangle(wa.Left + 100, wa.Top + 100, 300, 200);
        else
        {
            var w = Math.Min(wa.Width - 80, 1000);
            var h = Math.Min(wa.Height - 80, 700);
            Bounds = new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2, w, h);
        }
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(255, 20, 90, 160), Color.FromArgb(255, 235, 170, 60), 25f);
        e.Graphics.FillRectangle(brush, ClientRectangle);
        using var font = new Font("Segoe UI", 15);
        for (var y = 30; y < Height; y += 46)
            e.Graphics.DrawString("Shiftvert drag harness backdrop - the wheel's glass blurs this - " + y, font, Brushes.White, 20, y);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _dragged) return;
        _dragged = true;
        DragRequested?.Invoke();
    }
}

/// <summary>A minimal data object with one text-like format (no file formats at all).</summary>
[System.Runtime.InteropServices.ComVisible(true)]
internal sealed class TextDataObject : DataObject
{
    public TextDataObject(string text, string? format = null)
    {
        if (format is null) SetText(text);
        else SetData(format, text);
    }
}
