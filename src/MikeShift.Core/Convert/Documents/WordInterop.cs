using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace MikeShift.Core.Documents;

/// <summary>
/// Optional high-fidelity path through an installed Microsoft Word (late-bound COM, no interop assemblies): DOC/DOCX to PDF
/// (<c>ExportAsFixedFormat</c>) and legacy DOC to the other formats (<c>SaveAs2</c>). Word is started invisible, on a dedicated
/// STA thread, works on a private copy of the input and is always quit again. Only used when <see cref="Capabilities.WordCom"/>.
/// A conversion that does not finish within <see cref="Timeout"/> (or is cancelled) has its own WINWORD.EXE killed, so a hung
/// or prompting Word can never block the job forever.
/// </summary>
internal static partial class WordInterop
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
        var work = Path.Combine(Path.GetTempPath(), "mikeshift-word-" + Guid.NewGuid().ToString("N"));
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
            { IsBackground = true, Name = "MikeShift Word COM" };
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
            catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
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
        object? word = null;
        object? doc = null;
        try
        {
            word = LateBound.CreateInstance("Word.Application")
                   ?? throw new ConversionException("Microsoft Word isn't available.");
            // Word's Application object has no Hwnd (only windows do, and none is open yet), so the instance is found by a
            // process diff. Conversions are serialised by s_gate, so a single new WINWORD.EXE is ours.
            var started = WordPids().Except(instance.Before).ToList();
            if (started.Count == 1) instance.Pid = started[0];
            LateBound.Set(word, "Visible", false);
            LateBound.Set(word, "DisplayAlerts", 0);               // wdAlertsNone
            try { LateBound.Set(word, "AutomationSecurity", 3); } catch (Exception) { /* msoAutomationSecurityForceDisable; optional */ }

            try
            {
                doc = LateBound.Call(LateBound.Get(word, "Documents"), "Open",
                    ("FileName", input), ("ConfirmConversions", false), ("ReadOnly", true), ("AddToRecentFiles", false),
                    ("PasswordDocument", BogusPassword), ("WritePasswordDocument", BogusPassword),
                    ("Visible", false), ("NoEncodingDialog", true), ("OpenAndRepair", false));
            }
            catch (COMException ex) when (IsPasswordError(ex))
            {
                throw new ConversionException("This document is password-protected, so Word can't open it.", ex);
            }

            if (format == DocFormat.Pdf)
            {
                LateBound.Call(doc, "ExportAsFixedFormat", ("OutputFileName", output), ("ExportFormat", WdExportFormatPdf));
            }
            else if (format == DocFormat.Txt)
            {
                LateBound.Call(doc, "SaveAs2", ("FileName", output), ("FileFormat", SaveFormat(format)), ("Encoding", MsoEncodingUtf8), ("AllowSubstitutions", false));
            }
            else
            {
                LateBound.Call(doc, "SaveAs2", ("FileName", output), ("FileFormat", SaveFormat(format)));
            }
        }
        finally
        {
            try { if (doc is not null) LateBound.Call(doc, "Close", ("SaveChanges", 0)); } catch (Exception) { /* ignore */ }
            try { if (word is not null) LateBound.Call(word, "Quit", ("SaveChanges", 0)); } catch (Exception) { /* ignore */ }
            try { if (doc is not null) Marshal.FinalReleaseComObject(doc); } catch (Exception) { /* ignore */ }
            try { if (word is not null) Marshal.FinalReleaseComObject(word); } catch (Exception) { /* ignore */ }
        }
    }

    /// <summary>
    /// Late-bound (IDispatch) calls on a COM automation object, without <c>dynamic</c>: the C# runtime binder is reflection over the
    /// object's type, which an IL-trimmed app cannot rely on. <see cref="Type.InvokeMember(string, BindingFlags, Binder?, object?, object?[]?, ParameterModifier[]?, CultureInfo?, string[]?)"/>
    /// on a COM object goes straight to <c>IDispatch::Invoke</c> with named arguments, as Word's optional-parameter methods need.
    /// COM failures are rethrown as the original <see cref="COMException"/> (reflection wraps them in a <see cref="TargetInvocationException"/>).
    /// </summary>
    private static partial class LateBound
    {
        private const uint ClsctxServer = 0x15;                        // CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER
        private static readonly Guid IidIDispatch = new("00020400-0000-0000-C000-000000000046");

        [LibraryImport("ole32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int CLSIDFromProgID(string progId, out Guid clsid);

        [LibraryImport("ole32.dll")]
        private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

        /// <summary>Starts the automation server registered as <paramref name="progId"/> and returns its IDispatch; null if it is not registered.</summary>
        public static object? CreateInstance(string progId)
        {
            if (CLSIDFromProgID(progId, out var clsid) < 0) return null;
            Marshal.ThrowExceptionForHR(CoCreateInstance(clsid, 0, ClsctxServer, IidIDispatch, out var unknown));
            try { return Marshal.GetObjectForIUnknown(unknown); }
            finally { Marshal.Release(unknown); }
        }

        public static object? Get(object? target, string property) =>
            Invoke(target, property, BindingFlags.GetProperty, [], null);

        public static void Set(object? target, string property, object value) =>
            Invoke(target, property, BindingFlags.SetProperty, [value], null);

        /// <summary>Calls <paramref name="method"/> with every argument passed by name (Word's methods take dozens of optional ones).</summary>
        public static object? Call(object? target, string method, params (string Name, object Value)[] args) =>
            Invoke(target, method, BindingFlags.InvokeMethod, args.Select(a => a.Value).ToArray(), args.Select(a => a.Name).ToArray());

        // IL2075 is about Type.InvokeMember on a Type the trimmer cannot see, which it warns about because for ordinary managed types it looks
        // members up in metadata that may have been trimmed. Here the target is always a COM object (the RCW of the IDispatch that CreateInstance
        // returned, or something Word handed back): for those, InvokeMember never consults managed metadata, it calls IDispatch::Invoke.
        [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The target is a COM object; Type.InvokeMember on it is an IDispatch call, not a metadata lookup.")]
        private static object? Invoke(object? target, string member, BindingFlags kind, object?[] args, string[]? names)
        {
            ArgumentNullException.ThrowIfNull(target);
            try
            {
                return target.GetType().InvokeMember(member, kind, binder: null, target, args, modifiers: null, culture: CultureInfo.InvariantCulture, names);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
