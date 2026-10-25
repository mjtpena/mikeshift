using System.Runtime.InteropServices;
using MikeShift.Core.Interaction;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace MikeShift.App.Wheel;

/// <summary>
/// Grabs the pixels under the wheel's rectangle from the screen. A plain GDI <c>BitBlt</c> from the screen DC:
/// without <c>CAPTUREBLT</c> it leaves out layered windows, so neither the wheel itself nor Explorer's drag image end up in the snapshot.
/// </summary>
internal static unsafe class ScreenCapture
{
    /// <summary>Top-down BGRA pixels (alpha forced to 255) of <paramref name="rect"/>; null if the capture failed.</summary>
    public static byte[]? Grab(ScreenRect rect, bool includeLayeredWindows = false)
    {
        var width = rect.Width;
        var height = rect.Height;
        if (width <= 0 || height <= 0) return null;

        var screen = PInvoke.GetDC(HWND.Null);
        if (screen.IsNull) return null;
        HDC mem = default;
        HBITMAP dib = default;
        HGDIOBJ old = default;
        try
        {
            mem = PInvoke.CreateCompatibleDC(screen);
            var bi = new BITMAPINFO();
            bi.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            bi.bmiHeader.biWidth = width;
            bi.bmiHeader.biHeight = -height;
            bi.bmiHeader.biPlanes = 1;
            bi.bmiHeader.biBitCount = 32;
            void* bits;
            dib = PInvoke.CreateDIBSection(screen, &bi, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
            if (dib.IsNull) return null;
            old = PInvoke.SelectObject(mem, dib);

            var rop = ROP_CODE.SRCCOPY | (includeLayeredWindows ? ROP_CODE.CAPTUREBLT : 0);
            if (!PInvoke.BitBlt(mem, 0, 0, width, height, screen, rect.Left, rect.Top, rop)) return null;
            PInvoke.GdiFlush();

            var pixels = new byte[width * height * 4];
            new ReadOnlySpan<byte>(bits, pixels.Length).CopyTo(pixels);
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255; // BitBlt leaves alpha undefined (0)
            return pixels;
        }
        catch (Exception ex)
        {
            WheelLog.Error("screen capture failed", ex);
            return null;
        }
        finally
        {
            if (!mem.IsNull && !old.IsNull) PInvoke.SelectObject(mem, old);
            if (!dib.IsNull) PInvoke.DeleteObject(dib);
            if (!mem.IsNull) PInvoke.DeleteDC(mem);
            PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }
}
