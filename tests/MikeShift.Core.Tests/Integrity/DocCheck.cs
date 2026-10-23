using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// Independent document readers. Each turns a file into the same plain "outline" notation as <c>Fixtures.DocumentOutline</c>:
/// <c>H1:text</c>, <c>P:text</c> (with <c>**bold**</c>, <c>_italic_</c>, <c>~underline~</c>), <c>UL0:item</c> / <c>OL0:item</c> (bulleted /
/// numbered, level), <c>T:[a|b];[c|d]</c> (table). The readers are written for the suite: DocumentFormat.OpenXml for DOCX,
/// System.Xml for ODT, a hand-rolled tokenizer for RTF and HTML. None of them shares code with the converters' own readers.
/// </summary>
internal static class DocCheck
{
    static DocCheck() => MikeShift.Core.Documents.TextDecoding.EnsureProviders();

    /// <summary>Appends text with its run formatting to a paragraph, merging adjacent runs of the same formatting.</summary>
    private sealed class Para
    {
        private readonly List<(string Text, bool B, bool I, bool U)> _runs = [];

        public void Add(string text, bool b, bool i, bool u)
        {
            if (text.Length == 0) return;
            if (_runs.Count > 0 && _runs[^1] is var l && (l.B, l.I, l.U) == (b, i, u)) _runs[^1] = l with { Text = l.Text + text };
            else _runs.Add((text, b, i, u));
        }

        public bool IsEmpty => _runs.All(r => string.IsNullOrWhiteSpace(r.Text));

        public string Marked()
        {
            var sb = new StringBuilder();
            foreach (var (text, b, i, u) in _runs)
            {
                // Markers wrap only the visible part so a trailing space stays outside them.
                var lead = text.Length - text.TrimStart().Length;
                var trail = text.Length - text.TrimEnd().Length;
                var core = text.Trim();
                sb.Append(text, 0, lead);
                if (core.Length > 0)
                {
                    if (b) sb.Append("**");
                    if (i) sb.Append('_');
                    if (u) sb.Append('~');
                    sb.Append(core);
                    if (u) sb.Append('~');
                    if (i) sb.Append('_');
                    if (b) sb.Append("**");
                }
                sb.Append(text, text.Length - trail, trail);
            }
            return Regex.Replace(sb.ToString().Trim(), @"\s+", " ");
        }

        public string Plain() => Regex.Replace(string.Concat(_runs.Select(r => r.Text)).Trim(), @"\s+", " ");
    }

    // ------------------------------------------------------------------ DOCX

    public static IReadOnlyList<string> DocxOutline(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var numbering = doc.MainDocumentPart.NumberingDefinitionsPart?.Numbering;
        var styles = doc.MainDocumentPart.StyleDefinitionsPart?.Styles;
        var lines = new List<string>();
        foreach (var el in body.ChildElements)
        {
            if (el is Paragraph p) AddDocxParagraph(p, lines, numbering, styles);
            else if (el is Table t) lines.Add("T:" + string.Join(";", t.Elements<TableRow>().Select(r =>
                "[" + string.Join("|", r.Elements<TableCell>().Select(c => string.Join(" ", c.Descendants<Paragraph>().Select(DocxPlain)).Trim())) + "]")));
        }
        return lines;
    }

    private static string DocxPlain(Paragraph p) => string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim();

    private static void AddDocxParagraph(Paragraph p, List<string> lines, Numbering? numbering, Styles? styles)
    {
        var para = new Para();
        foreach (var r in p.Descendants<Run>())
        {
            var rp = r.RunProperties;
            var b = rp?.Bold is { } bo && (bo.Val is null || bo.Val.Value);
            var i = rp?.Italic is { } it && (it.Val is null || it.Val.Value);
            var u = rp?.Underline is { } un && un.Val is not null && un.Val.Value != UnderlineValues.None;
            var text = string.Concat(r.ChildElements.Select(c => c switch { Text t => t.Text, TabChar => "\t", Break => " ", _ => "" }));
            para.Add(text, b, i, u);
        }
        if (para.IsEmpty) return;

        var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        var heading = HeadingLevel(styleId, styles);
        if (heading > 0)
        {
            // Headings are bold by style; the outline notation records the heading, not the bold.
            lines.Add($"H{heading}:" + para.Plain());
            return;
        }

        var num = p.ParagraphProperties?.NumberingProperties;
        if (num?.NumberingId?.Val is { } numId && numId.Value != 0)
        {
            var ilvl = num.NumberingLevelReference?.Val?.Value ?? 0;
            lines.Add($"{(IsBullet(numbering, numId.Value, ilvl) ? "UL" : "OL")}{ilvl}:" + para.Marked());
            return;
        }
        lines.Add("P:" + para.Marked());
    }

    private static int HeadingLevel(string? styleId, Styles? styles)
    {
        if (styleId is null) return 0;
        var m = Regex.Match(styleId, @"^Heading(\d)$", RegexOptions.IgnoreCase);
        if (m.Success) return int.Parse(m.Groups[1].Value);
        var style = styles?.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == styleId);
        var outline = style?.StyleParagraphProperties?.OutlineLevel?.Val?.Value;
        return outline is { } o ? o + 1 : 0;
    }

    private static bool IsBullet(Numbering? numbering, int numId, int ilvl)
    {
        var num = numbering?.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
        var abs = numbering?.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == num?.AbstractNumId?.Val?.Value);
        var lvl = abs?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == ilvl) ?? abs?.Elements<Level>().FirstOrDefault();
        return lvl?.NumberingFormat?.Val?.Value == NumberFormatValues.Bullet;
    }

    /// <summary>OpenXML SDK schema validation (Office 2019 rules); returns the error descriptions, empty when the package is valid.</summary>
    public static IReadOnlyList<string> ValidateDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var validator = new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019);
        return validator.Validate(doc).Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
    }

    // ------------------------------------------------------------------ ODT

    private const string NsText = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string NsTable = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string NsStyle = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private const string NsFo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";

    /// <summary>Structure rules of the ODF package: <c>mimetype</c> first, stored (not deflated), exact; manifest lists the parts; every XML part is well formed.</summary>
    public static void AssertOdtPackage(string path, bool manifestListsParts = true)
    {
        using var fs = File.OpenRead(path);
        // Raw bytes: the first local file header must be "mimetype", method 0 (stored), with no extra field.
        var head = new byte[30 + 8 + 39];
        fs.ReadExactly(head);
        Assert.True(head.AsSpan(0, 4).SequenceEqual(new byte[] { 0x50, 0x4B, 3, 4 }), "not a zip");
        Assert.Equal(0, BitConverter.ToUInt16(head, 8));                          // compression method: stored
        Assert.Equal("mimetype", Encoding.ASCII.GetString(head, 30, 8));
        Assert.Equal("application/vnd.oasis.opendocument.text", Encoding.ASCII.GetString(head, 38, 39));
        Assert.Equal(0, BitConverter.ToUInt16(head, 28));                         // no extra field before the data

        fs.Position = 0;
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        var names = zip.Entries.Select(e => e.FullName).ToHashSet();
        Assert.Contains("content.xml", names);
        Assert.Contains("META-INF/manifest.xml", names);
        var manifest = LoadXml(zip, "META-INF/manifest.xml");
        const string NsManifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
        var listed = manifest.GetElementsByTagName("file-entry", NsManifest).Cast<XmlElement>().Select(e => e.GetAttribute("full-path", NsManifest)).ToHashSet();
        Assert.Contains("/", listed);
        if (manifestListsParts) Assert.Contains("content.xml", listed);
        foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
            LoadXml(zip, e.FullName); // throws on malformed XML
    }

    private static XmlDocument LoadXml(ZipArchive zip, string name)
    {
        var d = new XmlDocument();
        using var s = zip.GetEntry(name)!.Open();
        d.Load(s);
        return d;
    }

    public static IReadOnlyList<string> OdtOutline(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var content = LoadXml(zip, "content.xml");
        var styles = zip.GetEntry("styles.xml") is not null ? LoadXml(zip, "styles.xml") : null;
        var ns = new XmlNamespaceManager(content.NameTable);
        ns.AddNamespace("text", NsText); ns.AddNamespace("table", NsTable); ns.AddNamespace("style", NsStyle); ns.AddNamespace("office", "urn:oasis:names:tc:opendocument:xmlns:office:1.0");

        var textStyles = new Dictionary<string, (bool B, bool I, bool U)>();
        var listStyles = new Dictionary<string, Dictionary<int, bool>>(); // style name -> level -> is bullet
        foreach (var doc in new[] { content, styles }.Where(d => d is not null))
        {
            foreach (var st in doc!.SelectNodes("//style:style[@style:family='text']", ns)!.Cast<XmlElement>())
            {
                var tp = st.SelectSingleNode("style:text-properties", ns) as XmlElement;
                textStyles[st.GetAttribute("name", NsStyle)] = (
                    tp?.GetAttribute("font-weight", NsFo) == "bold",
                    tp?.GetAttribute("font-style", NsFo) == "italic",
                    tp is not null && tp.GetAttribute("text-underline-style", NsStyle) is { Length: > 0 } us && us != "none");
            }
            foreach (var ls in doc.SelectNodes("//text:list-style", ns)!.Cast<XmlElement>())
            {
                var levels = new Dictionary<int, bool>();
                foreach (var lv in ls.ChildNodes.OfType<XmlElement>())
                    levels[int.Parse(lv.GetAttribute("level", NsText))] = lv.LocalName == "list-level-style-bullet";
                listStyles[ls.GetAttribute("name", NsStyle)] = levels;
            }
        }

        var lines = new List<string>();
        void Block(XmlElement e, int listDepth, string? listStyle)
        {
            switch (e.LocalName)
            {
                case "h":
                    lines.Add($"H{e.GetAttribute("outline-level", NsText)}:" + OdtPara(e, textStyles).Plain());
                    break;
                case "p":
                {
                    var para = OdtPara(e, textStyles);
                    if (para.IsEmpty) break;
                    if (listStyle is not null)
                    {
                        var bullet = listStyles.TryGetValue(listStyle, out var lv) && lv.TryGetValue(listDepth, out var bl) && bl;
                        lines.Add($"{(bullet ? "UL" : "OL")}{listDepth - 1}:" + para.Marked());
                    }
                    else lines.Add("P:" + para.Marked());
                    break;
                }
                case "list":
                {
                    var style = e.GetAttribute("style-name", NsText);
                    foreach (var item in e.ChildNodes.OfType<XmlElement>().Where(x => x.LocalName == "list-item"))
                        foreach (var child in item.ChildNodes.OfType<XmlElement>())
                            Block(child, listDepth + 1, string.IsNullOrEmpty(style) ? listStyle : style);
                    break;
                }
                case "table":
                    lines.Add("T:" + string.Join(";", e.SelectNodes(".//table:table-row", ns)!.Cast<XmlElement>().Select(r =>
                        "[" + string.Join("|", r.SelectNodes("table:table-cell", ns)!.Cast<XmlElement>().Select(c => Regex.Replace(c.InnerText.Trim(), @"\s+", " "))) + "]")));
                    break;
            }
        }
        foreach (var child in content.SelectSingleNode("//office:text", ns)!.ChildNodes.OfType<XmlElement>())
            Block(child, 0, null);
        return lines;
    }

    private static Para OdtPara(XmlElement e, Dictionary<string, (bool B, bool I, bool U)> styles)
    {
        var para = new Para();
        void Walk(XmlNode n, bool b, bool i, bool u)
        {
            foreach (var c in n.ChildNodes)
            {
                switch (c)
                {
                    case XmlText t: para.Add(t.Value ?? "", b, i, u); break;
                    case XmlElement { LocalName: "span" } sp:
                    {
                        var (sb, si, su) = styles.TryGetValue(sp.GetAttribute("style-name", NsText), out var s) ? s : default;
                        Walk(sp, b || sb, i || si, u || su);
                        break;
                    }
                    case XmlElement { LocalName: "s" } s: para.Add(new string(' ', int.TryParse(s.GetAttribute("c", NsText), out var n2) ? n2 : 1), b, i, u); break;
                    case XmlElement { LocalName: "tab" }: para.Add("\t", b, i, u); break;
                    case XmlElement { LocalName: "line-break" }: para.Add(" ", b, i, u); break;
                    case XmlElement x: Walk(x, b, i, u); break;
                }
            }
        }
        Walk(e, false, false, false);
        return para;
    }

    // ------------------------------------------------------------------ HTML

    public static IReadOnlyList<string> HtmlOutline(string html)
    {
        var lines = new List<string>();
        var para = new Para();
        var lists = new Stack<bool>(); // true = bulleted
        var table = new List<List<string>>();
        List<string>? row = null;
        var cell = new StringBuilder();
        var inCell = false;
        var skip = 0; // inside head / style / script / title
        var heading = 0;
        var li = false;
        int b = 0, i = 0, u = 0;

        void Flush()
        {
            if (para.IsEmpty) { para = new Para(); return; }
            if (inCell) { cell.Append(para.Plain()).Append(' '); }
            else if (heading > 0) lines.Add($"H{heading}:" + para.Plain());
            else if (li && lists.Count > 0) lines.Add($"{(lists.Peek() ? "UL" : "OL")}{lists.Count - 1}:" + para.Marked());
            else lines.Add("P:" + para.Marked());
            para = new Para();
        }

        foreach (Match m in Regex.Matches(html, @"<!--.*?-->|<![^>]*>|<(/?)([a-zA-Z][a-zA-Z0-9]*)([^>]*)>|([^<]+)", RegexOptions.Singleline))
        {
            if (m.Groups[4].Success)
            {
                if (skip > 0) continue;
                var text = WebUtility.HtmlDecode(m.Groups[4].Value);
                para.Add(Regex.Replace(text, @"\s+", " "), b > 0, i > 0, u > 0);
                continue;
            }
            if (!m.Groups[2].Success) continue;
            var closing = m.Groups[1].Value == "/";
            var tag = m.Groups[2].Value.ToLowerInvariant();
            switch (tag)
            {
                case "head" or "style" or "script" or "title": skip += closing ? -1 : 1; break;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    Flush();
                    heading = closing ? 0 : tag[1] - '0';
                    break;
                case "p" or "div": Flush(); break;
                case "br": para.Add(" ", false, false, false); break;
                case "ul" or "ol":
                    Flush();
                    if (closing) { if (lists.Count > 0) lists.Pop(); } else lists.Push(tag == "ul");
                    break;
                case "li": Flush(); li = !closing; break;
                case "strong" or "b": b += closing ? -1 : 1; break;
                case "em" or "i": i += closing ? -1 : 1; break;
                case "u": u += closing ? -1 : 1; break;
                case "table": Flush(); if (!closing) table = []; else lines.Add("T:" + string.Join(";", table.Select(r => "[" + string.Join("|", r) + "]"))); break;
                case "tr": if (!closing) { row = []; table.Add(row); } break;
                case "td" or "th":
                    if (!closing) { inCell = true; cell.Clear(); }
                    else { Flush(); row?.Add(Regex.Replace(cell.ToString().Trim(), @"\s+", " ")); inCell = false; }
                    break;
            }
        }
        Flush();
        return lines;
    }

    // ------------------------------------------------------------------ RTF

    public static IReadOnlyList<string> RtfOutline(string rtf)
    {
        var lines = new List<string>();
        var lsKind = new Dictionary<int, bool>(); // \lsN -> bullet?
        var listBullet = new Dictionary<int, bool>(); // \listidN -> bullet?
        var lsToList = new Dictionary<int, int>();

        // Pass 1: list table (bullet = \levelnfc23 on the first level), list override table (ls -> listid).
        foreach (Match l in Regex.Matches(rtf, @"\{\\list\\listtemplateid\d+.*?\\listid(\d+)\}", RegexOptions.Singleline))
        {
            var nfc = Regex.Match(l.Value, @"\\levelnfc(\d+)");
            listBullet[int.Parse(l.Groups[1].Value)] = nfc.Success && nfc.Groups[1].Value == "23";
        }
        foreach (Match o in Regex.Matches(rtf, @"\{\\listoverride\\listid(\d+)\\listoverridecount\d+\\ls(\d+)\}"))
            lsToList[int.Parse(o.Groups[2].Value)] = int.Parse(o.Groups[1].Value);
        foreach (var (lsNo, id) in lsToList) if (listBullet.TryGetValue(id, out var bl)) lsKind[lsNo] = bl;

        // Style numbers that are headings ("heading 1" in the stylesheet).
        var headingStyle = new Dictionary<int, int>();
        foreach (Match h in Regex.Matches(rtf, @"\{\\s(\d+)[^{}]*?heading (\d);"))
            headingStyle[int.Parse(h.Groups[1].Value)] = int.Parse(h.Groups[2].Value);

        // Pass 2: tokenize.
        var depth = 0;
        var stack = new Stack<(bool B, bool I, bool U, bool Ignore)>();
        bool bold = false, italic = false, ul = false, ignore = false;
        var para = new Para();
        int style = 0, ls = -1, ilvl = 0, outline = 0;
        bool inTable = false, hasOutline = false;
        var rows = new List<List<string>>();
        List<string> row = [];
        var cell = new StringBuilder();

        void EndPara()
        {
            if (inTable) { return; }
            FlushTable(); // a paragraph after table rows closes the table
            if (!para.IsEmpty)
            {
                if (headingStyle.TryGetValue(style, out var level)) lines.Add($"H{level}:" + para.Plain());
                else if (hasOutline) lines.Add($"H{outline + 1}:" + para.Plain());
                else if (ls >= 0 && lsKind.TryGetValue(ls, out var bullet)) lines.Add($"{(bullet ? "UL" : "OL")}{ilvl}:" + para.Marked());
                else lines.Add("P:" + para.Marked());
            }
            para = new Para();
        }

        void FlushTable()
        {
            if (rows.Count > 0) lines.Add("T:" + string.Join(";", rows.Select(r => "[" + string.Join("|", r) + "]")));
            rows.Clear();
        }
        // (declared before EndPara is used)

        var pos = 0;
        while (pos < rtf.Length)
        {
            var c = rtf[pos];
            if (c == '{') { stack.Push((bold, italic, ul, ignore)); depth++; pos++; if (pos + 2 < rtf.Length && rtf[pos] == '\\' && rtf[pos + 1] == '*') { ignore = true; } continue; }
            if (c == '}') { if (stack.Count > 0) (bold, italic, ul, ignore) = stack.Pop(); depth--; pos++; continue; }
            if (c is '\r' or '\n') { pos++; continue; }
            if (c == '\\')
            {
                pos++;
                if (pos >= rtf.Length) break;
                var n = rtf[pos];
                if (n is '\\' or '{' or '}') { if (!ignore) para.Add(n.ToString(), bold, italic, ul); pos++; continue; }
                if (n == '\'')
                {
                    var hex = rtf.Substring(pos + 1, 2); pos += 3;
                    if (!ignore) para.Add(Encoding.GetEncoding(1252).GetString([Convert.ToByte(hex, 16)]), bold, italic, ul);
                    continue;
                }
                if (n == '~') { if (!ignore) para.Add("\u00A0", bold, italic, ul); pos++; continue; }
                if (!char.IsLetter(n)) { pos++; continue; }
                var start = pos;
                while (pos < rtf.Length && char.IsLetter(rtf[pos])) pos++;
                var word = rtf[start..pos];
                int? arg = null;
                if (pos < rtf.Length && (rtf[pos] == '-' || char.IsDigit(rtf[pos])))
                {
                    var s = pos; pos++;
                    while (pos < rtf.Length && char.IsDigit(rtf[pos])) pos++;
                    arg = int.Parse(rtf[s..pos]);
                }
                if (pos < rtf.Length && rtf[pos] == ' ') pos++;

                if (word is "fonttbl" or "colortbl" or "stylesheet" or "listtable" or "listoverridetable" or "info" or "pict" or "listtext" or "pntext") { ignore = true; continue; }
                if (ignore) continue;
                switch (word)
                {
                    case "pard": style = 0; ls = -1; ilvl = 0; hasOutline = false; inTable = false; break;
                    case "plain": bold = italic = ul = false; break;
                    case "b": bold = arg != 0; break;
                    case "i": italic = arg != 0; break;
                    case "ul": ul = arg != 0; break;
                    case "ulnone": ul = false; break;
                    case "s": style = arg ?? 0; break;
                    case "outlinelevel": hasOutline = true; outline = arg ?? 0; break;
                    case "ls": ls = arg ?? -1; break;
                    case "ilvl": ilvl = arg ?? 0; break;
                    case "intbl": inTable = true; break;
                    case "par": if (!inTable) EndPara(); else cell.Append(' '); break;
                    case "line": para.Add(" ", bold, italic, ul); break;
                    case "tab": para.Add("\t", bold, italic, ul); break;
                    case "cell": row.Add(Regex.Replace(para.Plain() + cell.ToString().Trim(), @"\s+", " ").Trim()); para = new Para(); cell.Clear(); break;
                    case "row": rows.Add(row); row = []; break;
                    case "uc": break;
                    case "u":
                    {
                        var code = (ushort)(arg ?? 63);
                        para.Add(((char)code).ToString(), bold, italic, ul);
                        if (pos < rtf.Length && rtf[pos] != '\\' && rtf[pos] != '{' && rtf[pos] != '}') pos++; // \uc1: one fallback character follows
                        break;
                    }
                }
                continue;
            }
            if (ignore) { pos++; continue; }
            // plain text up to the next control character
            var run = pos;
            while (pos < rtf.Length && rtf[pos] is not ('\\' or '{' or '}' or '\r' or '\n')) pos++;
            para.Add(rtf[run..pos], bold, italic, ul);
        }
        EndPara();
        FlushTable();
        return lines;
    }

    // ------------------------------------------------------------------ helpers shared by the tests

    /// <summary>All visible words of an outline in order, without markers or list prefixes.</summary>
    public static string PlainText(IEnumerable<string> outline) => Regex.Replace(string.Join(" ", outline.Select(StripLine)), @"\s+", " ").Trim();

    private static string StripLine(string line)
    {
        var colon = line.IndexOf(':');
        var body = colon < 0 ? line : line[(colon + 1)..];
        if (line.StartsWith("T:", StringComparison.Ordinal)) body = body.Replace("[", " ").Replace("]", " ").Replace("|", " ").Replace(";", " ");
        return body.Replace("**", "").Replace("~", "").Replace("_", "");
    }
}
