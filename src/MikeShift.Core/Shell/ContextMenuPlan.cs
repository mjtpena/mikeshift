using MikeShift.Core.Actions;

namespace MikeShift.Core.Shell;

/// <summary>One line of the "Convert with MikeShift" submenu: the text Explorer shows and the action it runs.</summary>
/// <param name="Label">"Convert to JPG", "Compress", "Zip".</param>
/// <param name="ActionTitle">The <see cref="ActionDef.Title"/> to run ("JPG", "Compress", "Zip").</param>
public sealed record ContextMenuEntry(string Label, string ActionTitle);

/// <summary>
/// What the Explorer right-click entry shows for one selection: the action lines, then a separator and "More options…" (which opens the wheel).
/// </summary>
/// <param name="Visible">False when the command should not appear at all (nothing selected).</param>
/// <param name="Entries">The action lines in menu order; the last one is always Zip.</param>
/// <param name="Capped">True when the selection was too large to be looked at, so only Zip is offered next to "More options…".</param>
public sealed record ContextMenuPlan(bool Visible, IReadOnlyList<ContextMenuEntry> Entries, bool Capped)
{
    /// <summary>The plan for a selection the command does not appear for.</summary>
    public static ContextMenuPlan Hidden { get; } = new(false, [], false);
}

/// <summary>
/// Decides what the Explorer context menu offers (README "Right-click"): one top-level command with a one-level submenu holding the few most
/// useful actions for the selection, a separator and "More options…". Pure: it works from the paths and the cached capabilities only, and it
/// classifies by extension (<see cref="Kind.ByExtension"/>), never by reading files, because Windows asks for the menu on Explorer's UI thread.
/// The action that finally runs is resolved again, exactly, by the tray app (<see cref="ActionCatalog.For(IReadOnlyList{string}, Capabilities)"/>).
/// </summary>
public static class ContextMenuPlanner
{
    /// <summary>The one top-level command.</summary>
    public const string TopLevelTitle = "Convert with MikeShift";

    /// <summary>The last submenu line; opens the wheel for the selection.</summary>
    public const string MoreOptionsLabel = "More options…";

    /// <summary>How many actions besides Zip the submenu lists.</summary>
    public const int MaxRankedActions = 5;

    /// <summary>A selection with more items than this is not looked at: Zip and "More options…" only (the wheel classifies large selections by sampling).</summary>
    public const int LargeSelection = 500;

    /// <summary>Preferred order per category (titles from <c>Actions/Catalog</c>); visible actions not listed follow in catalog order.</summary>
    private static readonly Dictionary<Category, string[]> Ranking = new()
    {
        [Category.Image] = ["JPG", "PNG", "Merge PDF", "PDF", "Compress", "Resize 50%", "WebP", "HEIC", "TIFF", "GIF", "BMP", "Strip EXIF", "Crop"],
        [Category.Pdf] = ["Merge", "Compress", "PNG", "JPG", "Split", "TXT"],
        [Category.Video] = ["MP4", "MP3", "GIF", "Compress", "WMV", "M4A", "M4V", "Trim"],
        [Category.Audio] = ["MP3", "M4A", "WAV", "Compress", "FLAC", "AIFF", "Trim"],
        [Category.Document] = ["PDF", "DOCX", "TXT", "HTML", "RTF", "ODT"],
    };

    /// <summary>The submenu label of an action: "Convert to JPG" for a format, the title for a tool, a few tools worded for a menu.</summary>
    public static string LabelFor(ActionDef action) => action.Title switch
    {
        "Merge" => "Merge PDFs",
        "Split" => "Split pages",
        _ => action.DropDescription,
    };

    /// <summary>
    /// The plan for <paramref name="paths"/>. <paramref name="kindOf"/> classifies one path without reading it (default
    /// <see cref="Kind.ByExtension"/>); Explorer's folders must map to <see cref="Category.Other"/> (the caller knows which items are folders).
    /// </summary>
    public static ContextMenuPlan Build(IReadOnlyList<string> paths, Capabilities caps, Func<string, Category>? kindOf = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(caps);
        if (paths.Count == 0) return ContextMenuPlan.Hidden;

        var zip = new ContextMenuEntry(LabelFor(ZipAction.Def), ZipAction.Def.Title);
        if (paths.Count > LargeSelection) return new ContextMenuPlan(true, [zip], true);

        kindOf ??= Kind.ByExtension;
        var category = ActionCatalog.CommonCategory(paths, kindOf);
        var entries = new List<ContextMenuEntry>();
        if (category is { } c && Ranking.TryGetValue(c, out var order))
        {
            var visible = ActionCatalog.ListFor(c).Where(a => IsVisible(a, c, paths, caps)).ToList();
            var ranked = new List<ActionDef>();
            foreach (var title in order)
                if (visible.FirstOrDefault(a => a.Title == title) is { } hit) ranked.Add(hit);
            ranked.AddRange(visible.Where(a => !ranked.Contains(a)));
            entries.AddRange(ranked.Take(MaxRankedActions).Select(a => new ContextMenuEntry(LabelFor(a), a.Title)));
        }
        entries.Add(zip);
        return new ContextMenuPlan(true, entries, false);
    }

    /// <summary>
    /// <see cref="ActionDef.IsVisibleFor"/> without the Document probe: that one opens each DOCX/ODT to look for pictures, which Explorer's UI
    /// thread must not wait for. A legacy <c>.doc</c> without Word turned on still hides the document formats (a cheap check); the rest is
    /// settled when the action runs.
    /// </summary>
    private static bool IsVisible(ActionDef action, Category category, IReadOnlyList<string> paths, Capabilities caps)
    {
        if (category != Category.Document) return action.IsVisibleFor(paths, caps);
        if (action.Requires is not null && !action.Requires(caps)) return false;
        if (!caps.WordCom && paths.Any(p => Kind.ExtensionOf(p) == "doc")) return false;
        return action.HideFor.Count == 0 || !paths.All(p => action.HideFor.Contains(Kind.ExtensionOf(p)));
    }
}
