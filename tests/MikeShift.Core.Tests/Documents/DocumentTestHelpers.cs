using System.Text;
using MikeShift.Core.Documents;
using UglyToad.PdfPig;

namespace MikeShift.Core.Tests.Documents;

/// <summary>Helpers shared by the document tests.</summary>
internal static class DocumentTestHelpers
{
    /// <summary>A compact, comparable description of a model (see <see cref="Fixtures.DocumentOutline"/>); empty paragraphs are ignored.</summary>
    public static string[] Outline(DocModel model)
    {
        var list = new List<string>();
        foreach (var block in model.Blocks)
        {
            switch (block)
            {
                case DocParagraph p when p.IsEmpty:
                    break;
                case DocParagraph p:
                    list.Add(Describe(p));
                    break;
                case DocTable t:
                    list.Add("T:" + string.Join(";", t.Rows.Select(r =>
                        "[" + string.Join("|", r.Select(c => string.Join(" ", c.Where(x => !x.IsEmpty).Select(x => x.Text)))) + "]")));
                    break;
                case DocPageBreak:
                    list.Add("PB");
                    break;
            }
        }
        return [.. list];
    }

    private static string Describe(DocParagraph p) => p.Kind switch
    {
        ParagraphKind.Heading => $"H{p.Level}:{p.Text}",
        ParagraphKind.ListItem => $"{(p.Ordered ? "OL" : "UL")}{p.Level}:{p.Text}",
        _ => "P:" + string.Concat(p.Runs.Select(Mark)),
    };

    private static string Mark(DocRun r)
    {
        var t = r.Text;
        if (r.Underline) t = $"~{t}~";
        if (r.Italic) t = $"_{t}_";
        if (r.Bold) t = $"**{t}**";
        return t;
    }

    public static DocModel Read(string path) => DocReaders.Read(path);

    public static string PdfText(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    public static int PdfPageCount(string path)
    {
        using var pdf = PdfDocument.Open(path);
        return pdf.NumberOfPages;
    }

    /// <summary>The words of a PDF with whitespace collapsed, for substring checks.</summary>
    public static string PdfFlat(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var words = pdf.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text);
        return string.Join(" ", words);
    }

    public static Task<string> ConvertAsync(string input, DocFormat format, Capabilities? caps = null) =>
        DocumentConverter.ToFormatAsync(input, format, caps ?? Capabilities.None, CancellationToken.None);

    /// <summary>Writes the named sample fixture into <paramref name="tmp"/> and returns its path.</summary>
    public static string Sample(TempDir tmp, string kind) => kind switch
    {
        "docx" => tmp.WriteFile("sample.docx", Fixtures.DocxSample()),
        "odt" => tmp.WriteFile("sample.odt", Fixtures.OdtSample()),
        "rtf" => tmp.WriteFile("sample.rtf", Encoding.ASCII.GetBytes(Fixtures.RtfSample())),
        "html" => tmp.WriteFile("sample.html", Fixtures.HtmlSample()),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string ExtensionOf(DocFormat f) => DocumentConverter.ExtensionFor(f);
}
