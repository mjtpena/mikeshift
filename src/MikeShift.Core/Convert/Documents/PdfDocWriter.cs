using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using MikeShift.Core.Pdf;

namespace MikeShift.Core.Documents;

/// <summary>
/// PDF writer on PDFsharp: own line breaker, 54 pt margins, black text, bold larger headings, bullets and numbers,
/// simple ruled tables, hard page breaks, A4 (metric regions) or Letter. Right-to-left lines are reordered visually.
/// </summary>
public static class PdfDocWriter
{
    /// <summary>Longest document (characters) we lay out.</summary>
    public const long MaxChars = 2_000_000;

    /// <summary>Longest document (characters) we lay out when it contains right-to-left text (reordering is much slower).</summary>
    public const long MaxRtlChars = 400_000;

    private const double Margin = 54;
    private const double BodySize = 11;
    private static readonly double[] HeadingSizes = [22, 18, 15, 13, 12, 11];

    public static void Write(DocModel model, Stream output, bool a4, string title, CancellationToken ct)
    {
        var chars = model.CharCount();
        if (chars > MaxChars)
            throw new ConversionException("This document is too long to convert to PDF.");
        if (chars > MaxRtlChars && model.HasRtl())
            throw new ConversionException("This right-to-left document is too long to convert to PDF.");

        PdfFonts.EnsureInitialized();

        try
        {
            using var doc = new PdfDocument();
            doc.Info.Title = title;
            doc.Info.Creator = "MikeShift";
            var layout = new Layout(doc, a4, ct);
            try
            {
                layout.Run(model);
            }
            finally
            {
                layout.Dispose();
            }
            doc.Save(output, closeStream: false);
        }
        catch (Exception ex) when (ex is not (ConversionException or OperationCanceledException))
        {
            throw new ConversionException("The PDF couldn't be created: " + ex.Message, ex);
        }
    }

    // ------------------------------------------------------------ layout

    /// <summary>Which installed font a run of text is set in (Latin, Greek, Cyrillic, Hebrew, Arabic and neutrals use the default family).</summary>
    public enum Face { Default, Cjk, Hangul, Indic, Thai, Myanmar, Symbol, Emoji }

    private readonly record struct Style(bool Bold, bool Italic, bool Underline, double Size, Face Face = Face.Default);

    // Fallback families per script, in order of preference (PdfFonts knows the files).
    private static readonly Dictionary<Face, string[]> FaceCandidates = new()
    {
        [Face.Cjk] = ["Yu Gothic", "Microsoft YaHei", "SimSun", "MS Gothic"],
        [Face.Hangul] = ["Malgun Gothic"],
        [Face.Indic] = ["Nirmala UI"],
        [Face.Thai] = ["Leelawadee UI", "Tahoma"],
        [Face.Myanmar] = ["Myanmar Text"],
        [Face.Symbol] = ["Segoe UI Symbol"],
        [Face.Emoji] = ["Segoe UI Emoji", "Segoe UI Symbol"],
    };

    private static readonly Dictionary<Face, string[]> s_installedFaceFamilies = [];

    /// <summary>Installed fallback families for <paramref name="face"/> (empty if none of its fonts exist on this machine).</summary>
    private static string[] InstalledFamilies(Face face)
    {
        lock (s_installedFaceFamilies)
        {
            if (s_installedFaceFamilies.TryGetValue(face, out var cached)) return cached;
            var found = FaceCandidates.TryGetValue(face, out var list) ? list.Where(PdfFonts.IsInstalled).ToArray() : [];
            return s_installedFaceFamilies[face] = found;
        }
    }

    /// <summary>The font family that renders <paramref name="face"/> text, or null when no suitable font is installed.</summary>
    internal static string? FamilyFor(Face face) => face == Face.Default ? null : InstalledFamilies(face).FirstOrDefault();

    /// <summary>The script group (and so the font) a code point is set in.</summary>
    internal static Face FaceOf(int cp) => cp switch
    {
        < 0x0900 => Face.Default,
        <= 0x0DFF => Face.Indic,                                                   // Devanagari ... Sinhala
        >= 0x0E00 and <= 0x0EFF => Face.Thai,                                      // Thai, Lao
        >= 0x1000 and <= 0x109F => Face.Myanmar,
        >= 0x1100 and <= 0x11FF => Face.Hangul,
        >= 0x1780 and <= 0x17FF => Face.Thai,                                      // Khmer (Leelawadee UI)
        >= 0x2190 and <= 0x2BFF => Face.Symbol,                                    // arrows, math, technical, shapes, dingbats
        >= 0x2E80 and <= 0x2FDF => Face.Cjk,
        >= 0x3000 and <= 0x30FF => Face.Cjk,                                       // CJK punctuation, Hiragana, Katakana
        >= 0x3100 and <= 0x312F => Face.Cjk,
        >= 0x3130 and <= 0x318F => Face.Hangul,
        >= 0x31F0 and <= 0x31FF => Face.Cjk,
        >= 0x3400 and <= 0x4DBF => Face.Cjk,
        >= 0x4E00 and <= 0x9FFF => Face.Cjk,
        >= 0xA8E0 and <= 0xA8FF => Face.Indic,
        >= 0xA960 and <= 0xA97F => Face.Hangul,
        >= 0xAC00 and <= 0xD7FF => Face.Hangul,
        >= 0xF900 and <= 0xFAFF => Face.Cjk,
        >= 0xFE30 and <= 0xFE4F => Face.Cjk,
        >= 0xFF00 and <= 0xFFEF => Face.Cjk,                                       // full-width forms
        >= 0x1F000 and <= 0x1FAFF => Face.Emoji,
        >= 0x20000 and <= 0x3FFFF => Face.Cjk,
        _ => Face.Default,
    };

    private sealed record Atom(string Text, Style Style, bool IsSpace, bool IsBreak = false);

    private sealed class Line
    {
        public List<(string Text, Style Style)> Segments { get; } = [];
        public double Width;
        public double Height;
    }

    private sealed class Layout : IDisposable
    {
        private static string? s_family;

        private readonly PdfDocument _doc;
        private readonly bool _a4;
        private readonly CancellationToken _ct;
        private PdfPage _page;
        private XGraphics _gfx;
        private double _y;
        private bool _pageUsed;
        private bool _forceNewPage;
        private readonly Dictionary<(bool, bool, double, Face), XFont> _fonts = [];
        private readonly Dictionary<(XFont, string), double> _widths = [];
        private readonly Dictionary<XFont, double> _spaces = [];
        private readonly XPen _pen = new(XColors.Black, 0.5);

        public Layout(PdfDocument doc, bool a4, CancellationToken ct)
        {
            _doc = doc;
            _a4 = a4;
            _ct = ct;
            _page = NewPdfPage();
            _gfx = XGraphics.FromPdfPage(_page);
            _y = Margin;
        }

        public void Dispose() => _gfx.Dispose();

        private double PageWidth => _page.Width.Point;
        private double PageHeight => _page.Height.Point;
        private double Bottom => PageHeight - Margin;
        private double ContentWidth => PageWidth - 2 * Margin;

        private PdfPage NewPdfPage()
        {
            var page = _doc.AddPage();
            page.Size = _a4 ? PdfSharp.PageSize.A4 : PdfSharp.PageSize.Letter;
            return page;
        }

        private void NewPage()
        {
            _gfx.Dispose();
            _page = NewPdfPage();
            _gfx = XGraphics.FromPdfPage(_page);
            _y = Margin;
            _pageUsed = false;
            _forceNewPage = false;
        }

        private void Ensure(double height)
        {
            if (_forceNewPage && _pageUsed) NewPage();
            _forceNewPage = false;
            if (_y + height > Bottom && _pageUsed) NewPage();
        }

        // ------------------------------------------------------------ fonts and measuring

        private static string Family()
        {
            if (s_family is not null) return s_family;
            foreach (var name in new[] { "Arial", "Segoe UI", "Tahoma", "Verdana" })
            {
                try
                {
                    var f = new XFont(name, 10, XFontStyleEx.Regular);
                    using var probe = XGraphics.CreateMeasureContext(new XSize(100, 100), XGraphicsUnit.Point, XPageDirection.Downwards);
                    _ = probe.MeasureString("Ag", f);
                    return s_family = name;
                }
                catch (Exception)
                {
                    // try the next family
                }
            }
            return s_family = "Arial";
        }

        private XFont Font(Style s)
        {
            var key = (s.Bold, s.Italic, s.Size, s.Face);
            if (_fonts.TryGetValue(key, out var f)) return f;
            var style = (s.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular) | (s.Italic ? XFontStyleEx.Italic : XFontStyleEx.Regular);
            f = FallbackFont(s.Face, s.Size, style) ?? new XFont(Family(), s.Size, style);
            _fonts[key] = f;
            return f;
        }

        /// <summary>The first installed, loadable font for a non-default script (bold/italic dropped if that font lacks them); null if none.</summary>
        private static XFont? FallbackFont(Face face, double size, XFontStyleEx style)
        {
            if (face == Face.Default) return null;
            foreach (var family in InstalledFamilies(face))
            {
                foreach (var st in style == XFontStyleEx.Regular ? new[] { style } : new[] { style, XFontStyleEx.Regular })
                {
                    try
                    {
                        var f = new XFont(family, size, st);
                        using var probe = XGraphics.CreateMeasureContext(new XSize(100, 100), XGraphicsUnit.Point, XPageDirection.Downwards);
                        _ = probe.MeasureString("Ag", f);
                        return f;
                    }
                    catch (Exception)
                    {
                        // try the regular style, then the next family
                    }
                }
            }
            return null;
        }

        private double Measure(string text, Style s)
        {
            if (text.Length == 0) return 0;
            var font = Font(s);
            if (_widths.TryGetValue((font, text), out var w)) return w;
            w = _gfx.MeasureString(text, font).Width;
            if (_widths.Count > 50_000) _widths.Clear();
            _widths[(font, text)] = w;
            return w;
        }

        private double SpaceWidth(Style s)
        {
            var font = Font(s);
            if (_spaces.TryGetValue(font, out var w)) return w;
            w = _gfx.MeasureString("x x", font).Width - _gfx.MeasureString("xx", font).Width;
            if (w <= 0) w = s.Size * 0.28;
            _spaces[font] = w;
            return w;
        }

        private double AtomWidth(Atom a) => a.IsSpace ? SpaceWidth(a.Style) * a.Text.Length : Measure(a.Text, a.Style);

        private double LineHeight(Style s) => Font(s).GetHeight() * 1.05;

        // ------------------------------------------------------------ blocks

        public void Run(DocModel model)
        {
            var counter = new TextUtil.ListCounter();
            var count = 0;
            foreach (var block in model.Blocks)
            {
                if ((++count & 63) == 0) _ct.ThrowIfCancellationRequested();
                switch (block)
                {
                    case DocParagraph { Kind: ParagraphKind.ListItem } li:
                        WriteParagraph(li, counter.Next(li));
                        continue;
                    case DocParagraph p:
                        WriteParagraph(p, null);
                        break;
                    case DocTable t:
                        WriteTable(t);
                        break;
                    case DocPageBreak:
                        _forceNewPage = true;
                        break;
                }
                counter.Reset();
            }
        }

        private void WriteParagraph(DocParagraph p, string? label)
        {
            var heading = p.Kind == ParagraphKind.Heading;
            var size = heading ? HeadingSizes[Math.Clamp(p.Level, 1, 6) - 1] : BodySize;
            var indent = p.Kind == ParagraphKind.ListItem ? 18.0 * (Math.Clamp(p.Level, 0, 8) + 1) : 0;
            var rtl = p.EffectiveRtl;
            var width = ContentWidth - indent;

            if (heading && _pageUsed) _y += size * 0.5;

            var atoms = Atomise(p, size, heading);
            var lines = Wrap(atoms, width, size, heading);

            var first = true;
            foreach (var line in lines)
            {
                // Keep a heading with at least its first line; otherwise only break pages between lines.
                Ensure(line.Height + (heading && first ? line.Height : 0));
                var left = Margin + (rtl ? 0 : indent);
                var right = PageWidth - Margin - (rtl ? indent : 0);
                if (first && label is not null) DrawLabel(label, left, right, rtl, size);
                DrawLine(line, left, right, rtl);
                _y += line.Height;
                _pageUsed = true;
                first = false;
            }
            _y += heading ? 4 : p.Kind == ParagraphKind.ListItem ? 2 : 6;
        }

        private void DrawLabel(string label, double left, double right, bool rtl, double size)
        {
            var style = new Style(false, false, false, size);
            var w = Measure(label, style);
            var x = rtl ? right + 6 : left - w - 6;
            _gfx.DrawString(label, Font(style), XBrushes.Black, new XPoint(x, _y), XStringFormats.TopLeft);
        }

        private static List<Atom> Atomise(DocParagraph p, double size, bool heading)
        {
            var atoms = new List<Atom>();
            foreach (var run in p.Runs)
            {
                var style = new Style(run.Bold || heading, run.Italic && !heading, run.Underline, size);
                var text = Clean(run.Text);
                var sb = new StringBuilder();
                var spaces = 0;
                var face = Face.Default;
                // Words are split wherever the script (and so the font) changes, so each atom is measured and drawn in one font.
                void FlushWord() { if (sb.Length > 0) { atoms.Add(new Atom(sb.ToString(), style with { Face = face }, false)); sb.Clear(); } }
                void FlushSpaces() { if (spaces > 0) { atoms.Add(new Atom(new string(' ', spaces), style, true)); spaces = 0; } }
                foreach (var rune in text.EnumerateRunes())
                {
                    switch (rune.Value)
                    {
                        case '\n':
                            FlushWord(); FlushSpaces();
                            atoms.Add(new Atom("", style, false, IsBreak: true));
                            break;
                        case '\t':
                            FlushWord(); spaces += 4;
                            break;
                        case ' ':
                            FlushWord(); spaces++;
                            break;
                        default:
                            FlushSpaces();
                            var f = FaceOf(rune.Value);
                            if (f != Face.Default && FamilyFor(f) is null) f = Face.Default; // no font installed for it: stay in the default font
                            if (f != face) { FlushWord(); face = f; }
                            sb.Append(rune.ToString());
                            break;
                    }
                }
                FlushWord(); FlushSpaces();
            }
            return atoms;
        }

        private static string Clean(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\r': break;
                    case ' ': sb.Append(' '); break;
                    case '‑': sb.Append('-'); break;
                    case '​' or '‌' or '‍' or '﻿': break;
                    case >= '\uFE00' and <= '\uFE0F': break; // variation selectors would render as boxes in the symbol fonts
                    case < ' ' when ch is not ('\n' or '\t'): break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Greedy line breaking in logical order; over-long words are split by character.</summary>
        private List<Line> Wrap(List<Atom> atoms, double width, double size, bool heading)
        {
            var lines = new List<Line>();
            var line = new Line();
            var defaultStyle = new Style(heading, false, false, size);

            void Emit(string text, Style style, double w)
            {
                var segs = line.Segments;
                if (segs.Count > 0 && segs[^1].Style == style) segs[^1] = (segs[^1].Text + text, style);
                else segs.Add((text, style));
                line.Width += w;
                line.Height = Math.Max(line.Height, LineHeight(style));
            }

            void EndLine()
            {
                // Trailing spaces never count toward the width.
                while (line.Segments.Count > 0 && line.Segments[^1].Text.All(c => c == ' '))
                {
                    var (t, s) = line.Segments[^1];
                    line.Width -= SpaceWidth(s) * t.Length;
                    line.Segments.RemoveAt(line.Segments.Count - 1);
                }
                if (line.Segments.Count > 0)
                {
                    var (t, s) = line.Segments[^1];
                    var trimmed = t.TrimEnd(' ');
                    if (trimmed.Length != t.Length)
                    {
                        line.Width -= SpaceWidth(s) * (t.Length - trimmed.Length);
                        line.Segments[^1] = (trimmed, s);
                    }
                }
                if (line.Height == 0) line.Height = LineHeight(defaultStyle);
                lines.Add(line);
                line = new Line();
            }

            foreach (var atom in atoms)
            {
                if (atom.IsBreak)
                {
                    if (line.Height == 0) line.Height = LineHeight(atom.Style);
                    EndLine();
                    continue;
                }

                var w = AtomWidth(atom);
                if (atom.IsSpace)
                {
                    if (line.Segments.Count == 0) continue; // no leading spaces on wrapped lines
                    if (line.Width + w > width) { EndLine(); continue; }
                    Emit(atom.Text, atom.Style, w);
                    continue;
                }

                if (line.Width + w <= width || (line.Segments.Count == 0 && w <= width))
                {
                    Emit(atom.Text, atom.Style, w);
                    continue;
                }

                if (w <= width)
                {
                    EndLine();
                    Emit(atom.Text, atom.Style, w);
                    continue;
                }

                // A single word wider than the whole line: break it by characters.
                if (line.Segments.Count > 0) EndLine();
                var rest = atom.Text.AsSpan();
                while (rest.Length > 0)
                {
                    var take = 1;
                    while (take < rest.Length && Measure(rest[..(take + 1)].ToString(), atom.Style) <= width - line.Width) take++;
                    if (take < rest.Length && char.IsLowSurrogate(rest[take])) take++; // never split a surrogate pair
                    var piece = rest[..take].ToString();
                    Emit(piece, atom.Style, Measure(piece, atom.Style));
                    rest = rest[take..];
                    if (rest.Length > 0) EndLine();
                }
            }

            if (line.Segments.Count > 0 || lines.Count == 0 || atoms.Count == 0) EndLine();
            return lines;
        }

        // ------------------------------------------------------------ drawing

        private void DrawLine(Line line, double left, double right, bool rtl)
        {
            if (line.Segments.Count == 0) return;
            if (!rtl)
            {
                var x = left;
                foreach (var (text, style) in line.Segments)
                    x += DrawSegment(text, style, x, _y, line.Height);
                return;
            }

            // Right-to-left: reorder the whole line visually, then draw left to right starting at (right - width).
            var all = new StringBuilder();
            var styles = new List<Style>();
            foreach (var (text, style) in line.Segments)
            {
                all.Append(text);
                for (var i = 0; i < text.Length; i++) styles.Add(style);
            }
            var order = Bidi.VisualOrder(all.ToString());
            var visual = new List<(string Text, Style Style)>();
            var sb = new StringBuilder();
            Style? cur = null;
            foreach (var (index, reversed) in order)
            {
                var st = styles[index];
                if (cur is { } c && c != st)
                {
                    visual.Add((sb.ToString(), c));
                    sb.Clear();
                }
                cur = st;
                var ch = all[index];
                sb.Append(reversed ? Bidi.Mirror(ch) : ch);
            }
            if (sb.Length > 0 && cur is { } last) visual.Add((sb.ToString(), last));

            var total = visual.Sum(v => Measure(v.Text, v.Style));
            var px = right - total;
            foreach (var (text, style) in visual)
                px += DrawSegment(text, style, px, _y, line.Height);
        }

        private double DrawSegment(string text, Style style, double x, double y, double lineHeight)
        {
            if (text.Length == 0) return 0;
            var font = Font(style);
            var w = Measure(text, style);
            if (text.Any(c => !char.IsWhiteSpace(c)))
                _gfx.DrawString(text, font, XBrushes.Black, new XPoint(x, y), XStringFormats.TopLeft);
            if (style.Underline)
            {
                var uy = y + lineHeight * 0.88;
                _gfx.DrawLine(_pen, x, uy, x + w, uy);
            }
            return w;
        }

        // ------------------------------------------------------------ tables

        private void WriteTable(DocTable t)
        {
            var cols = Math.Max(1, t.ColumnCount);
            const double pad = 4;
            var colWidth = ContentWidth / cols;
            var bodyStyle = new Style(false, false, false, BodySize);
            var lh = LineHeight(bodyStyle);

            if (_pageUsed) _y += 4;
            foreach (var row in t.Rows)
            {
                _ct.ThrowIfCancellationRequested();
                var cells = new List<List<(Line Line, bool Rtl)>>();
                for (var c = 0; c < cols; c++)
                {
                    var lines = new List<(Line, bool)>();
                    var paragraphs = c < row.Count ? row[c] : [];
                    foreach (var p in paragraphs)
                    {
                        var rtl = p.EffectiveRtl;
                        foreach (var l in Wrap(Atomise(p, BodySize, false), colWidth - 2 * pad, BodySize, false))
                        {
                            l.Height = lh;
                            lines.Add((l, rtl));
                        }
                    }
                    cells.Add(lines);
                }

                var maxLines = Math.Max(1, cells.Max(c => c.Count));
                var offset = 0;
                while (offset < maxLines)
                {
                    if (_forceNewPage && _pageUsed) NewPage();
                    _forceNewPage = false;
                    var avail = (int)Math.Floor((Bottom - _y - 2 * pad) / lh);
                    if (avail < 1)
                    {
                        if (_pageUsed) { NewPage(); continue; }
                        avail = 1;
                    }
                    var n = Math.Min(avail, maxLines - offset);
                    // Keep short rows whole: if the whole row fits on a fresh page but not here, move it.
                    if (offset == 0 && n < maxLines && _pageUsed && maxLines <= (int)Math.Floor((PageHeight - 2 * Margin - 2 * pad) / lh))
                    {
                        NewPage();
                        continue;
                    }

                    var h = n * lh + 2 * pad;
                    for (var c = 0; c < cols; c++)
                    {
                        var x = Margin + c * colWidth;
                        _gfx.DrawRectangle(_pen, new XRect(x, _y, colWidth, h));
                        var lines = cells[c];
                        for (var k = 0; k < n && offset + k < lines.Count; k++)
                        {
                            var (line, rtl) = lines[offset + k];
                            var saveY = _y;
                            _y = saveY + pad + k * lh;
                            DrawLine(line, x + pad, x + colWidth - pad, rtl);
                            _y = saveY;
                        }
                    }
                    _y += h;
                    _pageUsed = true;
                    offset += n;
                    if (offset < maxLines) NewPage();
                }
            }
            _y += 8;
        }
    }
}
