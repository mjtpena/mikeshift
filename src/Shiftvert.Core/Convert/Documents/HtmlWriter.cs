using System.Net;
using System.Text;

namespace Shiftvert.Core.Documents;

/// <summary>HTML writer: one UTF-8 file, no scripts and no external references; <c>dir="rtl"</c> on right-to-left blocks.</summary>
public static class HtmlWriter
{
    public static void Write(DocModel model, Stream output, string title)
    {
        using var w = new StreamWriter(output, new UTF8Encoding(false), 64 * 1024, leaveOpen: true) { NewLine = "\n" };
        w.WriteLine("<!DOCTYPE html>");
        w.WriteLine("<html>");
        w.WriteLine("<head>");
        w.WriteLine("<meta charset=\"utf-8\">");
        w.WriteLine("<title>" + Enc(title) + "</title>");
        w.WriteLine("<style>body{font-family:Calibri,Segoe UI,Arial,sans-serif;max-width:46em;margin:2em auto;line-height:1.4}table{border-collapse:collapse}td,th{border:1px solid #000;padding:4px 8px;vertical-align:top}</style>");
        w.WriteLine("</head>");
        w.WriteLine("<body>");

        var openLists = new Stack<string>(); // "ul" / "ol" per open list; each has an open <li> except right after opening
        foreach (var block in model.Blocks)
        {
            if (block is DocParagraph { Kind: ParagraphKind.ListItem } item)
            {
                WriteListItem(w, item, openLists);
                continue;
            }
            CloseLists(w, openLists, 0);

            switch (block)
            {
                case DocParagraph { Kind: ParagraphKind.Heading } h:
                {
                    var level = Math.Clamp(h.Level, 1, 6);
                    w.Write($"<h{level}{Dir(h)}>");
                    WriteRuns(w, h);
                    w.WriteLine($"</h{level}>");
                    break;
                }
                case DocParagraph p:
                    w.Write($"<p{Dir(p)}>");
                    WriteRuns(w, p);
                    w.WriteLine("</p>");
                    break;
                case DocTable t:
                    WriteTable(w, t);
                    break;
                case DocPageBreak:
                    w.WriteLine("<div style=\"page-break-before:always\"></div>");
                    break;
            }
        }
        CloseLists(w, openLists, 0);

        w.WriteLine("</body>");
        w.WriteLine("</html>");
        w.Flush();
    }

    private static string Dir(DocParagraph p) => p.EffectiveRtl ? " dir=\"rtl\"" : "";

    private static string Enc(string s) => WebUtility.HtmlEncode(TextUtil.XmlSafe(s));

    private static void WriteListItem(StreamWriter w, DocParagraph item, Stack<string> open)
    {
        var tag = item.Ordered ? "ol" : "ul";
        var depth = Math.Min(Math.Max(item.Level, 0), open.Count) + 1; // number of open lists after this item

        if (open.Count >= depth)
        {
            while (open.Count > depth) CloseInnermost(w, open);
            if (depth == 1 && open.Peek() != tag)
            {
                CloseInnermost(w, open);
                w.WriteLine($"<{tag}>");
                open.Push(tag);
            }
            else
            {
                w.WriteLine("</li>");
            }
        }
        else
        {
            w.WriteLine($"<{tag}>"); // nested inside the parent's open <li>
            open.Push(tag);
        }
        w.Write($"<li{Dir(item)}>");
        WriteRuns(w, item);
    }

    // Every open list has an open <li> (items are opened as soon as their list is).
    private static void CloseInnermost(StreamWriter w, Stack<string> open)
    {
        w.WriteLine("</li>");
        w.WriteLine($"</{open.Pop()}>");
    }

    private static void CloseLists(StreamWriter w, Stack<string> open, int keep)
    {
        while (open.Count > keep) CloseInnermost(w, open);
    }

    private static void WriteTable(StreamWriter w, DocTable t)
    {
        var cols = Math.Max(1, t.ColumnCount);
        w.WriteLine("<table>");
        foreach (var row in t.Rows)
        {
            w.WriteLine("<tr>");
            for (var c = 0; c < cols; c++)
            {
                var paragraphs = c < row.Count && row[c].Count > 0 ? row[c] : [new DocParagraph()];
                w.Write("<td>");
                for (var k = 0; k < paragraphs.Count; k++)
                {
                    w.Write($"<p{Dir(paragraphs[k])}>");
                    WriteRuns(w, paragraphs[k]);
                    w.Write("</p>");
                }
                w.WriteLine("</td>");
            }
            w.WriteLine("</tr>");
        }
        w.WriteLine("</table>");
    }

    private static void WriteRuns(StreamWriter w, DocParagraph p)
    {
        var heading = p.Kind == ParagraphKind.Heading;
        var lastWasSpace = true; // a leading space or a second space in a row is written as &nbsp;
        foreach (var run in p.Runs)
        {
            var link = run.Link is not null && Uri.TryCreate(run.Link, UriKind.Absolute, out _) ? run.Link : null;
            if (link is not null) w.Write($"<a href=\"{Enc(link)}\">");
            if (run.Bold && !heading) w.Write("<strong>");
            if (run.Italic && !heading) w.Write("<em>");
            if (run.Underline) w.Write("<u>");

            foreach (var (kind, text) in TextUtil.Pieces(TextUtil.XmlSafe(run.Text)))
            {
                switch (kind)
                {
                    case 1:
                        w.Write("<br>");
                        lastWasSpace = true;
                        break;
                    case 2:
                        w.Write("<span style=\"white-space:pre\">\t</span>");
                        lastWasSpace = true;
                        break;
                    default:
                    {
                        var sb = new StringBuilder();
                        foreach (var ch in text)
                        {
                            if (ch == ' ')
                            {
                                sb.Append(lastWasSpace ? "&nbsp;" : " ");
                                lastWasSpace = true;
                            }
                            else
                            {
                                switch (ch)
                                {
                                    case '&': sb.Append("&amp;"); break;
                                    case '<': sb.Append("&lt;"); break;
                                    case '>': sb.Append("&gt;"); break;
                                    case '"': sb.Append("&quot;"); break;
                                    default: sb.Append(ch); break;
                                }
                                lastWasSpace = false;
                            }
                        }
                        w.Write(sb.ToString());
                        break;
                    }
                }
            }

            if (run.Underline) w.Write("</u>");
            if (run.Italic && !heading) w.Write("</em>");
            if (run.Bold && !heading) w.Write("</strong>");
            if (link is not null) w.Write("</a>");
        }
    }
}
