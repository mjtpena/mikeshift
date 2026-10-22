using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Shiftvert.Core.Documents;

/// <summary>ODT reader: zip + <c>content.xml</c> (and <c>styles.xml</c> for named styles) read through a hardened XmlReader.</summary>
public static class OdtReader
{
    private const string Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private const string Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private const string Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private const string XLink = "http://www.w3.org/1999/xlink";
    private const string Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

    private const long MaxXmlBytes = 400L * 1024 * 1024;

    public static DocModel Read(string path)
    {
        if (!DocReaders.StartsWithPk(path))
            throw new ConversionException("This isn't a valid ODT file (it may be damaged).");

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var content = zip.GetEntry("content.xml") ?? throw new ConversionException("This ODT file has no content.");
            var model = Read(LoadXml(content), LoadXmlOrNull(zip.GetEntry("styles.xml")));
            if (!model.HasPictures && PictureScan.ZipHasPictures(path, docx: false)) model.HasPictures = true;
            return model;
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or NotSupportedException)
        {
            throw new ConversionException("This ODT file is damaged and can't be read.", ex);
        }
    }

    private static XDocument? LoadXmlOrNull(ZipArchiveEntry? entry) => entry is null ? null : LoadXml(entry);

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxXmlBytes) throw new ConversionException("This ODT file is too large to convert.");
        using var s = entry.Open();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
            CheckCharacters = false,
        };
        using var xr = XmlReader.Create(s, settings);
        return XDocument.Load(xr, LoadOptions.PreserveWhitespace);
    }

    public static DocModel Read(XDocument content, XDocument? styles)
    {
        var reader = new Reader();
        if (styles is not null) reader.LoadStyles(styles.Root);
        reader.LoadStyles(content.Root);
        var body = content.Root?.Element(XName.Get("body", Office))?.Element(XName.Get("text", Office))
                   ?? throw new ConversionException("This ODT file has no text content.");
        reader.ReadBlocks(body, reader.Model.Blocks);
        return reader.Model;
    }

    private sealed class StyleProps
    {
        public string? Parent;
        public bool? Bold, Italic, Underline;
        public bool PageBreakBefore;
        public bool Rtl;
    }

    private sealed class Reader
    {
        private readonly Dictionary<string, StyleProps> _styles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> _listStyles = new(StringComparer.Ordinal);

        public DocModel Model { get; } = new();

        public void LoadStyles(XElement? root)
        {
            if (root is null) return;
            foreach (var container in root.Elements().Where(e => e.Name.Namespace == Office && e.Name.LocalName is "automatic-styles" or "styles"))
            {
                foreach (var st in container.Elements())
                {
                    if (st.Name == XName.Get("list-style", Text))
                    {
                        if ((string?)st.Attribute(XName.Get("name", Style)) is { } ln) _listStyles[ln] = st;
                        continue;
                    }
                    if (st.Name != XName.Get("style", Style)) continue;
                    var name = (string?)st.Attribute(XName.Get("name", Style));
                    if (name is null) continue;
                    var props = new StyleProps { Parent = (string?)st.Attribute(XName.Get("parent-style-name", Style)) };
                    if (st.Element(XName.Get("text-properties", Style)) is { } tp)
                    {
                        var w = (string?)tp.Attribute(XName.Get("font-weight", Fo));
                        if (w is not null) props.Bold = w == "bold" || (int.TryParse(w, out var wn) && wn >= 600);
                        var it = (string?)tp.Attribute(XName.Get("font-style", Fo));
                        if (it is not null) props.Italic = it is "italic" or "oblique";
                        var ul = (string?)tp.Attribute(XName.Get("text-underline-style", Style));
                        if (ul is not null) props.Underline = ul != "none";
                    }
                    if (st.Element(XName.Get("paragraph-properties", Style)) is { } pp)
                    {
                        props.PageBreakBefore = (string?)pp.Attribute(XName.Get("break-before", Fo)) == "page";
                        props.Rtl = ((string?)pp.Attribute(XName.Get("writing-mode", Style)))?.StartsWith("rl", StringComparison.Ordinal) == true;
                    }
                    _styles[name] = props;
                }
            }
        }

        private bool? Resolve(string? style, Func<StyleProps, bool?> pick)
        {
            for (var depth = 0; style is not null && depth < 12; depth++)
            {
                if (!_styles.TryGetValue(style, out var s)) return null;
                if (pick(s) is { } v) return v;
                style = s.Parent;
            }
            return null;
        }

        private bool ResolveFlag(string? style, Func<StyleProps, bool> pick)
        {
            for (var depth = 0; style is not null && depth < 12; depth++)
            {
                if (!_styles.TryGetValue(style, out var s)) return false;
                if (pick(s)) return true;
                style = s.Parent;
            }
            return false;
        }

        private readonly record struct Fmt(bool Bold, bool Italic, bool Underline, string? Link);

        // ------------------------------------------------------------ blocks

        public void ReadBlocks(XElement parent, List<DocBlock> into, int listDepth = -1, string? listStyle = null)
        {
            foreach (var e in parent.Elements())
            {
                if (e.Name.Namespace == Text)
                {
                    switch (e.Name.LocalName)
                    {
                        case "h":
                            ReadParagraph(e, into, heading: true, listDepth, listStyle);
                            break;
                        case "p":
                            ReadParagraph(e, into, heading: false, listDepth, listStyle);
                            break;
                        case "list":
                            ReadList(e, into, listDepth + 1, listStyle);
                            break;
                        case "section" or "index-body" or "table-of-content" or "alphabetical-index" or "user-index":
                            ReadBlocks(e, into, listDepth, listStyle);
                            break;
                    }
                }
                else if (e.Name == XName.Get("table", Table))
                {
                    into.Add(ReadTable(e));
                }
                else if (e.Name.Namespace == Draw)
                {
                    Model.HasPictures = true;
                }
            }
        }

        private void ReadList(XElement list, List<DocBlock> into, int depth, string? inheritedStyle)
        {
            var style = (string?)list.Attribute(XName.Get("style-name", Text)) ?? inheritedStyle;
            foreach (var item in list.Elements().Where(i => i.Name.Namespace == Text && i.Name.LocalName is "list-item" or "list-header"))
                ReadBlocks(item, into, depth, style);
        }

        private bool IsOrderedList(string? listStyle, int depth)
        {
            if (listStyle is null || !_listStyles.TryGetValue(listStyle, out var ls)) return false;
            var level = depth + 1;
            foreach (var lvl in ls.Elements())
            {
                var l = (string?)lvl.Attribute(XName.Get("level", Text));
                if (l is null || !int.TryParse(l, out var n) || n != level) continue;
                return lvl.Name.LocalName == "list-level-style-number";
            }
            return false;
        }

        private DocTable ReadTable(XElement table)
        {
            var result = new DocTable();
            foreach (var row in Rows(table))
            {
                var cells = new List<List<DocParagraph>>();
                foreach (var cell in row.Elements().Where(c => c.Name == XName.Get("table-cell", Table)))
                {
                    var repeat = int.TryParse((string?)cell.Attribute(XName.Get("number-columns-repeated", Table)), out var r) ? Math.Clamp(r, 1, 64) : 1;
                    var blocks = new List<DocBlock>();
                    ReadBlocks(cell, blocks);
                    var paragraphs = Flatten(blocks);
                    var hasText = paragraphs.Any(p => !p.IsEmpty);
                    for (var i = 0; i < (hasText ? repeat : Math.Min(repeat, 1)); i++)
                        cells.Add(i == 0 ? paragraphs : paragraphs.Select(Copy).ToList());
                }
                // Trailing empty repeated cells are layout padding.
                result.Rows.Add(cells);
            }
            return result;
        }

        private static DocParagraph Copy(DocParagraph p)
        {
            var c = new DocParagraph { Kind = p.Kind, Level = p.Level, Ordered = p.Ordered, Rtl = p.Rtl };
            foreach (var r in p.Runs) c.Runs.Add(r);
            return c;
        }

        private static IEnumerable<XElement> Rows(XElement table)
        {
            foreach (var e in table.Elements())
            {
                if (e.Name == XName.Get("table-row", Table)) yield return e;
                else if (e.Name == XName.Get("table-header-rows", Table) || e.Name == XName.Get("table-rows", Table) || e.Name == XName.Get("table-row-group", Table))
                    foreach (var r in Rows(e)) yield return r;
            }
        }

        private static List<DocParagraph> Flatten(List<DocBlock> blocks)
        {
            var list = new List<DocParagraph>();
            foreach (var b in blocks)
            {
                switch (b)
                {
                    case DocParagraph p:
                        list.Add(p.ForCell());
                        break;
                    case DocTable nested:
                        foreach (var row in nested.Rows)
                            foreach (var cell in row)
                                list.AddRange(cell);
                        break;
                }
            }
            if (list.Count == 0) list.Add(new DocParagraph());
            return list;
        }

        // ------------------------------------------------------------ paragraphs

        private void ReadParagraph(XElement e, List<DocBlock> into, bool heading, int listDepth, string? listStyle)
        {
            var styleName = (string?)e.Attribute(XName.Get("style-name", Text));
            if (ResolveFlag(styleName, s => s.PageBreakBefore))
                into.Add(new DocPageBreak());

            var p = new DocParagraph { Rtl = ResolveFlag(styleName, s => s.Rtl) };
            if (heading)
            {
                p.Kind = ParagraphKind.Heading;
                p.Level = int.TryParse((string?)e.Attribute(XName.Get("outline-level", Text)), out var lv) ? Math.Clamp(lv, 1, 6) : 1;
            }
            else if (listDepth >= 0)
            {
                p.Kind = ParagraphKind.ListItem;
                p.Level = Math.Clamp(listDepth, 0, 8);
                p.Ordered = IsOrderedList(listStyle, listDepth);
            }

            var fmt = new Fmt(
                !heading && (Resolve(styleName, s => s.Bold) ?? false),
                !heading && (Resolve(styleName, s => s.Italic) ?? false),
                Resolve(styleName, s => s.Underline) ?? false,
                null);

            var state = new TextState();
            ReadInline(e, p, fmt, state);

            // An empty paragraph that only carried a page-break style is just the break.
            if (p.Runs.Count == 0 && into.Count > 0 && into[^1] is DocPageBreak && ResolveFlag(styleName, s => s.PageBreakBefore))
                return;
            into.Add(p);
        }

        private sealed class TextState
        {
            public bool AtStart = true; // leading white space of a paragraph is dropped
            public bool LastWasSpace = true;
        }

        private void ReadInline(XElement parent, DocParagraph p, Fmt fmt, TextState st)
        {
            foreach (var node in parent.Nodes())
            {
                switch (node)
                {
                    case XText t:
                        AppendCollapsed(p, t.Value, fmt, st);
                        break;
                    case XElement e:
                        ReadInlineElement(e, p, fmt, st);
                        break;
                }
            }
        }

        private void ReadInlineElement(XElement e, DocParagraph p, Fmt fmt, TextState st)
        {
            if (e.Name.Namespace == Text)
            {
                switch (e.Name.LocalName)
                {
                    case "span":
                    {
                        var sn = (string?)e.Attribute(XName.Get("style-name", Text));
                        var f = fmt with
                        {
                            Bold = Resolve(sn, s => s.Bold) ?? fmt.Bold,
                            Italic = Resolve(sn, s => s.Italic) ?? fmt.Italic,
                            Underline = Resolve(sn, s => s.Underline) ?? fmt.Underline,
                        };
                        if (p.Kind == ParagraphKind.Heading) f = f with { Bold = false, Italic = false };
                        ReadInline(e, p, f, st);
                        break;
                    }
                    case "a":
                    {
                        var href = (string?)e.Attribute(XName.Get("href", XLink));
                        ReadInline(e, p, fmt with { Link = href ?? fmt.Link }, st);
                        break;
                    }
                    case "s":
                    {
                        var c = int.TryParse((string?)e.Attribute(XName.Get("c", Text)), out var n) ? Math.Clamp(n, 1, 10_000) : 1;
                        p.Add(new string(' ', c), fmt.Bold, fmt.Italic, fmt.Underline, fmt.Link);
                        st.AtStart = false;
                        st.LastWasSpace = true;
                        break;
                    }
                    case "tab":
                        p.Add("\t", fmt.Bold, fmt.Italic, fmt.Underline, fmt.Link);
                        st.AtStart = false;
                        st.LastWasSpace = true;
                        break;
                    case "line-break":
                        p.Add("\n", fmt.Bold, fmt.Italic, fmt.Underline, fmt.Link);
                        st.AtStart = true;
                        st.LastWasSpace = true;
                        break;
                    case "note" or "tracked-changes" or "annotation" or "change" or "change-start" or "change-end":
                        break; // footnote bodies, change tracking and comments are not body text
                    default:
                        ReadInline(e, p, fmt, st); // bookmarks, sequence/variable fields, soft-page-break ... keep their text
                        break;
                }
            }
            else if (e.Name.Namespace == Draw)
            {
                Model.HasPictures = true;
            }
            else if (e.Name.Namespace == Office && e.Name.LocalName == "annotation")
            {
                // comment: ignore
            }
            else
            {
                ReadInline(e, p, fmt, st);
            }
        }

        private static void AppendCollapsed(DocParagraph p, string raw, Fmt fmt, TextState st)
        {
            if (raw.Length == 0) return;
            var sb = new StringBuilder(raw.Length);
            foreach (var ch in raw)
            {
                if (ch is ' ' or '\t' or '\r' or '\n')
                {
                    if (st.LastWasSpace || st.AtStart) continue;
                    sb.Append(' ');
                    st.LastWasSpace = true;
                }
                else
                {
                    sb.Append(ch);
                    st.LastWasSpace = false;
                    st.AtStart = false;
                }
            }
            if (sb.Length > 0) p.Add(sb.ToString(), fmt.Bold, fmt.Italic, fmt.Underline, fmt.Link);
        }
    }
}
