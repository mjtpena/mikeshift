using System.Buffers.Binary;
using MikeShift.Core.Media;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MikeShift.Core.Tests.Media;

/// <summary>Helpers shared by the Media tests: a cached MP4 fixture, duration probing and a GIF structure parser.</summary>
internal static class MediaTestSupport
{
    private static readonly Lazy<string> s_mp4 = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "mikeshift-tests", "media-fixture-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(dir, true); } catch (Exception) { } };
        return Fixtures.WriteMp4(Path.Combine(dir, "master.mp4"));
    });

    private static readonly Lazy<string> s_mp4NoAudio = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "mikeshift-tests", "media-fixture-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(dir, true); } catch (Exception) { } };
        return Fixtures.WriteMp4(Path.Combine(dir, "master-silent.mp4"), withAudio: false);
    });

    /// <summary>Copies the shared 2 s H.264/AAC MP4 into <paramref name="tmp"/> and returns the copy.</summary>
    public static string CopyMp4(TempDir tmp, string name = "clip.mp4")
    {
        var dest = tmp.Combine(name);
        File.Copy(s_mp4.Value, dest);
        return dest;
    }

    public static string CopySilentMp4(TempDir tmp, string name = "silent.mp4")
    {
        var dest = tmp.Combine(name);
        File.Copy(s_mp4NoAudio.Value, dest);
        return dest;
    }

    /// <summary>The names of the files directly inside <paramref name="dir"/> (including hidden temp files; the fake Downloads sub-folder is not a file).</summary>
    public static string[] FileNames(string dir) =>
        Directory.GetFiles(dir).Select(f => Path.GetFileName(f)).ToArray();

    public static MediaInfo Info(string path) =>
        MediaInfo.TryRead(path) ?? throw new Xunit.Sdk.XunitException("Media Foundation can't open the output: " + path);

    /// <summary>The decoded PCM payload of any audio file, via the product's own sources (WAV/AIFF) or Media Foundation.</summary>
    public static byte[] DecodePcm(string path, out int rate, out int channels, out int bits)
    {
        var info = Info(path);
        using var src = MediaCommon.OpenPcm(path, info);
        rate = src.SampleRate;
        channels = src.Channels;
        bits = src.BitsPerSample;
        using var ms = new MemoryStream();
        var buf = new byte[1 << 16];
        int n;
        while ((n = src.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
        return ms.ToArray();
    }

    public sealed record GifInfo(int Width, int Height, IReadOnlyList<int> Delays, bool HasNetscapeLoop, int LoopCount);

    /// <summary>Walks the GIF block structure (no pixel decoding).</summary>
    public static GifInfo ParseGif(byte[] gif)
    {
        Assert.True(gif.AsSpan(0, 6).SequenceEqual("GIF89a"u8) || gif.AsSpan(0, 6).SequenceEqual("GIF87a"u8), "not a GIF");
        var w = BinaryPrimitives.ReadUInt16LittleEndian(gif.AsSpan(6));
        var h = BinaryPrimitives.ReadUInt16LittleEndian(gif.AsSpan(8));
        var flags = gif[10];
        var pos = 13;
        if ((flags & 0x80) != 0) pos += 3 * (1 << ((flags & 7) + 1));

        var delays = new List<int>();
        var loop = false;
        var loops = -1;
        while (pos < gif.Length)
        {
            switch (gif[pos])
            {
                case 0x3B:
                    return new GifInfo(w, h, delays, loop, loops);
                case 0x21:
                {
                    var label = gif[pos + 1];
                    pos += 2;
                    if (label == 0xF9)
                    {
                        delays.Add(BinaryPrimitives.ReadUInt16LittleEndian(gif.AsSpan(pos + 2)));
                    }
                    else if (label == 0xFF && gif.AsSpan(pos + 1, 11).SequenceEqual("NETSCAPE2.0"u8))
                    {
                        loop = true;
                        loops = BinaryPrimitives.ReadUInt16LittleEndian(gif.AsSpan(pos + 14));
                    }
                    pos = SkipSubBlocks(gif, pos);
                    break;
                }
                case 0x2C:
                {
                    var packed = gif[pos + 9];
                    pos += 10;
                    if ((packed & 0x80) != 0) pos += 3 * (1 << ((packed & 7) + 1));
                    pos++; // LZW minimum code size
                    pos = SkipSubBlocks(gif, pos);
                    break;
                }
                default:
                    throw new Xunit.Sdk.XunitException($"Unexpected GIF block 0x{gif[pos]:X2} at {pos}");
            }
        }
        throw new Xunit.Sdk.XunitException("GIF has no trailer");
    }

    private static int SkipSubBlocks(byte[] gif, int pos)
    {
        // pos points at the first sub-block length byte (or the block size byte for extensions).
        while (gif[pos] != 0) pos += gif[pos] + 1;
        return pos + 1;
    }

    /// <summary>Decodes a GIF with Windows' own decoder: frame count, size, and the colour of the pixel at (x, y) of frame 0.</summary>
    public static async Task<(int Frames, int Width, int Height, (byte R, byte G, byte B) Pixel)> DecodeGifWithWindows(string path, int x, int y)
    {
        using var stream = new InMemoryRandomAccessStream();
        var bytes = await File.ReadAllBytesAsync(path);
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var data = pixels.DetachPixelData();
        var o = (y * (int)decoder.PixelWidth + x) * 4;
        return ((int)decoder.FrameCount, (int)decoder.PixelWidth, (int)decoder.PixelHeight, (data[o + 2], data[o + 1], data[o]));
    }
}
