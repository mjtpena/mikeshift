using MikeShift.Core.Pdf;

namespace MikeShift.Core.Tests.Pdf;

public class PdfTextTests
{
    [Fact]
    public async Task Text_is_extracted_in_reading_order_with_blank_lines_between_pages()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("doc.pdf", Fixtures.PdfText(3));
        using var guard = OriginalsGuard.Watch(pdf);

        var output = await PdfConverter.ToTextAsync(pdf, default);

        Assert.Equal(tmp.Combine("doc.txt"), output);
        var lines = File.ReadAllText(output).Replace("\r\n", "\n").Split('\n');
        Assert.Equal(
            [
                "Page 1 of 3",
                "The quick brown fox jumps over the lazy dog.",
                "Second line on page 1.",
                "",
                "Page 2 of 3",
                "The quick brown fox jumps over the lazy dog.",
                "Second line on page 2.",
                "",
                "Page 3 of 3",
                "The quick brown fox jumps over the lazy dog.",
                "Second line on page 3.",
            ],
            lines);
    }

    [Fact]
    public async Task A_scan_has_no_text_and_says_so()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("scan.pdf", Fixtures.PdfScan());

        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.ToTextAsync(pdf, default));

        Assert.Equal("No text found — this PDF is probably a scan.", ex.Message);
        Assert.Equal(["scan.pdf"], Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).ToArray());
    }

    [Fact]
    public async Task Hebrew_in_logical_order_is_extracted()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("he.pdf", Fixtures.PdfHebrew(reversed: false));

        var output = await PdfConverter.ToTextAsync(pdf, default);

        var text = File.ReadAllText(output);
        Assert.Contains(Fixtures.HebrewShalomOlam, text);
    }

    [Fact]
    public async Task Hebrew_stored_reversed_is_refused()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("he-rev.pdf", Fixtures.PdfHebrew(reversed: true));

        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.ToTextAsync(pdf, default));

        Assert.Contains("reverse", ex.Message);
        Assert.Equal(["he-rev.pdf"], Directory.GetFiles(tmp.Path).Select(p => Path.GetFileName(p)!).ToArray());
    }

    [Fact]
    public async Task Encrypted_pdf_is_refused()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("locked.pdf", Fixtures.PdfEncrypted());
        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.ToTextAsync(pdf, default));
        Assert.Contains("password", ex.Message);
    }

    [Fact]
    public async Task Corrupt_pdf_is_refused()
    {
        using var tmp = new TempDir();
        var pdf = tmp.WriteFile("broken.pdf", Fixtures.PdfCorrupt());
        var ex = await Assert.ThrowsAsync<ConversionException>(() => PdfConverter.ToTextAsync(pdf, default));
        Assert.Contains("damaged", ex.Message);
    }

    // ---------------------------------------------------------------- the heuristic itself

    private const string Shalom = "שלום";   // ends in final mem
    private const string Melech = "מלך";         // ends in final kaf
    private const string Amen = "אמן";           // ends in final nun
    private const string Sfarim = "ספרים";
    private const string Beit = "בית";           // no final form

    private static string Rev(string s) => new(s.Reverse().ToArray());

    [Theory]
    [InlineData("")]
    [InlineData("plain English text, nothing Hebrew")]
    [InlineData("12345 !?")]
    public void Non_hebrew_text_is_never_reversed(string text) => Assert.False(PdfText.LooksReversedHebrew(text));

    [Fact]
    public void Correct_hebrew_is_not_reversed()
    {
        Assert.False(PdfText.LooksReversedHebrew($"{Shalom} {Melech} {Amen} {Sfarim} {Beit}"));
    }

    [Fact]
    public void Reversed_hebrew_is_detected()
    {
        Assert.True(PdfText.LooksReversedHebrew($"{Rev(Shalom)} {Rev(Melech)} {Rev(Amen)} {Rev(Sfarim)} {Rev(Beit)}"));
    }

    [Fact]
    public void Mixed_text_follows_the_majority()
    {
        // 3 correct words, 1 reversed -> fine; 1 correct, 3 reversed -> refuse.
        Assert.False(PdfText.LooksReversedHebrew($"{Shalom} {Melech} {Amen} {Rev(Sfarim)}"));
        Assert.True(PdfText.LooksReversedHebrew($"{Shalom} {Rev(Melech)} {Rev(Amen)} {Rev(Sfarim)}"));
    }

    [Fact]
    public void A_single_odd_word_is_not_enough()
    {
        Assert.False(PdfText.LooksReversedHebrew(Rev(Shalom)));
    }

    [Fact]
    public void Final_forms_are_counted_per_word_position()
    {
        Assert.Equal((3, 0), PdfText.CountFinalForms($"{Shalom} {Melech} {Amen}"));
        Assert.Equal((0, 3), PdfText.CountFinalForms($"{Rev(Shalom)} {Rev(Melech)} {Rev(Amen)}"));
        // Single letters and words without final forms count for nothing; embedded Latin text splits words.
        Assert.Equal((1, 0), PdfText.CountFinalForms($"ם {Beit} abc {Shalom}"));
    }
}
