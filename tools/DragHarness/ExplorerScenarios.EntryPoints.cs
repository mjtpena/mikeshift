using System.Text.RegularExpressions;

namespace DragHarness;

/// <summary>
/// Entry points: the Ctrl activation mode and the global hotkey for the current Explorer selection, with the real File Explorer and real input.
/// The app runs with a scratch settings file (MIKESHIFT_SETTINGS_PATH) that chooses the arming key; it never touches the user's preferences.
/// </summary>
internal static partial class ExplorerScenarios
{
    private const int VkLMenu = 0xA4, VkSpace = 0x20, VkDown = 0x28, VkReturn = 0x0D;

    /// <summary>A scratch settings file for one case, as the environment of the app.</summary>
    private static Dictionary<string, string> X2Env(string armKey, string? hotkey = null)
    {
        var path = Path.Combine(Root, "settings-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        var json = "{\"launched\":\"true\",\"armKey\":\"" + armKey + "\"" + (hotkey is null ? "" : ",\"hotkey\":\"" + hotkey + "\"") + "}";
        File.WriteAllText(path, json);
        return new Dictionary<string, string> { ["MIKESHIFT_SETTINGS_PATH"] = path, ["MIKESHIFT_NO_SINGLE_INSTANCE"] = "1" };
    }

    /// <summary>Presses Ctrl+Alt+Shift+Space the way a person does: modifiers down, the key tapped, modifiers up.</summary>
    private static void PressDefaultHotkey()
    {
        Pointer.KeyDown(Native.VK_LCONTROL); Thread.Sleep(30);
        Pointer.KeyDown(VkLMenu); Thread.Sleep(30);
        Pointer.KeyDown(Native.VK_LSHIFT); Thread.Sleep(30);
        Pointer.KeyDown(VkSpace); Thread.Sleep(50); Pointer.KeyUp(VkSpace); Thread.Sleep(30);
        Pointer.KeyUp(Native.VK_LSHIFT); Pointer.KeyUp(VkLMenu); Pointer.KeyUp(Native.VK_LCONTROL);
        Thread.Sleep(200);
    }

    private static void RegisterEntryPoints(Dictionary<string, Action> d)
    {
        // ---- Ctrl arms the wheel (positioning P1-2)
        d["ctrl-mode"] = () => Case("ctrl-mode", "Ctrl mode: Ctrl-drag from Explorer opens the wheel, Shift -> Tools, a fresh Ctrl -> Formats, drop on PNG",
            dir => Jpgs(dir, "photo.jpg"), s =>
            {
                Rec.Check("Explorer window lists photo.jpg", s.Ex.HasItem("photo.jpg"));
                var seen = s.Start("photo.jpg", "", "ctrl");
                Rec.Check("Ctrl armed the wheel (real Explorer source)", seen);
                if (!seen) { s.Abort(); return; }
                Latency(s);
                s.WaitTiles(); Thread.Sleep(450);
                string LastPage() => Regex.Matches(s.LogNew, @"page = (Tool|Format)").LastOrDefault()?.Groups[1].Value ?? "Format(start)";
                var wait = () => Thread.Sleep(500);

                Pointer.KeyDown(Native.VK_LSHIFT); Thread.Sleep(40); Pointer.KeyUp(Native.VK_LSHIFT); wait();
                Rec.Check("Shift press -> Tools", LastPage() == "Tool", LastPage());
                Shot(s, "ctrl-mode-tools");
                Pointer.KeyUp(Native.VK_LCONTROL); Thread.Sleep(60); Pointer.KeyDown(Native.VK_LCONTROL); wait();
                Rec.Check("fresh Ctrl press -> Formats", LastPage() == "Format", LastPage());
                for (var i = 0; i < 20; i++) { Pointer.KeyDown(Native.VK_LCONTROL); Thread.Sleep(30); } // the held key repeating
                wait();
                Rec.Check("Ctrl auto-repeat changes nothing", LastPage() == "Format", LastPage());
                Rec.Check("wheel still shown", WheelInfo.Visible());
                var hit = DropOn(s, "PNG", false, "ctrl-mode-formats", "photo.png");
                Rec.Check("the drop on PNG worked in Ctrl mode", hit);
                var l = s.LogNew;
                Rec.Check("drop effect was COPY", Regex.IsMatch(l, @"Drop on PNG .*effect=COPY"), Regex.Match(l, @"Drop on PNG.*").Value);
                Rec.Check("photo.jpg still listed by Explorer (not moved)", s.Ex.HasItem("photo.jpg", 3000));
            }, env: X2Env("Ctrl"));

        d["ctrl-mode-shift"] = () => Case("ctrl-mode-shift", "Ctrl mode: a Shift-drag does NOT open the wheel (and an Esc-cancelled drag moves nothing)",
            dir => Jpgs(dir, "photo.jpg"), s =>
            {
                var seen = s.Start("photo.jpg", "", "shift", expectWheel: false);
                Rec.Check("no wheel with Shift when Ctrl is the arming key", !seen);
                s.Abort();
                Thread.Sleep(400);
                Rec.Check("photo.jpg untouched, nothing created", Directory.GetFileSystemEntries(s.Work).Select(Path.GetFileName).SequenceEqual(["photo.jpg"]),
                    string.Join(",", Directory.GetFileSystemEntries(s.Work).Select(Path.GetFileName)));
            }, env: X2Env("Ctrl"));

        d["shift-mode-ctrl"] = () => Case("shift-mode-ctrl", "Shift mode (default): Ctrl-drag does not open the wheel, Shift-drag still does",
            dir => Jpgs(dir, "photo.jpg"), s =>
            {
                var seen = s.Start("photo.jpg", "", "ctrl", expectWheel: false);
                Rec.Check("no wheel with Ctrl when Shift is the arming key", !seen);
                s.Abort();
                Thread.Sleep(500);
                var shift = s.Start("photo.jpg", "", "shift");
                Rec.Check("Shift opens it", shift);
                if (shift) { s.WaitTiles(); Thread.Sleep(300); }
                s.Abort();
            }, env: X2Env("Shift"));

        // ---- the hotkey for the current Explorer selection (positioning P1-1)
        d["hotkey"] = () => Case("hotkey", "Hotkey: select 2 images in Explorer, press Ctrl+Alt+Shift+Space -> click-mode wheel with those 2 files -> Down, Enter -> outputs beside the originals",
            dir => { Jpgs(dir, "a.jpg", "b.jpg"); FileKit.MakeJpg(Path.Combine(dir, "c.png"), text: "c"); }, s =>
            {
                Rec.Check("hotkey registered", s.App.WaitLog(t => t.Contains("registered"), 3000), s.App.LastLine("hotkey"));
                Rec.Check("Explorer lists the files", s.Ex.HasItem("a.jpg") && s.Ex.HasItem("b.jpg") && s.Ex.HasItem("c.png"));
                s.Ex.Select("a.jpg", "b.jpg");
                var selected = s.Ex.Selected().OrderBy(x => x).ToArray();
                Rec.Check("Explorer reports exactly a.jpg and b.jpg selected", selected.SequenceEqual(["a.jpg", "b.jpg"]), string.Join(",", selected));
                ExplorerNative.Front(s.Ex.Hwnd); Thread.Sleep(300);

                s.LogMark = s.App.LogLength();
                PressDefaultHotkey();
                var shown = s.App.WaitLog(t => t.Length > s.LogMark && t[s.LogMark..].Contains("click-mode wheel shown"), 8000);
                Rec.Check("the click-mode wheel opened", shown, s.LogNew);
                Rec.Check("the hotkey read 2 selected items", s.LogNew.Contains("hotkey: Selected, 2 item(s)"), Regex.Match(s.LogNew, "hotkey:.*").Value);
                if (!shown) return;
                Thread.Sleep(600);
                var tiles = WheelInfo.LastTiles(s.Log);
                Rec.Check("tiles are the image formats (PNG first)", tiles is { } t && t.Formats.Count > 0 && t.Formats[0] == "PNG", tiles is null ? "no tiles" : string.Join(",", tiles.Value.Formats));
                var c = WheelInfo.LastRect(s.Log, click: true);
                Rec.Check("wheel is on screen", WheelInfo.Visible() && c is not null);
                if (c is { } r) Shot(s, "hotkey-wheel", new Point(r.Rect.Left + r.Rect.Width / 2, r.Rect.Top + r.Rect.Height / 2), 330);

                Tap(VkDown, 500);
                Rec.Check("Down highlighted PNG", s.LogNew.Contains("highlight: PNG"), s.App.LastLine("highlight:"));
                Tap(VkReturn, 600);
                foreach (var o in new[] { "a.png", "b.png" })
                {
                    var got = s.WaitFile(Path.Combine(s.Work, o));
                    Rec.Check("output beside the original: " + o, got);
                }
                Rec.Check("job ran on exactly 2 items", Regex.IsMatch(s.LogNew, @"enqueued: PNG on 2 item\(s\)"), Regex.Match(s.LogNew, "job .*enqueued.*").Value);
                Rec.Check("the unselected c.png was not converted", !File.Exists(Path.Combine(s.Work, "c 2.png")) && Directory.GetFileSystemEntries(s.Work).Length == 5,
                    string.Join(",", Directory.GetFileSystemEntries(s.Work).Select(Path.GetFileName)));
            }, env: X2Env("Shift"));

        d["hotkey-empty"] = () => Case("hotkey-empty", "Hotkey with nothing selected: the toast says 'Select files in File Explorer first', no wheel",
            dir => Jpgs(dir, "a.jpg"), s =>
            {
                Rec.Check("hotkey registered", s.App.WaitLog(t => t.Contains("registered"), 3000), s.App.LastLine("hotkey"));
                ExplorerNative.Front(s.Ex.Hwnd); Thread.Sleep(300);
                Rec.Check("nothing is selected", s.Ex.Selected().Length == 0, string.Join(",", s.Ex.Selected()));
                s.LogMark = s.App.LogLength();
                PressDefaultHotkey();
                var texts = s.ToastTexts(3500);
                Rec.Check("toast: Select files in File Explorer first", texts.Any(x => x.Contains("Select files in File Explorer first")), string.Join(" | ", texts));
                Rec.Check("no wheel", !WheelInfo.Visible());
                Rec.Check("the log says there was no selection", s.LogNew.Contains("hotkey: NoSelection"), Regex.Match(s.LogNew, "hotkey:.*").Value);
            }, env: X2Env("Shift"));

        d["hotkey-none"] = () => Case("hotkey-none", "Hotkey set to None: pressing the old shortcut does nothing",
            dir => Jpgs(dir, "a.jpg"), s =>
            {
                s.Ex.Select("a.jpg");
                ExplorerNative.Front(s.Ex.Hwnd); Thread.Sleep(300);
                s.LogMark = s.App.LogLength();
                PressDefaultHotkey();
                Thread.Sleep(1500);
                Rec.Check("no wheel and no hotkey line in the log", !WheelInfo.Visible() && !s.LogNew.Contains("hotkey pressed"), s.LogNew);
            }, env: X2Env("Shift", hotkey: "None"));
    }
}
