using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using MikeShift.Core.Documents;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace MikeShift.Core.Tests.Documents;

/// <summary>Bold, italic and underline come from the most-derived style that sets them, so a derived style can switch a base style's formatting off.</summary>
public class DocxStyleChainTests
{
    private sealed record StyleSpec(string Id, string? BasedOn = null, string? Bold = null, string? Italic = null, string? Underline = null, string Type = "paragraph");

    private static W.Style Style(StyleSpec s)
    {
        var style = new W.Style { Type = s.Type == "character" ? W.StyleValues.Character : W.StyleValues.Paragraph, StyleId = s.Id };
        style.Append(new W.StyleName { Val = s.Id });
        if (s.BasedOn is not null) style.Append(new W.BasedOn { Val = s.BasedOn });
        var rpr = new W.StyleRunProperties();
        if (s.Bold is not null) rpr.Append(s.Bold == "on" ? new W.Bold() : new W.Bold { Val = false });
        if (s.Italic is not null) rpr.Append(s.Italic == "on" ? new W.Italic() : new W.Italic { Val = false });
        if (s.Underline is not null) rpr.Append(new W.Underline { Val = s.Underline == "none" ? W.UnderlineValues.None : W.UnderlineValues.Single });
        if (rpr.HasChildren) style.Append(rpr);
        return style;
    }

    private static DocRun OneRun(string? paragraphStyle, string? runStyle, StyleSpec[] styles, W.RunProperties? direct = null)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var run = new W.Run(new W.Text("text"));
            if (direct is not null) run.PrependChild(direct);
            if (runStyle is not null) (run.RunProperties ??= new W.RunProperties()).RunStyle = new W.RunStyle { Val = runStyle };
            var paragraph = new W.Paragraph(run);
            if (paragraphStyle is not null) paragraph.PrependChild(new W.ParagraphProperties(new W.ParagraphStyleId { Val = paragraphStyle }));
            main.Document = new W.Document(new W.Body(paragraph));
            var stylePart = main.AddNewPart<StyleDefinitionsPart>();
            stylePart.Styles = new W.Styles(styles.Select(Style));
            main.Document.Save();
        }
        ms.Position = 0;
        return DocxReader.Read(ms).Blocks.OfType<DocParagraph>().Single().Runs.Single();
    }

    [Fact]
    public void A_base_style_formatting_applies_through_an_unspecific_derived_style()
    {
        var run = OneRun("Derived", null, [new("Base", Bold: "on", Italic: "on", Underline: "single"), new("Derived", BasedOn: "Base")]);
        Assert.True(run.Bold);
        Assert.True(run.Italic);
        Assert.True(run.Underline);
    }

    [Fact]
    public void A_derived_style_can_switch_bold_italic_and_underline_off()
    {
        var run = OneRun("Derived", null,
            [new("Base", Bold: "on", Italic: "on", Underline: "single"), new("Derived", BasedOn: "Base", Bold: "off", Italic: "off", Underline: "none")]);
        Assert.False(run.Bold);
        Assert.False(run.Italic);
        Assert.False(run.Underline);
    }

    [Fact]
    public void A_derived_style_can_switch_one_property_off_and_keep_the_others()
    {
        var run = OneRun("Derived", null, [new("Base", Bold: "on", Italic: "on"), new("Derived", BasedOn: "Base", Bold: "off")]);
        Assert.False(run.Bold);
        Assert.True(run.Italic);
    }

    [Fact]
    public void A_derived_style_can_switch_a_property_on_that_its_base_turned_off()
    {
        var run = OneRun("Derived", null, [new("Base", Bold: "off"), new("Derived", BasedOn: "Base", Bold: "on")]);
        Assert.True(run.Bold);
    }

    [Fact]
    public void A_character_style_overrides_the_paragraph_style_and_direct_formatting_overrides_both()
    {
        StyleSpec[] styles =
        [
            new("Para", Bold: "on", Underline: "single"),
            new("CharBase", Type: "character", Italic: "on", Bold: "on"),
            new("CharNoBold", BasedOn: "CharBase", Type: "character", Bold: "off"),
        ];
        var run = OneRun("Para", "CharNoBold", styles);
        Assert.False(run.Bold);      // the character style's off beats the paragraph style's on
        Assert.True(run.Italic);     // inherited from the character style's base
        Assert.True(run.Underline);  // not set by the character style: the paragraph style stays

        var direct = OneRun("Para", "CharNoBold", styles, new W.RunProperties(new W.Bold(), new W.Underline { Val = W.UnderlineValues.None }));
        Assert.True(direct.Bold);
        Assert.False(direct.Underline);
    }
}
