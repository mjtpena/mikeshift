using PdfSharp.Fonts;

namespace Shiftvert.Core.Pdf;

/// <summary>
/// Shared PDFsharp font setup (used by PDF and document converters). PDFsharp's Core build has no default font
/// resolver; call <see cref="EnsureInitialized"/> before drawing any text so both packages agree on one global setting.
/// </summary>
public static class PdfFonts
{
    private static readonly object Gate = new();
    private static bool _initialized;

    /// <summary>Lets PDFsharp load fonts from <c>%WINDIR%\Fonts</c>. Idempotent and thread-safe.</summary>
    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
                return;
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            _initialized = true;
        }
    }
}
