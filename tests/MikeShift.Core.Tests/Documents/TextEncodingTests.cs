using System.Text;
using MikeShift.Core.Documents;
using static MikeShift.Core.Tests.Documents.DocumentTestHelpers;

namespace MikeShift.Core.Tests.Documents;

public class TextEncodingTests
{
    [Fact]
    public void Utf8_without_bom_is_detected_by_strict_validation()
    {
        var text = TextDecoding.Decode(Fixtures.DocTxtUtf8());
        Assert.StartsWith(Fixtures.DocUnicodeLine, text);
    }

    [Fact]
    public void Utf8_bom_is_stripped()
    {
        var text = TextDecoding.Decode(Fixtures.DocTxtUtf8Bom());
        Assert.StartsWith(Fixtures.DocUnicodeLine, text);
        Assert.DoesNotContain('﻿', text);
    }

    [Fact]
    public void Utf16_boms_are_honoured()
    {
        Assert.Equal("héllo", TextDecoding.Decode([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("héllo")]));
        Assert.Equal("héllo", TextDecoding.Decode([.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes("héllo")]));
    }

    [Fact]
    public void Runs_of_three_bytes_in_C0_to_FA_mean_windows_1255_hebrew()
    {
        var bytes = Fixtures.DocTxtHebrew1255();
        Assert.Equal(1255, TextDecoding.Detect(bytes).Encoding.CodePage);
        Assert.StartsWith(Fixtures.DocHebrewText, TextDecoding.Decode(bytes));
    }

    [Fact]
    public void Other_invalid_utf8_is_windows_1252()
    {
        var bytes = Fixtures.DocTxtLatin1252();
        Assert.Equal(1252, TextDecoding.Detect(bytes).Encoding.CodePage);
        var text = TextDecoding.Decode(bytes);
        Assert.StartsWith(Fixtures.DocLatinLine, text);
        Assert.Contains("5€ — “fine”", text);
    }

    [Fact]
    public void Heuristic_boundary_two_high_bytes_are_not_hebrew_three_are()
    {
        TextDecoding.EnsureProviders();
        Assert.Equal(1252, TextDecoding.LegacyEncoding([0x61, 0xE0, 0xE1, 0x62, 0xE0, 0xE1, 0x62]).CodePage);
        Assert.Equal(1255, TextDecoding.LegacyEncoding([0x61, 0xE0, 0xE1, 0xE2, 0x62]).CodePage);
        Assert.Equal(1252, TextDecoding.LegacyEncoding([0xFB, 0xFC, 0xFD]).CodePage); // above 0xFA
    }

    [Fact]
    public void Ascii_and_empty_input_are_utf8()
    {
        Assert.Equal("plain", TextDecoding.Decode("plain"u8.ToArray()));
        Assert.Equal("", TextDecoding.Decode([]));
    }

    [Fact]
    public async Task Each_text_variant_converts_with_its_text_intact()
    {
        using var tmp = new TempDir();
        var cases = new (string Name, byte[] Bytes, string Expect)[]
        {
            ("utf8.txt", Fixtures.DocTxtUtf8(), Fixtures.DocUnicodeLine),
            ("bom.txt", Fixtures.DocTxtUtf8Bom(), Fixtures.DocUnicodeLine),
            ("heb.txt", Fixtures.DocTxtHebrew1255(), Fixtures.DocHebrewText),
            ("lat.txt", Fixtures.DocTxtLatin1252(), Fixtures.DocLatinLine),
        };
        foreach (var (name, bytes, expect) in cases)
        {
            var input = tmp.WriteFile(name, bytes);
            using var guard = OriginalsGuard.Watch(input);
            var docx = await ConvertAsync(input, DocFormat.Docx);
            Assert.Equal(expect, ((DocParagraph)Read(docx).Blocks[0]).Text);
            var html = await ConvertAsync(input, DocFormat.Html);
            Assert.Equal(expect, ((DocParagraph)Read(html).Blocks[0]).Text);
        }
    }

    [Fact]
    public async Task Hebrew_1255_html_without_declaration_uses_the_heuristic()
    {
        using var tmp = new TempDir();
        TextDecoding.EnsureProviders();
        var html = tmp.WriteFile("h.html", [.. "<p>"u8.ToArray(), .. Encoding.GetEncoding(1255).GetBytes(Fixtures.DocHebrewText), .. "</p>"u8.ToArray()]);
        Assert.Equal(Fixtures.DocHebrewText, ((DocParagraph)Read(html).Blocks[0]).Text);
        await Task.CompletedTask;
    }
}
