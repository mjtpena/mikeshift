using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Shiftvert.Core.Pdf;

/// <summary>Reads and opens PDFs with clear user-facing failures (encrypted, damaged, not a PDF).</summary>
internal static class PdfGuard
{
    public const string EncryptedMessage = "This PDF is password-protected.";
    public const string DamagedMessage = "This PDF is damaged and can't be read.";
    public const string NotPdfMessage = "This file isn't a valid PDF.";

    /// <summary>Reads the file fully and checks the <c>%PDF</c> magic.</summary>
    public static byte[] ReadBytes(string path)
    {
        byte[] bytes;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > int.MaxValue) throw new ConversionException("This PDF is too large to process.");
            bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConversionException("The PDF could not be read: " + ex.Message, ex);
        }

        if (bytes.Length < 5 || bytes[0] != (byte)'%' || bytes[1] != (byte)'P' || bytes[2] != (byte)'D' || bytes[3] != (byte)'F')
            throw new ConversionException(NotPdfMessage);
        return bytes;
    }

    /// <summary>
    /// Opens for importing pages (split, merge). Throws <see cref="ConversionException"/> for encrypted or damaged files; a PDF
    /// that opens with an empty user password but carries an owner password (print/copy restrictions) is refused too, because
    /// the output would silently drop those restrictions.
    /// </summary>
    public static PdfDocument OpenImport(byte[] bytes)
    {
        RequireNotEncrypted(bytes);
        var doc = Open(bytes, PdfDocumentOpenMode.Import);
        if (IsEncrypted(doc))
        {
            doc.Dispose();
            throw new ConversionException(EncryptedMessage);
        }
        return doc;
    }

    /// <summary>Throws the password-protected <see cref="ConversionException"/> for any encrypted PDF (user or owner password).</summary>
    public static void RequireNotEncrypted(byte[] bytes)
    {
        if (IsEncryptedFile(bytes)) throw new ConversionException(EncryptedMessage);
    }

    /// <summary>Opens for modification (compress). Encrypted documents are refused even with an empty user password.</summary>
    public static PdfDocument OpenModify(byte[] bytes)
    {
        RequireNotEncrypted(bytes);
        var doc = Open(bytes, PdfDocumentOpenMode.Modify);
        if (IsEncrypted(doc))
        {
            doc.Dispose();
            throw new ConversionException(EncryptedMessage);
        }
        return doc;
    }

    /// <summary>
    /// True if the file needs a password to open or is encrypted at all. An owner password with an empty user password counts:
    /// PDFsharp opens those silently, so the trailer's <c>/Encrypt</c> is checked through PdfPig. Never throws: damaged files give false.
    /// </summary>
    public static bool IsEncryptedFile(byte[] bytes)
    {
        try
        {
            using var probe = UglyToad.PdfPig.PdfDocument.Open(bytes, new UglyToad.PdfPig.ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
            if (probe.IsEncrypted) return true;
        }
        catch (UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException)
        {
            return true;
        }
        catch (Exception)
        {
            // not readable by PdfPig: fall through to the PDFsharp check
        }

        try
        {
            using var doc = PdfReader.Open(new MemoryStream(bytes, writable: false), PdfDocumentOpenMode.Import);
            return IsEncrypted(doc);
        }
        catch (PdfReaderException ex) when (IsPasswordError(ex))
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsEncrypted(PdfDocument doc) => doc.SecuritySettings.IsEncrypted;

    private static PdfDocument Open(byte[] bytes, PdfDocumentOpenMode mode)
    {
        try
        {
            var doc = PdfReader.Open(new MemoryStream(bytes, writable: false), mode);
            if (doc.PageCount == 0)
            {
                doc.Dispose();
                throw new ConversionException("This PDF has no pages.");
            }
            return doc;
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (PdfReaderException ex) when (IsPasswordError(ex))
        {
            throw new ConversionException(EncryptedMessage, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ConversionException(DamagedMessage, ex);
        }
    }

    private static bool IsPasswordError(Exception ex) =>
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);
}
