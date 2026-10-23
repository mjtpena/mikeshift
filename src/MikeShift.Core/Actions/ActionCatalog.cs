using MikeShift.Core.Actions.Catalog;

namespace MikeShift.Core.Actions;

/// <summary>
/// Decides which tiles the wheel shows for a selection (PLAN §1.1 multi-select rules, §1.2 order).
/// <list type="number">
/// <item>All items in the same category (not Other): that category's list from <c>Actions/Catalog</c>.</item>
/// <item>Mixed categories, unknown files or folders: only Zip.</item>
/// <item>Actions are dropped when their capability is missing, their selection predicate fails, or every selected
/// extension is in their <see cref="ActionDef.HideFor"/>.</item>
/// <item>Zip is always the last entry.</item>
/// </list>
/// The result is a flat ordered list; the wheel groups it by <see cref="ActionDef.Page"/> keeping this order.
/// </summary>
public static class ActionCatalog
{
    /// <summary>The actions for <paramref name="paths"/> on this machine (<see cref="Kind.Of"/> for classification).</summary>
    public static IReadOnlyList<ActionDef> For(IReadOnlyList<string> paths, Capabilities caps)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(caps);
        if (paths.Count == 0) return [];
        // Kind.CommonOf keeps big selections fast (sampled sniffing); small ones are classified exactly, file by file.
        return Build(Kind.CommonOf(paths), paths, caps);
    }

    /// <summary>As <see cref="For(IReadOnlyList{string}, Capabilities)"/> with an injectable classifier (unit tests).</summary>
    public static IReadOnlyList<ActionDef> For(IReadOnlyList<string> paths, Capabilities caps, Func<string, Category> kindOf)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(caps);
        if (paths.Count == 0) return [];
        return Build(CommonCategory(paths, kindOf), paths, caps);
    }

    private static IReadOnlyList<ActionDef> Build(Category? category, IReadOnlyList<string> paths, Capabilities caps)
    {
        var result = new List<ActionDef>();
        if (category is { } c)
        {
            foreach (var action in ListFor(c))
                if (action.IsVisibleFor(paths, caps))
                    result.Add(action);
        }
        result.Add(ZipAction.Def);
        return result;
    }

    /// <summary>The full (unfiltered) list for a category; empty for <see cref="Category.Other"/>. Zip is not included.</summary>
    public static IReadOnlyList<ActionDef> ListFor(Category category) => category switch
    {
        Category.Image => ImageActions.All,
        Category.Pdf => PdfActions.All,
        Category.Video => VideoActions.All,
        Category.Audio => AudioActions.All,
        Category.Document => DocumentActions.All,
        _ => [],
    };

    /// <summary>Finds an action by title (case-insensitive, ignoring surrounding spaces) among <paramref name="actions"/>.</summary>
    public static ActionDef? Find(IEnumerable<ActionDef> actions, string title)
    {
        var wanted = title.Trim();
        return actions.FirstOrDefault(a => string.Equals(a.Title, wanted, StringComparison.OrdinalIgnoreCase));
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
}
