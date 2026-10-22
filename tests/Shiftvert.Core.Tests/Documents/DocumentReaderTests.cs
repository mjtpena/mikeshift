using System.Text;
using Shiftvert.Core.Documents;
using static Shiftvert.Core.Tests.Documents.DocumentTestHelpers;

namespace Shiftvert.Core.Tests.Documents;

public class DocumentReaderTests
{
    public static TheoryData<string> SampleKinds() => ["docx", "odt", "rtf", "html"];

    [Theory, MemberData(nameof(SampleKinds))]
    public void Sample_reads_to_the_expected_outline(string kind)
    {
        using var tmp = new TempDir();
        var model = Read(Sample(tmp, kind));
        Assert.Equal(Fixtures.DocumentOutline, Outline(model));
        Assert.False(model.HasPictures);
    }

    [Fact]
    public void Docx_with_a_picture_sets_HasPictures_and_keeps_the_text()
    {
        using var tmp = new TempDir();
        var model = Read(tmp.WriteFile("pic.docx", Fixtures.DocxWithPicture()));
        Assert.True(model.HasPictures);
        Assert.Equal(Fixtures.DocumentOutline, Outline(model));
    }

    [Fact]
    public void Odt_with_a_picture_sets_HasPictures()
    {
        using var tmp = new TempDir();
        Assert.True(Read(tmp.WriteFile("pic.odt", Fixtures.OdtWithPicture())).HasPictures);
    }

    [Fact]
    public async Task Docx_hyperlinks_are_kept_as_run_links()
    {
        using var tmp = new TempDir();
        // Build a hyperlinked DOCX by converting from HTML and reading it back.
        var html = tmp.WriteFile("link.html", "<p>See <a href=\"https://example.com/a\">the site</a>.</p>");
        var docx = await ConvertAsync(html, DocFormat.Docx);
        var p = Assert.IsType<DocParagraph>(Assert.Single(Read(docx).Blocks));
        Assert.Contains(p.Runs, r => r.Link == "https://example.com/a" && r.Text == "the site");
        Assert.Equal("See the site.", p.Text);
    }

    [Fact]
    public void Docx_page_break_and_line_break_are_read()
    {
        using var tmp = new TempDir();
        var xml = "<w:p><w:r><w:t>one</w:t><w:br/><w:t>two</w:t></w:r></w:p><w:p><w:r><w:br w:type=\"page\"/></w:r></w:p><w:p><w:r><w:t>three</w:t></w:r></w:p>";
        var path = tmp.WriteFile("brk.docx", DocxWithBody(xml));
        var model = Read(path);
        Assert.Equal(["P:one\ntwo", "PB", "P:three"], Outline(model));
    }

    [Fact]
    public void Docx_headings_by_outline_level_are_recognised()
    {
        using var tmp = new TempDir();
        var xml = "<w:p><w:pPr><w:outlineLvl w:val=\"2\"/></w:pPr><w:r><w:t>Deep</w:t></w:r></w:p>";
        Assert.Equal(["H3:Deep"], Outline(Read(tmp.WriteFile("o.docx", DocxWithBody(xml)))));
    }

    [Fact]
    public void Docx_nested_list_levels_are_kept()
    {
        using var tmp = new TempDir();
        var xml = "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>a</w:t></w:r></w:p>" +
                  "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"1\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>b</w:t></w:r></w:p>";
        Assert.Equal(["UL0:a", "UL1:b"], Outline(Read(tmp.WriteFile("n.docx", DocxWithBody(xml)))));
    }

    [Fact]
    public void Docx_bidi_paragraph_is_marked_rtl()
    {
        using var tmp = new TempDir();
        var xml = "<w:p><w:pPr><w:bidi/></w:pPr><w:r><w:t>abc</w:t></w:r></w:p>";
        var p = Assert.IsType<DocParagraph>(Assert.Single(Read(tmp.WriteFile("r.docx", DocxWithBody(xml))).Blocks));
        Assert.True(p.Rtl);
    }

    // ------------------------------------------------------------------ ODT

    [Fact]
    public void Odt_collapses_whitespace_and_expands_text_s_and_tab_and_line_break()
    {
        using var tmp = new TempDir();
        var body = "<text:p>  a   b<text:s text:c=\"2\"/>c<text:tab/>d<text:line-break/>e</text:p>";
        var model = Read(tmp.WriteFile("w.odt", OdtWithBody(body)));
        var p = Assert.IsType<DocParagraph>(Assert.Single(model.Blocks));
        Assert.Equal("a b  c\td\ne", p.Text);
    }

    [Fact]
    public void Odt_nested_lists_and_page_break_style()
    {
        using var tmp = new TempDir();
        var body = "<text:list><text:list-item><text:p>a</text:p><text:list><text:list-item><text:p>b</text:p></text:list-item></text:list></text:list-item></text:list>" +
                   "<text:p text:style-name=\"PB\">after</text:p>";
        var autos = "<style:style style:name=\"PB\" style:family=\"paragraph\"><style:paragraph-properties fo:break-before=\"page\"/></style:style>";
        var model = Read(tmp.WriteFile("l.odt", OdtWithBody(body, autos)));
        Assert.Equal(["UL0:a", "UL1:b", "PB", "P:after"], Outline(model));
    }

    [Fact]
    public void Odt_DOCTYPE_is_rejected_not_expanded()
    {
        using var tmp = new TempDir();
        var content = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a \"boom\">]><office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"/>";
        var path = tmp.WriteFile("evil.odt", OdtRaw(content));
        Assert.Throws<ConversionException>(() => Read(path));
    }

    // ------------------------------------------------------------------ HTML

    [Fact]
    public void Html_tolerates_unclosed_tags_and_decodes_entities()
    {
        var model = HtmlReader.Read("<p>Fish &amp; chips &lt;b&gt; &copy; &#8364; &nbsp;x<p>Second<ul><li>one<li>two</ul>tail");
        Assert.Equal(["P:Fish & chips <b> © €  x", "P:Second", "UL0:one", "UL0:two", "P:tail"], Outline(model));
    }

    [Fact]
    public void Html_ignores_script_style_and_comments_and_collapses_whitespace()
    {
        var model = HtmlReader.Read("<html><head><title>T</title><style>p{}</style></head><body><!-- hidden <p>no</p> --><script>alert('<p>x</p>')</script><p>  a \n\t b  </p></body></html>");
        Assert.Equal(["P:a b"], Outline(model));
    }

    [Theory]
    [InlineData("<p>a<img src=\"http://example.invalid/x.png\">b</p>")]
    [InlineData("<svg><circle/></svg><p>a</p>")]
    [InlineData("<picture><source></picture><p>a</p>")]
    public void Html_pictures_are_flagged_and_never_fetched(string html)
    {
        Assert.True(HtmlReader.Read(html).HasPictures);
    }

    [Fact]
    public void Html_br_headings_and_nested_lists()
    {
        var model = HtmlReader.Read("<h3>Head <i>it</i></h3><p>a<br>b</p><ol><li>x<ul><li>y</li></ul></li><li>z</li></ol>");
        Assert.Equal(["H3:Head it", "P:a\nb", "OL0:x", "UL1:y", "OL0:z"], Outline(model));
    }

    [Fact]
    public void Html_dir_rtl_and_page_break_style()
    {
        var model = HtmlReader.Read("<p dir=\"rtl\">abc</p><div style=\"page-break-before:always\"></div><p>d</p>");
        Assert.True(Assert.IsType<DocParagraph>(model.Blocks[0]).Rtl);
        Assert.Equal(["P:abc", "PB", "P:d"], Outline(model));
    }

    [Fact]
    public void Html_table_cells_flatten_inner_markup()
    {
        var model = HtmlReader.Read("<table><tr><th>H1</th><th>H2</th></tr><tr><td><p>a</p><p>b</p></td><td>c</td></tr></table>");
        var t = Assert.IsType<DocTable>(Assert.Single(model.Blocks));
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal(2, t.Rows[1][0].Count);
        Assert.True(t.Rows[0][0][0].Runs[0].Bold); // <th> is bold
    }

    [Fact]
    public void Html_charset_declaration_decides_legacy_bytes()
    {
        TextDecoding.EnsureProviders();
        var bytes = Encoding.GetEncoding(1252).GetBytes("<meta charset=\"windows-1252\"><p>Crème</p>");
        Assert.Equal(["P:Crème"], Outline(HtmlReader.Read(TextDecoding.DecodeHtml(bytes))));
    }

    // ------------------------------------------------------------------ plain text

    [Fact]
    public void Txt_lines_become_paragraphs_and_a_trailing_newline_does_not_add_one()
    {
        var model = PlainTextReader.Read("a\r\n\r\nb\r\n");
        Assert.Equal(3, model.Blocks.Count);
        Assert.Equal(["a", "", "b"], model.Blocks.Cast<DocParagraph>().Select(p => p.Text).ToArray());
        Assert.Empty(PlainTextReader.Read("").Blocks);
        Assert.Single(PlainTextReader.Read("x").Blocks);
    }

    [Fact]
    public void Markdown_is_kept_as_plain_text()
    {
        using var tmp = new TempDir();
        var model = Read(tmp.WriteFile("a.md", Fixtures.MarkdownSample()));
        Assert.Equal("# Title", ((DocParagraph)model.Blocks[0]).Text);
        Assert.Equal("- one", ((DocParagraph)model.Blocks[4]).Text);
        Assert.All(model.Blocks, b => Assert.Equal(ParagraphKind.Normal, ((DocParagraph)b).Kind));
    }

    // ------------------------------------------------------------------ builders for odd packages

    private static byte[] DocxWithBody(string bodyXml)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            Put(zip, "[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/></Types>");
            Put(zip, "_rels/.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Put(zip, "word/_rels/document.xml.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/></Relationships>");
            Put(zip, "word/numbering.xml", "<?xml version=\"1.0\"?><w:numbering xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:numFmt w:val=\"bullet\"/></w:lvl><w:lvl w:ilvl=\"1\"><w:numFmt w:val=\"bullet\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num></w:numbering>");
            Put(zip, "word/document.xml", "<?xml version=\"1.0\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" + bodyXml + "</w:body></w:document>");
        }
        return ms.ToArray();
    }

    private static byte[] OdtWithBody(string bodyXml, string autoStyles = "")
    {
        var content = "<?xml version=\"1.0\"?><office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\">" +
                      "<office:automatic-styles>" + autoStyles + "</office:automatic-styles><office:body><office:text>" + bodyXml + "</office:text></office:body></office:document-content>";
        return OdtRaw(content);
    }

    private static byte[] OdtRaw(string contentXml)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            Put(zip, "mimetype", "application/vnd.oasis.opendocument.text");
            Put(zip, "content.xml", contentXml);
        }
        return ms.ToArray();
    }

    private static void Put(System.IO.Compression.ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name);
        using var s = e.Open();
        s.Write(new UTF8Encoding(false).GetBytes(content));
    }
}
