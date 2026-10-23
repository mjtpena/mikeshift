using System.IO.Compression;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace MikeShift.Core.Documents;

/// <summary>DOCX reader (Open XML SDK): headings from styles or outline level, numbering to list items, tables, pictures flagged.</summary>
public static partial class DocxReader
{
    public static DocModel Read(string path)
    {
        if (!DocReaders.StartsWithPk(path))
            throw new ConversionException("This isn't a valid DOCX file (it may be password-protected or damaged).");

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var model = Read(fs);
            if (!model.HasPictures && PictureScan.ZipHasPictures(path, docx: true))
                model.HasPictures = true;
            return model;
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or FileFormatException
                                       or System.Xml.XmlException or InvalidOperationException or ArgumentException)
        {
            throw new ConversionException("This DOCX file is damaged and can't be read.", ex);
        }
    }

    public static DocModel Read(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var main = doc.MainDocumentPart ?? throw new ConversionException("This DOCX file has no document body.");
        var body = main.Document?.Body ?? throw new ConversionException("This DOCX file has no document body.");
        var ctx = new Context(main);
        ctx.ReadBlocks(body.ChildElements, ctx.Model.Blocks);
        if (main.ImageParts.Any()) ctx.Model.HasPictures = true;
        return ctx.Model;
    }

    private sealed class StyleInfo
    {
        public string? Name;
        public string? BasedOn;
        public int? OutlineLevel;
        public (int NumId, int Level)? Numbering;
        /// <summary>Character formatting this style sets itself; null = not set here, so the style it is based on decides.</summary>
        public bool? Bold, Italic, Underline;
    }

    private sealed class Context
    {
        private readonly MainDocumentPart _main;
        private readonly Dictionary<string, StyleInfo> _styles = new(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _numToAbstract = [];
        private readonly Dictionary<(int Abstract, int Level), bool> _orderedLevels = [];

        public DocModel Model { get; } = new();

        public Context(MainDocumentPart main)
        {
            _main = main;
            LoadStyles();
            LoadNumbering();
        }

        private void LoadStyles()
        {
            var styles = _main.StyleDefinitionsPart?.Styles;
            if (styles is null) return;
            foreach (var s in styles.Elements<W.Style>())
            {
                var id = s.StyleId?.Value;
                if (id is null) continue;
                var info = new StyleInfo { Name = s.StyleName?.Val?.Value, BasedOn = s.BasedOn?.Val?.Value };
                var pp = s.StyleParagraphProperties;
                if (pp?.OutlineLevel?.Val?.Value is { } ol) info.OutlineLevel = ol;
                if (pp?.NumberingProperties is { } np && np.NumberingId?.Val?.Value is { } nid)
                    info.Numbering = (nid, np.NumberingLevelReference?.Val?.Value ?? 0);
                var rp = s.StyleRunProperties;
                if (rp is not null)
                {
                    if (rp.Bold is not null) info.Bold = IsOn(rp.Bold);
                    if (rp.Italic is not null) info.Italic = IsOn(rp.Italic);
                    if (rp.Underline is { } u) info.Underline = u.Val?.Value != W.UnderlineValues.None;
                }
                _styles[id] = info;
            }
        }

        private void LoadNumbering()
        {
            var numbering = _main.NumberingDefinitionsPart?.Numbering;
            if (numbering is null) return;
            foreach (var abs in numbering.Elements<W.AbstractNum>())
            {
                var absId = abs.AbstractNumberId?.Value;
                if (absId is null) continue;
                foreach (var lvl in abs.Elements<W.Level>())
                {
                    var idx = lvl.LevelIndex?.Value ?? 0;
                    var fmt = lvl.NumberingFormat?.Val?.Value;
                    var ordered = fmt is { } f && f != W.NumberFormatValues.Bullet && f != W.NumberFormatValues.None;
                    _orderedLevels[(absId.Value, idx)] = ordered;
                }
            }
            foreach (var num in numbering.Elements<W.NumberingInstance>())
            {
                if (num.NumberID?.Value is { } nid && num.AbstractNumId?.Val?.Value is { } aid)
                    _numToAbstract[nid] = aid;
            }
        }

        private static bool IsOn(OpenXmlElement? e) => e is W.OnOffType t ? t.Val?.Value ?? true : e is not null;

        private StyleInfo? Style(string? id) => id is not null && _styles.TryGetValue(id, out var s) ? s : null;

        /// <summary>Walks the style chain (a style, then its BasedOn ancestors).</summary>
        private IEnumerable<StyleInfo> Chain(string? id)
        {
            for (var depth = 0; id is not null && depth < 12; depth++)
            {
                if (!_styles.TryGetValue(id, out var s)) yield break;
                yield return s;
                id = s.BasedOn;
            }
        }

        /// <summary>
        /// A character property resolved along the style chain: the most-derived style that sets it wins, so <c>&lt;w:b w:val="0"/&gt;</c> or
        /// <c>&lt;w:u w:val="none"/&gt;</c> in a derived style cancels what the style it is based on turned on. Null if no style in the chain sets it.
        /// </summary>
        private bool? Resolve(string? styleId, Func<StyleInfo, bool?> property)
        {
            foreach (var s in Chain(styleId))
                if (property(s) is { } value) return value;
            return null;
        }

        private int? HeadingLevel(string? styleId, W.ParagraphProperties? pp)
        {
            foreach (var s in Chain(styleId))
            {
                var m = HeadingName().Match(s.Name ?? "");
                if (m.Success) return Math.Clamp(int.Parse(m.Groups[1].Value), 1, 6);
                if (string.Equals(s.Name, "Title", StringComparison.OrdinalIgnoreCase)) return 1;
            }
            if (styleId is not null)
            {
                var m = HeadingId().Match(styleId);
                if (m.Success) return Math.Clamp(int.Parse(m.Groups[1].Value), 1, 6);
            }
            if (pp?.OutlineLevel?.Val?.Value is { } ol && ol is >= 0 and <= 5) return ol + 1;
            foreach (var s in Chain(styleId))
                if (s.OutlineLevel is { } sl && sl is >= 0 and <= 5) return sl + 1;
            return null;
        }

        private (int NumId, int Level)? ListInfo(string? styleId, W.ParagraphProperties? pp)
        {
            if (pp?.NumberingProperties is { } np)
            {
                var nid = np.NumberingId?.Val?.Value;
                if (nid is null) return null;
                if (nid == 0) return null; // numId 0 explicitly removes numbering
                return (nid.Value, np.NumberingLevelReference?.Val?.Value ?? 0);
            }
            foreach (var s in Chain(styleId))
                if (s.Numbering is { } n && n.NumId != 0) return n;
            return null;
        }

        private bool IsOrdered(int numId, int level)
        {
            if (!_numToAbstract.TryGetValue(numId, out var abs)) return false;
            return _orderedLevels.TryGetValue((abs, level), out var o) && o;
        }

        // ------------------------------------------------------------ blocks

        public void ReadBlocks(IEnumerable<OpenXmlElement> elements, List<DocBlock> into)
        {
            foreach (var e in elements)
            {
                switch (e)
                {
                    case W.Paragraph p:
                        ReadParagraph(p, into);
                        break;
                    case W.Table t:
                        into.Add(ReadTable(t));
                        break;
                    case W.SdtBlock sdt:
                        if (sdt.SdtContentBlock is { } content) ReadBlocks(content.ChildElements, into);
                        break;
                    case W.CustomXmlBlock cx:
                        ReadBlocks(cx.ChildElements, into);
                        break;
                }
            }
        }

        private DocTable ReadTable(W.Table table)
        {
            var result = new DocTable();
            foreach (var row in table.Elements<W.TableRow>())
            {
                var cells = new List<List<DocParagraph>>();
                foreach (var cell in row.Elements<W.TableCell>())
                {
                    var blocks = new List<DocBlock>();
                    ReadBlocks(cell.ChildElements, blocks);
                    cells.Add(FlattenCell(blocks));
                }
                result.Rows.Add(cells);
            }
            return result;
        }

        private static List<DocParagraph> FlattenCell(List<DocBlock> blocks)
        {
            var paragraphs = new List<DocParagraph>();
            foreach (var b in blocks)
            {
                switch (b)
                {
                    case DocParagraph p:
                        paragraphs.Add(p.ForCell());
                        break;
                    case DocTable nested:
                        foreach (var row in nested.Rows)
                            foreach (var cell in row)
                                paragraphs.AddRange(cell);
                        break;
                }
            }
            if (paragraphs.Count == 0) paragraphs.Add(new DocParagraph());
            return paragraphs;
        }

        private void ReadParagraph(W.Paragraph p, List<DocBlock> into)
        {
            var pp = p.ParagraphProperties;
            var styleId = pp?.ParagraphStyleId?.Val?.Value;

            DocParagraph NewParagraph()
            {
                var para = new DocParagraph { Rtl = pp?.BiDi is { } b && IsOn(b) };
                if (HeadingLevel(styleId, pp) is { } level)
                {
                    para.Kind = ParagraphKind.Heading;
                    para.Level = level;
                }
                else if (ListInfo(styleId, pp) is { } list)
                {
                    para.Kind = ParagraphKind.ListItem;
                    para.Level = Math.Clamp(list.Level, 0, 8);
                    para.Ordered = IsOrdered(list.NumId, list.Level);
                }
                return para;
            }

            if (pp?.PageBreakBefore is { } pb && IsOn(pb))
                into.Add(new DocPageBreak());

            var chainBold = Resolve(styleId, s => s.Bold) ?? false;
            var chainItalic = Resolve(styleId, s => s.Italic) ?? false;
            var chainUnderline = Resolve(styleId, s => s.Underline) ?? false;
            var current = NewParagraph();
            var emittedAny = false;

            void Flush(bool force)
            {
                if (current.Runs.Count > 0 || force)
                {
                    into.Add(current);
                    emittedAny = true;
                }
                current = NewParagraph();
            }

            var heading = current.Kind == ParagraphKind.Heading;
            WalkInline(p.ChildElements, new Fmt(heading ? false : chainBold, heading ? false : chainItalic, chainUnderline, null), current: () => current,
                onPageBreak: () =>
                {
                    Flush(force: false);
                    into.Add(new DocPageBreak());
                    emittedAny = true;
                });

            // Keep empty paragraphs (blank lines) unless they only existed to hold a page break.
            if (current.Runs.Count > 0 || !emittedAny)
                into.Add(current);
        }

        private readonly record struct Fmt(bool Bold, bool Italic, bool Underline, string? Link);

        private void WalkInline(IEnumerable<OpenXmlElement> children, Fmt fmt, Func<DocParagraph> current, Action onPageBreak)
        {
            foreach (var child in children)
            {
                switch (child)
                {
                    case W.Run run:
                        ReadRun(run, fmt, current, onPageBreak);
                        break;
                    case W.Hyperlink link:
                        WalkInline(link.ChildElements, fmt with { Link = ResolveLink(link) ?? fmt.Link }, current, onPageBreak);
                        break;
                    case W.DeletedRun or W.MoveFromRun or W.ParagraphProperties or W.BookmarkStart or W.BookmarkEnd:
                        break;
                    case W.SimpleField field:
                        WalkInline(field.ChildElements, fmt, current, onPageBreak);
                        break;
                    case W.SdtRun sdt:
                        if (sdt.SdtContentRun is { } c) WalkInline(c.ChildElements, fmt, current, onPageBreak);
                        break;
                    case OpenXmlCompositeElement composite when composite is W.InsertedRun or W.MoveToRun
                        or W.CustomXmlRun:
                        WalkInline(composite.ChildElements, fmt, current, onPageBreak);
                        break;
                    default:
                        if (ContainsPicture(child)) Model.HasPictures = true;
                        break;
                }
            }
        }

        private static bool ContainsPicture(OpenXmlElement e) =>
            e is W.Drawing or W.Picture or W.EmbeddedObject
            || e.Descendants().Any(d => d is W.Drawing or W.Picture or W.EmbeddedObject);

        private string? ResolveLink(W.Hyperlink link)
        {
            var id = link.Id?.Value;
            if (id is null) return null;
            var rel = _main.HyperlinkRelationships.FirstOrDefault(r => r.Id == id);
            return rel?.Uri.OriginalString;
        }

        private void ReadRun(W.Run run, Fmt fmt, Func<DocParagraph> current, Action onPageBreak)
        {
            var rp = run.RunProperties;
            var bold = fmt.Bold;
            var italic = fmt.Italic;
            var underline = fmt.Underline;
            if (rp is not null)
            {
                var rstyle = rp.RunStyle?.Val?.Value;
                bold = Resolve(rstyle, s => s.Bold) ?? bold;
                italic = Resolve(rstyle, s => s.Italic) ?? italic;
                underline = Resolve(rstyle, s => s.Underline) ?? underline;
                if (rp.Bold is not null) bold = IsOn(rp.Bold);
                if (rp.Italic is not null) italic = IsOn(rp.Italic);
                if (rp.Underline is { } u) underline = u.Val?.Value != W.UnderlineValues.None;
            }
            if (current().Kind == ParagraphKind.Heading) { bold = false; italic = false; }

            void Append(string text) => current().Add(text, bold, italic, underline, fmt.Link);

            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case W.Text t:
                        Append(t.Text);
                        break;
                    case W.TabChar:
                        Append("\t");
                        break;
                    case W.CarriageReturn:
                        Append("\n");
                        break;
                    case W.NoBreakHyphen:
                        Append("-");
                        break;
                    case W.Break br:
                        if (br.Type?.Value == W.BreakValues.Page) onPageBreak();
                        else if (br.Type?.Value == W.BreakValues.Column) { /* ignore */ }
                        else Append("\n");
                        break;
                    case W.Drawing or W.Picture or W.EmbeddedObject:
                        Model.HasPictures = true;
                        break;
                    case AlternateContent:
                        if (child.Descendants().Any(d => d is W.Drawing or W.Picture)) Model.HasPictures = true;
                        break;
                }
            }
        }
    }

    [GeneratedRegex(@"^\s*heading\s*([1-9])\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingName();

    [GeneratedRegex(@"^Heading([1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingId();
}

/// <summary>Fast "does this file embed pictures" scans used by the probe and by the readers.</summary>
public static class PictureScan
{
    /// <summary>True if the zip package (DOCX or ODT) has picture/embedded-object entries. Reads only the central directory.</summary>
    public static bool ZipHasPictures(string path, bool docx)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        foreach (var e in zip.Entries)
        {
            var n = e.FullName;
            if (n.EndsWith('/')) continue;
            if (docx ? n.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase) || n.StartsWith("word/embeddings/", StringComparison.OrdinalIgnoreCase)
                     : n.StartsWith("Pictures/", StringComparison.OrdinalIgnoreCase) || n.StartsWith("ObjectReplacements/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Scans an RTF file for <c>\pict</c> / <c>\objemb</c> / <c>\objdata</c>. Files above the cap are assumed to contain pictures.</summary>
    public static bool RtfHasPictures(string path) =>
        ScanFile(path, 32 * 1024 * 1024, ["\\pict"u8.ToArray(), "\\objdata"u8.ToArray(), "\\objemb"u8.ToArray()], ignoreCase: false, assumeOnOverflow: true);

    /// <summary>Scans an HTML file for <c>&lt;img</c>, <c>&lt;svg</c>, <c>&lt;picture</c>, <c>&lt;object</c>, <c>&lt;embed</c>, <c>&lt;canvas</c>.</summary>
    public static bool HtmlHasPictures(string path) =>
        ScanFile(path, 16 * 1024 * 1024, ["<img"u8.ToArray(), "<svg"u8.ToArray(), "<picture"u8.ToArray(), "<object"u8.ToArray(), "<embed"u8.ToArray(), "<canvas"u8.ToArray()],
            ignoreCase: true, assumeOnOverflow: false);

    private static bool ScanFile(string path, long cap, byte[][] needles, bool ignoreCase, bool assumeOnOverflow)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        var maxNeedle = needles.Max(n => n.Length);
        var buffer = new byte[(1 << 16) + maxNeedle];
        var carry = 0;
        long total = 0;
        while (true)
        {
            var read = fs.Read(buffer, carry, 1 << 16);
            if (read <= 0) return false;
            total += read;
            var span = buffer.AsSpan(0, carry + read);
            if (ignoreCase)
                for (var i = 0; i < span.Length; i++)
                    if (span[i] is >= (byte)'A' and <= (byte)'Z') span[i] = (byte)(span[i] + 32);
            foreach (var n in needles)
                if (span.IndexOf(n) >= 0) return true;
            if (total >= cap) return assumeOnOverflow && fs.Length > cap;
            carry = Math.Min(maxNeedle - 1, span.Length);
            span[^carry..].CopyTo(buffer);
        }
    }
}
