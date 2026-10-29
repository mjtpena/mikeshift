using System.Text.RegularExpressions;

namespace DragHarness;

internal static partial class ExplorerScenarios
{
    /// <summary>The command line, kept so the generic <c>action</c> scenario can read its options.</summary>
    private static string[] _args = [];

    private static string Opt(string name, string fallback = "")
    {
        var i = Array.IndexOf(_args, name);
        return i >= 0 && i + 1 < _args.Length ? _args[i + 1] : fallback;
    }

    /// <summary>
    /// <c>--explorer action</c>: one drag of your own files onto any tile, by name. It is not part of <c>--explorer all</c>, because it needs these options:
    /// <code>
    /// --action-file a.png[,b.png]   files to copy into a fresh folder, select in Explorer and drag
    /// --action-tile "Blur faces"    the tile to drop on (found on the Formats or the Tools page)
    /// --action-out "a faces.png"    output names expected beside the originals (* matches any run of characters)
    /// --action-toast "Blurred 1"    text the toast must contain (without it, the toast is only printed)
    /// --action-absent               instead, require that the tile is on neither page and drop nothing
    /// --action-timeout 120          seconds to wait for each output (default 60)
    /// </code>
    /// </summary>
    private static void RegisterActions(Dictionary<string, Action> d)
    {
        d["action"] = () =>
        {
            var sources = Opt("--action-file").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var tile = Opt("--action-tile");
            var expected = Opt("--action-out").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var toast = Opt("--action-toast");
            var absent = _args.Contains("--action-absent");
            var timeoutMs = int.TryParse(Opt("--action-timeout", "60"), out var secs) ? secs * 1000 : 60000;
            if (sources.Length == 0 || tile.Length == 0)
            {
                Rec.Check("--action-file and --action-tile were given", false);
                return;
            }
            var names = sources.Select(p => Path.GetFileName(p)).ToArray();

            Case("action-" + Path.GetFileNameWithoutExtension(names[0]), $"Explorer: drag {string.Join(", ", names)} onto '{tile}'" + (absent ? " (must not be offered)" : ""),
                dir => { foreach (var f in sources) File.Copy(f, Path.Combine(dir, Path.GetFileName(f))); }, s =>
                {
                    // Explorer never shows the extension of an Internet shortcut: its item is called "link", not "link.url".
                    var shown = names.Select(n => n.EndsWith(".url", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(n) : n).ToArray();
                    Rec.Check("Explorer lists " + shown[0], s.Ex.HasItem(shown[0]));
                    if (names.Length > 1) s.Ex.Select(shown);
                    var seen = s.Start(shown[0]);
                    Rec.Check("wheel became visible (real Explorer source)", seen);
                    if (!seen) { s.Abort(); return; }
                    var tiled = s.WaitTiles();
                    Rec.Check("tiles populated", tiled);
                    if (!tiled) { s.Abort(); return; }
                    Thread.Sleep(450);
                    var formats = s.Tiles(false);
                    var tools = s.Tiles(true);
                    Rec.Note("Formats: " + string.Join(", ", formats) + " | Tools: " + string.Join(", ", tools));

                    if (absent)
                    {
                        Rec.Check($"'{tile}' is on neither page", !formats.Concat(tools).Contains(tile, StringComparer.OrdinalIgnoreCase));
                        Shot(s, "action-absent-" + tile.Replace(' ', '-'));
                        s.Abort();
                        return;
                    }

                    var onTools = tools.Contains(tile, StringComparer.OrdinalIgnoreCase);
                    var onFormats = formats.Contains(tile, StringComparer.OrdinalIgnoreCase);
                    Rec.Check($"'{tile}' is offered", onTools || onFormats, onTools ? "Tools page" : onFormats ? "Formats page" : "missing");
                    if (!onTools && !onFormats) { s.Abort(); return; }
                    if (onTools && !onFormats && formats.Length > 0)
                    {
                        Pointer.KeyDown(Native.VK_LCONTROL); Thread.Sleep(40); Pointer.KeyUp(Native.VK_LCONTROL);
                        Rec.Check("Ctrl switched to the Tools page", s.App.WaitLog(t => t[s.LogMark..].Contains("page = Tool"), 1500));
                        Thread.Sleep(450);
                    }
                    // The offered checks ignore case, ToTile does not: aim at the title as the wheel spells it.
                    var title = (onFormats ? formats : tools).First(t => string.Equals(t, tile, StringComparison.OrdinalIgnoreCase));
                    var hit = s.ToTile(title, onTools && !onFormats);
                    Rec.Check($"pointer on '{title}' highlighted it", hit, hit ? "" : s.App.LastLine("highlight:"));
                    Shot(s, "action-" + title.Replace(' ', '-'));
                    s.Release();

                    foreach (var pattern in expected)
                    {
                        string? found = null;
                        var got = Sess.Wait(() =>
                        {
                            // Naming's hidden .name.guid.mikeshift-tmp container is not an output, even when the pattern matches it.
                            found = Directory.EnumerateFileSystemEntries(s.Work, pattern).FirstOrDefault(p =>
                                !names.Contains(Path.GetFileName(p)) && !p.EndsWith(".mikeshift-tmp", StringComparison.OrdinalIgnoreCase));
                            return found is not null;
                        }, timeoutMs);
                        Rec.Check("output appeared: " + pattern, got, got ? Path.GetFileName(found) + (File.Exists(found) ? $", {new FileInfo(found!).Length} bytes" : "") : "");
                        if (got) Rec.Check("Explorer lists the output: " + Path.GetFileName(found), s.Ex.HasItem(Path.GetFileName(found!), 6000));
                    }
                    var toasts = string.Join(" | ", s.ToastTexts(2500));
                    if (toast.Length > 0) Rec.Check($"toast contains '{toast}'", toasts.Contains(toast, StringComparison.OrdinalIgnoreCase), toasts);
                    else Rec.Note("toast: " + toasts);
                    var drop = Regex.Match(s.LogNew, @"Drop on.*").Value;
                    Rec.Check("drop effect was COPY", Regex.IsMatch(drop, @"effect=COPY"), drop);
                    Rec.Note("folder now: " + string.Join(", ", Directory.GetFileSystemEntries(s.Work).Select(p => Path.GetFileName(p))));
                });
        };
    }
}
