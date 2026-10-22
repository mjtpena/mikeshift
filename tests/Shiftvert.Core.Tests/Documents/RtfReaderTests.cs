using System.Text;
using Shiftvert.Core.Documents;
using static Shiftvert.Core.Tests.Documents.DocumentTestHelpers;

namespace Shiftvert.Core.Tests.Documents;

public class RtfReaderTests
{
    private static DocModel R(string rtf) => RtfReader.Read(Encoding.Latin1.GetBytes(rtf));

    [Fact]
    public void Paragraphs_line_breaks_tabs_and_escapes()
    {
        var m = R(@"{\rtf1\ansi First\par Second\line wrapped\tab tabbed \{braces\} \\ back\par}");
        Assert.Equal(["P:First", "P:Second\nwrapped\ttabbed {braces} \\ back"], Outline(m));
    }

    [Fact]
    public void Trailing_paragraph_mark_does_not_create_an_extra_empty_paragraph()
    {
        Assert.Single(R(@"{\rtf1 one\par}").Blocks);
        Assert.Equal(2, R(@"{\rtf1 one\par\par}").Blocks.Count); // a real blank paragraph
        Assert.Single(R(@"{\rtf1 no paragraph mark}").Blocks);
    }

    [Fact]
    public void Character_formatting_is_group_scoped_and_plain_resets()
    {
        var m = R(@"{\rtf1 a {\b b {\i bi} b2} c \b d\plain e\par}");
        var p = (DocParagraph)m.Blocks[0];
        Assert.Equal("a b bi b2 c de", p.Text);
        Assert.Equal(["a ", "b ", "bi", " b2", " c ", "d", "e"], p.Runs.Select(r => r.Text).ToArray());
        Assert.Equal([false, true, true, true, false, true, false], p.Runs.Select(r => r.Bold).ToArray());
        Assert.Equal([false, false, true, false, false, false, false], p.Runs.Select(r => r.Italic).ToArray());
    }

    [Fact]
    public void B0_and_ulnone_switch_off()
    {
        var p = (DocParagraph)R(@"{\rtf1 \b on\b0 off \ul under\ulnone none\par}").Blocks[0];
        Assert.Equal(["on", "off ", "under", "none"], p.Runs.Select(r => r.Text).ToArray());
        Assert.Equal([true, false, false, false], p.Runs.Select(r => r.Bold).ToArray());
        Assert.Equal([false, false, true, false], p.Runs.Select(r => r.Underline).ToArray());
    }

    [Fact]
    public void Unicode_escapes_skip_the_fallback_characters()
    {
        // \uc1 (default): one fallback char; \uc2: two; \uc0: none. Negative values are signed 16-bit.
        Assert.Equal("P:é-x", Outline(R(@"{\rtf1 \u233?-x\par}"))[0]);
        Assert.Equal("P:éx", Outline(R(@"{\rtf1 \uc2\u233ab x\par}"))[0].Replace(" ", ""));
        Assert.Equal("P:éa", Outline(R(@"{\rtf1 \uc0\u233 a\par}"))[0]);
        Assert.Equal("P:\u00ff|", Outline(R(@"{\rtf1 \u255?|\par}"))[0]);
        Assert.Equal("P:\uF000|", Outline(R(@"{\rtf1 \u-4096?|\par}"))[0]);
        // Surrogate pair for an astral character (😀 U+1F600).
        Assert.Equal("P:😀", Outline(R(@"{\rtf1 \u-10179?\u-8704?\par}"))[0]);
        // A hex fallback also counts as one skipped character.
        Assert.Equal("P:éz", Outline(R(@"{\rtf1 \u233\'e9z\par}"))[0]);
    }

    [Fact]
    public void Hex_escapes_use_ansicpg()
    {
        Assert.Equal("P:é €", Outline(R(@"{\rtf1\ansi\ansicpg1252 \'e9 \'80\par}"))[0]);
        Assert.Equal("P:" + Fixtures.DocHebrewText.Replace(" ", ""), Outline(R(@"{\rtf1\ansi\ansicpg1255 \'f9\'ec\'e5\'ed\'f2\'e5\'ec\'ed\par}"))[0].Replace(" ", ""));
        Assert.Equal("P:Ж", Outline(R(@"{\rtf1\ansi\ansicpg1251 \'c6\par}"))[0]);
    }

    [Fact]
    public void Hex_escapes_use_the_font_charset_when_it_names_one()
    {
        var m = R(@"{\rtf1\ansi\ansicpg1252{\fonttbl{\f0 Calibri;}{\f1\fcharset177 Arial;}}\f0 \'e9\f1 \'e0\par}");
        Assert.Equal("P:éא", Outline(m)[0]);
    }

    [Fact]
    public void Double_byte_code_pages_decode_across_two_hex_escapes()
    {
        // Shift-JIS "日本" = 93 FA 96 7B
        Assert.Equal("P:日本", Outline(R(@"{\rtf1\ansi\ansicpg932 \'93\'fa\'96\'7b\par}"))[0]);
    }

    [Fact]
    public void Header_tables_info_and_unknown_star_destinations_are_skipped()
    {
        var m = R(@"{\rtf1\ansi{\fonttbl{\f0 Hidden font;}}{\colortbl;\red1\green2\blue3;}{\stylesheet{\s1 Hidden style;}}{\info{\title Hidden title}}" +
                  @"{\*\generator Hidden gen}{\*\unknownthing some {nested} text}{\header Hidden header}{\footnote Hidden note}{\*\bkmkstart x}Visible\par}");
        Assert.Equal(["P:Visible"], Outline(m));
    }

    [Fact]
    public void Pictures_set_HasPictures_and_are_skipped_including_binary_data()
    {
        var m = R(@"{\rtf1 before {\pict\pngblip\picw1\pich1 89504e47}after\par}");
        Assert.True(m.HasPictures);
        Assert.Equal(["P:before after"], Outline(m));

        // \bin data may contain braces and backslashes; it is skipped by length.
        var bin = R(@"{\rtf1 a{\pict\wbitmap0\bin5 }{\}\x}b\par}");
        Assert.True(bin.HasPictures);
        Assert.Equal(["P:ab"], Outline(bin));

        Assert.False(R(@"{\rtf1 no picture\par}").HasPictures);
        Assert.True(R(@"{\rtf1 {\*\shppict{\pict abc}}{\nonshppict{\pict abc}}x\par}").HasPictures);
    }

    [Fact]
    public void Headings_come_from_stylesheet_names_or_outline_levels()
    {
        var rtf = @"{\rtf1{\stylesheet{\s0 Normal;}{\s2\sbasedon0 heading 3;}}\pard\s2\b Title three\par\pard\plain Body\par\pard\outlinelevel1 Outlined\par}";
        Assert.Equal(["H3:Title three", "P:Body", "H2:Outlined"], Outline(R(rtf)));
    }

    [Fact]
    public void Lists_bullet_and_decimal_with_levels_and_the_legacy_pn_form()
    {
        var rtf = @"{\rtf1{\*\listtable{\list{\listlevel\levelnfc23{\leveltext\'01\u8226 ?;}}\listid10}{\list{\listlevel\levelnfc0{\leveltext\'02\'00.;}}\listid20}}" +
                  @"{\*\listoverridetable{\listoverride\listid10\ls1}{\listoverride\listid20\ls2}}" +
                  @"\pard\ls1\ilvl0{\listtext\u8226 \tab}bullet\par\pard\ls1\ilvl1 sub\par\pard\ls2\ilvl0 number\par\pard plain\par" +
                  @"\pard{\pntext\'b7\tab}{\*\pn\pnlvlblt}legacy bullet\par\pard{\*\pn\pnlvlbody\pndec}legacy number\par}";
        Assert.Equal(["UL0:bullet", "UL1:sub", "OL0:number", "P:plain", "UL0:legacy bullet", "OL0:legacy number"], Outline(R(rtf)));
    }

    [Fact]
    public void Tables_rows_cells_and_word_style_repeated_trowd()
    {
        var rtf = @"{\rtf1\trowd\cellx1000\cellx2000\pard\intbl a\cell b\cell\trowd\cellx1000\cellx2000\row" +
                  @"\trowd\cellx1000\cellx2000\pard\intbl c1\par c2\cell d\cell\row\pard after\par}";
        var m = R(rtf);
        var t = Assert.IsType<DocTable>(m.Blocks[0]);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal(["a", "b"], t.Rows[0].Select(c => c[0].Text).ToArray());
        Assert.Equal(["c1", "c2"], t.Rows[1][0].Select(p => p.Text).ToArray());
        Assert.Equal("P:after", Outline(m)[1]);
    }

    [Fact]
    public void Hyperlink_fields_keep_the_visible_text_and_target()
    {
        var rtf = @"{\rtf1 see {\field{\*\fldinst{HYPERLINK ""https://example.com/x""}}{\fldrslt {\ul the page}}} now\par}";
        var p = (DocParagraph)R(rtf).Blocks[0];
        Assert.Equal("see the page now", p.Text);
        var link = Assert.Single(p.Runs, r => r.Link is not null);
        Assert.Equal("the page", link.Text);
        Assert.Equal("https://example.com/x", link.Link);
    }

    [Fact]
    public void Page_break_and_rtl_paragraph()
    {
        var m = R(@"{\rtf1 one\par\page two\par\pard\rtlpar \u1488?\u1489?\par}");
        Assert.Equal(["P:one", "PB", "P:two", "P:אב"], Outline(m));
        Assert.True(((DocParagraph)m.Blocks[^1]).Rtl);
    }

    [Fact]
    public void Hidden_text_is_not_extracted()
    {
        Assert.Equal(["P:shown"], Outline(R(@"{\rtf1 {\v secret}shown\par}")));
    }

    [Fact]
    public void Non_rtf_and_empty_input_is_refused()
    {
        Assert.Throws<ConversionException>(() => RtfReader.Read("hello"u8.ToArray()));
        Assert.Throws<ConversionException>(() => RtfReader.Read([]));
    }

    [Fact]
    public void Truncated_and_unbalanced_input_does_not_throw()
    {
        Assert.Equal(["P:text"], Outline(R(@"{\rtf1 {{{text")));
        Assert.Equal(["P:text"], Outline(R(@"{\rtf1 text}}}}}")));
        Assert.Empty(R(@"{\rtf1").Blocks);
        Assert.Empty(R(@"{\rtf1 \").Blocks);
        Assert.Equal(["P:x"], Outline(R(@"{\rtf1 x\u")));
    }

    [Fact]
    public async Task Rtf_file_goes_through_the_converter_to_pdf()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("doc.rtf", Encoding.ASCII.GetBytes(Fixtures.RtfSample()));
        using var guard = OriginalsGuard.Watch(input);
        var pdf = await ConvertAsync(input, DocFormat.Pdf);
        Assert.Contains("Quarterly", PdfFlat(pdf));
    }

    [Fact]
    public void Pathological_nesting_and_long_input_stay_fast()
    {
        var deep = @"{\rtf1 " + new string('{', 50_000) + "deep" + new string('}', 50_000) + @"\par}";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(["P:deep"], Outline(R(deep)));
        var big = @"{\rtf1 " + string.Concat(Enumerable.Repeat(@"{\b word} plain \par ", 50_000)) + "}";
        Assert.Equal(50_000, R(big).Blocks.Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), sw.Elapsed.ToString());
    }
}
