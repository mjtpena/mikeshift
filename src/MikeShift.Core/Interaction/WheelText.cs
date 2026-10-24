using MikeShift.Core.Actions;

namespace MikeShift.Core.Interaction;

/// <summary>
/// Every string the wheel shows or says (hub, callout pill, UI Automation), in Windows sentence case. One place, so the hub, the pill, the
/// shell's drop description and the screen-reader names cannot drift apart (UX review W-10, W-15).
/// </summary>
public static class WheelText
{
    /// <summary>Hub text when nothing is highlighted on the Format page.</summary>
    public const string FormatTitle = "Convert to";

    /// <summary>Hub text when nothing is highlighted on the Tool page.</summary>
    public const string ToolTitle = "Tools";

    /// <summary>Shown (pill, hub tooltip, shell drop description) when the drag carries virtual files that cannot be converted in place.</summary>
    public const string SaveFirst = "Save the file first, then drag it";

    /// <summary>Shown (pill) instead of the "Only Zip works" explanation when legacy .doc files are selected, Word is installed and the user has not turned it on.</summary>
    public const string TurnOnWord = "Turn on Microsoft Word in Settings to convert .doc files";

    /// <summary>
    /// <see cref="TurnOnWord"/> when <paramref name="paths"/> contain a legacy .doc that only Word can read, Word is installed and the
    /// "Use Microsoft Word" setting is off; otherwise null. (Without Word installed, turning the setting on would change nothing.)
    /// </summary>
    public static string? WordNote(IReadOnlyList<string> paths, Capabilities caps) =>
        caps.WordInstalled && !caps.WordCom && paths.Any(p => Kind.ExtensionOf(p) == "doc") && Kind.CommonOf(paths) == Category.Document
            ? TurnOnWord
            : null;

    /// <summary>The idle hub title of <paramref name="page"/>.</summary>
    public static string PageTitle(WheelPage page) => page == WheelPage.Format ? FormatTitle : ToolTitle;

    /// <summary>"Only Zip works for these files": the pill for a wheel with a single tile (there is nothing else to choose).</summary>
    public static string OnlyOption(string title) => $"Only {title} works for these files";

    /// <summary>The screen-reader name of a tile: the same words as the shell's drop description ("Convert to PNG", "Compress").</summary>
    public static string AutomationName(ActionDef action) => action.DropDescription;

    /// <summary>A short sentence saying what a tile does, for UI Automation's <c>FullDescription</c> (so the names can stay short).</summary>
    public static string Describe(ActionDef action)
    {
        if (action.Page == WheelPage.Format) return $"Save a copy as {action.Title} next to the original";
        return action.Title switch
        {
            "Compress" => "Make the files smaller",
            _ when ActionCatalog.ResizePercentIn(action.Title) is { } percent => percent == 50
                ? "Save a copy at half the width and height"
                : $"Save a copy at {percent}% of the width and height",
            "Crop" => "Choose the part of the picture to keep",
            "Strip EXIF" => "Save a copy without camera and location data",
            "Trim" => "Choose the part to keep",
            "Split" => "Save each page as its own PDF",
            "Merge" => "Combine the files into one PDF",
            "Merge PDF" => "Combine the pictures into one PDF",
            "Zip" => "Put the files in a zip archive",
            _ => action.Title,
        };
    }

    /// <summary>The spoken name of the page: "Formats" or "Tools".</summary>
    public static string PageName(WheelPage page) => page == WheelPage.Format ? "Formats" : "Tools";

    /// <summary>Announced when the page changes: "Tools page, 5 items".</summary>
    public static string PageAnnouncement(WheelPage page, int count) =>
        $"{PageName(page)} page, {count} {(count == 1 ? "item" : "items")}";

    /// <summary>The list's name: "MikeShift wheel: convert to" / "MikeShift wheel: tools".</summary>
    public static string ListName(bool visible, WheelPage page) =>
        !visible ? "MikeShift wheel" : $"MikeShift wheel: {(page == WheelPage.Format ? "convert to" : "tools")}";

    /// <summary>UI Automation <c>HelpText</c> of the wheel (and, shortened, of a tile).</summary>
    public static string Help(bool clickMode, bool hasBothPages, WheelPage page)
    {
        if (!clickMode) return "Drop the files on a tile.";
        var other = page == WheelPage.Format ? "tools" : "formats";
        return hasBothPages
            ? $"Use the arrow keys to choose, Enter to convert, Tab for {other}, Escape to close."
            : "Use the arrow keys to choose, Enter to convert, Escape to close.";
    }

    /// <summary>HelpText of a single tile.</summary>
    public static string TileHelp(bool clickMode) => clickMode ? "Press Enter to choose" : "Drop the files here";

    /// <summary>The announcement when the wheel opens in click mode.</summary>
    public static string OpenAnnouncement(int count, bool hasBothPages) =>
        $"MikeShift wheel, {count} {(count == 1 ? "option" : "options")}. Arrow keys move, Enter chooses, " + (hasBothPages ? "Tab switches pages, " : "") + "Escape closes.";
}

/// <summary>A one-line description of what was selected, for the click-mode pill ("photo.jpg", "3 images").</summary>
public static class SelectionSummary
{
    /// <summary>
    /// "photo.jpg" for one item; "3 images", "2 videos", "4 PDFs", "2 documents", "5 audio files" when every item is of one
    /// <paramref name="common"/> category (<c>Kind.CommonOf</c>); "7 items" otherwise.
    /// </summary>
    public static string For(IReadOnlyList<string> paths, Category? common)
    {
        if (paths.Count == 0) return "";
        if (paths.Count == 1) return NameOf(paths[0]);
        var n = paths.Count;
        return common switch
        {
            Category.Image => $"{n} images",
            Category.Video => $"{n} videos",
            Category.Pdf => $"{n} PDFs",
            Category.Document => $"{n} documents",
            Category.Audio => $"{n} audio files",
            _ => $"{n} items",
        };
    }

    private static string NameOf(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : trimmed;
    }
}
