using System.Buffers.Binary;
using System.IO.Compression;
using MikeShift.Core.Media;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace MikeShift.Core.Tests.Infrastructure;

// Audio/video fixtures, generated at test time (nothing binary is checked in).
public static partial class Fixtures
{
    /// <summary>A canonical PCM WAV: a sine wave (a different pitch per channel) of <paramref name="seconds"/> at the given depth.</summary>
    public static byte[] SineWav(double seconds = 1, int rate = 44100, int bits = 16, int channels = 2, double hz = 440)
    {
        var frames = (int)Math.Round(seconds * rate);
        var width = bits / 8;
        var data = new byte[frames * channels * width];
        var o = 0;
        for (var i = 0; i < frames; i++)
        {
            for (var c = 0; c < channels; c++)
            {
                var v = 0.5 * Math.Sin(2 * Math.PI * hz * (1 + c * 0.25) * i / rate);
                var s = (long)Math.Round(v * ((1L << (bits - 1)) - 1));
                for (var b = 0; b < width; b++) data[o++] = (byte)(s >> (8 * b));
            }
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + data.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate);
        w.Write(rate * channels * width); w.Write((short)(channels * width)); w.Write((short)bits);
        w.Write("data"u8); w.Write(data.Length); w.Write(data);
        return ms.ToArray();
    }

    /// <summary>The same sine as <see cref="SineWav"/> as an AIFF, produced by the product's own AIFF writer.</summary>
    public static byte[] SineAiff(double seconds = 1, int rate = 44100, int bits = 16, int channels = 2)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mikeshift-fixture-{Guid.NewGuid():N}.wav");
        try
        {
            File.WriteAllBytes(path, SineWav(seconds, rate, bits, channels));
            using var src = WavPcmSource.TryOpen(path) ?? throw new InvalidOperationException("fixture WAV unreadable");
            using var ms = new MemoryStream();
            PcmWriter.WriteAiff(src, ms, bits, -1, CancellationToken.None);
            return ms.ToArray();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Raw PCM payload (the bytes after the <c>data</c> chunk header) of a WAV file.</summary>
    public static byte[] WavPayload(byte[] wav)
    {
        var pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(pos + 4));
            if (wav.AsSpan(pos, 4).SequenceEqual("data"u8))
                return wav.AsSpan(pos + 8, Math.Min(size, wav.Length - pos - 8)).ToArray();
            pos += 8 + size + (size & 1);
        }
        throw new InvalidOperationException("no data chunk");
    }

    /// <summary>
    /// A small H.264/AAC MP4 (<paramref name="seconds"/> long, 640x360, 30 fps, 44.1 kHz stereo sine audio when
    /// <paramref name="withAudio"/>) rendered with <c>MediaComposition</c>. Throws if this machine has no H.264 encoder.
    /// </summary>
    public static string WriteMp4(string path, double seconds = 2, bool withAudio = true) =>
        Task.Run(() => WriteMp4Async(path, seconds, withAudio)).GetAwaiter().GetResult();

    private static async Task<string> WriteMp4Async(string path, double seconds, bool withAudio)
    {
        var work = Path.Combine(Path.GetTempPath(), "mikeshift-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            const int segments = 4;
            var composition = new MediaComposition();
            for (var i = 0; i < segments; i++)
            {
                var png = Path.Combine(work, $"frame{i}.png");
                File.WriteAllBytes(png, GradientPng(640, 360, i));
                var file = await StorageFile.GetFileFromPathAsync(png);
                var clip = await MediaClip.CreateFromImageFileAsync(file, TimeSpan.FromSeconds(seconds / segments));
                composition.Clips.Add(clip);
            }

            if (withAudio)
            {
                var wav = Path.Combine(work, "audio.wav");
                File.WriteAllBytes(wav, SineWav(seconds + 0.5));
                var audioFile = await StorageFile.GetFileFromPathAsync(wav);
                composition.BackgroundAudioTracks.Add(await BackgroundAudioTrack.CreateFromFileAsync(audioFile));
            }

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga);
            profile.Video.Width = 640;
            profile.Video.Height = 360;
            profile.Video.Bitrate = 4_000_000;
            if (!withAudio) profile.Audio = null;

            var rendered = Path.Combine(work, "out.mp4");
            File.WriteAllBytes(rendered, []);
            var dest = await StorageFile.GetFileFromPathAsync(rendered);
            var result = await composition.RenderToFileAsync(dest, MediaTrimmingPreference.Fast, profile);
            if (result != Windows.Media.Transcoding.TranscodeFailureReason.None)
                throw new InvalidOperationException("Could not render the MP4 fixture: " + result);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.Copy(rendered, path, overwrite: true);
            return path;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    /// <summary>A PNG with a colour gradient and a box whose position depends on <paramref name="frame"/> (so frames differ).</summary>
    private static byte[] GradientPng(int width, int height, int frame)
    {
        using var raw = new MemoryStream();
        var boxX = 40 + frame * 120;
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < width; x++)
            {
                var inBox = x >= boxX && x < boxX + 100 && y >= 120 && y < 220;
                raw.WriteByte((byte)(inBox ? 255 : x * 255 / width));
                raw.WriteByte((byte)(inBox ? 255 : y * 255 / height));
                raw.WriteByte((byte)(inBox ? 0 : 60 + frame * 50));
                raw.WriteByte(255);
            }
        }

        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
            z.Write(raw.GetBuffer(), 0, (int)raw.Length);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", idat.GetBuffer().AsSpan(0, (int)idat.Length));
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    static partial void WriteMediaFixtures(string dir)
    {
        File.WriteAllBytes(Path.Combine(dir, "sine.wav"), SineWav());
        File.WriteAllBytes(Path.Combine(dir, "sine24.wav"), SineWav(bits: 24));
        File.WriteAllBytes(Path.Combine(dir, "sine.aiff"), SineAiff());
        try
        {
            WriteMp4(Path.Combine(dir, "clip.mp4"));
        }
        catch (Exception ex)
        {
            // No H.264 encoder (or no composition support) on this machine: the audio fixtures above are still useful.
            File.WriteAllText(Path.Combine(dir, "clip.mp4.missing.txt"), "Could not generate clip.mp4: " + ex.Message);
        }
    }
}
