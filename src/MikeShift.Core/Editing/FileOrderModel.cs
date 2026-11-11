namespace MikeShift.Core.Editing;

/// <summary>
/// The state and rules of the Merge order dialog, free of any UI: the selected files in the order they will be joined, moves by one
/// place or to any place (drag and drop), and a sort by name that reads numbers as numbers ("scan 2" before "scan 10"), as File Explorer does.
/// </summary>
public sealed class FileOrderModel
{
    private readonly List<string> _paths;
    private readonly string[] _original;

    public FileOrderModel(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = [.. paths];
        _original = [.. _paths];
    }

    /// <summary>Raised after any change of the order.</summary>
    public event Action? Changed;

    /// <summary>The files, first to join first.</summary>
    public IReadOnlyList<string> Paths => _paths;

    public int Count => _paths.Count;

    /// <summary>True when the order differs from the one the model was created with.</summary>
    public bool IsChanged => !_paths.SequenceEqual(_original, StringComparer.Ordinal);

    public bool CanMoveUp(int index) => index > 0 && index < _paths.Count;

    public bool CanMoveDown(int index) => index >= 0 && index < _paths.Count - 1;

    /// <summary>Moves the file at <paramref name="from"/> to place <paramref name="to"/> (clamped to the list). Returns where it ended up; an index outside the list changes nothing and is returned as it was.</summary>
    public int Move(int from, int to)
    {
        if (from < 0 || from >= _paths.Count) return from;
        to = Math.Clamp(to, 0, _paths.Count - 1);
        if (to == from) return from;

        var item = _paths[from];
        _paths.RemoveAt(from);
        _paths.Insert(to, item);
        Changed?.Invoke();
        return to;
    }

    /// <summary>
    /// Takes over <paramref name="ordered"/> as the order, for a list control that reorders itself when items are dragged and reports the
    /// result. It must hold exactly the files of the model (<see cref="IsPermutation"/>), otherwise nothing changes and it throws.
    /// </summary>
    public void SetOrder(IReadOnlyList<string> ordered)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        if (!IsPermutation(_paths, ordered))
            throw new ArgumentException("The new order must hold exactly the files that are being ordered.", nameof(ordered));
        if (ordered.SequenceEqual(_paths, StringComparer.Ordinal)) return;

        _paths.Clear();
        _paths.AddRange(ordered);
        Changed?.Invoke();
    }

    /// <summary>One place earlier. Returns the new index.</summary>
    public int MoveUp(int index) => CanMoveUp(index) ? Move(index, index - 1) : index;

    /// <summary>One place later. Returns the new index.</summary>
    public int MoveDown(int index) => CanMoveDown(index) ? Move(index, index + 1) : index;

    /// <summary>Orders the files by name (not folder), numbers read as numbers, ignoring case. Files with the same name keep their relative order.</summary>
    public void SortByName()
    {
        var sorted = _paths
            .OrderBy(p => Path.GetFileName(p), NameComparer)
            .ToList();
        if (sorted.SequenceEqual(_paths, StringComparer.Ordinal)) return;

        _paths.Clear();
        _paths.AddRange(sorted);
        Changed?.Invoke();
    }

    /// <summary>True when <paramref name="ordered"/> holds exactly the files of <paramref name="selection"/>, each as often, in any order (paths compare ignoring case, as on Windows).</summary>
    public static bool IsPermutation(IReadOnlyList<string> selection, IReadOnlyList<string> ordered)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(ordered);
        if (selection.Count != ordered.Count) return false;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in selection) counts[p] = counts.GetValueOrDefault(p) + 1;
        foreach (var p in ordered)
        {
            if (!counts.TryGetValue(p, out var n) || n == 0) return false;
            counts[p] = n - 1;
        }
        return true;
    }

    /// <summary>
    /// Natural comparison of two file names: runs of digits compare by value ("a2" before "a10", "a02" equals "a2"), everything else
    /// by the user's culture, ignoring case.
    /// </summary>
    public static int CompareNames(string? a, string? b)
    {
        a ??= string.Empty;
        b ??= string.Empty;

        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var ei = EndOfRun(a, i, digits: true);
                var ej = EndOfRun(b, j, digits: true);
                var na = a.AsSpan(i, ei - i).TrimStart('0');
                var nb = b.AsSpan(j, ej - j).TrimStart('0');
                if (na.Length != nb.Length) return na.Length < nb.Length ? -1 : 1;
                var byValue = na.CompareTo(nb, StringComparison.Ordinal);
                if (byValue != 0) return byValue;
                i = ei;
                j = ej;
                continue;
            }

            // Text up to the next digit on each side (empty on the side that is at a digit, so a digit sorts before a letter).
            var ti = EndOfRun(a, i, digits: false);
            var tj = EndOfRun(b, j, digits: false);
            var byText = a.AsSpan(i, ti - i).CompareTo(b.AsSpan(j, tj - j), StringComparison.CurrentCultureIgnoreCase);
            if (byText != 0) return byText;
            i = ti;
            j = tj;
        }

        var rest = (a.Length - i).CompareTo(b.Length - j);
        return rest != 0 ? rest : string.CompareOrdinal(a, b);
    }

    private static readonly Comparer<string?> NameComparer = Comparer<string?>.Create(CompareNames);

    private static int EndOfRun(string s, int start, bool digits)
    {
        var end = start;
        while (end < s.Length && char.IsAsciiDigit(s[end]) == digits) end++;
        return end;
    }
}
