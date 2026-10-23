using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace MikeShift.Core.Documents;

/// <summary>
/// DOCX writer (Open XML SDK): real Heading1-6 styles, bullet/decimal numbering (each ordered list restarts at 1),
/// tables with borders, hyperlinks, RTL paragraphs, hard page breaks, A4 or Letter page size.
/// </summary>
public static class DocxWriter
{
    private const int MaxLevels = 9;

    public static void Write(DocModel model, Stream output, bool a4)
    {
        // The Open XML SDK needs a readable, seekable stream; build in memory and copy.
        using var buffer = new MemoryStream();
        Build(model, buffer, a4);
        buffer.Position = 0;
        buffer.CopyTo(output);
    }

    private static void Build(DocModel model, Stream output, bool a4)
    {
        using var doc = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document, autoSave: true);
        var main = doc.AddMainDocumentPart();
        main.Document = new W.Document();
        var body = main.Document.AppendChild(new W.Body());

        AddStyles(main);
        var numbering = AddNumbering(main);

        var state = new State(main, numbering);
        foreach (var block in model.Blocks)
            AppendBlock(body, block, state, inCell: false);
        state.EndList();

        body.AppendChild(new W.SectionProperties(
            a4 ? new W.PageSize { Width = 11906, Height = 16838 } : new W.PageSize { Width = 12240, Height = 15840 },
            new W.PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440, Header = 720, Footer = 720, Gutter = 0 }));

        main.Document.Save();
    }

    private sealed class State(MainDocumentPart main, W.Numbering numbering)
    {
        public MainDocumentPart Main { get; } = main;
        public W.Numbering Numbering { get; } = numbering;
        private int _nextNumId = 3;
        private int _currentNumId;
        private bool _currentOrdered;
        private bool _inList;

        /// <summary>The numbering instance to use for this list item; ordered lists restart at 1 for every new list.</summary>
        public int NumIdFor(DocParagraph item)
        {
            if (!_inList || _currentOrdered != item.Ordered)
            {
                _inList = true;
                _currentOrdered = item.Ordered;
                if (!item.Ordered)
                {
                    _currentNumId = 1;
                }
                else
                {
                    _currentNumId = _nextNumId++;
                    var num = new W.NumberingInstance { NumberID = _currentNumId };
                    num.AppendChild(new W.AbstractNumId { Val = 1 });
                    for (var l = 0; l < MaxLevels; l++)
                        num.AppendChild(new W.LevelOverride(new W.StartOverrideNumberingValue { Val = 1 }) { LevelIndex = l });
                    Numbering.AppendChild(num);
                }
            }
            return _currentNumId;
        }

        public void EndList() => _inList = false;
    }

    // ------------------------------------------------------------ blocks

    private static void AppendBlock(OpenXmlElement parent, DocBlock block, State state, bool inCell)
    {
        switch (block)
        {
            case DocParagraph p:
                if (p.Kind != ParagraphKind.ListItem) state.EndList();
                parent.AppendChild(BuildParagraph(p, state));
                break;
            case DocTable t:
                state.EndList();
                parent.AppendChild(BuildTable(t, state));
                // Word requires a paragraph after a table at the end of the body or of a cell; a separator also keeps tables apart.
                parent.AppendChild(new W.Paragraph(new W.ParagraphProperties(new W.SpacingBetweenLines { After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto })));
                break;
            case DocPageBreak:
                state.EndList();
                parent.AppendChild(new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page })));
                break;
        }
    }

    private static W.Paragraph BuildParagraph(DocParagraph p, State state)
    {
        var props = new W.ParagraphProperties();
        switch (p.Kind)
        {
            case ParagraphKind.Heading:
                props.ParagraphStyleId = new W.ParagraphStyleId { Val = "Heading" + Math.Clamp(p.Level, 1, 6) };
                break;
            case ParagraphKind.ListItem:
                props.ParagraphStyleId = new W.ParagraphStyleId { Val = "ListParagraph" };
                props.NumberingProperties = new W.NumberingProperties(
                    new W.NumberingLevelReference { Val = Math.Clamp(p.Level, 0, MaxLevels - 1) },
                    new W.NumberingId { Val = state.NumIdFor(p) });
                break;
        }
        if (p.EffectiveRtl)
        {
            props.BiDi = new W.BiDi();
            props.Justification = new W.Justification { Val = W.JustificationValues.Left }; // "start" in a bidi paragraph
        }

        var para = new W.Paragraph();
        if (props.HasChildren) para.AppendChild(props);

        var rtl = p.EffectiveRtl;
        foreach (var run in p.Runs)
        {
            var elements = BuildRun(run, rtl && run.Text.Any(TextUtil.IsRtlChar));
            if (run.Link is { } link && TryLink(state.Main, link) is { } id)
            {
                var h = new W.Hyperlink { Id = id, History = true };
                foreach (var e in elements) h.AppendChild(e);
                para.AppendChild(h);
            }
            else
            {
                foreach (var e in elements) para.AppendChild(e);
            }
        }
        return para;
    }

    private static string? TryLink(MainDocumentPart main, string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)) return null;
        return main.AddHyperlinkRelationship(uri, isExternal: true).Id;
    }

    private static IEnumerable<OpenXmlElement> BuildRun(DocRun run, bool rtlRun)
    {
        foreach (var (kind, text) in TextUtil.Pieces(TextUtil.XmlSafe(run.Text)))
        {
            var r = new W.Run();
            if (run.Bold || run.Italic || run.Underline || run.Link is not null || rtlRun)
            {
                var rp = new W.RunProperties();
                if (run.Link is not null) rp.RunStyle = new W.RunStyle { Val = "Hyperlink" };
                if (run.Bold) rp.Bold = new W.Bold();
                if (run.Italic) rp.Italic = new W.Italic();
                if (run.Underline) rp.Underline = new W.Underline { Val = W.UnderlineValues.Single };
                if (rtlRun) rp.RightToLeftText = new W.RightToLeftText();
                r.AppendChild(rp);
            }
            switch (kind)
            {
                case 0:
                    r.AppendChild(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });
                    break;
                case 1:
                    r.AppendChild(new W.Break());
                    break;
                default:
                    r.AppendChild(new W.TabChar());
                    break;
            }
            yield return r;
        }
    }

    private static W.Table BuildTable(DocTable t, State state)
    {
        var cols = Math.Max(1, t.ColumnCount);
        var table = new W.Table();
        table.AppendChild(new W.TableProperties(
            new W.TableStyle { Val = "TableGrid" },
            new W.TableWidth { Width = "5000", Type = W.TableWidthUnitValues.Pct },
            new W.TableLayout { Type = W.TableLayoutValues.Autofit }));
        var grid = new W.TableGrid();
        for (var c = 0; c < cols; c++) grid.AppendChild(new W.GridColumn { Width = (9000 / cols).ToString(CultureInfo.InvariantCulture) });
        table.AppendChild(grid);

        foreach (var row in t.Rows)
        {
            var tr = new W.TableRow();
            for (var c = 0; c < cols; c++)
            {
                var cellParagraphs = c < row.Count ? row[c] : [];
                var tc = new W.TableCell(new W.TableCellProperties(new W.TableCellWidth { Width = (5000 / cols).ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Pct }));
                if (cellParagraphs.Count == 0) cellParagraphs = [new DocParagraph()];
                foreach (var cp in cellParagraphs)
                    tc.AppendChild(BuildParagraph(cp, state));
                tr.AppendChild(tc);
            }
            table.AppendChild(tr);
        }
        return table;
    }

    // ------------------------------------------------------------ styles & numbering

    private static void AddStyles(MainDocumentPart main)
    {
        var part = main.AddNewPart<StyleDefinitionsPart>();
        var styles = new W.Styles(
            new W.DocDefaults(
                new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(
                    new W.RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", EastAsia = "Calibri", ComplexScript = "Arial" },
                    new W.FontSize { Val = "22" }, new W.FontSizeComplexScript { Val = "22" },
                    new W.Languages { Val = "en-US", EastAsia = "en-US", Bidi = "he-IL" })),
                new W.ParagraphPropertiesDefault(new W.ParagraphPropertiesBaseStyle(
                    new W.SpacingBetweenLines { After = "160", Line = "259", LineRule = W.LineSpacingRuleValues.Auto }))));

        styles.AppendChild(new W.Style(
            new W.StyleName { Val = "Normal" }, new W.PrimaryStyle())
        { Type = W.StyleValues.Paragraph, StyleId = "Normal", Default = true });

        styles.AppendChild(new W.Style(
            new W.StyleName { Val = "Default Paragraph Font" }, new W.UIPriority { Val = 1 }, new W.SemiHidden(), new W.UnhideWhenUsed())
        { Type = W.StyleValues.Character, StyleId = "DefaultParagraphFont", Default = true });

        var sizes = new[] { 32, 26, 24, 22, 22, 22 }; // half-points: 16, 13, 12, 11 pt
        for (var level = 1; level <= 6; level++)
        {
            styles.AppendChild(new W.Style(
                new W.StyleName { Val = "heading " + level },
                new W.BasedOn { Val = "Normal" },
                new W.NextParagraphStyle { Val = "Normal" },
                new W.UIPriority { Val = 9 },
                new W.PrimaryStyle(),
                new W.StyleParagraphProperties(
                    new W.KeepNext(), new W.KeepLines(),
                    new W.SpacingBetweenLines { Before = level == 1 ? "240" : "200", After = "80" },
                    new W.OutlineLevel { Val = level - 1 }),
                new W.StyleRunProperties(new W.Bold(), new W.FontSize { Val = sizes[level - 1].ToString(CultureInfo.InvariantCulture) }))
            { Type = W.StyleValues.Paragraph, StyleId = "Heading" + level });
        }

        styles.AppendChild(new W.Style(
            new W.StyleName { Val = "List Paragraph" },
            new W.BasedOn { Val = "Normal" },
            new W.UIPriority { Val = 34 },
            new W.PrimaryStyle(),
            new W.StyleParagraphProperties(
                new W.SpacingBetweenLines { After = "40" },
                new W.Indentation { Left = "720" },
                new W.ContextualSpacing()))
        { Type = W.StyleValues.Paragraph, StyleId = "ListParagraph" });

        styles.AppendChild(new W.Style(
            new W.StyleName { Val = "Hyperlink" },
            new W.BasedOn { Val = "DefaultParagraphFont" },
            new W.UIPriority { Val = 99 },
            new W.UnhideWhenUsed(),
            new W.StyleRunProperties(new W.Color { Val = "0563C1" }, new W.Underline { Val = W.UnderlineValues.Single }))
        { Type = W.StyleValues.Character, StyleId = "Hyperlink" });

        styles.AppendChild(new W.Style(
            new W.StyleName { Val = "Normal Table" },
            new W.UIPriority { Val = 99 },
            new W.SemiHidden(), new W.UnhideWhenUsed(),
            new W.StyleTableProperties(
                new W.TableIndentation { Width = 0, Type = W.TableWidthUnitValues.Dxa },
                new W.TableCellMarginDefault(
                    new W.TopMargin { Width = "0", Type = W.TableWidthUnitValues.Dxa },
                    new W.TableCellLeftMargin { Width = 108, Type = W.TableWidthValues.Dxa },
                    new W.BottomMargin { Width = "0", Type = W.TableWidthUnitValues.Dxa },
                    new W.TableCellRightMargin { Width = 108, Type = W.TableWidthValues.Dxa })))
        { Type = W.StyleValues.Table, StyleId = "TableNormal", Default = true });

        styles.AppendChild(new W.Style(
            new W.StyleName { Val = "Table Grid" },
            new W.BasedOn { Val = "TableNormal" },
            new W.UIPriority { Val = 39 },
            new W.StyleParagraphProperties(new W.SpacingBetweenLines { After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }),
            new W.StyleTableProperties(new W.TableBorders(
                Border<W.TopBorder>(), Border<W.LeftBorder>(), Border<W.BottomBorder>(), Border<W.RightBorder>(),
                Border<W.InsideHorizontalBorder>(), Border<W.InsideVerticalBorder>())))
        { Type = W.StyleValues.Table, StyleId = "TableGrid" });

        part.Styles = styles;
        styles.Save();
    }

    private static T Border<T>() where T : W.BorderType, new() =>
        new() { Val = W.BorderValues.Single, Size = 4, Space = 0, Color = "auto" };

    private static W.Numbering AddNumbering(MainDocumentPart main)
    {
        var part = main.AddNewPart<NumberingDefinitionsPart>();
        var numbering = new W.Numbering();

        // abstractNum 0: bullets, abstractNum 1: decimal numbers (nested levels use letters and roman numerals).
        numbering.AppendChild(Abstract(0, ordered: false));
        numbering.AppendChild(Abstract(1, ordered: true));
        numbering.AppendChild(new W.NumberingInstance(new W.AbstractNumId { Val = 0 }) { NumberID = 1 });
        numbering.AppendChild(new W.NumberingInstance(new W.AbstractNumId { Val = 1 }) { NumberID = 2 });

        part.Numbering = numbering;
        return numbering;
    }

    private static W.AbstractNum Abstract(int id, bool ordered)
    {
        var abs = new W.AbstractNum(new W.MultiLevelType { Val = W.MultiLevelValues.HybridMultilevel }) { AbstractNumberId = id };
        for (var l = 0; l < MaxLevels; l++)
        {
            W.NumberFormatValues fmt;
            string text;
            if (ordered)
            {
                fmt = (l % 3) switch { 0 => W.NumberFormatValues.Decimal, 1 => W.NumberFormatValues.LowerLetter, _ => W.NumberFormatValues.LowerRoman };
                text = $"%{l + 1}.";
            }
            else
            {
                fmt = W.NumberFormatValues.Bullet;
                text = (l % 3) switch { 0 => "•", 1 => "o", _ => "▪" };
            }
            var level = new W.Level(
                new W.StartNumberingValue { Val = 1 },
                new W.NumberingFormat { Val = fmt },
                new W.LevelText { Val = text },
                new W.LevelJustification { Val = W.LevelJustificationValues.Left },
                new W.PreviousParagraphProperties(new W.Indentation { Left = (720 * (l + 1)).ToString(CultureInfo.InvariantCulture), Hanging = "360" }))
            { LevelIndex = l };
            abs.AppendChild(level);
        }
        return abs;
    }
}
