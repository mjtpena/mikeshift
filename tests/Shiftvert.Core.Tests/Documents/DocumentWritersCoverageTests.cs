using System.Text;
using Shiftvert.Core.Documents;

namespace Shiftvert.Core.Tests.Documents;

/// <summary>
/// Writers fed a model full of awkward content (markup characters, control characters, surrogate pairs, bad links, right-to-left
/// text, odd tables) and the result read back: nothing may be lost or corrupted, whatever the format.
/// </summary>
public class DocumentWritersCoverageTests
{
    private const string Hebrew = "שלום עולם";

    /// <summary>A model that touches every writer branch.</summary>
    private static DocModel Awkward()
    {
        var m = new DocModel();
        m.Blocks.Add(DocParagraph.Heading(1, "Title <&> \"quoted\""));
        m.Blocks.Add(DocParagraph.Heading(6, "Deep heading"));

        var rich = new DocParagraph();
        rich.Add("plain ");
        rich.Add("bold", bold: true);
        rich.Add(" ");
        rich.Add("italic", italic: true);
        rich.Add(" ");
        rich.Add("under", underline: true);
        rich.Add(" ");
        rich.Add("all", bold: true, italic: true, underline: true);
        rich.Add(" ");
        rich.Add("web", link: "https://example.test/a?b=1&c=2");
        rich.Add(" ");
        rich.Add("relative", link: "not a uri");
        m.Blocks.Add(rich);

        var chars = new DocParagraph();
        chars.Add("tab\there\nnext line, back\\slash {braces} <b>&amp;</b> \"q\" café \U0001F600 ctrl\u0001char  double  spaces");
        m.Blocks.Add(chars);

        var rtl = new DocParagraph { Rtl = true };
        rtl.Add(Hebrew);
        rtl.Add(" and Latin", bold: true);
        m.Blocks.Add(rtl);

        void Item(int level, bool ordered, string text) =>
            m.Blocks.Add(new DocParagraph { Kind = ParagraphKind.ListItem, Level = level, Ordered = ordered, Runs = { new DocRun(text) } });
        Item(0, false, "bullet one");
        Item(1, false, "bullet nested");
        Item(0, true, "number one");
        Item(0, true, "number two");
        Item(1, true, "number nested");
        m.Blocks.Add(DocParagraph.Plain("between lists"));
        Item(0, true, "restarted");

        var t = new DocTable();
        t.Rows.Add([[DocParagraph.Plain("H1")], [DocParagraph.Plain("H2")], [DocParagraph.Plain("H3")]]);
        t.Rows.Add([[DocParagraph.Plain("two"), DocParagraph.Plain("paragraphs")], [], [DocParagraph.Plain("x < y & z")]]);
        t.Rows.Add([[DocParagraph.Plain("short row")]]);
        m.Blocks.Add(t);

        m.Blocks.Add(new DocPageBreak());
        m.Blocks.Add(DocParagraph.Plain("after the break"));
        m.Blocks.Add(new DocParagraph());
        m.Blocks.Add(DocParagraph.Plain("last"));
        return m;
    }

    private static string[] TextsOf(DocModel m) => [.. m.AllParagraphs().Where(p => !p.IsEmpty).Select(p => p.Text)];

    /// <summary>What the paragraphs of <see cref="Awkward"/> must read back as (the control character is dropped, the rest survives).</summary>
    private static readonly string[] ExpectedTexts =
    [
        "Title <&> \"quoted\"", "Deep heading",
        "plain bold italic under all web relative",
        "tab\there\nnext line, back\\slash {braces} <b>&amp;</b> \"q\" café \U0001F600 ctrlchar  double  spaces",
        Hebrew + " and Latin",
        "bullet one", "bullet nested", "number one", "number two", "number nested", "between lists", "restarted",
        "H1", "H2", "H3", "two", "paragraphs", "x < y & z", "short row",
        "after the break", "last",
    ];

    private static string[] Normalise(IEnumerable<string> texts) =>
        [.. texts.Select(t => t.Replace(" ", " "))];

    private static byte[] Write(DocFormat format, DocModel model)
    {
        using var ms = new MemoryStream();
        DocumentConverter.Write(model, format, ms, "Awkward title <&>", a4: true);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ DOCX

    [Fact]
    public void Docx_round_trips_the_awkward_model()
    {
        var back = DocxReader.Read(new MemoryStream(Write(DocFormat.Docx, Awkward())));
        Assert.Equal(ExpectedTexts, TextsOf(back));

        var ps = back.AllParagraphs().ToArray();
        Assert.Equal((ParagraphKind.Heading, 1), (ps[0].Kind, ps[0].Level));
        Assert.Equal((ParagraphKind.Heading, 6), (ps[1].Kind, ps[1].Level));
        var rich = ps[2];
        Assert.Equal("https://example.test/a?b=1&c=2", rich.Runs.Single(r => r.Text == "web").Link);
        Assert.Null(rich.Runs.Single(r => r.Text.Contains("relative")).Link); // not a URI: kept as text, no hyperlink
        Assert.True(rich.Runs.Single(r => r.Text == "all") is { Bold: true, Italic: true, Underline: true });
        Assert.True(ps[4].Rtl);
        Assert.Contains(back.Blocks, b => b is DocPageBreak);
        Assert.Equal(3, back.Blocks.OfType<DocTable>().Single().Rows.Count);
        Assert.Equal([(ParagraphKind.ListItem, 0, false), (ParagraphKind.ListItem, 1, false), (ParagraphKind.ListItem, 0, true), (ParagraphKind.ListItem, 0, true), (ParagraphKind.ListItem, 1, true)],
            ps.Skip(5).Take(5).Select(p => (p.Kind, p.Level, p.Ordered)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Docx_uses_a4_or_letter_page_sizes(bool a4)
    {
        using var ms = new MemoryStream();
        DocxWriter.Write(Awkward(), ms, a4);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(ms.ToArray()));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains(a4 ? "w:w=\"11906\"" : "w:w=\"12240\"", xml);
    }

    // ------------------------------------------------------------------ ODT

    [Fact]
    public void Odt_round_trips_the_awkward_model()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("out.odt", Write(DocFormat.Odt, Awkward()));
        var back = OdtReader.Read(path);
        Assert.Equal(ExpectedTexts, Normalise(TextsOf(back)));

        var rich = back.AllParagraphs().ToArray()[2];
        Assert.Equal("https://example.test/a?b=1&c=2", rich.Runs.Single(r => r.Text == "web").Link);
        Assert.Null(rich.Runs.Single(r => r.Text.Contains("relative")).Link);
        Assert.True(rich.Runs.Single(r => r.Text == "all") is { Bold: true, Italic: true, Underline: true });
        Assert.Contains(back.Blocks, b => b is DocPageBreak);
        Assert.True(back.AllParagraphs().Single(p => p.Text.StartsWith("ש")).Rtl);
    }

    // ------------------------------------------------------------------ RTF

    [Fact]
    public void Rtf_round_trips_the_awkward_model()
    {
        var back = RtfReader.Read(Write(DocFormat.Rtf, Awkward()));
        Assert.Equal(ExpectedTexts, Normalise(TextsOf(back)));

        var rich = back.AllParagraphs().ToArray()[2];
        Assert.Equal("https://example.test/a?b=1&c=2", rich.Runs.Single(r => r.Text == "web").Link);
        Assert.Null(rich.Runs.Single(r => r.Text.Contains("relative")).Link);
        Assert.True(rich.Runs.Single(r => r.Text == "all") is { Bold: true, Italic: true, Underline: true });
        Assert.Contains(back.Blocks, b => b is DocPageBreak);
        Assert.Equal(3, back.Blocks.OfType<DocTable>().Single().Rows.Count);
        Assert.True(back.AllParagraphs().Single(p => p.Text.StartsWith("ש")).Rtl);
    }

    [Fact]
    public void Rtf_output_is_pure_ascii_and_escapes_every_delimiter()
    {
        var bytes = Write(DocFormat.Rtf, Awkward());
        Assert.All(bytes, b => Assert.True(b < 0x80, "non-ASCII byte in RTF output"));
        var text = Encoding.ASCII.GetString(bytes);
        Assert.Contains("\\\\slash \\{braces\\}", text);
        Assert.Contains("\\u233?", text);        // é
        Assert.Contains("\\u-10179?\\u-8704?", text); // the surrogate pair of the emoji, as two signed UTF-16 units
        Assert.DoesNotContain('\u0001', text);
    }

    // ------------------------------------------------------------------ HTML

    [Fact]
    public void Html_round_trips_the_awkward_model_and_escapes_markup()
    {
        var bytes = Write(DocFormat.Html, Awkward());
        var html = Encoding.UTF8.GetString(bytes);
        Assert.Contains("&lt;b&gt;&amp;amp;&lt;/b&gt;", html);
        Assert.Contains("&quot;q&quot;", html);
        Assert.Contains("<title>Awkward title &lt;&amp;&gt;</title>", html);
        Assert.DoesNotContain("<b>&amp;</b>", html);

        var back = HtmlReader.Read(TextDecoding.DecodeHtml(bytes));
        Assert.Equal(ExpectedTexts, Normalise(TextsOf(back)));
        var rich = back.AllParagraphs().ToArray()[2];
        Assert.Equal("https://example.test/a?b=1&c=2", rich.Runs.Single(r => r.Text == "web").Link);
        Assert.Null(rich.Runs.Single(r => r.Text.Contains("relative")).Link);
        Assert.Contains(back.Blocks, b => b is DocPageBreak);
        Assert.Equal(3, back.Blocks.OfType<DocTable>().Single().Rows.Count);
        Assert.True(back.AllParagraphs().Single(p => p.Text.StartsWith("ש")).Rtl);
    }

    // ------------------------------------------------------------------ TXT

    [Fact]
    public void Txt_writes_utf8_without_bom_crlf_numbers_and_tab_separated_tables()
    {
        var bytes = Write(DocFormat.Txt, Awkward());
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("\r\r", text);
        var lines = text.Split("\r\n");
        Assert.Contains("• bullet one", lines);
        Assert.Contains("  • bullet nested", lines);
        Assert.Contains("1. number one", lines);
        Assert.Contains("2. number two", lines);
        Assert.Contains("  1. number nested", lines);
        Assert.Contains("1. restarted", lines);   // a paragraph in between restarts the numbering
        Assert.Contains("H1\tH2\tH3", lines);
        Assert.Contains("two paragraphs\t\tx < y & z", lines);
        Assert.Contains("short row", lines);
        Assert.Contains("tab\there\r\nnext line, back\\slash {braces} <b>&amp;</b> \"q\" café 😀 ctrl\u0001char  double  spaces", text);
    }

    [Fact]
    public void Txt_flattens_line_breaks_inside_table_cells_and_marks_page_breaks_with_a_blank_line()
    {
        var m = new DocModel();
        var cell = new DocParagraph();
        cell.Add("one\r\ntwo\rthree\nfour");
        var t = new DocTable();
        t.Rows.Add([[cell]]);
        m.Blocks.Add(t);
        m.Blocks.Add(new DocPageBreak());
        m.Blocks.Add(DocParagraph.Plain("end"));
        var text = Encoding.UTF8.GetString(Write(DocFormat.Txt, m));
        Assert.Equal("one two three four\r\n\r\nend\r\n", text);
    }

    // ------------------------------------------------------------------ PDF

    [Fact]
    public void Pdf_of_the_awkward_model_contains_its_text_and_spans_a_page_break()
    {
        using var tmp = new TempDir();
        var bytes = Write(DocFormat.Pdf, Awkward());
        var path = tmp.WriteFile("out.pdf", bytes);
        var flat = DocumentTestHelpers.PdfFlat(path);
        Assert.Contains("Deep heading", flat);
        Assert.Contains("bullet nested", flat);
        Assert.Contains("short row", flat);
        Assert.Contains("after the break", flat);
        Assert.Equal(2, DocumentTestHelpers.PdfPageCount(path));
    }

    [Fact]
    public void Pdf_keeps_line_breaks_and_wraps_long_unbroken_words()
    {
        using var tmp = new TempDir();
        var p = new DocParagraph();
        p.Add("first\nsecond\n\nfourth after a blank line " + new string('W', 400));
        var m = new DocModel();
        m.Blocks.Add(p);
        var path = tmp.WriteFile("out.pdf", Write(DocFormat.Pdf, m));
        var flat = DocumentTestHelpers.PdfFlat(path);
        Assert.Contains("first", flat);
        Assert.Contains("second", flat);
        Assert.Contains("fourth", flat);
        Assert.Equal(400, flat.Count(c => c == 'W')); // a 400-character word is split across lines, not dropped or clipped
    }

    [Fact]
    public void Pdf_splits_a_table_row_taller_than_a_page_over_several_pages()
    {
        using var tmp = new TempDir();
        var tall = new List<DocParagraph>();
        for (var i = 0; i < 140; i++) tall.Add(DocParagraph.Plain($"cell line {i:000}"));
        var t = new DocTable();
        t.Rows.Add([tall, [DocParagraph.Plain("beside")]]);
        t.Rows.Add([[DocParagraph.Plain("next row")], [DocParagraph.Plain("done")]]);
        var m = new DocModel();
        m.Blocks.Add(DocParagraph.Plain("intro"));
        m.Blocks.Add(t);
        var path = tmp.WriteFile("out.pdf", Write(DocFormat.Pdf, m));
        var flat = DocumentTestHelpers.PdfFlat(path);
        Assert.Contains("cell line 000", flat);
        Assert.Contains("cell line 139", flat);
        Assert.Contains("next row", flat);
        Assert.True(DocumentTestHelpers.PdfPageCount(path) >= 3);
    }

    [Fact]
    public void Pdf_moves_a_short_row_that_does_not_fit_the_rest_of_the_page_to_the_next_page_whole()
    {
        using var tmp = new TempDir();
        var m = new DocModel();
        for (var i = 0; i < 40; i++) m.Blocks.Add(DocParagraph.Plain($"filler {i}"));
        var t = new DocTable();
        t.Rows.Add([[.. Enumerable.Range(0, 12).Select(i => DocParagraph.Plain($"row line {i}"))]]);
        m.Blocks.Add(t);
        var path = tmp.WriteFile("out.pdf", Write(DocFormat.Pdf, m));
        var flat = DocumentTestHelpers.PdfFlat(path);
        Assert.Contains("row line 0", flat);
        Assert.Contains("row line 11", flat);
        Assert.Equal(2, DocumentTestHelpers.PdfPageCount(path));
    }

    [Fact]
    public void Pdf_handles_every_script_group_without_failing()
    {
        using var tmp = new TempDir();
        var p = new DocParagraph();
        p.Add("Latin αβγ жив 日本語 한글 ไทย हिन्दी မြန်မာ →∑ \U0001F600 العربية zero​width️ soft‑hyphen end");
        p.Add(" bold CJK 日本", bold: true, italic: true, underline: true);
        var m = new DocModel();
        m.Blocks.Add(p);
        var path = tmp.WriteFile("out.pdf", Write(DocFormat.Pdf, m));
        var flat = DocumentTestHelpers.PdfFlat(path);
        Assert.Contains("Latin", flat);
        Assert.Contains("end", flat);
        Assert.Contains("soft-hyphen", flat); // U+2011 is printed as an ordinary hyphen
        Assert.DoesNotContain(flat, c => c == (char)0x200B); // zero-width space is dropped, not drawn as a box
    }

    [Fact]
    public void Pdf_right_to_left_text_is_reordered_visually_in_paragraphs_and_cells()
    {
        using var tmp = new TempDir();
        var rtl = new DocParagraph { Rtl = true };
        rtl.Add(Hebrew + " (abc) 123");
        rtl.Add(" bold", bold: true);
        var t = new DocTable();
        t.Rows.Add([[rtl], [DocParagraph.Plain("left")]]);
        var m = new DocModel();
        m.Blocks.Add(rtl);
        m.Blocks.Add(t);
        var path = tmp.WriteFile("out.pdf", Write(DocFormat.Pdf, m));
        var flat = DocumentTestHelpers.PdfFlat(path);
        Assert.Contains("abc", flat);
        Assert.Contains("123", flat);
        Assert.Contains("left", flat);
    }

    [Fact]
    public void Pdf_refuses_documents_beyond_the_size_limits_before_laying_anything_out()
    {
        var huge = new DocModel();
        huge.Blocks.Add(DocParagraph.Plain(new string('a', (int)PdfDocWriter.MaxChars + 1)));
        var ex = Assert.Throws<ConversionException>(() => PdfDocWriter.Write(huge, new MemoryStream(), true, "t", CancellationToken.None));
        Assert.Equal("This document is too long to convert to PDF.", ex.Message);

        var hugeRtl = new DocModel();
        hugeRtl.Blocks.Add(DocParagraph.Plain(new string('א', (int)PdfDocWriter.MaxRtlChars + 1)));
        var rtlEx = Assert.Throws<ConversionException>(() => PdfDocWriter.Write(hugeRtl, new MemoryStream(), true, "t", CancellationToken.None));
        Assert.Equal("This right-to-left document is too long to convert to PDF.", rtlEx.Message);
    }

    [Fact]
    public void Pdf_layout_honours_cancellation_between_table_rows()
    {
        var t = new DocTable();
        for (var i = 0; i < 20; i++) t.Rows.Add([[DocParagraph.Plain($"row {i}")]]);
        var m = new DocModel();
        m.Blocks.Add(t);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => PdfDocWriter.Write(m, new MemoryStream(), true, "t", cts.Token));
    }

    [Theory]
    [InlineData(0x41, PdfDocWriter.Face.Default)]
    [InlineData(0x05D0, PdfDocWriter.Face.Default)]
    [InlineData(0x0627, PdfDocWriter.Face.Default)]
    [InlineData(0x0905, PdfDocWriter.Face.Indic)]
    [InlineData(0x0DFF, PdfDocWriter.Face.Indic)]
    [InlineData(0x0E01, PdfDocWriter.Face.Thai)]
    [InlineData(0x0E81, PdfDocWriter.Face.Thai)]
    [InlineData(0x1019, PdfDocWriter.Face.Myanmar)]
    [InlineData(0x1100, PdfDocWriter.Face.Hangul)]
    [InlineData(0x1780, PdfDocWriter.Face.Thai)]
    [InlineData(0x2192, PdfDocWriter.Face.Symbol)]
    [InlineData(0x2211, PdfDocWriter.Face.Symbol)]
    [InlineData(0x2E80, PdfDocWriter.Face.Cjk)]
    [InlineData(0x3042, PdfDocWriter.Face.Cjk)]
    [InlineData(0x3105, PdfDocWriter.Face.Cjk)]
    [InlineData(0x3131, PdfDocWriter.Face.Hangul)]
    [InlineData(0x31F0, PdfDocWriter.Face.Cjk)]
    [InlineData(0x3400, PdfDocWriter.Face.Cjk)]
    [InlineData(0x4E2D, PdfDocWriter.Face.Cjk)]
    [InlineData(0xA8E0, PdfDocWriter.Face.Indic)]
    [InlineData(0xA960, PdfDocWriter.Face.Hangul)]
    [InlineData(0xAC00, PdfDocWriter.Face.Hangul)]
    [InlineData(0xF900, PdfDocWriter.Face.Cjk)]
    [InlineData(0xFE30, PdfDocWriter.Face.Cjk)]
    [InlineData(0xFF21, PdfDocWriter.Face.Cjk)]
    [InlineData(0x1F600, PdfDocWriter.Face.Emoji)]
    [InlineData(0x20000, PdfDocWriter.Face.Cjk)]
    [InlineData(0x10FFFF, PdfDocWriter.Face.Default)]
    [InlineData(0x0F00, PdfDocWriter.Face.Default)] // Tibetan: no special font
    public void Code_points_map_to_the_font_group_that_can_draw_them(int codePoint, PdfDocWriter.Face expected) =>
        Assert.Equal(expected, PdfDocWriter.FaceOf(codePoint));

    [Fact]
    public void Default_face_text_never_asks_for_a_fallback_font()
    {
        Assert.Null(PdfDocWriter.FamilyFor(PdfDocWriter.Face.Default));
    }

    // ------------------------------------------------------------------ empty and trivial documents

    [Theory]
    [InlineData(DocFormat.Docx)]
    [InlineData(DocFormat.Odt)]
    [InlineData(DocFormat.Rtf)]
    [InlineData(DocFormat.Html)]
    [InlineData(DocFormat.Txt)]
    [InlineData(DocFormat.Pdf)]
    public void An_empty_document_is_still_a_valid_file_of_every_format(DocFormat format)
    {
        var bytes = Write(format, new DocModel());
        using var tmp = new TempDir();
        switch (format)
        {
            case DocFormat.Docx: Assert.Empty(TextsOf(DocxReader.Read(new MemoryStream(bytes)))); break;
            case DocFormat.Odt: Assert.Empty(TextsOf(OdtReader.Read(tmp.WriteFile("e.odt", bytes)))); break;
            case DocFormat.Rtf: Assert.Empty(TextsOf(RtfReader.Read(bytes))); break;
            case DocFormat.Html: Assert.Empty(TextsOf(HtmlReader.Read(Encoding.UTF8.GetString(bytes)))); break;
            case DocFormat.Txt: Assert.Empty(bytes); break;
            case DocFormat.Pdf: Assert.Equal(1, DocumentTestHelpers.PdfPageCount(tmp.WriteFile("e.pdf", bytes))); break;
        }
    }

    [Fact]
    public void Right_to_left_text_without_an_explicit_mark_is_detected_by_every_writer()
    {
        var m = new DocModel();
        m.Blocks.Add(DocParagraph.Plain(Hebrew)); // Rtl flag not set: the first strong letter decides
        using var tmp = new TempDir();
        Assert.True(DocxReader.Read(new MemoryStream(Write(DocFormat.Docx, m))).AllParagraphs().Single().Rtl);
        Assert.True(OdtReader.Read(tmp.WriteFile("r.odt", Write(DocFormat.Odt, m))).AllParagraphs().Single().Rtl);
        Assert.True(RtfReader.Read(Write(DocFormat.Rtf, m)).AllParagraphs().Single().Rtl);
        Assert.True(HtmlReader.Read(Encoding.UTF8.GetString(Write(DocFormat.Html, m))).AllParagraphs().Single().Rtl);
    }
}
