using System.Text;

namespace Shiftvert.Core.Documents;

/// <summary>What kind of paragraph a <see cref="DocParagraph"/> is.</summary>
public enum ParagraphKind
{
    /// <summary>Ordinary body text.</summary>
    Normal,
    /// <summary>A heading; <see cref="DocParagraph.Level"/> is 1 to 6.</summary>
    Heading,
    /// <summary>A list item; <see cref="DocParagraph.Level"/> is the 0-based nesting level, <see cref="DocParagraph.Ordered"/> says numbered vs bulleted.</summary>
    ListItem,
}

/// <summary>A run of text with uniform character formatting. <c>\n</c> in <see cref="Text"/> is a line break, <c>\t</c> a tab.</summary>
public sealed record DocRun(string Text, bool Bold = false, bool Italic = false, bool Underline = false, string? Link = null)
{
    /// <summary>True if <paramref name="other"/> has exactly the same formatting (so the two runs could be merged).</summary>
    public bool SameFormat(DocRun other) =>
        Bold == other.Bold && Italic == other.Italic && Underline == other.Underline && Link == other.Link;
}

/// <summary>A block of the pivot document.</summary>
public abstract class DocBlock;

/// <summary>A paragraph, heading or list item.</summary>
public sealed class DocParagraph : DocBlock
{
    public ParagraphKind Kind { get; set; } = ParagraphKind.Normal;

    /// <summary>Heading level 1 to 6, or the 0-based list nesting level.</summary>
    public int Level { get; set; }

    /// <summary>For list items: numbered (true) or bulleted (false).</summary>
    public bool Ordered { get; set; }

    /// <summary>The source marked this paragraph right-to-left. Writers also detect RTL from the text (<see cref="EffectiveRtl"/>).</summary>
    public bool Rtl { get; set; }

    public List<DocRun> Runs { get; } = [];

    /// <summary>The plain text of the paragraph.</summary>
    public string Text
    {
        get
        {
            if (Runs.Count == 1) return Runs[0].Text;
            var sb = new StringBuilder();
            foreach (var r in Runs) sb.Append(r.Text);
            return sb.ToString();
        }
    }

    public bool IsEmpty => Runs.Count == 0 || Runs.All(r => r.Text.Length == 0);

    /// <summary>Explicitly right-to-left, or (when not marked) starting with a strong right-to-left letter.</summary>
    public bool EffectiveRtl => Rtl || TextUtil.FirstStrongIsRtl(Text);

    /// <summary>Appends text, merging it into the previous run when the formatting matches.</summary>
    public void Add(string text, bool bold = false, bool italic = false, bool underline = false, string? link = null) =>
        Add(new DocRun(text, bold, italic, underline, link));

    public void Add(DocRun run)
    {
        if (run.Text.Length == 0) return;
        if (Runs.Count > 0 && Runs[^1].SameFormat(run))
            Runs[^1] = Runs[^1] with { Text = Runs[^1].Text + run.Text };
        else
            Runs.Add(run);
    }

    public static DocParagraph Plain(string text)
    {
        var p = new DocParagraph();
        p.Add(text);
        return p;
    }

    public static DocParagraph Heading(int level, string text)
    {
        var p = new DocParagraph { Kind = ParagraphKind.Heading, Level = Math.Clamp(level, 1, 6) };
        p.Add(text);
        return p;
    }

    /// <summary>
    /// A copy suitable for a table cell: headings become bold normal paragraphs, list items get a bullet/number-free
    /// "• " prefix (cells hold normal paragraphs only).
    /// </summary>
    public DocParagraph ForCell()
    {
        if (Kind == ParagraphKind.Normal) return this;
        var p = new DocParagraph { Rtl = Rtl };
        if (Kind == ParagraphKind.ListItem) p.Add("• ");
        foreach (var r in Runs)
            p.Add(Kind == ParagraphKind.Heading ? r with { Bold = true } : r);
        return p;
    }
}

/// <summary>A simple table: rows of cells, each cell a list of paragraphs (normal paragraphs only).</summary>
public sealed class DocTable : DocBlock
{
    public List<List<List<DocParagraph>>> Rows { get; } = [];

    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Count);
}

/// <summary>A hard page break.</summary>
public sealed class DocPageBreak : DocBlock;

/// <summary>
/// The pivot model every reader produces and every writer consumes (PLAN §3.4 Documents).
/// <see cref="HasPictures"/> records that the source embedded pictures, which the pivot cannot carry (only TXT is offered then).
/// </summary>
public sealed class DocModel
{
    public List<DocBlock> Blocks { get; } = [];

    public bool HasPictures { get; set; }

    /// <summary>Every paragraph, including those inside table cells, in document order.</summary>
    public IEnumerable<DocParagraph> AllParagraphs()
    {
        foreach (var b in Blocks)
        {
            switch (b)
            {
                case DocParagraph p:
                    yield return p;
                    break;
                case DocTable t:
                    foreach (var row in t.Rows)
                        foreach (var cell in row)
                            foreach (var cp in cell)
                                yield return cp;
                    break;
            }
        }
    }

    /// <summary>Total number of characters of text in the model.</summary>
    public long CharCount() => AllParagraphs().Sum(p => (long)p.Runs.Sum(r => r.Text.Length));

    /// <summary>True if any paragraph is right-to-left.</summary>
    public bool HasRtl() => AllParagraphs().Any(p => p.EffectiveRtl);
}

/// <summary>Small text helpers shared by readers and writers.</summary>
internal static class TextUtil
{
    /// <summary>True if the first strong directional character is Hebrew/Arabic/Syriac/Thaana/NKo etc.</summary>
    public static bool FirstStrongIsRtl(string text)
    {
        foreach (var ch in text)
        {
            if (IsRtlChar(ch)) return true;
            if (char.IsLetter(ch)) return false;
        }
        return false;
    }

    public static bool IsRtlChar(char c) =>
        c is >= '֐' and <= 'ࣿ' || c is >= 'יִ' and <= '﷿' || c is >= 'ﹰ' and <= '﻿';

    /// <summary>Removes characters that XML 1.0 cannot represent (control characters, lone surrogates, U+FFFE/FFFF).</summary>
    public static string XmlSafe(string s)
    {
        var needs = false;
        for (var i = 0; i < s.Length; i++)
        {
            if (!IsXmlChar(s, i)) { needs = true; break; }
            if (char.IsHighSurrogate(s[i])) i++;
        }
        if (!needs) return s;

        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (!IsXmlChar(s, i)) continue;
            sb.Append(s[i]);
            if (char.IsHighSurrogate(s[i])) sb.Append(s[++i]);
        }
        return sb.ToString();
    }

    private static bool IsXmlChar(string s, int i)
    {
        var c = s[i];
        if (char.IsHighSurrogate(c)) return i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
        if (char.IsLowSurrogate(c)) return false;
        return c is '\t' or '\n' or '\r' || (c >= 0x20 && c != '￾' && c != '￿');
    }

    /// <summary>
    /// Splits text on <c>\n</c>/<c>\t</c> so writers can emit their format's break and tab elements.
    /// Yields (kind, text): kind 0 = text, 1 = line break, 2 = tab. Carriage returns are dropped.
    /// </summary>
    public static IEnumerable<(int Kind, string Text)> Pieces(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is not ('\n' or '\t' or '\r')) continue;
            if (i > start) yield return (0, text[start..i]);
            start = i + 1;
            if (c == '\n') yield return (1, "");
            else if (c == '\t') yield return (2, "");
        }
        if (start < text.Length) yield return (0, text[start..]);
    }

    /// <summary>Labels for list items ("•", "1.", "2.") computed per level for consecutive items.</summary>
    public sealed class ListCounter
    {
        private readonly List<int> _counts = [];
        private readonly List<bool> _ordered = [];

        /// <summary>Call whenever a non-list block intervenes.</summary>
        public void Reset()
        {
            _counts.Clear();
            _ordered.Clear();
        }

        /// <summary>The label for the next list item.</summary>
        public string Next(DocParagraph item)
        {
            var level = Math.Clamp(item.Level, 0, 8);
            while (_counts.Count > level + 1) { _counts.RemoveAt(_counts.Count - 1); _ordered.RemoveAt(_ordered.Count - 1); }
            while (_counts.Count < level + 1) { _counts.Add(0); _ordered.Add(item.Ordered); }
            if (_ordered[level] != item.Ordered) { _ordered[level] = item.Ordered; _counts[level] = 0; }
            _counts[level]++;
            return item.Ordered ? _counts[level] + "." : "•";
        }
    }
}
