using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Shiftvert.Core.Documents;

/// <summary>
/// ODT writer: a valid ODF 1.3 package (<c>mimetype</c> stored first and uncompressed, manifest, content.xml, styles.xml, meta.xml).
/// Headings use named Heading styles; bold/italic/underline use automatic text styles; lists use bullet/number list styles.
/// </summary>
public static class OdtWriter
{
    public const string MimeType = "application/vnd.oasis.opendocument.text";

    private const string NsOffice = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string NsText = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string NsTable = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string NsStyle = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private const string NsFo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private const string NsXLink = "http://www.w3.org/1999/xlink";
    private const string NsSvg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    private const string NsMeta = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
    private const string NsDc = "http://purl.org/dc/elements/1.1/";
    private const string NsManifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    public static void Write(DocModel model, Stream output, bool a4, string title)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        // The mimetype entry must be first, stored (not deflated) and carry no extra field.
        var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var s = mime.Open())
            s.Write(Encoding.ASCII.GetBytes(MimeType));

        WriteEntry(zip, "META-INF/manifest.xml", w => WriteManifest(w));
        WriteEntry(zip, "meta.xml", w => WriteMeta(w, title));
        WriteEntry(zip, "styles.xml", w => WriteStyles(w, a4));
        WriteEntry(zip, "content.xml", w => WriteContent(w, model));
    }

    private static void WriteEntry(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        using var w = XmlWriter.Create(s, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, CloseOutput = false });
        w.WriteStartDocument();
        write(w);
        w.WriteEndDocument();
    }

    // ------------------------------------------------------------ package parts

    private static void WriteManifest(XmlWriter w)
    {
        w.WriteStartElement("manifest", "manifest", NsManifest);
        w.WriteAttributeString("manifest", "version", NsManifest, "1.3");
        void Entry(string path, string type, bool root = false)
        {
            w.WriteStartElement("manifest", "file-entry", NsManifest);
            w.WriteAttributeString("manifest", "full-path", NsManifest, path);
            if (root) w.WriteAttributeString("manifest", "version", NsManifest, "1.3");
            w.WriteAttributeString("manifest", "media-type", NsManifest, type);
            w.WriteEndElement();
        }
        Entry("/", MimeType, root: true);
        Entry("content.xml", "text/xml");
        Entry("styles.xml", "text/xml");
        Entry("meta.xml", "text/xml");
        w.WriteEndElement();
    }

    private static void WriteMeta(XmlWriter w, string title)
    {
        w.WriteStartElement("office", "document-meta", NsOffice);
        w.WriteAttributeString("xmlns", "meta", null, NsMeta);
        w.WriteAttributeString("xmlns", "dc", null, NsDc);
        w.WriteAttributeString("office", "version", NsOffice, "1.3");
        w.WriteStartElement("office", "meta", NsOffice);
        w.WriteElementString("meta", "generator", NsMeta, "Shiftvert");
        w.WriteElementString("dc", "title", NsDc, TextUtil.XmlSafe(title));
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void StartRoot(XmlWriter w, string name)
    {
        w.WriteStartElement("office", name, NsOffice);
        w.WriteAttributeString("xmlns", "text", null, NsText);
        w.WriteAttributeString("xmlns", "table", null, NsTable);
        w.WriteAttributeString("xmlns", "style", null, NsStyle);
        w.WriteAttributeString("xmlns", "fo", null, NsFo);
        w.WriteAttributeString("xmlns", "xlink", null, NsXLink);
        w.WriteAttributeString("xmlns", "svg", null, NsSvg);
        w.WriteAttributeString("office", "version", NsOffice, "1.3");
    }

    private static void WriteStyles(XmlWriter w, bool a4)
    {
        StartRoot(w, "document-styles");

        w.WriteStartElement("office", "font-face-decls", NsOffice);
        w.WriteEndElement();

        w.WriteStartElement("office", "styles", NsOffice);

        Style(w, "Standard", "Standard", "paragraph", parent: null, () =>
        {
            w.WriteStartElement("style", "paragraph-properties", NsStyle);
            w.WriteAttributeString("fo", "margin-bottom", NsFo, "0.2cm");
            w.WriteEndElement();
            w.WriteStartElement("style", "text-properties", NsStyle);
            w.WriteAttributeString("style", "font-name", NsStyle, "Calibri");
            w.WriteAttributeString("fo", "font-family", NsFo, "Calibri");
            w.WriteAttributeString("fo", "font-size", NsFo, "11pt");
            w.WriteEndElement();
        });

        var sizes = new[] { "20pt", "16pt", "14pt", "12pt", "11pt", "11pt" };
        for (var level = 1; level <= 6; level++)
        {
            Style(w, $"Heading_20_{level}", $"Heading {level}", "paragraph", "Standard", () =>
            {
                w.WriteStartElement("style", "paragraph-properties", NsStyle);
                w.WriteAttributeString("fo", "margin-top", NsFo, "0.4cm");
                w.WriteAttributeString("fo", "margin-bottom", NsFo, "0.15cm");
                w.WriteAttributeString("fo", "keep-with-next", NsFo, "always");
                w.WriteEndElement();
                w.WriteStartElement("style", "text-properties", NsStyle);
                w.WriteAttributeString("fo", "font-size", NsFo, sizes[level - 1]);
                w.WriteAttributeString("fo", "font-weight", NsFo, "bold");
                w.WriteEndElement();
            }, outlineLevel: level);
        }

        Style(w, "Table_20_Contents", "Table Contents", "paragraph", "Standard", () =>
        {
            w.WriteStartElement("style", "paragraph-properties", NsStyle);
            w.WriteAttributeString("fo", "margin-bottom", NsFo, "0cm");
            w.WriteEndElement();
        });
        w.WriteEndElement(); // office:styles

        w.WriteStartElement("office", "automatic-styles", NsOffice);
        w.WriteStartElement("style", "page-layout", NsStyle);
        w.WriteAttributeString("style", "name", NsStyle, "pm1");
        w.WriteStartElement("style", "page-layout-properties", NsStyle);
        w.WriteAttributeString("fo", "page-width", NsFo, a4 ? "21.001cm" : "21.59cm");
        w.WriteAttributeString("fo", "page-height", NsFo, a4 ? "29.7cm" : "27.94cm");
        w.WriteAttributeString("style", "print-orientation", NsStyle, "portrait");
        foreach (var side in new[] { "top", "bottom", "left", "right" })
            w.WriteAttributeString("fo", "margin-" + side, NsFo, "2.54cm");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("office", "master-styles", NsOffice);
        w.WriteStartElement("style", "master-page", NsStyle);
        w.WriteAttributeString("style", "name", NsStyle, "Standard");
        w.WriteAttributeString("style", "page-layout-name", NsStyle, "pm1");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteEndElement();
    }

    private static void Style(XmlWriter w, string name, string display, string family, string? parent, Action body, int outlineLevel = 0)
    {
        w.WriteStartElement("style", "style", NsStyle);
        w.WriteAttributeString("style", "name", NsStyle, name);
        w.WriteAttributeString("style", "display-name", NsStyle, display);
        w.WriteAttributeString("style", "family", NsStyle, family);
        if (parent is not null) w.WriteAttributeString("style", "parent-style-name", NsStyle, parent);
        if (outlineLevel > 0) w.WriteAttributeString("style", "default-outline-level", NsStyle, outlineLevel.ToString());
        body();
        w.WriteEndElement();
    }

    // ------------------------------------------------------------ content

    private static void WriteContent(XmlWriter w, DocModel model)
    {
        StartRoot(w, "document-content");

        w.WriteStartElement("office", "automatic-styles", NsOffice);
        // T1..T7: bold=1, italic=2, underline=4 combinations.
        for (var mask = 1; mask < 8; mask++)
        {
            w.WriteStartElement("style", "style", NsStyle);
            w.WriteAttributeString("style", "name", NsStyle, "T" + mask);
            w.WriteAttributeString("style", "family", NsStyle, "text");
            w.WriteStartElement("style", "text-properties", NsStyle);
            if ((mask & 1) != 0) { w.WriteAttributeString("fo", "font-weight", NsFo, "bold"); }
            if ((mask & 2) != 0) { w.WriteAttributeString("fo", "font-style", NsFo, "italic"); }
            if ((mask & 4) != 0)
            {
                w.WriteAttributeString("style", "text-underline-style", NsStyle, "solid");
                w.WriteAttributeString("style", "text-underline-width", NsStyle, "auto");
                w.WriteAttributeString("style", "text-underline-color", NsStyle, "font-color");
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }

        // Paragraph variants: RTL, and the explicit page break paragraph.
        ParagraphStyle(w, "P_rtl", rtl: true, pageBreak: false, "Standard");
        ParagraphStyle(w, "P_break", rtl: false, pageBreak: true, "Standard");
        ParagraphStyle(w, "P_rtl_cell", rtl: true, pageBreak: false, "Table_20_Contents");
        ParagraphStyle(w, "H_rtl", rtl: true, pageBreak: false, null);

        ListStyle(w, "L_bullet", ordered: false);
        ListStyle(w, "L_number", ordered: true);

        w.WriteStartElement("style", "style", NsStyle);
        w.WriteAttributeString("style", "name", NsStyle, "Tbl");
        w.WriteAttributeString("style", "family", NsStyle, "table");
        w.WriteStartElement("style", "table-properties", NsStyle);
        w.WriteAttributeString("style", "width", NsStyle, "16.99cm");
        w.WriteAttributeString("table", "align", NsTable, "margins");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("style", "style", NsStyle);
        w.WriteAttributeString("style", "name", NsStyle, "TblCell");
        w.WriteAttributeString("style", "family", NsStyle, "table-cell");
        w.WriteStartElement("style", "table-cell-properties", NsStyle);
        w.WriteAttributeString("fo", "padding", NsFo, "0.1cm");
        w.WriteAttributeString("fo", "border", NsFo, "0.5pt solid #000000");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement(); // automatic-styles

        w.WriteStartElement("office", "body", NsOffice);
        w.WriteStartElement("office", "text", NsOffice);

        var tableNo = 0;
        var lists = new ListEmitter(w);
        foreach (var block in model.Blocks)
        {
            if (block is DocParagraph { Kind: ParagraphKind.ListItem } item)
            {
                lists.Item(item, () => WriteParagraphBody(w, item, "text", "p", styleName: item.EffectiveRtl ? "P_rtl" : "Standard"));
                continue;
            }
            lists.Close();

            switch (block)
            {
                case DocParagraph p when p.Kind == ParagraphKind.Heading:
                    WriteHeading(w, p);
                    break;
                case DocParagraph p:
                    WriteParagraphBody(w, p, "text", "p", p.EffectiveRtl ? "P_rtl" : "Standard");
                    break;
                case DocTable t:
                    WriteTable(w, t, ++tableNo);
                    break;
                case DocPageBreak:
                    w.WriteStartElement("text", "p", NsText);
                    w.WriteAttributeString("text", "style-name", NsText, "P_break");
                    w.WriteEndElement();
                    break;
            }
        }
        lists.Close();

        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void ParagraphStyle(XmlWriter w, string name, bool rtl, bool pageBreak, string? parent)
    {
        w.WriteStartElement("style", "style", NsStyle);
        w.WriteAttributeString("style", "name", NsStyle, name);
        w.WriteAttributeString("style", "family", NsStyle, "paragraph");
        if (parent is not null) w.WriteAttributeString("style", "parent-style-name", NsStyle, parent);
        w.WriteStartElement("style", "paragraph-properties", NsStyle);
        if (pageBreak) w.WriteAttributeString("fo", "break-before", NsFo, "page");
        if (rtl)
        {
            w.WriteAttributeString("fo", "text-align", NsFo, "start");
            w.WriteAttributeString("style", "writing-mode", NsStyle, "rl-tb");
        }
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void ListStyle(XmlWriter w, string name, bool ordered)
    {
        w.WriteStartElement("text", "list-style", NsText);
        w.WriteAttributeString("style", "name", NsStyle, name);
        for (var level = 1; level <= 10; level++)
        {
            w.WriteStartElement("text", ordered ? "list-level-style-number" : "list-level-style-bullet", NsText);
            w.WriteAttributeString("text", "level", NsText, level.ToString());
            if (ordered)
            {
                w.WriteAttributeString("style", "num-suffix", NsStyle, ".");
                w.WriteAttributeString("style", "num-format", NsStyle, "1");
            }
            else
            {
                w.WriteAttributeString("text", "bullet-char", NsText, level % 2 == 1 ? "•" : "◦");
            }
            w.WriteStartElement("style", "list-level-properties", NsStyle);
            w.WriteAttributeString("text", "list-level-position-and-space-mode", NsText, "label-alignment");
            w.WriteStartElement("style", "list-level-label-alignment", NsStyle);
            w.WriteAttributeString("text", "label-followed-by", NsText, "listtab");
            w.WriteAttributeString("text", "list-tab-stop-position", NsText, (level * 1.27 + 0.635).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "cm");
            w.WriteAttributeString("fo", "text-indent", NsFo, "-0.635cm");
            w.WriteAttributeString("fo", "margin-left", NsFo, (level * 1.27 + 0.635).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "cm");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    /// <summary>Opens and closes nested <c>text:list</c> elements for a flat sequence of list-item paragraphs.</summary>
    private sealed class ListEmitter(XmlWriter w)
    {
        private int _depth;           // open text:list elements
        private bool _itemOpen;       // the innermost list currently has an open list-item
        private bool _rootOrdered;

        public void Item(DocParagraph item, Action writeBody)
        {
            var level = Math.Min(Math.Max(item.Level, 0), _depth); // can only go one level deeper than the current depth
            if (_depth > 0 && level == 0 && item.Ordered != _rootOrdered)
            {
                Close();
                level = 0;
            }

            // Close deeper levels.
            while (_depth > level + 1)
            {
                CloseItem();
                w.WriteEndElement(); // text:list
                _depth--;
                _itemOpen = true;     // the parent's list-item is still open
            }
            if (_depth == level + 1)
            {
                CloseItem();
            }
            else
            {
                // Open a new (nested) list inside the open list-item.
                w.WriteStartElement("text", "list", NsText);
                w.WriteAttributeString("text", "style-name", NsText, item.Ordered ? "L_number" : "L_bullet");
                if (_depth == 0) _rootOrdered = item.Ordered;
                _depth++;
            }
            w.WriteStartElement("text", "list-item", NsText);
            _itemOpen = true;
            writeBody();
        }

        private void CloseItem()
        {
            if (!_itemOpen) return;
            w.WriteEndElement();
            _itemOpen = false;
        }

        public void Close()
        {
            if (_depth == 0) return;
            CloseItem();
            while (_depth > 0)
            {
                w.WriteEndElement(); // text:list
                _depth--;
                if (_depth > 0) w.WriteEndElement(); // the enclosing list-item
            }
            _itemOpen = false;
        }
    }

    private static void WriteHeading(XmlWriter w, DocParagraph p)
    {
        var level = Math.Clamp(p.Level, 1, 6);
        w.WriteStartElement("text", "h", NsText);
        w.WriteAttributeString("text", "style-name", NsText, p.EffectiveRtl ? "H_rtl" : $"Heading_20_{level}");
        w.WriteAttributeString("text", "outline-level", NsText, level.ToString());
        WriteRuns(w, p);
        w.WriteEndElement();
    }

    private static void WriteParagraphBody(XmlWriter w, DocParagraph p, string prefix, string name, string styleName)
    {
        w.WriteStartElement(prefix, name, NsText);
        w.WriteAttributeString("text", "style-name", NsText, styleName);
        WriteRuns(w, p);
        w.WriteEndElement();
    }

    private static void WriteTable(XmlWriter w, DocTable t, int number)
    {
        var cols = Math.Max(1, t.ColumnCount);
        w.WriteStartElement("table", "table", NsTable);
        w.WriteAttributeString("table", "name", NsTable, "Table" + number);
        w.WriteAttributeString("table", "style-name", NsTable, "Tbl");
        w.WriteStartElement("table", "table-column", NsTable);
        w.WriteAttributeString("table", "number-columns-repeated", NsTable, cols.ToString());
        w.WriteEndElement();
        foreach (var row in t.Rows)
        {
            w.WriteStartElement("table", "table-row", NsTable);
            for (var c = 0; c < cols; c++)
            {
                w.WriteStartElement("table", "table-cell", NsTable);
                w.WriteAttributeString("table", "style-name", NsTable, "TblCell");
                w.WriteAttributeString("office", "value-type", NsOffice, "string");
                var paragraphs = c < row.Count && row[c].Count > 0 ? row[c] : [new DocParagraph()];
                foreach (var p in paragraphs)
                    WriteParagraphBody(w, p, "text", "p", p.EffectiveRtl ? "P_rtl_cell" : "Table_20_Contents");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    // ------------------------------------------------------------ inline

    private static void WriteRuns(XmlWriter w, DocParagraph p)
    {
        var atStart = true; // ODF collapses leading/repeated spaces; count them explicitly
        var lastWasSpace = true;
        var heading = p.Kind == ParagraphKind.Heading;
        foreach (var run in p.Runs)
        {
            var mask = (run.Bold && !heading ? 1 : 0) | (run.Italic && !heading ? 2 : 0) | (run.Underline ? 4 : 0);
            var link = run.Link is not null && Uri.TryCreate(run.Link, UriKind.Absolute, out _) ? run.Link : null;
            if (link is not null)
            {
                w.WriteStartElement("text", "a", NsText);
                w.WriteAttributeString("xlink", "type", NsXLink, "simple");
                w.WriteAttributeString("xlink", "href", NsXLink, link);
            }
            if (mask != 0)
            {
                w.WriteStartElement("text", "span", NsText);
                w.WriteAttributeString("text", "style-name", NsText, "T" + mask);
            }

            foreach (var (kind, text) in TextUtil.Pieces(TextUtil.XmlSafe(run.Text)))
            {
                switch (kind)
                {
                    case 1:
                        w.WriteElementString("text", "line-break", NsText, "");
                        atStart = true; lastWasSpace = true;
                        break;
                    case 2:
                        w.WriteElementString("text", "tab", NsText, "");
                        atStart = false; lastWasSpace = true;
                        break;
                    default:
                        WriteText(w, text, ref atStart, ref lastWasSpace);
                        break;
                }
            }

            if (mask != 0) w.WriteEndElement();
            if (link is not null) w.WriteEndElement();
        }
    }

    private static void WriteText(XmlWriter w, string text, ref bool atStart, ref bool lastWasSpace)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != ' ')
            {
                sb.Append(text[i]);
                atStart = false;
                lastWasSpace = false;
                i++;
                continue;
            }

            var j = i;
            while (j < text.Length && text[j] == ' ') j++;
            var count = j - i;
            // The first space after a visible character is a normal space; every other one needs <text:s/>.
            var plain = lastWasSpace || atStart ? 0 : 1;
            if (plain == 1) sb.Append(' ');
            var extra = count - plain;
            if (extra > 0)
            {
                if (sb.Length > 0) { w.WriteString(sb.ToString()); sb.Clear(); }
                w.WriteStartElement("text", "s", NsText);
                if (extra > 1) w.WriteAttributeString("text", "c", NsText, extra.ToString());
                w.WriteEndElement();
            }
            lastWasSpace = true;
            atStart = false;
            i = j;
        }
        if (sb.Length > 0) w.WriteString(sb.ToString());
    }
}
