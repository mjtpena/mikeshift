using System.Text;
using MikeShift.Core.Documents;

namespace MikeShift.Core.Tests.Documents;

/// <summary>The document pivot model, text decoding, the reader front door (<see cref="DocReaders"/>) and the converter's guard rails.</summary>
public class DocumentModelCoverageTests
{
    // ------------------------------------------------------------------ TextDecoding

    [Fact]
    public void Every_byte_order_mark_selects_its_encoding_and_is_skipped()
    {
        var utf32 = new byte[] { 0xFF, 0xFE, 0, 0 }.Concat(new UTF32Encoding(false, false).GetBytes("hé")).ToArray();
        Assert.Equal("hé", TextDecoding.Decode(utf32));
        var (enc32, skip32) = TextDecoding.Detect(utf32);
        Assert.Equal((4, 12000), (skip32, enc32.CodePage));

        var utf16le = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("hé")).ToArray();
        Assert.Equal("hé", TextDecoding.Decode(utf16le));
        Assert.Equal(1200, TextDecoding.Detect(utf16le).Encoding.CodePage);

        var utf16be = new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes("hé")).ToArray();
        Assert.Equal("hé", TextDecoding.Decode(utf16be));
        Assert.Equal(1201, TextDecoding.Detect(utf16be).Encoding.CodePage);

        var utf8 = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("hé"u8.ToArray()).ToArray();
        Assert.Equal("hé", TextDecoding.Decode(utf8));
        Assert.Equal(("hé", 3), (Encoding.UTF8.GetString(utf8, 3, utf8.Length - 3), TextDecoding.Detect(utf8).BomLength));
    }

    [Fact]
    public void Valid_utf8_wins_over_the_legacy_guess_and_empty_input_is_empty()
    {
        Assert.Equal("", TextDecoding.Decode([]));
        Assert.Equal("héllo wörld", TextDecoding.Decode("héllo wörld"u8.ToArray()));
        Assert.Equal(65001, TextDecoding.Detect("é"u8.ToArray()).Encoding.CodePage);
    }

    [Fact]
    public void Invalid_utf8_is_windows_1252_unless_a_run_of_three_hebrew_range_bytes_makes_it_1255()
    {
        Assert.Equal(1252, TextDecoding.LegacyEncoding([0x63, 0x61, 0x66, 0xE9]).CodePage);       // "café"
        Assert.Equal(1252, TextDecoding.LegacyEncoding([0xE0, 0xE1, 0x20, 0xE2]).CodePage);       // runs of 2, 1: not Hebrew
        Assert.Equal(1255, TextDecoding.LegacyEncoding([0x20, 0xF9, 0xEC, 0xE5, 0xED]).CodePage); // שלום
        Assert.Equal(1252, TextDecoding.LegacyEncoding([0xC0, 0xC1, 0xFB, 0xC2]).CodePage);       // 0xFB is outside C0..FA
        Assert.Equal("שלום", TextDecoding.Decode([0xF9, 0xEC, 0xE5, 0xED]));
        Assert.Equal("café", TextDecoding.Decode([0x63, 0x61, 0x66, 0xE9]));
    }

    [Fact]
    public void Html_prefers_a_declared_charset_over_the_heuristic_only_for_invalid_utf8_without_a_bom()
    {
        // 0xE9 alone is invalid UTF-8; the page says windows-1250 where 0xE9 is "é"; 0xF9 = "ů" there but "ù" in 1252.
        var page = Encoding.Latin1.GetBytes("<html><head><meta charset=\"windows-1250\"></head><body>ù</body></html>");
        Assert.Contains("ů", TextDecoding.DecodeHtml(page));

        var httpEquiv = Encoding.Latin1.GetBytes("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=iso-8859-2\"><p>±</p>");
        Assert.Contains("ą", TextDecoding.DecodeHtml(httpEquiv));

        var unknownLabel = Encoding.Latin1.GetBytes("<meta charset=\"not-a-real-charset\"><p>café</p>");
        Assert.Contains("café", TextDecoding.DecodeHtml(unknownLabel));

        var noMeta = Encoding.Latin1.GetBytes("<p>café</p>");
        Assert.Contains("café", TextDecoding.DecodeHtml(noMeta));

        // Valid UTF-8 beats a (wrong) declaration; a BOM beats everything.
        var utf8 = Encoding.UTF8.GetBytes("<meta charset=\"windows-1250\"><p>é</p>");
        Assert.Contains("é", TextDecoding.DecodeHtml(utf8));
        var bom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("<meta charset=\"windows-1250\"><p>é</p>"u8.ToArray()).ToArray();
        Assert.Contains("é", TextDecoding.DecodeHtml(bom));
        Assert.False(TextDecoding.DecodeHtml(bom).StartsWith('﻿')); // ordinal check: U+FEFF is ignorable in culture-sensitive comparisons
    }

    // ------------------------------------------------------------------ DocReaders front door

    [Theory]
    [InlineData("docx", true)]
    [InlineData("odt", true)]
    [InlineData("rtf", true)]
    [InlineData("html", true)]
    [InlineData("htm", true)]
    [InlineData("txt", true)]
    [InlineData("md", true)]
    [InlineData("markdown", true)]
    [InlineData("doc", false)]
    [InlineData("pdf", false)]
    [InlineData("", false)]
    [InlineData("DOCX", false)] // callers pass lower-case extensions
    public void CanRead_knows_exactly_the_supported_extensions(string ext, bool expected) =>
        Assert.Equal(expected, DocReaders.CanRead(ext));

    [Fact]
    public void Legacy_doc_and_unknown_extensions_are_refused_with_their_own_message()
    {
        using var tmp = new TempDir();
        var doc = Assert.Throws<ConversionException>(() => DocReaders.Read(tmp.WriteFile("a.doc", "x")));
        Assert.Equal("Legacy .doc files need Microsoft Word to be installed.", doc.Message);
        var pdf = Assert.Throws<ConversionException>(() => DocReaders.Read(tmp.WriteFile("a.pdf", "x")));
        Assert.Equal("Can't read .pdf documents.", pdf.Message);
        var none = Assert.Throws<ConversionException>(() => DocReaders.Read(tmp.WriteFile("noext", "x")));
        Assert.Equal("Can't read . documents.", none.Message);
    }

    [Fact]
    public void Missing_files_and_missing_folders_both_say_the_file_is_gone()
    {
        using var tmp = new TempDir();
        Assert.Equal("The file no longer exists.", Assert.Throws<ConversionException>(() => DocReaders.Read(tmp.Combine("gone.txt"))).Message);
        Assert.Equal("The file no longer exists.", Assert.Throws<ConversionException>(() => DocReaders.Read(tmp.Combine("nofolder", "gone.rtf"))).Message);
    }

    [Fact]
    public void A_file_another_program_holds_exclusively_is_reported_as_unreadable()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("busy.txt", "text");
        using var hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var ex = Assert.Throws<ConversionException>(() => DocReaders.Read(path));
        Assert.StartsWith("The file couldn't be read: ", ex.Message);
        Assert.IsAssignableFrom<IOException>(ex.InnerException);
    }

    [Fact]
    public void A_file_the_user_may_not_read_is_reported_as_such()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("secret.txt", "text");
        using (CoverageAcl.DenyRead(path))
        {
            var ex = Assert.Throws<ConversionException>(() => DocReaders.Read(path));
            Assert.Equal("MikeShift isn't allowed to read this file.", ex.Message);
        }
    }

    [Fact]
    public void Absurdly_large_inputs_are_refused_before_they_are_read()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("huge.txt");
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            fs.SetLength(DocReaders.MaxInputBytes + 1);
        var ex = Assert.Throws<ConversionException>(() => DocReaders.Read(path));
        Assert.Equal("This file is too large to convert.", ex.Message);
    }

    [Fact]
    public void A_cancelled_token_stops_the_read_without_wrapping_the_cancellation()
    {
        using var tmp = new TempDir();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => DocReaders.Read(tmp.WriteFile("a.txt", "x"), cts.Token));
    }

    [Fact]
    public void Each_extension_is_routed_to_its_reader()
    {
        using var tmp = new TempDir();
        Assert.Equal(["hello"], DocReaders.Read(tmp.WriteFile("a.md", "hello")).AllParagraphs().Select(p => p.Text));
        Assert.Equal(["hello"], DocReaders.Read(tmp.WriteFile("a.markdown", "hello")).AllParagraphs().Select(p => p.Text));
        Assert.Equal(["hi there"], DocReaders.Read(tmp.WriteFile("a.htm", "<p>hi there</p>")).AllParagraphs().Select(p => p.Text));
        Assert.Equal(["rich"], DocReaders.Read(tmp.WriteFile("a.rtf", @"{\rtf1 rich\par}")).AllParagraphs().Select(p => p.Text));
    }

    [Fact]
    public void StartsWithPk_checks_only_the_first_two_bytes()
    {
        using var tmp = new TempDir();
        Assert.True(DocReaders.StartsWithPk(tmp.WriteFile("a.bin", "PK\u0003\u0004")));
        Assert.False(DocReaders.StartsWithPk(tmp.WriteFile("b.bin", "P")));
        Assert.False(DocReaders.StartsWithPk(tmp.WriteFile("c.bin", "")));
        Assert.False(DocReaders.StartsWithPk(tmp.WriteFile("d.bin", "XPK")));
    }

    // ------------------------------------------------------------------ PlainTextReader

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("one", new[] { "one" })]
    [InlineData("a\nb", new[] { "a", "b" })]
    [InlineData("a\r\nb", new[] { "a", "b" })]
    [InlineData("a\rb", new[] { "a", "b" })]
    [InlineData("a\n", new[] { "a" })]
    [InlineData("a\r\n", new[] { "a" })]
    [InlineData("a\n\nb", new[] { "a", "", "b" })]
    [InlineData("\n", new[] { "" })]
    [InlineData("\n\n", new[] { "", "" })]
    [InlineData("a\r\n\r\nb\r\n", new[] { "a", "", "b" })]
    [InlineData("page\fbreak", new[] { "pagebreak" })]
    public void Every_line_is_a_paragraph_and_a_trailing_line_end_does_not_add_one(string text, string[] expected)
    {
        var model = PlainTextReader.Read(text);
        Assert.Equal(expected, model.Blocks.Cast<DocParagraph>().Select(p => p.Text));
    }

    // ------------------------------------------------------------------ DocModel

    [Fact]
    public void Paragraph_factories_and_run_merging()
    {
        Assert.Equal("plain", DocParagraph.Plain("plain").Text);
        Assert.Equal(ParagraphKind.Normal, DocParagraph.Plain("plain").Kind);

        var h = DocParagraph.Heading(9, "Title");
        Assert.Equal((ParagraphKind.Heading, 6, "Title"), (h.Kind, h.Level, h.Text));
        Assert.Equal(1, DocParagraph.Heading(0, "x").Level);
        Assert.Equal(3, DocParagraph.Heading(3, "x").Level);

        var p = new DocParagraph();
        p.Add("a");
        p.Add("b");
        p.Add("c", bold: true);
        p.Add("");
        p.Add(new DocRun("d", Bold: true));
        p.Add("e", link: "https://x.test/");
        Assert.Equal(3, p.Runs.Count);
        Assert.Equal("ab", p.Runs[0].Text);
        Assert.Equal("cd", p.Runs[1].Text);
        Assert.Equal("abcde", p.Text);
        Assert.False(p.IsEmpty);
        Assert.True(new DocParagraph().IsEmpty);
        Assert.True(DocParagraph.Plain("").IsEmpty);
    }

    [Fact]
    public void ForCell_flattens_headings_and_list_items_and_leaves_normal_paragraphs_alone()
    {
        var normal = DocParagraph.Plain("n");
        Assert.Same(normal, normal.ForCell());

        var heading = DocParagraph.Heading(2, "Head");
        heading.Rtl = true;
        var h = heading.ForCell();
        Assert.Equal((ParagraphKind.Normal, true, true), (h.Kind, h.Rtl, h.Runs.All(r => r.Bold)));

        var item = new DocParagraph { Kind = ParagraphKind.ListItem, Level = 3, Ordered = true };
        item.Add("thing", italic: true);
        var c = item.ForCell();
        Assert.Equal((ParagraphKind.Normal, "• thing"), (c.Kind, c.Text));
        Assert.Contains(c.Runs, r => r.Italic && r.Text.Contains("thing"));
    }

    [Fact]
    public void Model_queries_cover_tables_and_direction()
    {
        var model = new DocModel();
        model.Blocks.Add(DocParagraph.Plain("abc"));
        var table = new DocTable();
        table.Rows.Add([[DocParagraph.Plain("de")], [DocParagraph.Plain("f"), DocParagraph.Plain("gh")]]);
        table.Rows.Add([[DocParagraph.Plain("i")]]);
        model.Blocks.Add(table);
        model.Blocks.Add(new DocPageBreak());

        Assert.Equal(["abc", "de", "f", "gh", "i"], model.AllParagraphs().Select(p => p.Text));
        Assert.Equal(9, model.CharCount());
        Assert.Equal(2, table.ColumnCount);
        Assert.Equal(0, new DocTable().ColumnCount);
        Assert.False(model.HasRtl());

        model.Blocks.Add(DocParagraph.Plain("שלום"));
        Assert.True(model.HasRtl());
        var marked = new DocModel();
        marked.Blocks.Add(new DocParagraph { Rtl = true });
        Assert.True(marked.HasRtl());
    }

    // ------------------------------------------------------------------ TextUtil

    [Theory]
    [InlineData("abc", false)]
    [InlineData("אbc", true)]        // Hebrew
    [InlineData("اbc", true)]        // Arabic
    [InlineData("123 א", true)]      // neutrals first, then Hebrew
    [InlineData("aא", false)]        // Latin first
    [InlineData("", false)]
    [InlineData("123 !?", false)]
    public void The_first_strong_letter_decides_the_direction(string text, bool rtl) =>
        Assert.Equal(rtl, TextUtil.FirstStrongIsRtl(text));

    [Theory]
    [InlineData('֐', true)]
    [InlineData('א', true)]
    [InlineData('ࣿ', true)]
    [InlineData('יִ', true)]
    [InlineData('﷿', true)]
    [InlineData('ﹰ', true)]
    [InlineData('a', false)]
    [InlineData('ऀ', false)]
    public void Right_to_left_ranges(char c, bool expected) => Assert.Equal(expected, TextUtil.IsRtlChar(c));

    [Fact]
    public void XmlSafe_removes_what_xml_cannot_carry_and_keeps_the_rest()
    {
        Assert.Equal("plain\t\r\n text", TextUtil.XmlSafe("plain\t\r\n text"));
        Assert.Equal("ab", TextUtil.XmlSafe("a\u0001\u0008b"));
        Assert.Equal("ab", TextUtil.XmlSafe("a￾b￿"));
        Assert.Equal("😀 ok", TextUtil.XmlSafe("😀 ok"));
        Assert.Equal("ab", TextUtil.XmlSafe("a\uD83Db"));        // lone high surrogate
        Assert.Equal("ab", TextUtil.XmlSafe("a\uDE00b"));        // lone low surrogate
        Assert.Equal("a", TextUtil.XmlSafe("a\uD83D"));          // high surrogate at the very end
        Assert.Equal("", TextUtil.XmlSafe(""));
    }

    [Fact]
    public void Pieces_split_text_at_line_breaks_and_tabs_and_drop_carriage_returns()
    {
        Assert.Equal([(0, "ab")], TextUtil.Pieces("ab"));
        Assert.Equal([(0, "a"), (1, ""), (0, "b"), (2, ""), (0, "c")], TextUtil.Pieces("a\nb\tc"));
        Assert.Equal([(1, ""), (2, "")], TextUtil.Pieces("\n\t"));
        Assert.Equal([(0, "a"), (0, "b")], TextUtil.Pieces("a\rb"));
        Assert.Empty(TextUtil.Pieces(""));
    }

    [Fact]
    public void List_counters_number_per_level_and_restart_when_the_kind_or_depth_changes()
    {
        DocParagraph Item(int level, bool ordered) => new() { Kind = ParagraphKind.ListItem, Level = level, Ordered = ordered };
        var c = new TextUtil.ListCounter();
        Assert.Equal("1.", c.Next(Item(0, true)));
        Assert.Equal("2.", c.Next(Item(0, true)));
        Assert.Equal("•", c.Next(Item(1, false)));
        Assert.Equal("1.", c.Next(Item(1, true)));        // same level changes from bullet to number: restarts
        Assert.Equal("3.", c.Next(Item(0, true)));        // back up: level 0 continues, deeper counters are dropped
        Assert.Equal("1.", c.Next(Item(1, true)));        // deeper level starts afresh
        Assert.Equal("1.", c.Next(Item(20, true)));       // absurd depth is clamped to 8, one counter per level
        c.Reset();
        Assert.Equal("1.", c.Next(Item(0, true)));
        Assert.Equal("•", c.Next(Item(-5, false)));       // negative depth clamps to 0
    }

    // ------------------------------------------------------------------ DocumentConverter / WordInterop guards

    [Theory]
    [InlineData(DocFormat.Pdf, ".pdf")]
    [InlineData(DocFormat.Docx, ".docx")]
    [InlineData(DocFormat.Rtf, ".rtf")]
    [InlineData(DocFormat.Txt, ".txt")]
    [InlineData(DocFormat.Html, ".html")]
    [InlineData(DocFormat.Odt, ".odt")]
    public void Each_format_has_its_extension(DocFormat format, string ext) => Assert.Equal(ext, DocumentConverter.ExtensionFor(format));

    [Fact]
    public void An_undefined_format_is_rejected_everywhere_it_could_be_used()
    {
        var bad = (DocFormat)99;
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentConverter.ExtensionFor(bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentConverter.Write(new DocModel(), bad, new MemoryStream(), "t", true));
        Assert.Throws<ArgumentOutOfRangeException>(() => WordInterop.SaveFormat(bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => WordInterop.SaveFormat(DocFormat.Pdf)); // PDF goes through ExportAsFixedFormat, not SaveAs
    }

    [Fact]
    public async Task Converting_a_file_that_vanished_or_with_a_cancelled_token_fails_cleanly()
    {
        using var tmp = new TempDir();
        var gone = await Assert.ThrowsAsync<ConversionException>(() =>
            DocumentConverter.ToFormatAsync(tmp.Combine("gone.txt"), DocFormat.Pdf, Capabilities.None, CancellationToken.None));
        Assert.Equal("The file no longer exists.", gone.Message);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DocumentConverter.ToFormatAsync(tmp.WriteFile("a.txt", "x"), DocFormat.Pdf, Capabilities.None, cts.Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => DocumentConverter.ToFormatAsync("x", DocFormat.Pdf, null!, CancellationToken.None));
    }

    [Fact]
    public async Task Legacy_doc_without_word_and_packages_that_are_not_zips_are_refused_before_any_work()
    {
        using var tmp = new TempDir();
        var noWord = await Assert.ThrowsAsync<ConversionException>(() =>
            DocumentConverter.ToFormatAsync(tmp.WriteFile("old.doc", "x"), DocFormat.Txt, Capabilities.None, CancellationToken.None));
        Assert.Contains("Microsoft Word", noWord.Message);

        foreach (var name in new[] { "fake.docx", "fake.odt" })
        {
            var ex = await Assert.ThrowsAsync<ConversionException>(() =>
                DocumentConverter.ToFormatAsync(tmp.WriteFile(name, "not a zip"), DocFormat.Txt, Capabilities.None, CancellationToken.None));
            Assert.Contains("isn't a valid", ex.Message);
        }
        Assert.Equal(["fake.docx", "fake.odt", "old.doc"], Directory.GetFiles(tmp.Path).Select(f => Path.GetFileName(f)!).Order().ToArray());
    }

    [Fact]
    public async Task A_document_with_pictures_converts_to_txt_only()
    {
        using var tmp = new TempDir();
        var html = tmp.WriteFile("pic.html", "<p>hello</p><img src=x.png>");
        var refused = await Assert.ThrowsAsync<ConversionException>(() =>
            DocumentConverter.ToFormatAsync(html, DocFormat.Rtf, Capabilities.None, CancellationToken.None));
        Assert.Contains("only be converted to TXT", refused.Message);
        var txt = await DocumentConverter.ToFormatAsync(html, DocFormat.Txt, Capabilities.None, CancellationToken.None);
        Assert.Equal("hello" + "\r\n", File.ReadAllText(txt));
    }

    [Fact]
    public void Probe_hides_formats_for_unsupported_selections_and_never_reads_placeholders()
    {
        using var tmp = new TempDir();
        DocumentProbe.ClearCache();
        var doc = tmp.WriteFile("old.doc", "x");
        var fakeDocx = tmp.WriteFile("fake.docx", "not a zip");
        var txt = tmp.WriteFile("a.txt", "x");
        var html = tmp.WriteFile("pic.html", "<img src=x>");
        var unreadable = tmp.Combine("missing.rtf");

        Assert.False(DocumentProbe.CanConvertTo([doc], DocFormat.Pdf, Capabilities.None));
        Assert.True(DocumentProbe.CanConvertTo([doc], DocFormat.Pdf, Capabilities.All));
        Assert.False(DocumentProbe.CanConvertTo([fakeDocx], DocFormat.Pdf, Capabilities.All));
        Assert.True(DocumentProbe.CanConvertTo([fakeDocx], DocFormat.Docx, Capabilities.All)); // already the target: skipped, not judged
        Assert.True(DocumentProbe.CanConvertTo([txt], DocFormat.Odt, Capabilities.None));
        Assert.False(DocumentProbe.CanConvertTo([html], DocFormat.Docx, Capabilities.None));
        Assert.True(DocumentProbe.CanConvertTo([html], DocFormat.Txt, Capabilities.None));
        Assert.True(DocumentProbe.CanConvertTo([unreadable], DocFormat.Pdf, Capabilities.None)); // unreadable: never hide on a guess
        Assert.True(DocumentProbe.CanConvertTo([], DocFormat.Pdf, Capabilities.None));
        Assert.True(DocumentProbe.CanConvertTo([html], (DocFormat)99, Capabilities.None) == false);
        Assert.False(DocumentProbe.CanConvertTo([txt, doc], DocFormat.Rtf, Capabilities.None));
    }
}
