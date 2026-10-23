using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Shiftvert.Core.Documents;

/// <summary>
/// Optional high-fidelity path through an installed Microsoft Word (late-bound COM, no interop assemblies): DOC/DOCX to PDF
/// (<c>ExportAsFixedFormat</c>) and legacy DOC to the other formats (<c>SaveAs2</c>). Word is started invisible, on a dedicated
/// STA thread, works on a private copy of the input and is always quit again. Only used when <see cref="Capabilities.WordCom"/>.
/// A conversion that does not finish within <see cref="Timeout"/> (or is cancelled) has its own WINWORD.EXE killed, so a hung
/// or prompting Word can never block the job forever.
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

    /// <summary>How long one Word conversion may take before Word is killed and the job fails (test hook: settable).</summary>
    internal static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(90);

    // A password that no document uses: a protected file fails fast instead of making Word prompt.
    private const string BogusPassword = "\u0001";

    /// <summary>
    /// One Word conversion at a time per process. Word instances started together start slowly and stall each other, and the
    /// "which WINWORD.EXE is mine" bookkeeping below (a before/after process diff) is only unambiguous when no other conversion
    /// is launching or killing a Word at the same moment. The wait honours cancellation and does not count against <see cref="Timeout"/>.
    /// </summary>
    private static readonly SemaphoreSlim s_gate = new(1, 1);

    /// <summary>The WINWORD.EXE processes that existed before this conversion, and the one it started (once known).</summary>
    private sealed class Instance(HashSet<int> before)
    {
        public HashSet<int> Before { get; } = before;
        public DateTime StartedUtc { get; } = DateTime.UtcNow;
        public volatile int Pid;
    }

    private static HashSet<int> WordPids()
    {
        var pids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("WINWORD"))
        {
            using (p) pids.Add(p.Id);
        }
        return pids;
    }

    /// <summary>
    /// Kills the Word process this conversion started (never one that was already running). When the pid isn't known yet
    /// (Word is still starting) only WINWORD.EXE processes that appeared since this conversion began are candidates.
    /// </summary>
    private static void Kill(Instance instance)
    {
        var pids = instance.Pid != 0 ? [instance.Pid] : WordPids().Except(instance.Before).ToList();
        foreach (var pid in pids)
        {
            if (instance.Before.Contains(pid)) continue;
            try
            {
                using var p = Process.GetProcessById(pid);
                if (!string.Equals(p.ProcessName, "WINWORD", StringComparison.OrdinalIgnoreCase)) continue;
                if (instance.Pid == 0 && p.StartTime.ToUniversalTime() < instance.StartedUtc.AddSeconds(-1)) continue;
                p.Kill();
            }
            catch (Exception)
            {
                // already gone
            }
        }
    }

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
        await s_gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ConvertCoreAsync(input, format, outputPath, ct).ConfigureAwait(false);
        }
        finally
        {
            s_gate.Release();
        }
    }

    private static async Task ConvertCoreAsync(string input, DocFormat format, string outputPath, CancellationToken ct)
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

            var instance = new Instance(WordPids());
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    Run(copy, format, produced, instance);
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

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(Timeout);
            try
            {
                await tcs.Task.WaitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Timed out or cancelled: take Word down so the COM thread unblocks, then give it a moment to unwind.
                Kill(instance);
                _ = tcs.Task.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
                await Task.WhenAny(tcs.Task, Task.Delay(3000, CancellationToken.None)).ConfigureAwait(false);
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                throw new ConversionException("Word didn't respond, so the conversion was stopped. Close any Word dialogs and try again.");
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

    // 5408/5174-style VBA errors ("password is incorrect") surface as HRESULT 0x800A1520; the text check covers the English message.
    private static bool IsPasswordError(COMException ex) =>
        (ex.HResult & 0xFFFF) == 0x1520 || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);

    private static void Run(string input, DocFormat format, string output, Instance instance)
    {
        var type = Type.GetTypeFromProgID("Word.Application")
                   ?? throw new ConversionException("Microsoft Word isn't available.");
        dynamic? word = null;
        dynamic? doc = null;
        try
        {
            word = Activator.CreateInstance(type) ?? throw new ConversionException("Microsoft Word couldn't be started.");
            // Word's Application object has no Hwnd (only windows do, and none is open yet), so the instance is found by a
            // process diff. Conversions are serialised by s_gate, so a single new WINWORD.EXE is ours.
            var started = WordPids().Except(instance.Before).ToList();
            if (started.Count == 1) instance.Pid = started[0];
            word.Visible = false;
            word.DisplayAlerts = 0;               // wdAlertsNone
            try { word.AutomationSecurity = 3; } catch (Exception) { /* msoAutomationSecurityForceDisable; optional */ }

            try
            {
                doc = word.Documents.Open(
                    FileName: input, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false,
                    PasswordDocument: BogusPassword, WritePasswordDocument: BogusPassword,
                    Visible: false, NoEncodingDialog: true, OpenAndRepair: false);
            }
            catch (COMException ex) when (IsPasswordError(ex))
            {
                throw new ConversionException("This document is password-protected, so Word can't open it.", ex);
            }

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
