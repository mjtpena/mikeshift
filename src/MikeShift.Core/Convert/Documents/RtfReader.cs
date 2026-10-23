using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MikeShift.Core.Documents;

/// <summary>
/// Hand-written RTF reader for the text/paragraph/basic character-format subset (PLAN §3.4): groups, control words,
/// <c>\par \line \tab</c>, <c>\b \i \ul</c>, <c>\uN</c> with <c>\ucN</c> skipping, <c>\'hh</c> through <c>\ansicpg</c> or the font's charset,
/// headings (stylesheet names or outline level), lists (<c>\ls</c> with the list table), tables, hyperlink fields, page breaks.
/// Font/colour/style tables, <c>\info</c>, headers, footnotes and unknown <c>\*</c> destinations are skipped; <c>\pict</c> only flags pictures.
/// </summary>
public static partial class RtfReader
{
    static RtfReader() => TextDecoding.EnsureProviders();

    public static DocModel Read(byte[] data)
    {
        var start = 0;
        while (start < data.Length && data[start] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t') start++;
        if (data.Length - start < 5 || data[start] != '{' || data[start + 1] != '\\' || data[start + 2] != 'r' || data[start + 3] != 't' || data[start + 4] != 'f')
            throw new ConversionException("This isn't a valid RTF file.");
        return new Parser(Encoding.Latin1.GetString(data, start, data.Length - start)).Parse();
    }

    public static DocModel Read(string rtf) => Read(Encoding.Latin1.GetBytes(rtf));

    private sealed class State
    {
        public bool Bold, Italic, Underline, Hidden, Skip, StarPending;
        public int Uc = 1;
        public int FontNum = -1;
        public string? Dest;
        public string? Link;
        public StringBuilder? Capture;
        public int FontDef = -1;           // fonttbl: font being defined
        public int StyleNum = -1;          // stylesheet: style being defined
        public int StyleOutline = -1;
        public bool OpensField, OpensFldInst, OpensStyle, OpensList, OpensOverride;

        public State Clone()
        {
            var c = (State)MemberwiseClone();
            c.OpensField = c.OpensFldInst = c.OpensStyle = c.OpensList = c.OpensOverride = false;
            c.StarPending = false;
            return c;
        }
    }

    private sealed class Parser(string s)
    {
        private int _i;
        private readonly Stack<State> _stack = new();
        private State _st = new();
        private readonly DocModel _model = new();
        private int _skipCount;

        private int _ansiCp = 1252;
        private int _defFont;
        private readonly Dictionary<int, int> _fontCp = [];
        private readonly Dictionary<int, Decoder> _decoders = [];
        private readonly Dictionary<int, int> _styleHeading = [];   // style number -> heading level
        private readonly Dictionary<int, bool> _listOrdered = [];   // listid -> ordered
        private readonly Dictionary<int, int> _lsToList = [];      // \ls -> listid
        private int _curListId = -1, _curListNfc = -1, _ovListId = -1, _ovLs = -1;
        private string? _pendingLink;

        // paragraph state
        private DocParagraph _cur = new();
        private int _pStyle = -1, _pOutline = -1, _pLs, _pIlvl;
        private bool _pRtl;
        private bool _pnList, _pnOrdered;

        // tables
        private bool _inRow;
        private List<DocParagraph> _cell = [];
        private List<List<DocParagraph>> _row = [];
        private DocTable? _table;

        public DocModel Parse()
        {
            var n = s.Length;
            while (_i < n)
            {
                var c = s[_i];
                switch (c)
                {
                    case '{':
                        _stack.Push(_st);
                        _st = _st.Clone();
                        _skipCount = 0;
                        _i++;
                        break;
                    case '}':
                        _skipCount = 0;
                        EndGroup();
                        _i++;
                        break;
                    case '\\':
                        _i++;
                        Control();
                        break;
                    case '\r' or '\n':
                        _i++;
                        break;
                    default:
                        _i++;
                        Literal(c);
                        break;
                }
            }

            // Whatever is still open at EOF.
            if (_cur.Runs.Count > 0) EndParagraph(false);
            FlushTable();
            return _model;
        }

        // ------------------------------------------------------------ groups

        private void EndGroup()
        {
            var closing = _st;
            if (_stack.Count > 0) _st = _stack.Pop();

            if (closing.OpensFldInst && closing.Capture is { } cap)
            {
                var m = HyperlinkField().Match(cap.ToString());
                _pendingLink = m.Success ? m.Groups[1].Value : null;
            }
            if (closing.OpensField) _pendingLink = null;
            if (closing.OpensStyle && closing.StyleNum >= 0)
            {
                var name = (closing.Capture?.ToString() ?? "").Trim().TrimEnd(';').Trim();
                var h = HeadingStyleName().Match(name);
                if (h.Success) _styleHeading[closing.StyleNum] = Math.Clamp(int.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture), 1, 6);
                else if (closing.StyleOutline is >= 0 and <= 5 && !name.Equals("Normal", StringComparison.OrdinalIgnoreCase))
                    _styleHeading[closing.StyleNum] = closing.StyleOutline + 1;
            }
            if (closing.OpensList && _curListId >= 0)
            {
                _listOrdered[_curListId] = _curListNfc is not (23 or 255 or -1);
                _curListId = -1;
            }
            if (closing.OpensOverride && _ovLs >= 0 && _ovListId >= 0)
            {
                _lsToList[_ovLs] = _ovListId;
                _ovLs = _ovListId = -1;
            }
        }

        // ------------------------------------------------------------ control words

        private static readonly HashSet<string> SkipDestinations = new(StringComparer.Ordinal)
        {
            "colortbl", "info", "header", "headerl", "headerr", "headerf", "footer", "footerl", "footerr", "footerf",
            "footnote", "annotation", "bkmkstart", "bkmkend", "xe", "tc", "themedata", "colorschememapping", "latentstyles",
            "datastore", "generator", "revtbl", "rsidtbl", "listtext", "pntext", "pnseclvl", "private", "userprops",
            "fchars", "lchars", "background", "docvar", "objdata", "objclass", "objname", "result_", "pgdsctbl",
            "filetbl", "fontemb", "mmathPr", "wgrffmtfilter", "xmlnstbl", "protusertbl", "nonshppict", "falt",
            "panose", "fname", "txfieldtext", "upr", "ud", "title", "subject", "author", "operator", "keywords", "comment", "company",
        };

        private static readonly HashSet<string> StarKnown = new(StringComparer.Ordinal)
        {
            "listtable", "listoverridetable", "fldinst", "pn", "list", "listoverride",
        };

        private void Control()
        {
            if (_i >= s.Length) return;
            var c = s[_i];

            if (!char.IsAsciiLetter(c))
            {
                _i++;
                ControlSymbol(c);
                return;
            }

            var startWord = _i;
            while (_i < s.Length && char.IsAsciiLetter(s[_i])) _i++;
            var word = s[startWord.._i];

            int? param = null;
            var neg = false;
            if (_i < s.Length && s[_i] == '-') { neg = true; _i++; }
            var startNum = _i;
            while (_i < s.Length && char.IsAsciiDigit(s[_i])) _i++;
            if (_i > startNum && long.TryParse(s.AsSpan(startNum, _i - startNum), NumberStyles.None, CultureInfo.InvariantCulture, out var num))
                param = (int)Math.Clamp(neg ? -num : num, int.MinValue, int.MaxValue);
            else if (neg) _i = startNum - 1; // a lone '-' is text, not part of the control word
            if (_i < s.Length && s[_i] == ' ') _i++;

            Word(word, param);
        }

        private void ControlSymbol(char c)
        {
            switch (c)
            {
                case '*':
                    _st.StarPending = true;
                    break;
                case '\'':
                    if (_i + 1 < s.Length && Uri.IsHexDigit(s[_i]) && Uri.IsHexDigit(s[_i + 1]))
                    {
                        var b = Convert.ToByte(s.Substring(_i, 2), 16);
                        _i += 2;
                        EmitByte(b);
                    }
                    break;
                case '\\' or '{' or '}':
                    EmitChar(c);
                    break;
                case '~':
                    EmitChar(' ');
                    break;
                case '_':
                    EmitChar('‑');
                    break;
                case '\r' or '\n':
                    Word("par", null);
                    break;
                // '-' optional hyphen, '|' '\:' formula/index marks: no visible text.
            }
        }

        private void Word(string word, int? p)
        {
            if (_st.StarPending)
            {
                _st.StarPending = false;
                if (!StarKnown.Contains(word)) _st.Skip = true;
            }

            switch (word)
            {
                // --- document / encoding
                case "ansicpg":
                    if (p is { } cp) _ansiCp = cp;
                    return;
                case "mac": _ansiCp = 10000; return;
                case "pc": _ansiCp = 437; return;
                case "pca": _ansiCp = 850; return;
                case "deff": _defFont = p ?? 0; return;
                case "uc": _st.Uc = p ?? 1; return;
                case "u":
                    if (p is { } u)
                    {
                        var ch = (char)(u < 0 ? u + 65536 : u);
                        EmitChar(ch);
                        _skipCount = Math.Max(0, _st.Uc);
                    }
                    return;
                case "bin":
                    _i = (int)Math.Min(s.Length, (long)_i + Math.Max(0, p ?? 0));
                    return;

                // --- tables of things
                case "fonttbl":
                    _st.Dest = "fonttbl"; _st.Skip = true; return;
                case "stylesheet":
                    _st.Dest = "stylesheet"; _st.Skip = true; return;
                case "listtable":
                    _st.Dest = "listtable"; _st.Skip = true; return;
                case "listoverridetable":
                    _st.Dest = "listoverridetable"; _st.Skip = true; return;
                case "list" when _st.Dest == "listtable":
                    _st.Dest = "list"; _st.OpensList = true; _curListId = -1; _curListNfc = -1; return;
                case "listlevel" when _st.Dest is "list" or "listlevel":
                    _st.Dest = "listlevel"; return;
                case "levelnfc" when _st.Dest == "listlevel":
                    if (_curListNfc < 0 && p is { } nfc) _curListNfc = nfc;
                    return;
                case "listid":
                    if (_st.Dest == "list") _curListId = p ?? -1;
                    else if (_st.Dest == "listoverride") _ovListId = p ?? -1;
                    return;
                case "listoverride" when _st.Dest == "listoverridetable":
                    _st.Dest = "listoverride"; _st.OpensOverride = true; _ovListId = _ovLs = -1; return;
                case "fldinst":
                    _st.Dest = "fldinst"; _st.OpensFldInst = true; _st.Skip = true; _st.Capture = new StringBuilder(); return;
                case "field":
                    _st.OpensField = true; return;
                case "fldrslt":
                    _st.Link = _pendingLink; return;

                // --- pictures
                case "pict" or "shppict" or "objemb" or "objlink" or "shpinst" or "shp" or "object":
                    _model.HasPictures = true;
                    _st.Skip = true;
                    return;

                // --- fonts
                case "f":
                    if (_st.Dest == "fonttbl") _st.FontDef = p ?? -1;
                    else _st.FontNum = p ?? -1;
                    return;
                case "fcharset":
                    if (_st.Dest == "fonttbl" && _st.FontDef >= 0 && p is { } charset && CodePageForCharset(charset) is { } fcp)
                        _fontCp[_st.FontDef] = fcp;
                    return;

                // --- character formatting
                case "b": _st.Bold = p != 0; return;
                case "i": _st.Italic = p != 0; return;
                case "ul" or "uld" or "uldb" or "ulw" or "ulth" or "uldash" or "ulwave" or "uldashd" or "uldashdd" or "ulhwave":
                    _st.Underline = p != 0; return;
                case "ulnone": _st.Underline = false; return;
                case "v": _st.Hidden = p != 0; return;
                case "plain":
                    _st.Bold = _st.Italic = _st.Underline = _st.Hidden = false; return;

                // --- paragraph formatting
                case "pard":
                    _pStyle = _pOutline = -1; _pLs = _pIlvl = 0; _pRtl = false; _pnList = _pnOrdered = false;
                    return;
                case "s":
                    if (_st.Dest == "stylesheet") { _st.OpensStyle = true; _st.StyleNum = p ?? -1; _st.Capture = new StringBuilder(); }
                    else _pStyle = p ?? -1;
                    return;
                case "outlinelevel":
                    if (_st.Dest == "stylesheet") _st.StyleOutline = p ?? -1; else _pOutline = p ?? -1;
                    return;
                case "ls":
                    if (_st.Dest == "listoverride") _ovLs = p ?? -1;
                    else if (!_st.Skip) _pLs = p ?? 0;
                    return;
                case "ilvl":
                    if (!_st.Skip) _pIlvl = p ?? 0;
                    return;
                case "pn":
                    _st.Skip = true; return;
                case "pnlvlblt": _pnList = true; _pnOrdered = false; return;
                case "pnlvlbody" or "pndec": _pnList = true; _pnOrdered = true; return;
                case "rtlpar": _pRtl = true; return;
                case "ltrpar": _pRtl = false; return;

                // --- structure
                case "par":
                    if (!_st.Skip) EndParagraph(true);
                    return;
                case "sect":
                    if (!_st.Skip && _cur.Runs.Count > 0) EndParagraph(false);
                    return;
                case "page":
                    if (!_st.Skip) PageBreak();
                    return;
                case "line": EmitChar('\n'); return;
                case "tab": EmitChar('\t'); return;
                case "emdash": EmitChar('—'); return;
                case "endash": EmitChar('–'); return;
                case "emspace" or "enspace" or "qmspace": EmitChar(' '); return;
                case "bullet": EmitChar('•'); return;
                case "lquote": EmitChar('‘'); return;
                case "rquote": EmitChar('’'); return;
                case "ldblquote": EmitChar('“'); return;
                case "rdblquote": EmitChar('”'); return;

                case "trowd":
                    if (!_inRow) { _inRow = true; _cell = []; _row = []; }
                    return;
                case "intbl":
                    if (!_inRow) { _inRow = true; _cell = []; _row = []; }
                    return;
                case "cell":
                    if (!_st.Skip) EndCell();
                    return;
                case "row":
                    if (!_st.Skip) EndRow();
                    return;
            }

            if (SkipDestinations.Contains(word))
                _st.Skip = true;
        }

        // ------------------------------------------------------------ emission

        private void Literal(char c)
        {
            if (c < ' ') return;
            if (c < 0x80) { if (_skipCount > 0) { _skipCount--; return; } EmitText(c.ToString()); }
            else EmitByte((byte)c);
        }

        private void EmitChar(char c)
        {
            EmitText(c.ToString());
        }

        private void EmitByte(byte b)
        {
            if (_skipCount > 0) { _skipCount--; return; }
            if (_st.Capture is null && (_st.Skip || _st.Hidden)) return;

            var font = _st.FontNum >= 0 ? _st.FontNum : _defFont;
            var cp = _fontCp.TryGetValue(font, out var fcp) ? fcp : _ansiCp;
            if (b < 0x80 && cp is not (932 or 936 or 949 or 950 or 1361))
            {
                EmitText(((char)b).ToString());
                return;
            }
            if (!_decoders.TryGetValue(cp, out var dec))
            {
                Encoding enc;
                try { enc = Encoding.GetEncoding(cp); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { enc = Encoding.GetEncoding(1252); }
                dec = enc.GetDecoder();
                _decoders[cp] = dec;
            }
            Span<char> chars = stackalloc char[4];
            var n = dec.GetChars([b], chars, flush: false);
            if (n > 0) EmitText(new string(chars[..n]));
        }

        private void EmitText(string text)
        {
            if (_st.Capture is { } cap)
            {
                cap.Append(text);
                return;
            }
            if (_st.Skip || _st.Hidden) return;
            _cur.Add(text, _st.Bold, _st.Italic, _st.Underline, _st.Link);
        }

        // ------------------------------------------------------------ blocks

        private int? HeadingLevel()
        {
            if (_pStyle >= 0 && _styleHeading.TryGetValue(_pStyle, out var h)) return h;
            if (_pOutline is >= 0 and <= 5) return _pOutline + 1;
            return null;
        }

        private DocParagraph TakeParagraph()
        {
            var p = _cur;
            p.Rtl = _pRtl;
            if (HeadingLevel() is { } level)
            {
                p.Kind = ParagraphKind.Heading;
                p.Level = level;
                var plain = new DocParagraph { Kind = p.Kind, Level = p.Level, Rtl = p.Rtl };
                foreach (var r in p.Runs) plain.Add(r with { Bold = false, Italic = false });
                p = plain;
            }
            else if (_pLs > 0 || _pnList)
            {
                p.Kind = ParagraphKind.ListItem;
                p.Level = Math.Clamp(_pIlvl, 0, 8);
                p.Ordered = _pLs > 0
                    ? _lsToList.TryGetValue(_pLs, out var lid) && _listOrdered.TryGetValue(lid, out var o) && o
                    : _pnOrdered;
            }
            _cur = new DocParagraph();
            return p;
        }

        private void EndParagraph(bool force)
        {
            if (!force && _cur.Runs.Count == 0) return;
            var p = TakeParagraph();
            if (_inRow) _cell.Add(p.ForCell());
            else AddBlock(p);
        }

        private void PageBreak()
        {
            if (_cur.Runs.Count > 0) EndParagraph(false);
            AddBlock(new DocPageBreak());
        }

        private void EndCell()
        {
            if (!_inRow) { _inRow = true; _cell = []; _row = []; }
            if (_cur.Runs.Count > 0 || _cell.Count == 0)
                _cell.Add(TakeParagraph().ForCell());
            _row.Add(_cell);
            _cell = [];
        }

        private void EndRow()
        {
            if (_cur.Runs.Count > 0 && _inRow) EndCell();
            if (_inRow && _row.Count > 0)
            {
                _table ??= new DocTable();
                _table.Rows.Add(_row);
            }
            _inRow = false;
            _row = [];
            _cell = [];
        }

        private void AddBlock(DocBlock b)
        {
            FlushTable();
            _model.Blocks.Add(b);
        }

        private void FlushTable()
        {
            if (_table is null) return;
            _model.Blocks.Add(_table);
            _table = null;
        }

        private static int? CodePageForCharset(int charset) => charset switch
        {
            77 => 10000,
            128 => 932,
            129 => 949,
            130 => 1361,
            134 => 936,
            136 => 950,
            161 => 1253,
            162 => 1254,
            163 => 1258,
            177 => 1255,
            178 => 1256,
            186 => 1257,
            204 => 1251,
            222 => 874,
            238 => 1250,
            254 => 437,
            255 => 850,
            _ => null,
        };
    }

    [GeneratedRegex(@"HYPERLINK\s+(?:\\[a-z]\s+)*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HyperlinkField();

    [GeneratedRegex(@"^heading\s*([1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingStyleName();
}
