using System.Net;
using System.Text;

namespace MikeShift.Core.Documents;

/// <summary>
/// Tolerant HTML mini parser (PLAN §3.4): p, div, h1-h6, ul/ol/li, b/strong, i/em, u, br, a (text and href kept, never fetched),
/// table/tr/td/th, pre, <c>img</c>-like tags flag <see cref="DocModel.HasPictures"/>. Entities are decoded; script/style/title are
/// ignored. Nothing is ever downloaded or executed.
/// </summary>
public static class HtmlReader
{
    public static DocModel Read(string html) => new Parser(html).Parse();

    private static readonly HashSet<string> BlockTags = new(StringComparer.Ordinal)
    {
        "p", "div", "section", "article", "header", "footer", "main", "aside", "nav", "blockquote", "figure", "figcaption",
        "address", "form", "fieldset", "center", "dd", "dt", "dl", "caption", "details", "summary",
    };

    private static readonly HashSet<string> IgnoredContent = new(StringComparer.Ordinal)
    {
        "script", "style", "title", "noscript", "template", "svg", "textarea", "select", "iframe", "math",
    };

    private static readonly HashSet<string> PictureTags = new(StringComparer.Ordinal)
    {
        "img", "svg", "picture", "object", "embed", "canvas",
    };

    private sealed class Parser(string html)
    {
        private int _i;
        private readonly DocModel _model = new();

        private DocParagraph? _cur;
        private int _bold, _italic, _underline, _pre;
        private bool _keepWs, _nextKeepWs; // current / next block is white-space:pre or pre-wrap: spaces, tabs and blank lines are content
        private bool _inTh;
        private readonly List<string?> _links = [];
        private readonly List<bool> _lists = [];
        private readonly List<(string Tag, bool Rtl)> _dirs = [];

        private int _tableDepth;
        private DocTable? _table;
        private List<List<DocParagraph>>? _row;
        private List<DocParagraph>? _cell;

        public DocModel Parse()
        {
            var n = html.Length;
            while (_i < n)
            {
                var lt = html.IndexOf('<', _i);
                if (lt < 0)
                {
                    Text(html[_i..]);
                    _i = n;
                    break;
                }
                if (lt > _i) Text(html[_i..lt]);
                _i = lt;
                Tag();
            }
            FlushParagraph();
            if (_tableDepth > 0) EndTable(force: true);
            return _model;
        }

        // ------------------------------------------------------------ tokens

        private void Tag()
        {
            var n = html.Length;
            if (_i + 1 >= n) { Text("<"); _i = n; return; }
            var next = html[_i + 1];

            if (next == '!')
            {
                if (string.CompareOrdinal(html, _i, "<!--", 0, 4) == 0)
                {
                    var end = html.IndexOf("-->", _i + 4, StringComparison.Ordinal);
                    _i = end < 0 ? n : end + 3;
                }
                else
                {
                    var end = html.IndexOf('>', _i);
                    _i = end < 0 ? n : end + 1;
                }
                return;
            }
            if (next == '?')
            {
                var end = html.IndexOf('>', _i);
                _i = end < 0 ? n : end + 1;
                return;
            }

            var closing = next == '/';
            var nameStart = _i + (closing ? 2 : 1);
            if (nameStart >= n || !char.IsAsciiLetter(html[nameStart]))
            {
                Text("<");
                _i++;
                return;
            }

            var p = nameStart;
            while (p < n && !char.IsWhiteSpace(html[p]) && html[p] != '>' && html[p] != '/') p++;
            var name = html[nameStart..p].ToLowerInvariant();

            var attrs = closing ? null : new Dictionary<string, string>(StringComparer.Ordinal);
            var selfClosing = false;
            // Scan to the closing '>' honouring quotes, collecting attributes.
            while (p < n)
            {
                var ch = html[p];
                if (ch == '>') { p++; break; }
                if (char.IsWhiteSpace(ch)) { p++; continue; }
                if (ch == '/') { selfClosing = p + 1 < n && html[p + 1] == '>'; p++; continue; }

                var aStart = p;
                var tokenStart = p;
                while (p < n && !char.IsWhiteSpace(html[p]) && html[p] != '=' && html[p] != '>' && html[p] != '/') p++;
                var aName = html[aStart..p].ToLowerInvariant();
                while (p < n && char.IsWhiteSpace(html[p])) p++;
                var value = "";
                if (p < n && html[p] == '=')
                {
                    p++;
                    while (p < n && char.IsWhiteSpace(html[p])) p++;
                    if (p < n && html[p] is '"' or '\'')
                    {
                        var q = html[p];
                        var close = html.IndexOf(q, p + 1);
                        if (close < 0) close = n;
                        value = html[(p + 1)..close];
                        p = Math.Min(n, close + 1);
                    }
                    else
                    {
                        var vStart = p;
                        while (p < n && !char.IsWhiteSpace(html[p]) && html[p] != '>') p++;
                        value = html[vStart..p];
                    }
                }
                if (aName.Length > 0 && attrs is not null) attrs[aName] = value;
                if (p == tokenStart) p++; // always make progress (cannot normally happen: a lone '=' is consumed above)
            }
            _i = p;

            if (closing) EndTag(name);
            else StartTag(name, attrs!, selfClosing);
        }

        // ------------------------------------------------------------ tags

        private static bool IsRtlAttr(IReadOnlyDictionary<string, string> attrs, out bool has)
        {
            has = attrs.TryGetValue("dir", out var d);
            return has && string.Equals(d?.Trim(), "rtl", StringComparison.OrdinalIgnoreCase);
        }

        private bool CurrentRtl => _dirs.Count > 0 && _dirs[^1].Rtl;

        private void StartTag(string name, Dictionary<string, string> attrs, bool selfClosing)
        {
            if (IgnoredContent.Contains(name))
            {
                if (PictureTags.Contains(name)) _model.HasPictures = true;
                SkipUntilClose(name, selfClosing);
                return;
            }
            if (PictureTags.Contains(name)) _model.HasPictures = true;

            var rtl = IsRtlAttr(attrs, out var hasDir);
            if (hasDir && !IsVoid(name) && !selfClosing) _dirs.Add((name, rtl));

            var pageBreak = attrs.TryGetValue("style", out var style) && HasPageBreakBefore(style);
            _nextKeepWs = style is not null && HasPreservedWhitespace(style);

            switch (name)
            {
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    StartBlock(ParagraphKind.Heading, name[1] - '0', false, pageBreak);
                    break;
                case "ul" or "ol":
                    FlushParagraph();
                    if (pageBreak) AddBlock(new DocPageBreak());
                    _lists.Add(name == "ol");
                    break;
                case "li":
                    StartBlock(ParagraphKind.ListItem, Math.Max(0, _lists.Count - 1), _lists.Count > 0 && _lists[^1], pageBreak);
                    break;
                case "pre":
                    StartBlock(ParagraphKind.Normal, 0, false, pageBreak);
                    _pre++;
                    break;
                case "br":
                    AddText("\n", keepLeading: true);
                    break;
                case "hr":
                    FlushParagraph();
                    break;
                case "table":
                    FlushParagraph();
                    if (pageBreak) AddBlock(new DocPageBreak());
                    if (++_tableDepth == 1) _table = new DocTable();
                    break;
                case "tr":
                    FlushParagraph();
                    if (_tableDepth == 1) { EndCell(); EndRow(); _row = []; }
                    break;
                case "td" or "th":
                    FlushParagraph();
                    if (_tableDepth == 1)
                    {
                        EndCell();
                        _row ??= [];
                        _cell = [];
                        _inTh = name == "th";
                    }
                    break;
                case "b" or "strong":
                    _bold++;
                    break;
                case "i" or "em" or "cite" or "dfn" or "var":
                    _italic++;
                    break;
                case "u" or "ins":
                    _underline++;
                    break;
                case "a":
                    _links.Add(attrs.TryGetValue("href", out var href) && IsSafeLink(href) ? WebUtility.HtmlDecode(href.Trim()) : null);
                    break;
                default:
                    if (BlockTags.Contains(name))
                        StartBlock(ParagraphKind.Normal, 0, false, pageBreak);
                    else if (pageBreak)
                        AddBlock(new DocPageBreak());
                    break;
            }
        }

        private void EndTag(string name)
        {
            if (IgnoredContent.Contains(name)) return;

            if (_dirs.Count > 0)
            {
                var idx = _dirs.FindLastIndex(d => d.Tag == name);
                if (idx >= 0) _dirs.RemoveRange(idx, _dirs.Count - idx);
            }

            switch (name)
            {
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "li" or "p" or "div":
                    FlushParagraph();
                    break;
                case "pre":
                    FlushParagraph();
                    if (_pre > 0) _pre--;
                    break;
                case "ul" or "ol":
                    FlushParagraph();
                    if (_lists.Count > 0) _lists.RemoveAt(_lists.Count - 1);
                    break;
                case "table":
                    FlushParagraph();
                    EndTable(force: false);
                    break;
                case "tr":
                    FlushParagraph();
                    if (_tableDepth == 1) { EndCell(); EndRow(); }
                    break;
                case "td" or "th":
                    FlushParagraph();
                    if (_tableDepth == 1) { EndCell(); _inTh = false; }
                    break;
                case "b" or "strong":
                    if (_bold > 0) _bold--;
                    break;
                case "i" or "em" or "cite" or "dfn" or "var":
                    if (_italic > 0) _italic--;
                    break;
                case "u" or "ins":
                    if (_underline > 0) _underline--;
                    break;
                case "a":
                    if (_links.Count > 0) _links.RemoveAt(_links.Count - 1);
                    break;
                default:
                    if (BlockTags.Contains(name)) FlushParagraph();
                    break;
            }
        }

        private static bool IsVoid(string name) => name is "br" or "hr" or "img" or "input" or "meta" or "link" or "area" or "base" or "col" or "embed" or "source" or "wbr";

        private static bool IsSafeLink(string href)
        {
            var h = href.Trim();
            return h.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                   || h.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                   || h.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasPreservedWhitespace(string style)
        {
            var s = style.Replace(" ", "").ToLowerInvariant();
            var at = s.IndexOf("white-space:", StringComparison.Ordinal);
            if (at < 0) return false;
            var value = s[(at + "white-space:".Length)..];
            var end = value.IndexOfAny([';', '!']);
            if (end >= 0) value = value[..end];
            return value is "pre" or "pre-wrap" or "break-spaces";
        }

        private static bool HasPageBreakBefore(string style)
        {
            var s = style.Replace(" ", "").ToLowerInvariant();
            return s.Contains("page-break-before:always") || s.Contains("break-before:page");
        }

        private void SkipUntilClose(string name, bool selfClosing)
        {
            if (selfClosing || IsVoid(name)) return;
            var end = html.IndexOf("</" + name, _i, StringComparison.OrdinalIgnoreCase);
            if (end < 0) { _i = html.Length; return; }
            var gt = html.IndexOf('>', end);
            _i = gt < 0 ? html.Length : gt + 1;
        }

        // ------------------------------------------------------------ text and paragraphs

        private void StartBlock(ParagraphKind kind, int level, bool ordered, bool pageBreak)
        {
            FlushParagraph();
            if (pageBreak) AddBlock(new DocPageBreak());
            _keepWs = _nextKeepWs;
            _cur = new DocParagraph { Kind = kind, Level = level, Ordered = ordered, Rtl = CurrentRtl };
        }

        private void Text(string raw)
        {
            if (raw.Length == 0) return;
            AddText(WebUtility.HtmlDecode(raw), keepLeading: false);
        }

        private void AddText(string text, bool keepLeading)
        {
            if (_pre > 0 || _keepWs || keepLeading)
            {
                text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u00A0', ' ');
                if (_pre > 0 && _cur is { Runs.Count: 0 } && text.StartsWith('\n') && !keepLeading) text = text[1..];
            }
            else
            {
                var sb = new StringBuilder(text.Length);
                var lastSpace = _cur is null || _cur.Runs.Count == 0 || EndsWithWhitespace(_cur);
                foreach (var ch in text)
                {
                    if (ch is ' ' or '\t' or '\r' or '\n' or '\f')
                    {
                        if (lastSpace) continue;
                        sb.Append(' ');
                        lastSpace = true;
                    }
                    else
                    {
                        sb.Append(ch == '\u00A0' ? ' ' : ch); // &nbsp; is a space that does not collapse
                        lastSpace = false;
                    }
                }
                text = sb.ToString();
            }
            if (text.Length == 0) return;

            _cur ??= new DocParagraph { Rtl = CurrentRtl };
            var link = _links.Count > 0 ? _links[^1] : null;
            _cur.Add(text, _bold > 0 || _inTh, _italic > 0, _underline > 0, link);
        }

        private static bool EndsWithWhitespace(DocParagraph p)
        {
            var last = p.Runs[^1].Text;
            return last.Length > 0 && last[^1] is ' ' or '\n' or '\t';
        }

        private void FlushParagraph()
        {
            var p = _cur;
            var keep = _keepWs;
            _cur = null;
            _keepWs = false;
            if (p is null) return;

            // In a white-space:pre-wrap block a block made only of line breaks (<p><br></p>) is a blank line, and spaces are content.
            var blankLine = keep && p.Runs.Count > 0 && p.Runs.All(r => r.Text.All(c => c == '\n'));
            var trailing = keep ? new[] { '\n', '\r' } : new[] { ' ', '\n', '\t', '\r' };
            for (var k = p.Runs.Count - 1; k >= 0; k--)
            {
                var trimmed = p.Runs[k].Text.TrimEnd(trailing);
                if (trimmed.Length == 0) { p.Runs.RemoveAt(k); continue; }
                p.Runs[k] = p.Runs[k] with { Text = trimmed };
                break;
            }
            // Leading breaks are not content either (except in a preserved block, where they are blank lines).
            while (!keep && p.Runs.Count > 0 && p.Runs[0].Text.TrimStart('\n').Length != p.Runs[0].Text.Length)
            {
                var t = p.Runs[0].Text.TrimStart('\n');
                if (t.Length == 0) p.Runs.RemoveAt(0);
                else { p.Runs[0] = p.Runs[0] with { Text = t }; break; }
            }
            if (p.Runs.Count == 0 && !blankLine) return;
            if (p.Kind == ParagraphKind.Heading)
                for (var k = 0; k < p.Runs.Count; k++)
                    p.Runs[k] = p.Runs[k] with { Bold = false, Italic = false };

            if (_tableDepth >= 1 && _cell is not null)
                _cell.Add(p.ForCell());
            else
                AddBlock(p);
        }

        private void AddBlock(DocBlock b) => _model.Blocks.Add(b);

        // ------------------------------------------------------------ tables

        private void EndCell()
        {
            if (_cell is null) return;
            if (_cell.Count == 0) _cell.Add(new DocParagraph());
            (_row ??= []).Add(_cell);
            _cell = null;
        }

        private void EndRow()
        {
            if (_row is { Count: > 0 } && _table is not null) _table.Rows.Add(_row);
            _row = null;
        }

        private void EndTable(bool force)
        {
            if (_tableDepth == 0) return;
            if (!force && _tableDepth > 1) { _tableDepth--; return; }
            EndCell();
            EndRow();
            if (_table is { Rows.Count: > 0 }) AddBlock(_table);
            _table = null;
            _tableDepth = 0;
            _cell = null;
            _row = null;
        }
    }
}
