using System.IO.Compression;
using System.Text;

namespace MikeShift.Core.Tests.Infrastructure;

/// <summary>
/// Document fixtures, generated at test time. The DOCX and ODT packages are hand-written XML (not produced by our own
/// writers) so the readers are tested against independent input. Every "sample" fixture carries the same content,
/// described by <see cref="DocumentOutline"/>:
/// <code>
/// # Quarterly Report            (heading 1)
/// ## Overview                   (heading 2)
/// Revenue grew by **twelve percent** and costs were _flat_.
/// Plain ~underlined~ words.
/// - Apples / - Oranges          (bullets)
/// 1. First / 2. Second          (numbered)
/// table: Name | Qty ; Apples | 3
/// </code>
/// </summary>
public static partial class Fixtures
{
    /// <summary>What every sample fixture (DOCX, ODT, RTF, HTML) must read as; see <c>DocumentOutlines.Of</c>.</summary>
    public static readonly string[] DocumentOutline =
    [
        "H1:Quarterly Report",
        "H2:Overview",
        "P:Revenue grew by **twelve percent** and costs were _flat_.",
        "P:Plain ~underlined~ words.",
        "UL0:Apples",
        "UL0:Oranges",
        "OL0:First",
        "OL0:Second",
        "T:[Name|Qty];[Apples|3]",
    ];

    /// <summary>Hebrew sample text used by the encoding and RTL tests.</summary>
    public const string DocHebrewText = "שלום עולם";

    static partial void WriteDocumentsFixtures(string dir)
    {
        File.WriteAllBytes(Path.Combine(dir, "sample.docx"), DocxSample());
        File.WriteAllBytes(Path.Combine(dir, "with-picture.docx"), DocxWithPicture());
        File.WriteAllBytes(Path.Combine(dir, "sample.odt"), OdtSample());
        File.WriteAllBytes(Path.Combine(dir, "sample.rtf"), Encoding.ASCII.GetBytes(RtfSample()));
        File.WriteAllText(Path.Combine(dir, "sample.html"), HtmlSample(), new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(dir, "utf8.txt"), DocTxtUtf8());
        File.WriteAllBytes(Path.Combine(dir, "utf8-bom.txt"), DocTxtUtf8Bom());
        File.WriteAllBytes(Path.Combine(dir, "hebrew-1255.txt"), DocTxtHebrew1255());
        File.WriteAllBytes(Path.Combine(dir, "latin-1252.txt"), DocTxtLatin1252());
        File.WriteAllText(Path.Combine(dir, "sample.md"), MarkdownSample(), new UTF8Encoding(false));
    }

    // ------------------------------------------------------------------ TXT / MD

    public const string DocUnicodeLine = "Naïve café — “quotes” and Ελληνικά";

    public static byte[] DocTxtUtf8() => new UTF8Encoding(false).GetBytes($"{DocUnicodeLine}\r\nSecond line\r\n");

    public static byte[] DocTxtUtf8Bom() => [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. new UTF8Encoding(false).GetBytes($"{DocUnicodeLine}\r\nSecond line\r\n")];

    public static byte[] DocTxtHebrew1255()
    {
        MikeShift.Core.Documents.TextDecoding.EnsureProviders();
        return Encoding.GetEncoding(1255).GetBytes($"{DocHebrewText}\r\nשלום שוב\r\n");
    }

    public const string DocLatinLine = "Crème brûlée à la façon de Zoë";

    public static byte[] DocTxtLatin1252()
    {
        MikeShift.Core.Documents.TextDecoding.EnsureProviders();
        return Encoding.GetEncoding(1252).GetBytes($"{DocLatinLine}\r\nPrice: 5€ — “fine”\r\n");
    }

    public static string MarkdownSample() => "# Title\n\nSome *emphasis* and `code`.\n\n- one\n- two\n";

    // ------------------------------------------------------------------ HTML

    public static string HtmlSample() => """
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><title>Ignored title</title>
        <style>p { color: red }</style><script>var x = "<p>not text</p>";</script></head>
        <body>
        <h1>Quarterly Report</h1>
        <h2>Overview</h2>
        <p>Revenue grew by <b>twelve percent</b> and costs were <em>flat</em>.</p>
        <p>Plain <u>underlined</u> words.</p>
        <ul><li>Apples</li><li>Oranges</li></ul>
        <ol><li>First</li><li>Second</li></ol>
        <table><tr><td>Name</td><td>Qty</td></tr><tr><td>Apples</td><td>3</td></tr></table>
        </body></html>
        """;

    // ------------------------------------------------------------------ RTF

    /// <summary>A Word-style RTF: font/colour/stylesheet/list tables, heading styles, real lists, a table, one hyperlink-free body.</summary>
    public static string RtfSample() =>
        @"{\rtf1\ansi\ansicpg1252\deff0\nouicompat\deflang1033" + "\r\n" +
        @"{\fonttbl{\f0\fnil\fcharset0 Calibri;}{\f1\fnil\fcharset2 Symbol;}}" + "\r\n" +
        @"{\colortbl ;\red0\green0\blue255;}" + "\r\n" +
        @"{\stylesheet{\s0 Normal;}{\s1\sbasedon0\snext0\outlinelevel0\b\fs32 heading 1;}{\s2\sbasedon0\snext0\outlinelevel1\b\fs26 heading 2;}{\s3\sbasedon0 List Paragraph;}}" + "\r\n" +
        @"{\*\listtable{\list\listtemplateid1\listhybrid{\listlevel\levelnfc23\leveljc0\levelstartat1{\leveltext\'01舦 ?;}{\levelnumbers;}\fi-360\li720\lin720}\listid1}" +
        @"{\list\listtemplateid2\listhybrid{\listlevel\levelnfc0\leveljc0\levelstartat1{\leveltext\'02\'00.;}{\levelnumbers\'01;}\fi-360\li720\lin720}\listid2}}" + "\r\n" +
        @"{\*\listoverridetable{\listoverride\listid1\listoverridecount0\ls1}{\listoverride\listid2\listoverridecount0\ls2}}" + "\r\n" +
        @"{\info{\title Ignored}{\author Nobody}}" + "\r\n" +
        @"\viewkind4\uc1" + "\r\n" +
        @"\pard\s1\sa200\b\f0\fs32 Quarterly Report\par" + "\r\n" +
        @"\pard\s2\sa200\b\fs26 Overview\par" + "\r\n" +
        @"\pard\sa200\sl276\slmult1\plain\f0\fs22 Revenue grew by {\b twelve percent} and costs were {\i flat}.\par" + "\r\n" +
        @"\pard\sa200 Plain {\ul underlined} words.\par" + "\r\n" +
        @"\pard\s3\fi-360\li720\ls1\ilvl0\plain\f0 Apples\par" + "\r\n" +
        @"\pard\s3\fi-360\li720\ls1\ilvl0 Oranges\par" + "\r\n" +
        @"\pard\s3\fi-360\li720\ls2\ilvl0 First\par" + "\r\n" +
        @"\pard\s3\fi-360\li720\ls2\ilvl0 Second\par" + "\r\n" +
        @"\trowd\trgaph108\cellx3000\cellx6000" + "\r\n" +
        @"\pard\intbl Name\cell Qty\cell\row" + "\r\n" +
        @"\trowd\trgaph108\cellx3000\cellx6000" + "\r\n" +
        @"\pard\intbl Apples\cell 3\cell\row" + "\r\n" +
        @"\pard\par" + "\r\n" +
        "}\r\n";

    // ------------------------------------------------------------------ DOCX

    private const string DocWNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static string DocxBodyXml() => $"""
        <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Quarterly Report</w:t></w:r></w:p>
        <w:p><w:pPr><w:pStyle w:val="Heading2"/></w:pPr><w:r><w:t>Overview</w:t></w:r></w:p>
        <w:p><w:r><w:t xml:space="preserve">Revenue grew by </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>twelve percent</w:t></w:r><w:r><w:t xml:space="preserve"> and costs were </w:t></w:r><w:r><w:rPr><w:i/></w:rPr><w:t>flat</w:t></w:r><w:r><w:t>.</w:t></w:r></w:p>
        <w:p><w:r><w:t xml:space="preserve">Plain </w:t></w:r><w:r><w:rPr><w:u w:val="single"/></w:rPr><w:t>underlined</w:t></w:r><w:r><w:t xml:space="preserve"> words.</w:t></w:r></w:p>
        <w:p><w:pPr><w:pStyle w:val="ListParagraph"/><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>Apples</w:t></w:r></w:p>
        <w:p><w:pPr><w:pStyle w:val="ListParagraph"/><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>Oranges</w:t></w:r></w:p>
        <w:p><w:pPr><w:pStyle w:val="ListParagraph"/><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr></w:pPr><w:r><w:t>First</w:t></w:r></w:p>
        <w:p><w:pPr><w:pStyle w:val="ListParagraph"/><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr></w:pPr><w:r><w:t>Second</w:t></w:r></w:p>
        <w:tbl><w:tblPr><w:tblW w:w="0" w:type="auto"/></w:tblPr><w:tblGrid><w:gridCol w:w="3000"/><w:gridCol w:w="3000"/></w:tblGrid>
        <w:tr><w:tc><w:tcPr><w:tcW w:w="3000" w:type="dxa"/></w:tcPr><w:p><w:r><w:t>Name</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:tcW w:w="3000" w:type="dxa"/></w:tcPr><w:p><w:r><w:t>Qty</w:t></w:r></w:p></w:tc></w:tr>
        <w:tr><w:tc><w:tcPr><w:tcW w:w="3000" w:type="dxa"/></w:tcPr><w:p><w:r><w:t>Apples</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:tcW w:w="3000" w:type="dxa"/></w:tcPr><w:p><w:r><w:t>3</w:t></w:r></w:p></w:tc></w:tr>
        </w:tbl>
        <w:p/>
        """;

    private const string DocxStylesXml = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="{DocWNs}">
        <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
        <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:sz w:val="32"/></w:rPr></w:style>
        <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:b/><w:sz w:val="26"/></w:rPr></w:style>
        <w:style w:type="paragraph" w:styleId="ListParagraph"><w:name w:val="List Paragraph"/><w:basedOn w:val="Normal"/></w:style>
        </w:styles>
        """;

    private const string DocxNumberingXml = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:numbering xmlns:w="{DocWNs}">
        <w:abstractNum w:abstractNumId="0"><w:multiLevelType w:val="hybridMultilevel"/><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="&#8226;"/><w:lvlJc w:val="left"/></w:lvl></w:abstractNum>
        <w:abstractNum w:abstractNumId="1"><w:multiLevelType w:val="hybridMultilevel"/><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/></w:lvl></w:abstractNum>
        <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
        <w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num>
        </w:numbering>
        """;

    private static byte[] DocxPackage(string bodyXml, bool withPicture)
    {
        var picture = withPicture ? "<Default Extension=\"png\" ContentType=\"image/png\"/>" : "";
        var contentTypes = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
            <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
            <Default Extension="xml" ContentType="application/xml"/>
            {picture}
            <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
            <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
            </Types>
            """;
        const string rels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
            """;
        var docRels = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
            {(withPicture ? "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.png\"/>" : "")}
            </Relationships>
            """;
        var document = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="{DocWNs}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body>
            {bodyXml}
            <w:sectPr><w:pgSz w:w="12240" w:h="15840"/></w:sectPr></w:body></w:document>
            """;

        var parts = new List<(string, byte[])>
        {
            ("[Content_Types].xml", DocUtf8(contentTypes)),
            ("_rels/.rels", DocUtf8(rels)),
            ("word/document.xml", DocUtf8(document)),
            ("word/_rels/document.xml.rels", DocUtf8(docRels)),
            ("word/styles.xml", DocUtf8(DocxStylesXml)),
            ("word/numbering.xml", DocUtf8(DocxNumberingXml)),
        };
        if (withPicture) parts.Add(("word/media/image1.png", Png(4, 4)));
        return DocZip(parts);
    }

    public static byte[] DocxSample() => DocxPackage(DocxBodyXml(), withPicture: false);

    /// <summary>A DOCX like <see cref="DocxSample"/> with one embedded PNG in the body (so only TXT is offered).</summary>
    public static byte[] DocxWithPicture()
    {
        const string drawing = """
            <w:p><w:r><w:drawing><wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"><wp:extent cx="95250" cy="95250"/><wp:docPr id="1" name="Picture 1"/>
            <a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
            <pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="0" name="image1.png"/><pic:cNvPicPr/></pic:nvPicPr>
            <pic:blipFill><a:blip r:embed="rId3"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
            <pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="95250" cy="95250"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic>
            </a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>
            """;
        return DocxPackage(DocxBodyXml() + drawing, withPicture: true);
    }

    /// <summary>A minimal DOCX with the given paragraphs (plain text), for quick reader/limit tests.</summary>
    public static byte[] DocxFromParagraphs(params string[] paragraphs) =>
        DocxPackage(string.Concat(paragraphs.Select(p => $"<w:p><w:r><w:t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(p)}</w:t></w:r></w:p>")), withPicture: false);

    // ------------------------------------------------------------------ ODT

    public static byte[] OdtSample() => OdtPackage(withPicture: false);

    public static byte[] OdtWithPicture() => OdtPackage(withPicture: true);

    private static byte[] OdtPackage(bool withPicture)
    {
        var picture = withPicture
            ? "<text:p><draw:frame draw:name=\"img\" text:anchor-type=\"as-char\"><draw:image xlink:href=\"Pictures/image1.png\"/></draw:frame></text:p>"
            : "";
        var content = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0" xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0" xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0" xmlns:xlink="http://www.w3.org/1999/xlink" office:version="1.3">
            <office:automatic-styles>
            <style:style style:name="Bold" style:family="text"><style:text-properties fo:font-weight="bold"/></style:style>
            <style:style style:name="Italic" style:family="text"><style:text-properties fo:font-style="italic"/></style:style>
            <style:style style:name="Underline" style:family="text"><style:text-properties style:text-underline-style="solid"/></style:style>
            <text:list-style style:name="LBullet"><text:list-level-style-bullet text:level="1" text:bullet-char="&#8226;"/></text:list-style>
            <text:list-style style:name="LNumber"><text:list-level-style-number text:level="1" style:num-format="1" style:num-suffix="."/></text:list-style>
            </office:automatic-styles>
            <office:body><office:text>
            <text:h text:style-name="Heading_20_1" text:outline-level="1">Quarterly Report</text:h>
            <text:h text:style-name="Heading_20_2" text:outline-level="2">Overview</text:h>
            <text:p text:style-name="Standard">Revenue grew by <text:span text:style-name="Bold">twelve percent</text:span> and costs were <text:span text:style-name="Italic">flat</text:span>.</text:p>
            <text:p text:style-name="Standard">Plain <text:span text:style-name="Underline">underlined</text:span> words.</text:p>
            <text:list text:style-name="LBullet"><text:list-item><text:p>Apples</text:p></text:list-item><text:list-item><text:p>Oranges</text:p></text:list-item></text:list>
            <text:list text:style-name="LNumber"><text:list-item><text:p>First</text:p></text:list-item><text:list-item><text:p>Second</text:p></text:list-item></text:list>
            <table:table table:name="Table1"><table:table-column table:number-columns-repeated="2"/>
            <table:table-row><table:table-cell office:value-type="string"><text:p>Name</text:p></table:table-cell><table:table-cell office:value-type="string"><text:p>Qty</text:p></table:table-cell></table:table-row>
            <table:table-row><table:table-cell office:value-type="string"><text:p>Apples</text:p></table:table-cell><table:table-cell office:value-type="string"><text:p>3</text:p></table:table-cell></table:table-row>
            </table:table>
            {picture}
            </office:text></office:body></office:document-content>
            """;
        const string manifest = """
            <?xml version="1.0" encoding="UTF-8"?>
            <manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0" manifest:version="1.3"><manifest:file-entry manifest:full-path="/" manifest:media-type="application/vnd.oasis.opendocument.text"/></manifest:manifest>
            """;

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var s = mime.Open()) s.Write(Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.text"));
            DocZipAdd(zip, "META-INF/manifest.xml", DocUtf8(manifest));
            DocZipAdd(zip, "content.xml", DocUtf8(content));
            if (withPicture) DocZipAdd(zip, "Pictures/image1.png", Png(4, 4));
        }
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ helpers

    private static byte[] DocUtf8(string s) => new UTF8Encoding(false).GetBytes(s.Replace("\r\n", "\n"));

    private static byte[] DocZip(IEnumerable<(string Name, byte[] Data)> entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in entries)
                DocZipAdd(zip, name, data);
        return ms.ToArray();
    }

    private static void DocZipAdd(ZipArchive zip, string name, byte[] data)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        s.Write(data);
    }
}
