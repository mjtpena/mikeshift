using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Shiftvert.Core.Documents;
using static Shiftvert.Core.Tests.Documents.DocumentTestHelpers;

namespace Shiftvert.Core.Tests.Documents;

[Collection(WordCollection.Name)]
public class DocumentConversionTests
{
    public static TheoryData<string, DocFormat> Pairs()
    {
        var data = new TheoryData<string, DocFormat>();
        foreach (var source in new[] { "docx", "odt", "rtf", "html" })
            foreach (var target in new[] { DocFormat.Docx, DocFormat.Odt, DocFormat.Rtf, DocFormat.Html })
                if (DocumentConverter.ExtensionFor(target) != "." + source)
                    data.Add(source, target);
        return data;
    }

    /// <summary>Every reader to every writer that can carry the structure: re-reading the output gives the same outline.</summary>
    [Theory, MemberData(nameof(Pairs))]
    public async Task Reader_to_writer_pair_round_trips_text_and_formatting(string source, DocFormat target)
    {
        using var tmp = new TempDir();
        var input = Sample(tmp, source);
        using var guard = OriginalsGuard.Watch(input);

        var output = await ConvertAsync(input, target);

        Assert.Equal(tmp.Combine("sample" + ExtensionOf(target)), output);
        Assert.Equal(Fixtures.DocumentOutline, Outline(Read(output)));
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("odt")]
    [InlineData("rtf")]
    [InlineData("html")]
    public async Task Any_reader_to_Txt_keeps_the_text(string source)
    {
        using var tmp = new TempDir();
        var input = Sample(tmp, source);
        var output = await ConvertAsync(input, DocFormat.Txt);

        var text = File.ReadAllText(output, Encoding.UTF8);
        Assert.Contains("Quarterly Report", text);
        Assert.Contains("Revenue grew by twelve percent and costs were flat.", text);
        Assert.Contains("Plain underlined words.", text);
        Assert.Contains("• Apples", text);
        Assert.Contains("• Oranges", text);
        Assert.Contains("1. First", text);
        Assert.Contains("2. Second", text);
        Assert.Contains("Name\tQty", text);
        Assert.Contains("Apples\t3", text);
        Assert.False(File.ReadAllBytes(output).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "TXT output is UTF-8 without BOM");
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("odt")]
    [InlineData("rtf")]
    [InlineData("html")]
    public async Task Any_reader_to_Pdf_contains_the_text(string source)
    {
        using var tmp = new TempDir();
        var input = Sample(tmp, source);
        using var guard = OriginalsGuard.Watch(input);
        var output = await ConvertAsync(input, DocFormat.Pdf);

        Assert.Equal(tmp.Combine("sample.pdf"), output);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 4));
        var text = PdfFlat(output);
        Assert.Contains("Quarterly Report", text);
        Assert.Contains("Overview", text);
        Assert.Contains("Revenue grew by twelve percent and costs were flat.", text);
        Assert.Contains("underlined", text);
        Assert.Contains("Apples", text);
        Assert.Contains("Oranges", text);
        Assert.Contains("First", text);
        Assert.Contains("Qty", text);
        Assert.Contains("•", text); // bullets
        Assert.Contains("1.", text);
        Assert.Contains("2.", text);
    }

    [Fact]
    public async Task Txt_and_md_convert_to_every_format()
    {
        using var tmp = new TempDir();
        var txt = tmp.WriteFile("notes.txt", "First line\r\nSecond line\r\n\r\nFourth");
        foreach (var f in new[] { DocFormat.Docx, DocFormat.Odt, DocFormat.Rtf, DocFormat.Html })
        {
            var outPath = await ConvertAsync(txt, f);
            Assert.Equal(["P:First line", "P:Second line", "P:Fourth"], Outline(Read(outPath)));
        }

        var pdf = await ConvertAsync(txt, DocFormat.Pdf);
        Assert.Contains("Second line", PdfFlat(pdf));

        var md = tmp.WriteFile("readme.md", Fixtures.MarkdownSample());
        var mdDocx = await ConvertAsync(md, DocFormat.Docx);
        Assert.Equal("# Title", ((DocParagraph)Read(mdDocx).Blocks[0]).Text); // Markdown stays plain text
        var mdTxt = await ConvertAsync(md, DocFormat.Txt);
        Assert.Equal("readme.txt", Path.GetFileName(mdTxt));
        Assert.Contains("- one", File.ReadAllText(mdTxt));
    }

    [Fact]
    public async Task Output_is_next_to_the_input_and_never_overwrites()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("report.rtf", Encoding.ASCII.GetBytes(Fixtures.RtfSample()));
        var first = await ConvertAsync(input, DocFormat.Pdf);
        var second = await ConvertAsync(input, DocFormat.Pdf);
        Assert.Equal(tmp.Combine("report.pdf"), first);
        Assert.Equal(tmp.Combine("report 2.pdf"), second);
        Assert.Empty(Directory.GetFiles(tmp.Path, "*.shiftvert-tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_failed_conversion_leaves_no_partial_output_and_no_temp_file()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("bad.docx", "this is not a zip");
        await Assert.ThrowsAsync<ConversionException>(() => ConvertAsync(input, DocFormat.Pdf));
        Assert.Equal(["bad.docx"], Directory.GetFiles(tmp.Path).Select(f => Path.GetFileName(f)!).ToArray());
    }

    // ------------------------------------------------------------------ formatting details per writer

    [Fact]
    public async Task Docx_output_is_valid_and_uses_real_heading_styles_and_numbering()
    {
        using var tmp = new TempDir();
        var output = await ConvertAsync(Sample(tmp, "html"), DocFormat.Docx);

        using var doc = WordprocessingDocument.Open(output, false);
        var errors = new OpenXmlValidator().Validate(doc).Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));

        var xml = new StreamReader(doc.MainDocumentPart!.GetStream()).ReadToEnd();
        Assert.Contains("w:val=\"Heading1\"", xml);
        Assert.Contains("w:val=\"Heading2\"", xml);
        Assert.Contains("<w:numPr>", xml);
        Assert.Contains("<w:tbl>", xml);
        Assert.NotNull(doc.MainDocumentPart.NumberingDefinitionsPart);
        Assert.NotNull(doc.MainDocumentPart.StyleDefinitionsPart);
    }

    [Fact]
    public async Task Docx_numbered_lists_restart_at_one()
    {
        using var tmp = new TempDir();
        var html = tmp.WriteFile("two.html", "<ol><li>a</li><li>b</li></ol><p>x</p><ol><li>c</li></ol>");
        var output = await ConvertAsync(html, DocFormat.Docx);
        using var doc = WordprocessingDocument.Open(output, false);
        var xml = new StreamReader(doc.MainDocumentPart!.GetStream()).ReadToEnd();
        var ids = System.Text.RegularExpressions.Regex.Matches(xml, "<w:numId w:val=\"(\\d+)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(3, ids.Length);
        Assert.Equal(ids[0], ids[1]);
        Assert.NotEqual(ids[1], ids[2]);
        Assert.Equal(["OL0:a", "OL0:b", "P:x", "OL0:c"], Outline(Read(output)));
    }

    [Fact]
    public async Task Odt_package_is_valid_mimetype_first_and_stored()
    {
        using var tmp = new TempDir();
        var output = await ConvertAsync(Sample(tmp, "docx"), DocFormat.Odt);

        var bytes = File.ReadAllBytes(output);
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
        // Local file header of the first entry: method at offset 8 (0 = stored), name length at 26, extra length at 28, name at 30.
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 8));
        var nameLen = BitConverter.ToUInt16(bytes, 26);
        var extraLen = BitConverter.ToUInt16(bytes, 28);
        Assert.Equal("mimetype", Encoding.ASCII.GetString(bytes, 30, nameLen));
        Assert.Equal(0, extraLen);
        Assert.Equal("application/vnd.oasis.opendocument.text", Encoding.ASCII.GetString(bytes, 30 + nameLen, 39));

        using var zip = ZipFile.OpenRead(output);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.All(new[] { "META-INF/manifest.xml", "content.xml", "styles.xml", "meta.xml" }, n => Assert.NotNull(zip.GetEntry(n)));
        foreach (var name in new[] { "META-INF/manifest.xml", "content.xml", "styles.xml", "meta.xml" })
        {
            using var s = zip.GetEntry(name)!.Open();
            System.Xml.Linq.XDocument.Load(s); // well-formed
        }
    }

    [Fact]
    public async Task Odt_preserves_runs_of_spaces_tabs_and_line_breaks()
    {
        using var tmp = new TempDir();
        var model = new DocModel();
        var p = new DocParagraph();
        p.Add("  lead   gap\tTab\nNext  ");
        model.Blocks.Add(p);
        var path = tmp.Combine("sp.odt");
        using (var fs = File.Create(path)) DocumentConverter.Write(model, DocFormat.Odt, fs, "sp", a4: true);
        Assert.Equal("  lead   gap\tTab\nNext  ", ((DocParagraph)Read(path).Blocks[0]).Text);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Html_output_is_utf8_with_rtl_direction_and_escaped_text()
    {
        using var tmp = new TempDir();
        var model = new DocModel();
        model.Blocks.Add(DocParagraph.Plain(Fixtures.DocHebrewText));
        model.Blocks.Add(DocParagraph.Plain("a < b & c > d \"q\""));
        var path = tmp.Combine("x.html");
        using (var fs = File.Create(path)) DocumentConverter.Write(model, DocFormat.Html, fs, "x", a4: true);

        var text = File.ReadAllText(path, new UTF8Encoding(false, true));
        Assert.Contains("<meta charset=\"utf-8\">", text);
        Assert.Contains("<p dir=\"rtl\" style=\"white-space:pre-wrap\">" + Fixtures.DocHebrewText + "</p>", text);
        Assert.Contains("a &lt; b &amp; c &gt; d &quot;q&quot;", text);
        Assert.DoesNotContain("<script", text);
        Assert.Equal(["P:" + Fixtures.DocHebrewText, "P:a < b & c > d \"q\""], Outline(Read(path)));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Rtf_output_is_seven_bit_and_escapes_specials()
    {
        using var tmp = new TempDir();
        var model = new DocModel();
        model.Blocks.Add(DocParagraph.Plain("braces { } backslash \\ café — Ελ " + Fixtures.DocHebrewText + " 😀"));
        var path = tmp.Combine("x.rtf");
        using (var fs = File.Create(path)) DocumentConverter.Write(model, DocFormat.Rtf, fs, "x", a4: true);

        var bytes = File.ReadAllBytes(path);
        Assert.All(bytes, b => Assert.True(b < 0x80));
        var back = (DocParagraph)Read(path).Blocks[0];
        Assert.Equal("braces { } backslash \\ café — Ελ " + Fixtures.DocHebrewText + " 😀", back.Text);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Rtl_text_round_trips_through_every_writer()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("heb.txt", Fixtures.DocTxtHebrew1255());
        foreach (var f in new[] { DocFormat.Docx, DocFormat.Odt, DocFormat.Rtf, DocFormat.Html })
        {
            var path = await ConvertAsync(input, f);
            var model = Read(path);
            Assert.Equal([Fixtures.DocHebrewText, "שלום שוב"], model.Blocks.Cast<DocParagraph>().Select(p => p.Text).ToArray());
            Assert.All(model.Blocks.Cast<DocParagraph>(), p => Assert.True(p.EffectiveRtl));
        }
        // Explicit RTL markers survive the formats that carry them.
        foreach (var f in new[] { DocFormat.Docx, DocFormat.Odt, DocFormat.Rtf, DocFormat.Html })
            Assert.All(Read(await ConvertAsync(input, f)).Blocks.Cast<DocParagraph>(), p => Assert.True(p.Rtl, f.ToString()));
    }

    [Fact]
    public async Task Tables_with_uneven_rows_and_multiple_paragraphs_round_trip()
    {
        using var tmp = new TempDir();
        var model = new DocModel();
        var t = new DocTable();
        t.Rows.Add([[DocParagraph.Plain("a")], [DocParagraph.Plain("b1"), DocParagraph.Plain("b2")]]);
        t.Rows.Add([[DocParagraph.Plain("c")]]);
        model.Blocks.Add(t);
        model.Blocks.Add(DocParagraph.Plain("after"));
        foreach (var f in new[] { DocFormat.Docx, DocFormat.Odt, DocFormat.Rtf, DocFormat.Html })
        {
            var path = tmp.Combine("t" + ExtensionOf(f));
            using (var fs = File.Create(path)) DocumentConverter.Write(model, f, fs, "t", a4: true);
            var read = Read(path);
            var table = Assert.IsType<DocTable>(read.Blocks[0]);
            Assert.Equal(2, table.Rows.Count);
            Assert.Equal(["b1", "b2"], table.Rows[0][1].Select(p => p.Text).ToArray());
            Assert.Equal("c", table.Rows[1][0][0].Text);
            Assert.Equal("after", ((DocParagraph)read.Blocks[^1]).Text);
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Page_breaks_and_nested_lists_round_trip()
    {
        using var tmp = new TempDir();
        var model = new DocModel();
        model.Blocks.Add(DocParagraph.Plain("one"));
        model.Blocks.Add(new DocPageBreak());
        model.Blocks.Add(new DocParagraph { Kind = ParagraphKind.ListItem, Level = 0, Runs = { new DocRun("a") } });
        model.Blocks.Add(new DocParagraph { Kind = ParagraphKind.ListItem, Level = 1, Runs = { new DocRun("b") } });
        model.Blocks.Add(new DocParagraph { Kind = ParagraphKind.ListItem, Level = 2, Ordered = true, Runs = { new DocRun("c") } });
        model.Blocks.Add(new DocParagraph { Kind = ParagraphKind.ListItem, Level = 0, Runs = { new DocRun("d") } });
        model.Blocks.Add(DocParagraph.Plain("end"));
        string[] expected = ["P:one", "PB", "UL0:a", "UL1:b", "OL2:c", "UL0:d", "P:end"];
        foreach (var f in new[] { DocFormat.Docx, DocFormat.Odt, DocFormat.Rtf, DocFormat.Html })
        {
            var path = tmp.Combine("p" + ExtensionOf(f));
            using (var fs = File.Create(path)) DocumentConverter.Write(model, f, fs, "p", a4: true);
            var outline = Outline(Read(path));
            // HTML nests <ol> inside <ul>: the ordered flag of a deeper item is kept, but the level is the nesting depth.
            Assert.Equal(f == DocFormat.Html ? ["P:one", "PB", "UL0:a", "UL1:b", "OL2:c", "UL0:d", "P:end"] : expected, outline);
        }
        await Task.CompletedTask;
    }

    // ------------------------------------------------------------------ PDF specifics

    [Fact]
    public async Task Pdf_page_size_is_A4_in_metric_regions_and_Letter_otherwise()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.txt", "hello");
        var original = DocumentConverter.UseA4;
        try
        {
            DocumentConverter.UseA4 = () => true;
            var a4 = await ConvertAsync(input, DocFormat.Pdf);
            DocumentConverter.UseA4 = () => false;
            var letter = await ConvertAsync(input, DocFormat.Pdf);

            using var d1 = UglyToad.PdfPig.PdfDocument.Open(a4);
            using var d2 = UglyToad.PdfPig.PdfDocument.Open(letter);
            Assert.Equal(595, d1.GetPage(1).Width, 1);
            Assert.Equal(842, d1.GetPage(1).Height, 1);
            Assert.Equal(612, d2.GetPage(1).Width, 1);
            Assert.Equal(792, d2.GetPage(1).Height, 1);
        }
        finally
        {
            DocumentConverter.UseA4 = original;
        }
    }

    [Fact]
    public async Task Pdf_long_text_paginates_wraps_and_keeps_every_word()
    {
        using var tmp = new TempDir();
        var words = Enumerable.Range(1, 3000).Select(i => "word" + i);
        var paragraph = string.Join(' ', words);
        var input = tmp.WriteFile("long.txt", paragraph + "\r\n" + string.Join("\r\n", Enumerable.Range(1, 120).Select(i => "line " + i)));
        var output = await ConvertAsync(input, DocFormat.Pdf);

        Assert.True(PdfPageCount(output) >= 4);
        var flat = PdfFlat(output).Split(' ');
        foreach (var w in new[] { "word1", "word1500", "word3000", "1", "120" })
            Assert.Contains(w, flat);
        // 54 pt margins: no word starts left of the margin or ends right of the page's text area.
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
        foreach (var page in pdf.GetPages())
            foreach (var word in page.GetWords())
            {
                Assert.True(word.BoundingBox.Left >= 53, $"{word.Text} left {word.BoundingBox.Left}");
                Assert.True(word.BoundingBox.Right <= page.Width - 53, $"{word.Text} right {word.BoundingBox.Right}");
            }
    }

    [Fact]
    public async Task Pdf_breaks_a_word_wider_than_the_line()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("wide.txt", new string('W', 400));
        var output = await ConvertAsync(input, DocFormat.Pdf);
        var flat = PdfFlat(output).Replace(" ", "");
        Assert.Equal(400, flat.Count(c => c == 'W'));
    }

    [Fact]
    public async Task Txt_to_html_to_txt_keeps_leading_and_repeated_spaces_tabs_and_blank_lines()
    {
        using var tmp = new TempDir();
        const string text = "  indented two\r\nsingle then  double  spaces\r\n\tTabbed\r\n\r\n\r\nafter two blank lines\r\n    \r\ntrailing   \r\nlast line\r\n";
        var txt = tmp.WriteFile("spaces.txt", text);

        var html = await ConvertAsync(txt, DocFormat.Html);
        var html2 = File.ReadAllText(html);
        Assert.DoesNotContain("&nbsp;", html2);
        Assert.DoesNotContain(' ', html2);
        Assert.Contains("white-space:pre-wrap", html2);

        var back = await ConvertAsync(html, DocFormat.Txt);
        Assert.Equal(text, File.ReadAllText(back));
    }

    [Fact]
    public void Html_writer_keeps_blank_paragraphs_and_the_reader_maps_nbsp_back_to_a_space()
    {
        var model = new DocModel();
        model.Blocks.Add(DocParagraph.Plain("one"));
        model.Blocks.Add(new DocParagraph());
        model.Blocks.Add(DocParagraph.Plain("two"));
        using var ms = new MemoryStream();
        HtmlWriter.Write(model, ms, "t");
        var written = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("<p style=\"white-space:pre-wrap\"><br></p>", written);

        var read = HtmlReader.Read(written);
        Assert.Equal(["one", "", "two"], read.Blocks.OfType<DocParagraph>().Select(p => p.Text).ToArray());

        // HTML from older Shiftvert versions (or elsewhere) used &nbsp; for indentation.
        var legacy = HtmlReader.Read("<p>&nbsp;&nbsp;a&nbsp;b</p>");
        Assert.Equal("  a b", Assert.IsType<DocParagraph>(Assert.Single(legacy.Blocks)).Text);
    }

    [Fact]
    public async Task Pre_wrap_blocks_from_other_html_keep_their_spaces_but_plain_blocks_still_collapse()
    {
        using var tmp = new TempDir();
        var html = tmp.WriteFile("mix.html", "<p style=\"white-space: pre-wrap\">  a   b</p><p>c    d\n  e</p>");
        var back = await ConvertAsync(html, DocFormat.Txt);
        Assert.Equal("  a   b\r\nc d e\r\n", File.ReadAllText(back));
    }

    [Theory]
    [InlineData("日本語のテキスト", PdfDocWriter.Face.Cjk)]            // Japanese: Han + Hiragana + Katakana
    [InlineData("안녕하세요 세계", PdfDocWriter.Face.Hangul)]             // Korean
    [InlineData("नमस्ते दुनिया", PdfDocWriter.Face.Indic)]  // Hindi
    [InlineData("สวัสดีชาวโลก", PdfDocWriter.Face.Thai)] // Thai
    [InlineData("★→♫", PdfDocWriter.Face.Symbol)]
    public async Task Pdf_renders_non_latin_scripts_with_a_fallback_font_not_boxes(string line, PdfDocWriter.Face face)
    {
        if (PdfDocWriter.FamilyFor(face) is null) return; // the system has no font for this script: nothing to assert
        using var tmp = new TempDir();
        var input = tmp.WriteFile("scripts.txt", "Hello before\n" + line + "\nAfter line");
        var output = await ConvertAsync(input, DocFormat.Pdf);

        var text = PdfText(output).Replace("\r", "").Replace("\n", "");
        Assert.Contains(line.Replace(" ", ""), text.Replace(" ", ""));
        Assert.DoesNotContain('\0', text);
        Assert.Contains("Hello before", text);
        Assert.Contains("After line", text);
    }

    [Fact]
    public async Task Pdf_mixed_script_line_keeps_latin_and_cjk_in_order()
    {
        if (PdfDocWriter.FamilyFor(PdfDocWriter.Face.Cjk) is null) return;
        using var tmp = new TempDir();
        var input = tmp.WriteFile("mixed.txt", "Tokyo 東京 2024 から done");
        var output = await ConvertAsync(input, DocFormat.Pdf);

        // Order by position on the line (the extractor groups words by font).
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
        var onLine = string.Concat(pdf.GetPage(1).Letters.OrderBy(l => l.BoundingBox.Left).Select(l => l.Value)).Replace(" ", "");
        Assert.Equal("Tokyo東京2024からdone", onLine);
    }

    [Fact]
    public void Script_classifier_maps_code_points_to_font_groups()
    {
        Assert.Equal(PdfDocWriter.Face.Default, PdfDocWriter.FaceOf('A'));
        Assert.Equal(PdfDocWriter.Face.Default, PdfDocWriter.FaceOf(0x0416)); // Cyrillic
        Assert.Equal(PdfDocWriter.Face.Default, PdfDocWriter.FaceOf(0x05D0)); // Hebrew
        Assert.Equal(PdfDocWriter.Face.Default, PdfDocWriter.FaceOf(0x0627)); // Arabic
        Assert.Equal(PdfDocWriter.Face.Cjk, PdfDocWriter.FaceOf(0x4E2D));
        Assert.Equal(PdfDocWriter.Face.Cjk, PdfDocWriter.FaceOf(0x3042));
        Assert.Equal(PdfDocWriter.Face.Cjk, PdfDocWriter.FaceOf(0x30AB));
        Assert.Equal(PdfDocWriter.Face.Hangul, PdfDocWriter.FaceOf(0xD55C));
        Assert.Equal(PdfDocWriter.Face.Indic, PdfDocWriter.FaceOf(0x0928));
        Assert.Equal(PdfDocWriter.Face.Indic, PdfDocWriter.FaceOf(0x0B95)); // Tamil
        Assert.Equal(PdfDocWriter.Face.Thai, PdfDocWriter.FaceOf(0x0E01));
        Assert.Equal(PdfDocWriter.Face.Symbol, PdfDocWriter.FaceOf(0x2605));
        Assert.Equal(PdfDocWriter.Face.Emoji, PdfDocWriter.FaceOf(0x1F600));
    }

    [Fact]
    public async Task Pdf_hard_page_break_starts_a_new_page_and_headings_are_larger()
    {
        using var tmp = new TempDir();
        var html = tmp.WriteFile("pb.html", "<h1>Big</h1><p>body</p><div style=\"page-break-before:always\"></div><p>second page</p>");
        var output = await ConvertAsync(html, DocFormat.Pdf);
        Assert.Equal(2, PdfPageCount(output));

        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
        var page1 = pdf.GetPage(1).GetWords().ToList();
        var big = page1.First(w => w.Text == "Big").Letters[0].FontSize;
        var body = page1.First(w => w.Text == "body").Letters[0].FontSize;
        Assert.True(big > body + 4, $"{big} vs {body}");
        Assert.Contains("second", pdf.GetPage(2).Text);
    }

    [Fact]
    public async Task Pdf_hebrew_text_is_present_in_visual_order()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("heb.txt", Fixtures.DocTxtHebrew1255());
        var output = await ConvertAsync(input, DocFormat.Pdf);
        var text = PdfText(output);
        // Same letters; the glyphs are stored in visual (reversed) order, as PDF producers do.
        var expected = Fixtures.DocHebrewText.Where(char.IsLetter).OrderBy(c => c).ToArray();
        var actual = text.Where(c => c is >= 'א' and <= 'ת').ToArray();
        foreach (var c in expected.Distinct())
            Assert.Contains(c, actual);
        Assert.Contains(string.Concat(Fixtures.DocHebrewText.Split(' ')[0].Reverse()), text.Replace(" ", ""));
    }

    [Fact]
    public async Task Empty_document_still_produces_a_one_page_pdf()
    {
        using var tmp = new TempDir();
        var output = await ConvertAsync(tmp.WriteFile("empty.txt", ""), DocFormat.Pdf);
        Assert.Equal(1, PdfPageCount(output));
    }

    [Fact]
    public async Task Pdf_tables_with_tall_rows_spill_to_the_next_page()
    {
        using var tmp = new TempDir();
        var cell = string.Join("</p><p>", Enumerable.Range(1, 120).Select(i => "cell line " + i));
        var html = tmp.WriteFile("tall.html", $"<table><tr><td><p>{cell}</p></td><td>side</td></tr></table>");
        var output = await ConvertAsync(html, DocFormat.Pdf);
        Assert.True(PdfPageCount(output) >= 2);
        var flat = PdfFlat(output);
        Assert.Contains("120", flat.Split(' '));
        Assert.Contains("side", flat);
    }

    // ------------------------------------------------------------------ guards

    [Fact]
    public async Task Pictures_allow_only_txt()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("pic.docx", Fixtures.DocxWithPicture());
        foreach (var f in new[] { DocFormat.Pdf, DocFormat.Rtf, DocFormat.Html, DocFormat.Odt })
        {
            var ex = await Assert.ThrowsAsync<ConversionException>(() => ConvertAsync(input, f));
            Assert.Contains("pictures", ex.Message);
        }
        var txt = await ConvertAsync(input, DocFormat.Txt);
        Assert.Contains("Quarterly Report", File.ReadAllText(txt));
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("odt")]
    public async Task Non_zip_docx_and_odt_are_refused(string ext)
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("fake." + ext, Encoding.ASCII.GetBytes(Fixtures.RtfSample()));
        var ex = await Assert.ThrowsAsync<ConversionException>(() => ConvertAsync(input, DocFormat.Txt));
        Assert.Contains(ext.ToUpperInvariant(), ex.Message);
    }

    [Fact]
    public async Task Truncated_zip_gives_a_friendly_error()
    {
        using var tmp = new TempDir();
        var bytes = Fixtures.DocxSample();
        var input = tmp.WriteFile("cut.docx", bytes[..(bytes.Length / 2)]);
        await Assert.ThrowsAsync<ConversionException>(() => ConvertAsync(input, DocFormat.Txt));
    }

    [Fact]
    public async Task Text_longer_than_two_million_characters_is_refused_for_pdf()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("big.txt", new string('a', 2_000_001));
        var ex = await Assert.ThrowsAsync<ConversionException>(() => ConvertAsync(input, DocFormat.Pdf));
        Assert.Contains("too long", ex.Message);
        // Other formats are not limited.
        Assert.True(File.Exists(await ConvertAsync(input, DocFormat.Docx)));
    }

    [Fact]
    public async Task Right_to_left_text_longer_than_400000_characters_is_refused_for_pdf()
    {
        using var tmp = new TempDir();
        var hebrew = new string('א', 400_001);
        var input = tmp.WriteFile("bigheb.txt", hebrew);
        var ex = await Assert.ThrowsAsync<ConversionException>(() => ConvertAsync(input, DocFormat.Pdf));
        Assert.Contains("too long", ex.Message);

        // Just under the limit of the latin text is fine (limits differ by direction).
        var latin = tmp.WriteFile("latin.txt", string.Join("\r\n", Enumerable.Repeat("latin text", 1000)));
        Assert.True(File.Exists(await ConvertAsync(latin, DocFormat.Pdf)));
    }

    [Fact]
    public async Task Cancellation_is_honoured_and_leaves_nothing_behind()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("a.txt", "hello");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DocumentConverter.ToFormatAsync(input, DocFormat.Pdf, Capabilities.None, cts.Token));
        Assert.Equal(["a.txt"], Directory.GetFiles(tmp.Path).Select(f => Path.GetFileName(f)!).ToArray());
    }

    [Fact]
    public async Task Output_goes_to_downloads_when_the_folder_is_not_writable()
    {
        using var tmp = new TempDir();
        var sub = tmp.CreateDir("locked");
        var input = tmp.WriteFile("locked/a.txt", "hello");
        string output;
        using (TempDir.DenyWrites(sub))
            output = await ConvertAsync(input, DocFormat.Docx);
        Assert.Equal(tmp.Downloads, Path.GetDirectoryName(output));
    }

    [Fact]
    public async Task Legacy_doc_without_word_is_refused()
    {
        using var tmp = new TempDir();
        var input = tmp.WriteFile("old.doc", Fixtures.Noise(64));
        var ex = await Assert.ThrowsAsync<ConversionException>(() => ConvertAsync(input, DocFormat.Txt));
        Assert.Contains("Word", ex.Message);
    }

    // ------------------------------------------------------------------ Word COM (skipped when Word is not installed)

    [SkipIfNoCapability(nameof(Capabilities.WordCom))]
    public async Task Word_exports_docx_to_pdf()
    {
        using var tmp = new TempDir();
        var input = Sample(tmp, "docx");
        using var guard = OriginalsGuard.Watch(input);
        var output = await ConvertAsync(input, DocFormat.Pdf, Capabilities.Current);
        Assert.Contains("Quarterly Report", PdfFlat(output));
    }

    [SkipIfNoCapability(nameof(Capabilities.WordCom))]
    public async Task Word_converts_a_legacy_doc_to_the_other_formats()
    {
        using var tmp = new TempDir();
        // Word opens by content, so an RTF saved as .doc stands in for a real binary .doc.
        var doc = tmp.WriteFile("legacy.doc", Encoding.ASCII.GetBytes(Fixtures.RtfSample()));
        using var guard = OriginalsGuard.Watch(doc);
        foreach (var f in new[] { DocFormat.Pdf, DocFormat.Docx, DocFormat.Rtf, DocFormat.Txt, DocFormat.Html })
        {
            var output = await ConvertAsync(doc, f, Capabilities.Current);
            Assert.True(new FileInfo(output).Length > 0, f.ToString());
        }
        Assert.Contains("Quarterly Report", File.ReadAllText(await ConvertAsync(doc, DocFormat.Txt, Capabilities.Current)));
    }

    private static HashSet<int> WordPids() =>
        System.Diagnostics.Process.GetProcessesByName("WINWORD").Select(p => { using (p) return p.Id; }).ToHashSet();

    private static void MakePasswordProtectedDoc(string path)
    {
        dynamic word = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)!;
        try
        {
            word.Visible = false;
            word.DisplayAlerts = 0;
            dynamic doc = word.Documents.Add();
            doc.Content.Text = "secret";
            doc.SaveAs2(FileName: path, FileFormat: 0, Password: "pw");
            doc.Close(SaveChanges: 0);
        }
        finally
        {
            word.Quit(SaveChanges: 0);
        }
    }

    [SkipIfNoCapability(nameof(Capabilities.WordCom))]
    public async Task Word_fails_fast_with_a_clear_message_on_a_password_protected_doc()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("locked.doc");
        MakePasswordProtectedDoc(path);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<ConversionException>(() => ConvertAsync(path, DocFormat.Txt, Capabilities.Current));

        Assert.Contains("password", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), clock.Elapsed.ToString());
        Assert.Empty(Directory.GetFileSystemEntries(tmp.Path, "*.shiftvert-tmp"));
    }

    [SkipIfNoCapability(nameof(Capabilities.WordCom))]
    public async Task Word_conversion_that_times_out_is_killed_and_reports_it()
    {
        using var tmp = new TempDir();
        var doc = tmp.WriteFile("legacy.doc", Encoding.ASCII.GetBytes(Fixtures.RtfSample()));
        var before = WordPids();
        var previous = WordInterop.Timeout;
        WordInterop.Timeout = TimeSpan.FromMilliseconds(100); // Word needs far longer than this just to start
        try
        {
            var ex = await Assert.ThrowsAsync<ConversionException>(() => ConvertAsync(doc, DocFormat.Txt, Capabilities.Current));
            Assert.Contains("didn't respond", ex.Message);
        }
        finally
        {
            WordInterop.Timeout = previous;
        }

        await AssertNoNewWordProcess(before);
        Assert.Empty(Directory.GetFileSystemEntries(tmp.Path, "*.shiftvert-tmp"));
    }

    [SkipIfNoCapability(nameof(Capabilities.WordCom))]
    public async Task Word_conversion_honours_cancellation_and_kills_its_Word()
    {
        using var tmp = new TempDir();
        var doc = tmp.WriteFile("legacy.doc", Encoding.ASCII.GetBytes(Fixtures.RtfSample()));
        var before = WordPids();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DocumentConverter.ToFormatAsync(doc, DocFormat.Txt, Capabilities.Current, cts.Token));

        await AssertNoNewWordProcess(before);
    }

    private static async Task AssertNoNewWordProcess(HashSet<int> before)
    {
        for (var i = 0; i < 50; i++)
        {
            if (!WordPids().Except(before).Any()) return;
            await Task.Delay(100);
        }
        Assert.Empty(WordPids().Except(before));
    }
}
