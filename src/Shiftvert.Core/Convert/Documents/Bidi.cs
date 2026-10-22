namespace Shiftvert.Core.Documents;

/// <summary>
/// A deliberately small bidirectional reordering for PDF output (PDFsharp draws glyphs left to right and does no shaping):
/// right-to-left letters and the neutrals between them are reversed, left-to-right runs (Latin text, digits) keep their order, and
/// the runs are laid out right to left. Arabic contextual shaping is not performed.
/// </summary>
internal static class Bidi
{
    private enum T : byte { L, R, N }

    /// <summary>For a line of text in a right-to-left paragraph: the logical index of the character at each visual position (left to right).</summary>
    public static (int Index, bool Reversed)[] VisualOrder(string text)
    {
        var n = text.Length;
        if (n == 0) return [];

        var types = new T[n];
        for (var i = 0; i < n; i++)
        {
            var c = text[i];
            types[i] = TextUtil.IsRtlChar(c) ? T.R : char.IsLetterOrDigit(c) ? T.L : T.N;
        }

        // Neutrals take the direction of their neighbours if both agree, otherwise the paragraph direction (R).
        for (var i = 0; i < n;)
        {
            if (types[i] != T.N) { i++; continue; }
            var j = i;
            while (j < n && types[j] == T.N) j++;
            var left = i > 0 ? types[i - 1] : T.R;
            var right = j < n ? types[j] : T.R;
            var resolved = left == right ? left : T.R;
            for (var k = i; k < j; k++) types[k] = resolved;
            i = j;
        }

        // Split into runs, then emit runs from last to first; R runs reversed.
        var runs = new List<(int Start, int End, T Type)>();
        for (var i = 0; i < n;)
        {
            var j = i;
            while (j < n && types[j] == types[i]) j++;
            runs.Add((i, j, types[i]));
            i = j;
        }

        var order = new List<(int, bool)>(n);
        for (var r = runs.Count - 1; r >= 0; r--)
        {
            var (s, e, t) = runs[r];
            if (t == T.R)
                for (var k = e - 1; k >= s; k--) order.Add((k, true));
            else
                for (var k = s; k < e; k++) order.Add((k, false));
        }
        return [.. order];
    }

    /// <summary>The glyph to draw for <paramref name="c"/> when it sits in a reversed (right-to-left) run: brackets are mirrored.</summary>
    public static char Mirror(char c) => c switch
    {
        '(' => ')',
        ')' => '(',
        '[' => ']',
        ']' => '[',
        '{' => '}',
        '}' => '{',
        '<' => '>',
        '>' => '<',
        '«' => '»',
        '»' => '«',
        _ => c,
    };
}
