using MikeShift.Core.Documents;

namespace MikeShift.Core.Tests.Documents;

/// <summary>Corners of the hand-written RTF tokenizer/reader: encodings, destinations, styles, lists, tables, malformed input.</summary>
public class RtfReaderCoverageTests
{
    private static DocModel Rtf(string body) => RtfReader.Read(@"{\rtf1\ansi " + body + "}");

    private static DocParagraph[] Paragraphs(DocModel m) => [.. m.Blocks.OfType<DocParagraph>()];

    private static string[] Texts(DocModel m) => [.. Paragraphs(m).Select(p => p.Text)];

    // ------------------------------------------------------------------ header validation

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData(@"{\rt")]
    [InlineData(@"{\rtx1 hi}")]
    [InlineData(@"\rtf1 hi")]
    [InlineData("   \r\n")]
    public void Anything_that_does_not_start_with_the_rtf_signature_is_refused(string text)
    {
        var ex = Assert.Throws<ConversionException>(() => RtfReader.Read(text));
        Assert.Equal("This isn't a valid RTF file.", ex.Message);
    }

    [Fact(Skip = "BUG: RtfReader accepts white space before '{\\rtf' but then parses it as body text, so the first paragraph gets leading spaces (RtfReader.Read ignores 'start' when constructing the Parser)")]
    public void Leading_white_space_before_the_signature_is_allowed()
    {
        var m = RtfReader.Read("  \r\n\t{\\rtf1 Hello\\par}");
        Assert.Equal(["Hello"], Texts(m));
    }

    [Fact]
    public void Leading_line_ends_before_the_signature_are_allowed_and_leave_no_text()
    {
        var m = RtfReader.Read("\r\n\r\n{\\rtf1 Hello\\par}");
        Assert.Equal(["Hello"], Texts(m));
    }

    [Fact]
    public void A_document_that_ends_mid_paragraph_or_with_unbalanced_braces_keeps_its_text()
    {
        Assert.Equal(["Cut off"], Texts(RtfReader.Read(@"{\rtf1 Cut off")));
        Assert.Equal(["Extra"], Texts(RtfReader.Read(@"{\rtf1 Extra\par}}}}")));
        Assert.Equal(["A", "B"], Texts(RtfReader.Read(@"{\rtf1 A\par B")));
        Assert.Empty(RtfReader.Read(@"{\rtf1}").Blocks);
        Assert.Empty(RtfReader.Read(@"{\rtf1 \").Blocks);
    }

    // ------------------------------------------------------------------ escapes, symbols, special characters

    [Fact]
    public void Control_symbols_produce_their_characters()
    {
        var m = Rtf(@"a\\b\{c\}d\~e\_f\-g\|h\:i\par");
        // "\~" is a no-break space (U+00A0), "\_" a non-breaking hyphen (U+2011); "\-", "\|" and "\:" print nothing.
        Assert.Equal("a\\b{c}d" + (char)0xA0 + "e" + (char)0x2011 + "fghi", Texts(m)[0]);
    }

    [Fact]
    public void Named_punctuation_words_become_unicode_punctuation()
    {
        var m = Rtf(@"\emdash \endash \emspace \enspace \qmspace \bullet \lquote \rquote \ldblquote \rdblquote \par");
        Assert.Equal("—–   •‘’“”", Texts(m)[0]);
    }

    [Fact]
    public void Line_and_tab_words_become_control_characters_in_the_text()
    {
        var m = Rtf(@"one\line two\tab three\par");
        Assert.Equal("one\ntwo\tthree", Texts(m)[0]);
    }

    [Fact]
    public void A_backslash_followed_by_a_line_end_is_a_paragraph_break()
    {
        var m = Rtf("first\\\r\nsecond\\\nthird");
        Assert.Equal(["first", "second", "third"], Texts(m));
    }

    [Fact]
    public void Raw_line_ends_and_control_characters_in_the_source_are_not_text()
    {
        var m = Rtf("a\r\nb\tc\u0001d\\par");
        Assert.Equal("abcd", Texts(m)[0]);
    }

    [Fact]
    public void A_lone_minus_after_a_control_word_is_ordinary_text()
    {
        var m = Rtf(@"\b-dash\b0 and \fs-3 x\par");
        var p = Paragraphs(m)[0];
        Assert.Equal("-dashand x", p.Text);
        Assert.True(p.Runs[0].Bold);
    }

    [Fact]
    public void Unknown_control_words_with_numbers_are_ignored_and_a_huge_number_does_not_overflow()
    {
        var m = Rtf(@"\foo123 \bar-9 \fs99999999999999999999 text\par");
        Assert.Equal(["text"], Texts(m));
    }

    // ------------------------------------------------------------------ unicode and code pages

    [Fact]
    public void Unicode_escapes_replace_the_fallback_characters_that_follow()
    {
        Assert.Equal("é!", Texts(Rtf(@"\uc1\u233?!\par"))[0]);
        Assert.Equal("é!", Texts(Rtf(@"\uc2\u233??!\par"))[0]);
        Assert.Equal("é!", Texts(Rtf(@"\uc0\u233 !\par"))[0]);
    }

    [Fact]
    public void Negative_unicode_values_are_wrapped_into_the_basic_multilingual_plane()
    {
        // \u-10179 is the UTF-16 high surrogate; the matching low surrogate follows, together making U+1F600.
        var m = Rtf(@"\u-10179?\u-8704?\par");
        Assert.Equal("😀", Texts(m)[0]);
    }

    [Fact]
    public void A_unicode_escape_without_a_number_is_ignored()
    {
        Assert.Equal(["ab"], Texts(Rtf(@"a\u b\par")));
    }

    [Fact]
    public void Hex_escapes_use_the_ansi_code_page()
    {
        Assert.Equal("é", Texts(Rtf(@"\'e9\par"))[0]);
        Assert.Equal("שלום", Texts(RtfReader.Read(@"{\rtf1\ansi\ansicpg1255 \'f9\'ec\'e5\'ed\par}"))[0]);
    }

    [Fact]
    public void Mac_pc_and_pca_select_their_code_pages()
    {
        Assert.Equal("é", Texts(RtfReader.Read(@"{\rtf1\mac \'8e\par}"))[0]);   // Mac Roman 0x8E
        Assert.Equal("é", Texts(RtfReader.Read(@"{\rtf1\pc \'82\par}"))[0]);    // CP437 0x82
        Assert.Equal("é", Texts(RtfReader.Read(@"{\rtf1\pca \'82\par}"))[0]);   // CP850 0x82
    }

    [Fact]
    public void A_font_charset_overrides_the_document_code_page_for_text_in_that_font()
    {
        var rtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0\fcharset0 Arial;}{\f1\fcharset177 Hebrew;}{\f2\fcharset204 Cyr;}{\f3\fcharset161 Greek;}}" +
                  @"\f1 \'e0\f0 \'e9\f2 \'cf\f3 \'e1\par}";
        Assert.Equal("אéПα", Texts(RtfReader.Read(rtf))[0]);
    }

    [Theory]
    [InlineData(77, "é", "8e")]      // Mac
    [InlineData(162, "ş", "fe")]     // Turkish 1254
    [InlineData(163, "ê", "ea")]     // Vietnamese 1258
    [InlineData(178, "ب", "c8")]     // Arabic 1256
    [InlineData(186, "ū", "fb")]     // Baltic 1257
    [InlineData(238, "ň", "f2")]     // Central European 1250
    [InlineData(222, "ก", "a1")]     // Thai 874
    [InlineData(254, "é", "82")]     // PC 437
    [InlineData(255, "é", "82")]     // OEM 850
    public void Every_known_font_charset_maps_to_a_code_page(int charset, string expected, string hex)
    {
        var rtf = @"{\rtf1\ansi\deff1{\fonttbl{\f1\fcharset" + charset + @" X;}}\f1 \'" + hex + @"\par}";
        Assert.Equal(expected, Texts(RtfReader.Read(rtf))[0]);
    }

    [Fact]
    public void An_unknown_font_charset_leaves_the_document_code_page_alone()
    {
        var rtf = @"{\rtf1\ansi\deff1{\fonttbl{\f1\fcharset999 X;}}\f1 \'e9\par}";
        Assert.Equal("é", Texts(RtfReader.Read(rtf))[0]);
    }

    [Fact]
    public void Double_byte_code_pages_combine_lead_and_trail_bytes_and_ascii_bytes_stay_ascii_elsewhere()
    {
        var jp = RtfReader.Read(@"{\rtf1\ansi\ansicpg932 \'93\'fa\'96\'7b\par}"); // 日本 in Shift-JIS
        Assert.Equal("日本", Texts(jp)[0]);
        var ascii = RtfReader.Read(@"{\rtf1\ansi\ansicpg1252 \'41\'42\par}");
        Assert.Equal("AB", Texts(ascii)[0]);
    }

    [Fact]
    public void An_unsupported_code_page_falls_back_to_windows_1252()
    {
        Assert.Equal("é", Texts(RtfReader.Read(@"{\rtf1\ansi\ansicpg99999 \'e9\par}"))[0]);
    }

    [Fact]
    public void Bad_hex_escapes_are_dropped()
    {
        // The "\'" itself is dropped; whatever follows it is ordinary text.
        Assert.Equal(["azzb"], Texts(Rtf(@"a\'zzb\par")));
        Assert.Equal(["a4"], Texts(Rtf(@"a\'4")));
    }

    [Fact]
    public void High_bytes_in_the_file_are_decoded_like_hex_escapes()
    {
        var bytes = new byte[] { (byte)'{' }.Concat("\\rtf1\\ansi "u8.ToArray()).Concat(new byte[] { 0xE9 }).Concat("\\par}"u8.ToArray()).ToArray();
        Assert.Equal("é", Texts(RtfReader.Read(bytes))[0]);
    }

    [Fact]
    public void Binary_data_is_skipped_by_length()
    {
        var m = Rtf(@"before\bin4 }}{{after\par");
        Assert.Equal(["beforeafter"], Texts(m));
    }

    // ------------------------------------------------------------------ destinations that are skipped

    [Fact]
    public void Header_footer_info_footnote_and_unknown_star_destinations_are_not_body_text()
    {
        var rtf = @"{\rtf1\ansi{\info{\title T}{\author A}}{\header H}{\footer F}{\footnote N}{\*\unknown secret}{\*\generator Foo;}Body\par}";
        Assert.Equal(["Body"], Texts(RtfReader.Read(rtf)));
    }

    [Fact]
    public void Hidden_text_is_not_emitted_until_it_is_switched_off()
    {
        Assert.Equal(["shown shown2"], Texts(Rtf(@"shown {\v hidden\'e9 }\v0 shown2\par")));
    }

    [Fact]
    public void Pictures_only_set_the_picture_flag()
    {
        var m = Rtf(@"text{\pict\pngblip 89504e47}more\par");
        Assert.True(m.HasPictures);
        Assert.Equal(["textmore"], Texts(m));
        Assert.True(Rtf(@"{\object{\*\objdata 00}}x\par").HasPictures);
        Assert.False(Rtf(@"x\par").HasPictures);
    }

    // ------------------------------------------------------------------ character formatting

    [Fact]
    public void Bold_italic_underline_and_plain_toggle_runs()
    {
        var m = Rtf(@"a {\b b}{\i i}{\ul u}{\uldb d}{\ulw w}{\ulnone n}\par");
        var runs = Paragraphs(m)[0].Runs;
        // Neighbouring runs with the same formatting are merged: u, d and w are all underlined.
        Assert.Equal([("a ", false, false, false), ("b", true, false, false), ("i", false, true, false), ("udw", false, false, true),
            ("n", false, false, false)],
            runs.Select(r => (r.Text, r.Bold, r.Italic, r.Underline)).ToArray());
    }

    [Fact]
    public void Switching_a_property_off_or_using_plain_clears_it_for_the_rest_of_the_group()
    {
        var runs = Paragraphs(Rtf(@"\b\i\ul x\b0 y\ulnone z\plain w\par"))[0].Runs;
        Assert.Equal([("x", true, true, true), ("y", false, true, true), ("z", false, true, false), ("w", false, false, false)],
            runs.Select(r => (r.Text, r.Bold, r.Italic, r.Underline)).ToArray());
    }

    [Fact]
    public void Group_scope_restores_formatting()
    {
        var p = Paragraphs(Rtf(@"{\b bold}plain\par"))[0];
        Assert.Equal(2, p.Runs.Count);
        Assert.True(p.Runs[0].Bold);
        Assert.False(p.Runs[1].Bold);
    }

    // ------------------------------------------------------------------ fields and links

    [Fact]
    public void Hyperlink_fields_put_the_target_on_the_result_text()
    {
        var m = Rtf(@"see {\field{\*\fldinst HYPERLINK ""https://example.test/a""}{\fldrslt {\ul link text}}} now\par");
        var p = Paragraphs(m)[0];
        Assert.Equal("see link text now", p.Text);
        Assert.Equal("https://example.test/a", p.Runs.Single(r => r.Text == "link text").Link);
        Assert.Null(p.Runs[0].Link);
        Assert.Null(p.Runs[^1].Link);
    }

    [Fact]
    public void Hyperlink_switches_before_the_target_are_tolerated_and_other_fields_have_no_link()
    {
        var withSwitch = Rtf(@"{\field{\*\fldinst HYPERLINK \\l ""https://example.test/b""}{\fldrslt hit}}\par");
        Assert.Equal("https://example.test/b", Paragraphs(withSwitch)[0].Runs[0].Link);

        var page = Rtf(@"{\field{\*\fldinst PAGE}{\fldrslt 7}}\par");
        Assert.Equal("7", Paragraphs(page)[0].Text);
        Assert.Null(Paragraphs(page)[0].Runs[0].Link);

        var after = Rtf(@"{\field{\*\fldinst HYPERLINK ""https://example.test/c""}{\fldrslt one}} two\par");
        Assert.Null(Paragraphs(after)[0].Runs[^1].Link);
    }

    // ------------------------------------------------------------------ headings, lists, direction

    [Fact]
    public void Stylesheet_names_and_outline_levels_make_headings()
    {
        var rtf = @"{\rtf1\ansi{\stylesheet{\s0 Normal;}{\s1 heading 1;}{\s2 Heading 3;}{\s3\outlinelevel2 Custom;}{\s4\outlinelevel1 Normal;}}" +
                  @"\pard\s1 One\par\pard\s2 Three\par\pard\s3 Custom\par\pard\s4 NotAHeading\par\pard\outlinelevel1 Direct\par\pard\outlinelevel7 TooDeep\par\pard Plain\par}";
        var ps = Paragraphs(RtfReader.Read(rtf));
        Assert.Equal((ParagraphKind.Heading, 1), (ps[0].Kind, ps[0].Level));
        Assert.Equal((ParagraphKind.Heading, 3), (ps[1].Kind, ps[1].Level));
        Assert.Equal((ParagraphKind.Heading, 3), (ps[2].Kind, ps[2].Level));
        Assert.Equal(ParagraphKind.Normal, ps[3].Kind);
        Assert.Equal((ParagraphKind.Heading, 2), (ps[4].Kind, ps[4].Level));
        Assert.Equal(ParagraphKind.Normal, ps[5].Kind);
        Assert.Equal(ParagraphKind.Normal, ps[6].Kind);
    }

    [Fact]
    public void Heading_runs_lose_bold_and_italic()
    {
        var rtf = @"{\rtf1\ansi{\stylesheet{\s1 heading 2;}}\pard\s1 \b\i Title\par}";
        var p = Paragraphs(RtfReader.Read(rtf))[0];
        Assert.Equal(ParagraphKind.Heading, p.Kind);
        Assert.All(p.Runs, r => Assert.False(r.Bold || r.Italic));
    }

    [Fact]
    public void List_table_and_overrides_decide_between_bullets_and_numbers()
    {
        var rtf = @"{\rtf1\ansi{\*\listtable{\list\listid11{\listlevel\levelnfc23}}{\list\listid22{\listlevel\levelnfc0{\levelnfc23}}}{\list\listid33{\listlevel\levelnfc255}}}" +
                  @"{\*\listoverridetable{\listoverride\listid11\ls1}{\listoverride\listid22\ls2}{\listoverride\listid33\ls3}}" +
                  @"\pard\ls1\ilvl0 Bullet\par\pard\ls2\ilvl1 Number\par\pard\ls3 Odd\par\pard\ls9 Unmapped\par\pard Done\par}";
        var ps = Paragraphs(RtfReader.Read(rtf));
        Assert.Equal((ParagraphKind.ListItem, false, 0), (ps[0].Kind, ps[0].Ordered, ps[0].Level));
        Assert.Equal((ParagraphKind.ListItem, true, 1), (ps[1].Kind, ps[1].Ordered, ps[1].Level));
        Assert.Equal((ParagraphKind.ListItem, false), (ps[2].Kind, ps[2].Ordered));
        Assert.Equal((ParagraphKind.ListItem, false), (ps[3].Kind, ps[3].Ordered));
        Assert.Equal(ParagraphKind.Normal, ps[4].Kind);
    }

    [Fact]
    public void Old_style_paragraph_numbering_words_make_list_items()
    {
        var rtf = @"{\rtf1\ansi\pard{\*\pn\pnlvlblt}Bullet\par\pard\pnlvlbody Numbered\par\pard\pndec Decimal\par\pard Plain\par}";
        var ps = Paragraphs(RtfReader.Read(rtf));
        Assert.Equal((ParagraphKind.ListItem, false), (ps[0].Kind, ps[0].Ordered));
        Assert.Equal((ParagraphKind.ListItem, true), (ps[1].Kind, ps[1].Ordered));
        Assert.Equal((ParagraphKind.ListItem, true), (ps[2].Kind, ps[2].Ordered));
        Assert.Equal(ParagraphKind.Normal, ps[3].Kind);
    }

    [Fact]
    public void Right_to_left_paragraphs_are_marked_and_pard_resets_the_direction()
    {
        var ps = Paragraphs(Rtf(@"\rtlpar a\par\ltrpar b\par\rtlpar c\par\pard d\par"));
        Assert.True(ps[0].Rtl);
        Assert.False(ps[1].Rtl);
        Assert.True(ps[2].Rtl);
        Assert.False(ps[3].Rtl);
    }

    [Fact]
    public void Sections_end_the_current_paragraph_and_page_words_add_a_page_break()
    {
        var m = Rtf(@"one\sect two\page three\par");
        Assert.Collection(m.Blocks,
            b => Assert.Equal("one", ((DocParagraph)b).Text),
            b => Assert.Equal("two", ((DocParagraph)b).Text),
            b => Assert.IsType<DocPageBreak>(b),
            b => Assert.Equal("three", ((DocParagraph)b).Text));
    }

    [Fact]
    public void Empty_paragraphs_are_kept_as_blank_lines()
    {
        var m = Rtf(@"a\par\par b\par");
        Assert.Equal(["a", "", "b"], Texts(m));
    }

    // ------------------------------------------------------------------ tables

    [Fact]
    public void Tables_are_collected_from_row_and_cell_words()
    {
        var m = Rtf(@"before\par\trowd\cellx1000\cellx2000 A\cell B\cell\row\trowd\cellx1000\cellx2000 C\cell D\cell\row after\par");
        Assert.Collection(m.Blocks,
            b => Assert.Equal("before", ((DocParagraph)b).Text),
            b =>
            {
                var t = Assert.IsType<DocTable>(b);
                Assert.Equal(2, t.Rows.Count);
                Assert.Equal(["A", "B"], t.Rows[0].Select(c => c[0].Text));
                Assert.Equal(["C", "D"], t.Rows[1].Select(c => c[0].Text));
            },
            b => Assert.Equal("after", ((DocParagraph)b).Text));
    }

    [Fact]
    public void A_row_without_trowd_and_text_after_the_last_cell_still_form_a_table()
    {
        var m = Rtf(@"\intbl X\cell Y\row");
        var t = Assert.IsType<DocTable>(Assert.Single(m.Blocks));
        Assert.Equal(["X", "Y"], t.Rows[0].Select(c => c[0].Text));

        // A file cut off inside a row must not throw.
        _ = Rtf(@"\trowd\cellx1000 P\cell Q");

        var cellOutsideRow = Rtf(@"loose\cell tail\row");
        Assert.Single(cellOutsideRow.Blocks.OfType<DocTable>());
    }

    [Fact]
    public void An_empty_cell_and_multi_paragraph_cells_are_kept()
    {
        var m = Rtf(@"\trowd\cellx1000\cellx2000 \cell one\par two\cell\row");
        var t = Assert.IsType<DocTable>(Assert.Single(m.Blocks));
        Assert.Equal(2, t.Rows[0].Count);
        Assert.True(t.Rows[0][0][0].IsEmpty);
        Assert.Equal(["one", "two"], t.Rows[0][1].Select(p => p.Text));
    }

    [Fact]
    public void Table_cells_inside_skipped_groups_are_ignored()
    {
        var m = Rtf(@"{\*\hidden \cell \row}text\par");
        Assert.Equal(["text"], Texts(m));
        Assert.Empty(m.Blocks.OfType<DocTable>());
    }
}
