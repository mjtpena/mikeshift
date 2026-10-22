using System.Text;

namespace Shiftvert.Core.Documents;

/// <summary>
/// Plain-text writer: UTF-8 <b>without</b> a byte-order mark (what macOS wrote and what every modern Windows editor reads),
/// CRLF line ends, one line per paragraph, list items prefixed with a bullet or number, table cells separated by tabs,
/// page breaks as a blank line.
/// </summary>
public static class TxtWriter
{
    public static void Write(DocModel model, Stream output)
    {
        using var w = new StreamWriter(output, new UTF8Encoding(false), 64 * 1024, leaveOpen: true) { NewLine = "\r\n" };
        var counter = new TextUtil.ListCounter();
        foreach (var block in model.Blocks)
        {
            switch (block)
            {
                case DocParagraph p when p.Kind == ParagraphKind.ListItem:
                    w.Write(new string(' ', 2 * Math.Clamp(p.Level, 0, 8)));
                    w.Write(counter.Next(p));
                    w.Write(' ');
                    w.WriteLine(Flatten(p));
                    continue;
                case DocParagraph p:
                    w.WriteLine(Flatten(p));
                    break;
                case DocTable t:
                    foreach (var row in t.Rows)
                        w.WriteLine(string.Join('\t', row.Select(cell => string.Join(" ", cell.Select(c => Flatten(c, inCell: true)).Where(x => x.Length > 0)))));
                    break;
                case DocPageBreak:
                    w.WriteLine();
                    break;
            }
            counter.Reset();
        }
        w.Flush();
    }

    private static string Flatten(DocParagraph p, bool inCell = false)
    {
        var text = p.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        return inCell ? text.Replace('\n', ' ') : text.Replace("\n", "\r\n");
    }
}
