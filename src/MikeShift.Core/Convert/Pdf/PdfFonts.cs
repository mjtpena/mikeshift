using System.Buffers.Binary;
using PdfSharp.Fonts;

namespace MikeShift.Core.Pdf;

/// <summary>
/// Shared PDFsharp font setup (used by PDF and document converters). PDFsharp's Core build has no default font
/// resolver; call <see cref="EnsureInitialized"/> before drawing any text so both packages agree on one global setting.
/// It installs a resolver for the installed Windows fonts we use: the Latin families plus the per-script fallbacks
/// (CJK, Hangul, Indic, Thai, Myanmar, symbols, emoji) that the document-to-PDF writer switches to.
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
            GlobalFontSettings.FontResolver = new WindowsFontResolver();
            _initialized = true;
        }
    }

    /// <summary>True if the Windows font family <paramref name="family"/> is one we know and its regular face file is installed.</summary>
    public static bool IsInstalled(string family) => WindowsFontResolver.IsInstalled(family);

    /// <summary>Resolves family names to files in the Windows fonts folder (TrueType collections are cut down to their first face).</summary>
    private sealed class WindowsFontResolver : IFontResolver
    {
        // Family -> files for regular, bold, italic, bold italic (null = no such file; the nearest face is used).
        private static readonly Dictionary<string, string?[]> Families = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Arial"] = ["arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"],
            ["Segoe UI"] = ["segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"],
            ["Tahoma"] = ["tahoma.ttf", "tahomabd.ttf", null, null],
            ["Verdana"] = ["verdana.ttf", "verdanab.ttf", "verdanai.ttf", "verdanaz.ttf"],
            ["Yu Gothic"] = ["YuGothR.ttc", "YuGothB.ttc", null, null],
            ["Microsoft YaHei"] = ["msyh.ttc", "msyhbd.ttc", null, null],
            ["SimSun"] = ["simsun.ttc", null, null, null],
            ["MS Gothic"] = ["msgothic.ttc", null, null, null],
            ["Malgun Gothic"] = ["malgun.ttf", "malgunbd.ttf", null, null],
            ["Nirmala UI"] = ["Nirmala.ttc", null, null, null],
            ["Leelawadee UI"] = ["LeelawUI.ttf", "LeelaUIb.ttf", null, null],
            ["Myanmar Text"] = ["mmrtext.ttf", "mmrtextb.ttf", null, null],
            ["Segoe UI Symbol"] = ["seguisym.ttf", null, null, null],
            ["Segoe UI Emoji"] = ["seguiemj.ttf", null, null, null],
        };

        private static readonly string FontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        private static readonly Dictionary<string, byte[]> Cache = [];

        public static bool IsInstalled(string family) =>
            Families.TryGetValue(family, out var files) && files[0] is { } f && File.Exists(Path.Combine(FontsDir, f));

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (!Families.TryGetValue(familyName, out var files)) return null;

            // Exact face, else the same weight upright (italic simulated), else regular (bold is not simulated).
            (int Index, bool SimItalic)[] order = isBold
                ? isItalic ? [(3, false), (1, true), (2, false), (0, true)] : [(1, false), (0, false)]
                : isItalic ? [(2, false), (0, true)] : [(0, false)];
            foreach (var (index, simItalic) in order)
            {
                if (files[index] is { } file && File.Exists(Path.Combine(FontsDir, file)))
                    return new FontResolverInfo(file, mustSimulateBold: false, mustSimulateItalic: simItalic);
            }
            return null;
        }

        public byte[]? GetFont(string faceName)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(faceName, out var cached)) return cached;
                try
                {
                    var data = File.ReadAllBytes(Path.Combine(FontsDir, faceName));
                    return Cache[faceName] = FirstFace(data);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return null;
                }
            }
        }

        /// <summary>For a TrueType collection ('ttcf') returns a stand-alone font made of its first face's tables; other data is returned as is.</summary>
        private static byte[] FirstFace(byte[] data)
        {
            if (data.Length < 16 || data[0] != (byte)'t' || data[1] != (byte)'t' || data[2] != (byte)'c' || data[3] != (byte)'f')
                return data;

            var dir = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12));
            int tableCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(dir + 4));
            var headerLength = 12 + 16 * tableCount;

            var total = headerLength;
            for (var i = 0; i < tableCount; i++)
                total += Align4((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(dir + 12 + 16 * i + 12)));

            var result = new byte[total];
            data.AsSpan(dir, 12).CopyTo(result);
            var next = headerLength;
            for (var i = 0; i < tableCount; i++)
            {
                var record = dir + 12 + 16 * i;
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 8));
                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 12));
                data.AsSpan(record, 8).CopyTo(result.AsSpan(12 + 16 * i)); // tag + checksum
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12 + 16 * i + 8), (uint)next);
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12 + 16 * i + 12), (uint)length);
                data.AsSpan(offset, length).CopyTo(result.AsSpan(next));
                next += Align4(length);
            }
            return result;
        }

        private static int Align4(int n) => (n + 3) & ~3;
    }
}
