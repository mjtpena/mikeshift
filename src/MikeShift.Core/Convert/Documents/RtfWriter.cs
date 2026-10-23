using System.Globalization;
using System.Text;

namespace MikeShift.Core.Documents;

/// <summary>
/// RTF writer: 7-bit output (non-ASCII as <c>\uN?</c>), stylesheet with heading 1-6 (outline levels), a real list table
/// (bullets and decimal numbers, restarted per list), tables with borders, hyperlink fields, RTL paragraphs and page breaks.
/// </summary>
public static class RtfWriter
{
    private static readonly int[] HeadingHalfPoints = [32, 28, 26, 24, 22, 22];

    public static void Write(DocModel model, Stream output, bool a4)
    {
        // Pre-scan lists: every run of consecutive list items of the same kind is one list with its own \ls.
        var listIds = new List<bool>(); // index+1 = \ls value; value = ordered
        var itemLs = new Dictionary<DocParagraph, int>(ReferenceEqualityComparer.Instance);
        var prevOrdered = (bool?)null;
        foreach (var b in model.Blocks)
        {
            if (b is DocParagraph { Kind: ParagraphKind.ListItem } li)
            {
                if (prevOrdered != li.Ordered) { listIds.Add(li.Ordered); prevOrdered = li.Ordered; }
                itemLs[li] = listIds.Count;
            }
            else
            {
                prevOrdered = null;
            }
        }

        var sb = new StringBuilder(64 * 1024);
        sb.Append("{\\rtf1\\ansi\\ansicpg1252\\deff0\\uc1\\deflang1033\n");
        sb.Append("{\\fonttbl{\\f0\\fswiss\\fcharset0 Calibri;}{\\f1\\fnil\\fcharset2 Symbol;}}\n");
        sb.Append("{\\colortbl;\\red0\\green0\\blue0;\\red5\\green99\\blue193;}\n");
        sb.Append("{\\stylesheet{\\s0\\snext0 Normal;}");
        for (var level = 1; level <= 6; level++)
            sb.Append($"{{\\s{level}\\sbasedon0\\snext0\\keepn\\outlinelevel{level - 1}\\sb{(level == 1 ? 240 : 200)}\\sa80\\b\\fs{HeadingHalfPoints[level - 1]} heading {level};}}");
        sb.Append("}\n");

        if (listIds.Count > 0) WriteListTable(sb, listIds);

        sb.Append(a4 ? "\\paperw11906\\paperh16838" : "\\paperw12240\\paperh15840");
        sb.Append("\\margl1440\\margr1440\\margt1440\\margb1440\\viewkind4\\f0\\fs22\n");

        foreach (var block in model.Blocks)
        {
            switch (block)
            {
                case DocParagraph p:
                    WriteParagraph(sb, p, itemLs.TryGetValue(p, out var ls) ? ls : 0, inCell: false);
                    sb.Append("\\par\n");
                    break;
                case DocTable t:
                    WriteTable(sb, t);
                    break;
                case DocPageBreak:
                    sb.Append("\\page\n");
                    break;
            }
        }
        sb.Append('}');

        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        output.Write(bytes, 0, bytes.Length);
    }

    private static void WriteListTable(StringBuilder sb, List<bool> lists)
    {
        sb.Append("{\\*\\listtable");
        for (var i = 0; i < lists.Count; i++)
        {
            var ordered = lists[i];
            sb.Append($"{{\\list\\listtemplateid{1000 + i}\\listhybrid");
            for (var level = 0; level < 9; level++)
            {
                var nfc = ordered ? (level % 3) switch { 0 => 0, 1 => 4, _ => 2 } : 23;
                var left = 720 * (level + 1);
                sb.Append($"{{\\listlevel\\levelnfc{nfc}\\leveljc0\\levelfollow0\\levelstartat1\\levelindent0");
                if (ordered)
                    sb.Append($"{{\\leveltext\\'02\\'0{level};}}{{\\levelnumbers\\'01;}}");
                else
                    sb.Append("{\\leveltext\\'01\\u8226 ?;}{\\levelnumbers;}");
                sb.Append($"\\fi-360\\li{left}\\lin{left}}}");
            }
            sb.Append($"\\listid{100 + i}}}");
        }
        sb.Append('}');

        sb.Append("{\\*\\listoverridetable");
        for (var i = 0; i < lists.Count; i++)
            sb.Append($"{{\\listoverride\\listid{100 + i}\\listoverridecount0\\ls{i + 1}}}");
        sb.Append("}\n");
    }

    private static void WriteParagraph(StringBuilder sb, DocParagraph p, int ls, bool inCell)
    {
        sb.Append("\\pard");
        if (inCell) sb.Append("\\intbl");
        var rtl = p.EffectiveRtl;
        if (rtl) sb.Append("\\rtlpar\\qr");

        var heading = p.Kind == ParagraphKind.Heading;
        var level = Math.Clamp(p.Level, 1, 6);
        if (heading)
        {
            sb.Append($"\\s{level}\\outlinelevel{level - 1}\\keepn\\sb{(level == 1 ? 240 : 200)}\\sa80");
        }
        else if (p.Kind == ParagraphKind.ListItem && ls > 0)
        {
            var left = 720 * (Math.Clamp(p.Level, 0, 8) + 1);
            sb.Append($"\\ls{ls}\\ilvl{Math.Clamp(p.Level, 0, 8)}\\fi-360\\li{left}");
        }
        if (!heading) sb.Append("\\sa160\\sl259\\slmult1");
        sb.Append("\\plain\\f0\\fs").Append(heading ? HeadingHalfPoints[level - 1] : 22);
        if (heading) sb.Append("\\b");
        sb.Append(' ');

        foreach (var run in p.Runs)
            WriteRun(sb, run, heading, rtl);
    }

    private static void WriteRun(StringBuilder sb, DocRun run, bool heading, bool rtlParagraph)
    {
        var link = run.Link is not null && Uri.TryCreate(run.Link, UriKind.Absolute, out _) ? run.Link : null;
        if (link is not null)
            sb.Append("{\\field{\\*\\fldinst{HYPERLINK \"").Append(Escape(link.Replace("\"", "%22"))).Append("\"}}{\\fldrslt ");

        sb.Append('{');
        var before = sb.Length;
        if (run.Bold && !heading) sb.Append("\\b");
        if (run.Italic && !heading) sb.Append("\\i");
        if (run.Underline) sb.Append("\\ul");
        if (link is not null) sb.Append("\\cf2\\ul");
        if (rtlParagraph && run.Text.Any(TextUtil.IsRtlChar)) sb.Append("\\rtlch");
        if (sb.Length > before) sb.Append(' '); // delimiter after the last control word (only if there is one)
        AppendText(sb, run.Text);
        sb.Append('}');

        if (link is not null) sb.Append("}}");
    }

    private static void AppendText(StringBuilder sb, string text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '{': sb.Append("\\{"); break;
                case '}': sb.Append("\\}"); break;
                case '\n': sb.Append("\\line "); break;
                case '\t': sb.Append("\\tab "); break;
                case '\r': break;
                case < ' ': break;
                case < (char)0x7F: sb.Append(ch); break;
                default:
                    sb.Append("\\u").Append(((short)ch).ToString(CultureInfo.InvariantCulture)).Append('?');
                    break;
            }
        }
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder();
        AppendText(sb, s);
        return sb.ToString();
    }

    private static void WriteTable(StringBuilder sb, DocTable t)
    {
        var cols = Math.Max(1, t.ColumnCount);
        var width = 9360 / cols;
        foreach (var row in t.Rows)
        {
            sb.Append("\\trowd\\trgaph108\\trleft0");
            for (var c = 0; c < cols; c++)
            {
                sb.Append("\\clbrdrt\\brdrs\\brdrw10\\clbrdrl\\brdrs\\brdrw10\\clbrdrb\\brdrs\\brdrw10\\clbrdrr\\brdrs\\brdrw10");
                sb.Append("\\cellx").Append((c + 1) * width);
            }
            sb.Append('\n');
            for (var c = 0; c < cols; c++)
            {
                var paragraphs = c < row.Count && row[c].Count > 0 ? row[c] : [new DocParagraph()];
                for (var k = 0; k < paragraphs.Count; k++)
                {
                    WriteParagraph(sb, paragraphs[k], 0, inCell: true);
                    if (k < paragraphs.Count - 1) sb.Append("\\par\n");
                }
                sb.Append("\\cell\n");
            }
            sb.Append("\\row\n");
        }
    }
}
