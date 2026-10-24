using System.Text.RegularExpressions;
using MikeShift.Core.Actions.Catalog;
using MikeShift.Core.Feedback;

namespace MikeShift.Core.Actions;

/// <summary>
/// Decides which tiles the wheel shows for a selection.
/// <list type="number">
/// <item>All items in the same category (not Other): that category's list from <c>Actions/Catalog</c>.</item>
/// <item>Mixed categories, unknown files or folders: only Zip.</item>
/// <item>Actions are dropped when their capability is missing, their selection predicate fails, or every selected
/// extension is in their <see cref="ActionDef.HideFor"/>.</item>
/// <item>Zip is always the last entry.</item>
/// </list>
/// The result is a flat ordered list; the wheel groups it by <see cref="ActionDef.Page"/> keeping this order. The user's
/// <see cref="ConversionOptions"/> only change a tile's name (the Resize percentage: "Resize 25%"), never which tiles there are.
/// </summary>
public static partial class ActionCatalog
{
    /// <summary>The actions for <paramref name="paths"/> on this machine (<see cref="Kind.Of"/> for classification), with the default conversion settings.</summary>
    public static IReadOnlyList<ActionDef> For(IReadOnlyList<string> paths, Capabilities caps) =>
        For(paths, caps, ConversionOptions.Default);

    /// <summary>The actions for <paramref name="paths"/> on this machine, named for <paramref name="options"/> ("Resize 25%").</summary>
    public static IReadOnlyList<ActionDef> For(IReadOnlyList<string> paths, Capabilities caps, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(caps);
        ArgumentNullException.ThrowIfNull(options);
        if (paths.Count == 0) return [];
        // Kind.CommonOf keeps big selections fast (sampled sniffing); small ones are classified exactly, file by file.
        return Build(Kind.CommonOf(paths), paths, caps, Kind.Of, options);
    }

    /// <summary>As <see cref="For(IReadOnlyList{string}, Capabilities)"/> with an injectable classifier (unit tests).</summary>
    public static IReadOnlyList<ActionDef> For(IReadOnlyList<string> paths, Capabilities caps, Func<string, Category> kindOf, ConversionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(caps);
        if (paths.Count == 0) return [];
        return Build(CommonCategory(paths, kindOf), paths, caps, kindOf, options ?? ConversionOptions.Default);
    }

    private static IReadOnlyList<ActionDef> Build(Category? category, IReadOnlyList<string> paths, Capabilities caps, Func<string, Category> kindOf, ConversionOptions options)
    {
        var result = new List<ActionDef>();
        if (category is { } c)
        {
            foreach (var action in ListFor(c, options))
                if (action.IsVisibleFor(paths, caps))
                    result.Add(action);
        }
        else if (CodecHelp.ForSelection(paths, caps, kindOf) is { } missing)
        {
            // HEIC/AVIF photos Windows cannot open yet: not "Zip only" but the one thing that fixes it.
            result.Add(CodecHelp.TileFor(missing));
        }
        result.Add(ZipAction.Def);
        return result;
    }

    /// <summary>The full (unfiltered) list for a category with the default settings; empty for <see cref="Category.Other"/>. Zip is not included.</summary>
    public static IReadOnlyList<ActionDef> ListFor(Category category) => ListFor(category, ConversionOptions.Default);

    /// <summary>The full (unfiltered) list for a category, named for <paramref name="options"/>; empty for <see cref="Category.Other"/>. Zip is not included.</summary>
    public static IReadOnlyList<ActionDef> ListFor(Category category, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return category switch
        {
            Category.Image => ImageActions.WithResize(options.ResizePercent),
            Category.Pdf => PdfActions.All,
            Category.Video => VideoActions.All,
            Category.Audio => AudioActions.All,
            Category.Document => DocumentActions.All,
            _ => [],
        };
    }

    /// <summary>
    /// Finds an action among <paramref name="actions"/> by title (case-insensitive, ignoring surrounding spaces), else by its stable
    /// <see cref="ActionDef.Id"/>: "Resize 25%" still finds the Resize tool when the settings now say 50%, and plain "Resize" finds it too.
    /// </summary>
    public static ActionDef? Find(IEnumerable<ActionDef> actions, string title)
    {
        var wanted = title.Trim();
        var list = actions as IReadOnlyList<ActionDef> ?? actions.ToList();
        if (list.FirstOrDefault(a => string.Equals(a.Title, wanted, StringComparison.OrdinalIgnoreCase)) is { } exact) return exact;
        var stem = PercentSuffix().Match(wanted) is { Success: true } m ? m.Groups[1].Value : wanted;
        return list.FirstOrDefault(a => !string.Equals(a.Id, a.Title, StringComparison.Ordinal)
                                        && string.Equals(a.Id, stem, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"Resize 25%" gives 25; null for any other title (or a percentage the Resize tool does not offer).</summary>
    public static int? ResizePercentIn(string title)
    {
        var m = ResizeTitle().Match(title.Trim());
        return m.Success && ConversionOptions.TryParseResizePercent(m.Groups[1].Value, out var percent) ? percent : null;
    }

    /// <summary>The single shared category of the selection, or null if mixed or <see cref="Category.Other"/>.</summary>
    public static Category? CommonCategory(IReadOnlyList<string> paths, Func<string, Category> kindOf)
    {
        Category? common = null;
        foreach (var p in paths)
        {
            var k = kindOf(p);
            if (k == Category.Other) return null;
            if (common is null) common = k;
            else if (common != k) return null;
        }
        return common;
    }

    [GeneratedRegex(@"^(.*\S)\s+\d{1,3}%$")]
    private static partial Regex PercentSuffix();

    [GeneratedRegex(@"^Resize\s+(\d{1,3})%$", RegexOptions.IgnoreCase)]
    private static partial Regex ResizeTitle();
}
