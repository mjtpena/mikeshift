using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace DragHarness;

internal static partial class Anger
{
    // ------------------------------------------------------------------ shared steps

    private static void Jpgs(string dir, params string[] names)
    {
        foreach (var n in names) FileKit.MakeJpg(Path.Combine(dir, n), text: n);
    }

    private static void Latency(Sess s)
    {
        var cpu = CpuLoad.Now();
        Rec.Check("wheel visible within 100 ms of Shift", s.LatencyMs is >= 0 and < 100, $"{s.LatencyMs} ms, system CPU {cpu}%");
    }

    /// <summary>Finishes a drag onto a tile: highlight, screenshot, release, and wait for the output files.</summary>
    private static bool DropOn(Sess s, string tile, bool tools, string shot, params string[] outputs)
    {
        var tiled = s.WaitTiles();
        Rec.Check("tiles populated", tiled, tiled ? string.Join(",", s.Tiles(false)) + " | " + string.Join(",", s.Tiles(true)) : s.Log);
        if (!tiled) { s.Abort(); return false; }
        Thread.Sleep(450);
        var hit = s.ToTile(tile, tools);
        Rec.Check($"pointer on '{tile}' highlighted it", hit, hit ? "" : s.App.LastLine("highlight:"));
        if (shot.Length > 0) Shot(s, shot);
        s.Release();
        var ok = true;
        foreach (var o in outputs)
        {
            var path = Path.Combine(s.Work, o);
            var got = s.WaitFile(path);
            ok &= Rec.Check("output appeared: " + o, got, got && File.Exists(path) ? new FileInfo(path).Length + " bytes" : "");
            if (got) Rec.Check("Explorer lists the output: " + o, s.Ex.HasItem(o, 6000));
        }
        return ok && hit;
    }

    private static void ShiftDragJpg(string id, string desc, string pre, string during, string shot)
    {
        Case(id, desc, d => Jpgs(d, "photo.jpg"), s =>
        {
            Rec.Check("Explorer window on the folder lists photo.jpg", s.Ex.HasItem("photo.jpg"));
            var seen = s.Start("photo.jpg", pre, during);
            Rec.Check("wheel became visible (real Explorer source)", seen);
            if (!seen) { s.Abort(); return; }
            Latency(s);
            DropOn(s, "PNG", false, shot, "photo.png");
            var l = s.LogNew;
            Rec.Check("drop effect was COPY", Regex.IsMatch(l, @"Drop on PNG .*effect=COPY"), Regex.Match(l, @"Drop on PNG.*").Value);
            Rec.Check("photo.jpg still listed by Explorer (not moved)", s.Ex.HasItem("photo.jpg", 3000));
            Rec.Check("folder holds exactly photo.jpg + photo.png", Directory.GetFileSystemEntries(s.Work).Select(Path.GetFileName).OrderBy(x => x).SequenceEqual(["photo.jpg", "photo.png"]),
                string.Join(",", Directory.GetFileSystemEntries(s.Work).Select(Path.GetFileName)));
        });
    }

    private static void RegisterTests(Dictionary<string, Action> d)
    {
        d["x1a"] = () => ShiftDragJpg("x1a-shift", "Explorer: Shift-drag a JPG, drop on PNG", "", "shift", "x1a-shift-png");
        d["x1b"] = () => ShiftDragJpg("x1b-ctrl", "Explorer: Ctrl-drag (Ctrl before the click) then Shift", "ctrl", "shift", "x1b-ctrl-png");
        d["x1c"] = () => Case("x1c-ctrlshift", "Explorer: item selected first, then Ctrl+Shift held and dragged",
            dir => Jpgs(dir, "photo.jpg"), s =>
            {
                s.Ex.Select("photo.jpg");
                var seen = s.Start("photo.jpg", "ctrl+shift", "");
                Rec.Check("wheel visible", seen);
                if (!seen) { s.Abort(); return; }
                DropOn(s, "PNG", false, "x1c-ctrlshift-png", "photo.png");
                s.Release("");
            });

        d["x1d"] = () => Case("x1d-range", "Explorer: Shift held BEFORE the click (range select of 3), then drop on PNG",
            dir => Jpgs(dir, "a1.jpg", "a2.jpg", "a3.jpg"), s =>
            {
                s.Ex.Select("a1.jpg");
                var seen = s.Start("a3.jpg", pre: "shift", during: "");
                Rec.Check("wheel armed by the drag threshold with Shift already down", seen);
                if (!seen) { s.Abort(); return; }
                Latency(s);
                DropOn(s, "PNG", false, "x1d-range", "a1.png", "a2.png", "a3.png");
                Rec.Check("log: 3 items in the drag", Regex.IsMatch(s.LogNew, @"DragEnter .*paths=3"), Regex.Match(s.LogNew, @"DragEnter.*").Value);
            });

        d["x1e"] = () => Case("x1e-merge", "Explorer: multi-select 3 images, Tools page, Merge PDF",
            dir => Jpgs(dir, "m1.jpg", "m2.jpg", "m3.jpg"), s =>
            {
                s.Ex.Select("m1.jpg", "m2.jpg", "m3.jpg");
                Rec.Check("Explorer selection is 3 items", s.Ex.Selected().Length == 3, string.Join(",", s.Ex.Selected()));
                var seen = s.Start("m2.jpg");
                Rec.Check("wheel visible", seen);
                if (!seen) { s.Abort(); return; }
                s.WaitTiles();
                Pointer.KeyDown(Native.VK_LCONTROL); Thread.Sleep(40); Pointer.KeyUp(Native.VK_LCONTROL);
                var toggled = s.App.WaitLog(t => t[s.LogMark..].Contains("page = Tool"), 1500);
                Rec.Check("Ctrl switched to the Tools page", toggled);
                Thread.Sleep(450);
                Rec.Check("Merge PDF is offered for 3 images", s.Tiles(true).Any(t => t.Contains("Merge")), string.Join(",", s.Tiles(true)));
                var merge = s.Tiles(true).FirstOrDefault(t => t.Contains("Merge")) ?? "Merge PDF";
                DropOn(s, merge, true, "x1e-merge-tools", "Merged.pdf");
                var pdf = Path.Combine(s.Work, "Merged.pdf");
                if (File.Exists(pdf))
                {
                    var txt = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(pdf));
                    var pages = Regex.Matches(txt, @"/Type\s*/Page[^s]").Count;
                    Rec.Check("Merged.pdf has 3 pages", pages == 3, "pages=" + pages);
                }
            });

        d["x1f"] = () => Case("x1f-zip-mixed", "Explorer: image + PDF selected -> Zip is the only tile",
            dir =>
            {
                Jpgs(dir, "pic.jpg");
                File.Copy(Path.Combine(Root, "fx", "minimal.pdf"), Path.Combine(dir, "doc.pdf"));
            }, s =>
            {
                s.Ex.Select("pic.jpg", "doc.pdf");
                var seen = s.Start("pic.jpg");
                Rec.Check("wheel visible", seen);
                if (!seen) { s.Abort(); return; }
                s.WaitTiles();
                var f = s.Tiles(false); var t = s.Tiles(true);
                Rec.Check("only Zip is offered", (f.Length + t.Length) >= 1 && f.Concat(t).All(x => x == "Zip"), string.Join(",", f) + " | " + string.Join(",", t));
                DropOn(s, "Zip", f.Length == 0, "x1f-zip-only", "Archive.zip");
                var z = Path.Combine(s.Work, "Archive.zip");
                if (File.Exists(z))
                {
                    using var a = ZipFile.OpenRead(z);
                    Rec.Check("Archive.zip contains both files", a.Entries.Select(e => e.FullName).OrderBy(x => x).SequenceEqual(["doc.pdf", "pic.jpg"]), string.Join(",", a.Entries.Select(e => e.FullName)));
                }
            });

        d["x1g"] = () => Case("x1g-folder", "Explorer: a folder -> Zip",
            dir =>
            {
                var f = Path.Combine(dir, "myfolder"); Directory.CreateDirectory(f);
                File.WriteAllText(Path.Combine(f, "a.txt"), "hello"); File.WriteAllText(Path.Combine(f, "b.txt"), "world");
            }, s =>
            {
                var seen = s.Start("myfolder");
                Rec.Check("wheel visible", seen);
                if (!seen) { s.Abort(); return; }
                s.WaitTiles();
                var f = s.Tiles(false); var t = s.Tiles(true);
                Rec.Check("Zip only", f.Concat(t).All(x => x == "Zip") && f.Concat(t).Any(), string.Join(",", f) + " | " + string.Join(",", t));
                DropOn(s, "Zip", f.Length == 0, "x1g-folder-zip", "myfolder.zip");
                Rec.Check("folder still there and listed", s.Ex.HasItem("myfolder", 3000));
            });

        d["x1h"] = () => Case("x1h-500", "Explorer: Ctrl+A over 500 JPEGs, wheel stays responsive, conversion completes",
            dir =>
            {
                var src = Path.Combine(dir, "seed.jpg"); FileKit.MakeJpg(src, 320, 200, "seed");
                for (var i = 0; i < 500; i++) File.Copy(src, Path.Combine(dir, $"img{i:000}.jpg"));
                File.Delete(src);
            }, s =>
            {
                Rec.Check("Explorer lists img000.jpg", s.Ex.HasItem("img000.jpg", 15000));
                s.Ex.Select("img000.jpg");
                s.Ex.SelectAll();
                var cpu = CpuLoad.Now();
                var seen = s.Start("img000.jpg");
                Rec.Check("wheel visible", seen);
                if (!seen) { s.Abort(); return; }
                Latency(s);
                var sw = Stopwatch.StartNew();
                var tiled = s.WaitTiles(8000);
                Rec.Check("tiles populated quickly for 500 files", tiled && sw.ElapsedMilliseconds < 2000, $"{sw.ElapsedMilliseconds} ms after the wheel showed, CPU {cpu}%");
                Rec.Check("log: 500 items in the drag", Regex.IsMatch(s.LogNew, @"DragEnter .*paths=500"), Regex.Match(s.LogNew, @"DragEnter.*").Value);
                Thread.Sleep(450);
                var hit = s.ToTile("PNG");
                Rec.Check("highlight works with 500 files", hit);
                Shot(s, "x1h-500");
                s.Release();
                var t0 = Stopwatch.StartNew();
                var done = Sess.Wait(() => Directory.EnumerateFiles(s.Work, "*.png").Count() == 500, 180000);
                Rec.Check("500 PNGs written", done, $"{Directory.EnumerateFiles(s.Work, "*.png").Count()} PNGs, {t0.ElapsedMilliseconds} ms");
                Rec.Check("no temp folders left", !Directory.EnumerateFileSystemEntries(s.Work, ".*mikeshift-tmp").Any());
                Thread.Sleep(2000);
                Rec.Check("toast says 500 files saved", s.ToastTexts(1500).Any(x => x.Contains("500")), string.Join(" | ", s.ToastTexts(100)));
            });

        d["x1i"] = () => Case("x1i-mixed", "Explorer: PNG + JPGs selected, drop on PNG -> 'already PNG' skip text",
            dir =>
            {
                FileKit.MakeJpg(Path.Combine(dir, "one.png"), text: "one");
                Jpgs(dir, "two.jpg", "three.jpg");
            }, s =>
            {
                s.Ex.Select("one.png", "two.jpg", "three.jpg");
                var seen = s.Start("two.jpg");
                Rec.Check("wheel visible", seen);
                if (!seen) { s.Abort(); return; }
                Rec.Check("PNG tile is still offered for mixed image formats", s.WaitTiles() && s.Tiles(false).Contains("PNG"), string.Join(",", s.Tiles(false)));
                Thread.Sleep(450);
                s.ToTile("PNG");
                Shot(s, "x1i-mixed");
                s.Release();
                var texts = s.ToastTexts(4000);
                Rec.Check("toast mentions 'already PNG'", texts.Any(t => t.Contains("already PNG")), string.Join(" | ", texts));
                Rec.Check("two.png and three.png written", s.WaitFile(Path.Combine(s.Work, "two.png")) && s.WaitFile(Path.Combine(s.Work, "three.png")));
                Rec.Check("one.png was not re-converted (no 'one 2.png')", !File.Exists(Path.Combine(s.Work, "one 2.png")));
            });
    }
}
