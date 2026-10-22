using System.Runtime.InteropServices;

namespace Shiftvert.Core.Documents;

/// <summary>
/// Optional high-fidelity path through an installed Microsoft Word (late-bound COM, no interop assemblies): DOC/DOCX to PDF
/// (<c>ExportAsFixedFormat</c>) and legacy DOC to the other formats (<c>SaveAs2</c>). Word is started invisible, on a dedicated
/// STA thread, works on a private copy of the input and is always quit again. Only used when <see cref="Capabilities.WordCom"/>.
/// </summary>
internal static class WordInterop
{
    // WdSaveFormat / WdExportFormat values.
    private const int WdFormatText = 2;
    private const int WdFormatRtf = 6;
    private const int WdFormatFilteredHtml = 10;
    private const int WdFormatDocumentDefault = 16; // .docx
    private const int WdFormatOpenDocumentText = 23;
    private const int WdExportFormatPdf = 17;
    private const int MsoEncodingUtf8 = 65001;

    public static int SaveFormat(DocFormat format) => format switch
    {
        DocFormat.Docx => WdFormatDocumentDefault,
        DocFormat.Rtf => WdFormatRtf,
        DocFormat.Txt => WdFormatText,
        DocFormat.Html => WdFormatFilteredHtml,
        DocFormat.Odt => WdFormatOpenDocumentText,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>
    /// Converts <paramref name="input"/> to <paramref name="format"/> with Word and copies the result to <paramref name="outputPath"/>.
    /// Throws <see cref="ConversionException"/> if Word can't be started or can't open the file.
    /// </summary>
    public static async Task ConvertAsync(string input, DocFormat format, string outputPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var work = Path.Combine(Path.GetTempPath(), "shiftvert-word-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var ext = Path.GetExtension(input);
            var copy = Path.Combine(work, "input" + ext);
            File.Copy(input, copy);
            var produced = Path.Combine(work, "output" + ExtensionOf(format));

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    Run(copy, format, produced);
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            })
            { IsBackground = true, Name = "Shiftvert Word COM" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            try
            {
                await tcs.Task.ConfigureAwait(false);
            }
            catch (ConversionException)
            {
                throw;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException
                                           or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                throw new ConversionException("Microsoft Word couldn't convert this file.", ex);
            }

            ct.ThrowIfCancellationRequested();
            if (!File.Exists(produced))
                throw new ConversionException("Microsoft Word didn't produce an output file.");
            File.Copy(produced, outputPath, overwrite: true);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (Exception) { /* best effort */ }
        }
    }

    private static string ExtensionOf(DocFormat format) => format switch
    {
        DocFormat.Pdf => ".pdf",
        DocFormat.Docx => ".docx",
        DocFormat.Rtf => ".rtf",
        DocFormat.Txt => ".txt",
        DocFormat.Html => ".html",
        _ => ".odt",
    };

    private static void Run(string input, DocFormat format, string output)
    {
        var type = Type.GetTypeFromProgID("Word.Application")
                   ?? throw new ConversionException("Microsoft Word isn't available.");
        dynamic? word = null;
        dynamic? doc = null;
        try
        {
            word = Activator.CreateInstance(type) ?? throw new ConversionException("Microsoft Word couldn't be started.");
            word.Visible = false;
            word.DisplayAlerts = 0;               // wdAlertsNone
            try { word.AutomationSecurity = 3; } catch (Exception) { /* msoAutomationSecurityForceDisable; optional */ }

            doc = word.Documents.Open(
                FileName: input, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false,
                Visible: false, NoEncodingDialog: true, OpenAndRepair: false);

            if (format == DocFormat.Pdf)
            {
                doc.ExportAsFixedFormat(OutputFileName: output, ExportFormat: WdExportFormatPdf);
            }
            else if (format == DocFormat.Txt)
            {
                doc.SaveAs2(FileName: output, FileFormat: SaveFormat(format), Encoding: MsoEncodingUtf8, AllowSubstitutions: false);
            }
            else
            {
                doc.SaveAs2(FileName: output, FileFormat: SaveFormat(format));
            }
        }
        finally
        {
            try { if (doc is not null) doc.Close(SaveChanges: 0); } catch (Exception) { /* ignore */ }
            try { if (word is not null) word.Quit(SaveChanges: 0); } catch (Exception) { /* ignore */ }
            try { if (doc is not null) Marshal.FinalReleaseComObject((object)doc); } catch (Exception) { /* ignore */ }
            try { if (word is not null) Marshal.FinalReleaseComObject((object)word); } catch (Exception) { /* ignore */ }
        }
    }
}
