namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// The audio fixtures: a 3 s stereo chirp (frequency sweeping 220 Hz upwards with a slow amplitude wobble), so that every moment of
/// the signal is unique and a cut from the wrong place, a shifted signal or a damaged one cannot correlate with the source.
/// WAV and AIFF are written by hand (not by the product); MP3, M4A and FLAC are made by Windows' own encoders from the WAV.
/// </summary>
internal sealed record AudioSource(string Id, string FileName, int Bits, bool Lossless, string RequiresCapability)
{
    public const int Rate = 44100;
    public const double Seconds = 3.0;

    public string Ext => Path.GetExtension(FileName).TrimStart('.').ToLowerInvariant();

    public override string ToString() => Id;

    public static readonly string[] Ids = ["wav16", "wav24", "aiff", "mp3", "m4a", "flac"];

    public static AudioSource Get(string id) => id switch
    {
        "wav16" => new(id, "tone16.wav", 16, true, ""),
        "wav24" => new(id, "tone24.wav", 24, true, ""),
        "aiff" => new(id, "tone.aiff", 16, true, ""),
        "mp3" => new(id, "tone.mp3", 16, false, nameof(Capabilities.Mp3Encode)),
        "m4a" => new(id, "tone.m4a", 16, false, nameof(Capabilities.AacEncode)),
        "flac" => new(id, "tone.flac", 16, true, nameof(Capabilities.FlacEncode)),
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    /// <summary>True if this PC has what is needed to make (and convert) the fixture.</summary>
    public bool Available => RequiresCapability.Length == 0 || (bool)typeof(Capabilities).GetProperty(RequiresCapability)!.GetValue(Kit.Caps)!;

    /// <summary>The 3 s stereo chirp at the given depth.</summary>
    public static Pcm Chirp(int bits, double seconds = Seconds, int rate = Rate, int channels = 2)
    {
        var frames = (int)Math.Round(seconds * rate);
        var width = bits / 8;
        var data = new byte[frames * channels * width];
        var max = (1L << (bits - 1)) - 1;
        var o = 0;
        for (var i = 0; i < frames; i++)
        {
            var t = (double)i / rate;
            for (var c = 0; c < channels; c++)
            {
                var f0 = 220 + 40 * c;
                var phase = 2 * Math.PI * (f0 * t + 0.5 * 1200 * t * t);
                var v = 0.45 * (0.8 + 0.2 * Math.Sin(2 * Math.PI * 3 * t)) * Math.Sin(phase);
                var s = (long)Math.Round(v * max);
                for (var b = 0; b < width; b++) data[o++] = (byte)(s >> (8 * b));
            }
        }
        return new Pcm(rate, channels, bits, data);
    }

    private static readonly Dictionary<string, string> s_masters = new();
    private static readonly string s_dir = Path.Combine(Path.GetTempPath(), "mikeshift-tests", "audio-integrity-" + Environment.ProcessId);

    /// <summary>Path of the cached master file for this source (created on first use, deleted at process exit).</summary>
    public async Task<string> MasterAsync()
    {
        lock (s_masters)
            if (s_masters.TryGetValue(Id, out var cached)) return cached;

        Directory.CreateDirectory(s_dir);
        AppDomain.CurrentDomain.ProcessExit -= Cleanup;
        AppDomain.CurrentDomain.ProcessExit += Cleanup;
        var path = Path.Combine(s_dir, FileName);
        var wav16 = Chirp(16);
        switch (Id)
        {
            case "wav16": await File.WriteAllBytesAsync(path, AudioCheck.WavBytes(wav16)); break;
            case "wav24": await File.WriteAllBytesAsync(path, AudioCheck.WavBytes(Chirp(24))); break;
            case "aiff": await File.WriteAllBytesAsync(path, AudioCheck.AiffBytes(wav16)); break;
            default:
            {
                var wav = Path.Combine(s_dir, "master-" + Id + ".wav");
                await File.WriteAllBytesAsync(wav, AudioCheck.WavBytes(wav16));
                await AudioCheck.EncodeWithMfAsync(wav, path, Id);
                break;
            }
        }
        lock (s_masters) s_masters[Id] = path;
        return path;
    }

    private static void Cleanup(object? sender, EventArgs e)
    {
        try { Directory.Delete(s_dir, true); } catch (Exception) { /* temp */ }
    }

    /// <summary>Copies the master into <paramref name="tmp"/> and returns the copy.</summary>
    public async Task<string> CopyToAsync(TempDir tmp)
    {
        var master = await MasterAsync();
        var dest = tmp.Combine(FileName);
        File.Copy(master, dest);
        return dest;
    }

    /// <summary>
    /// The samples a correct conversion must reproduce. WAV/AIFF: parsed directly. MP3/M4A/FLAC: decoded by Media Foundation
    /// (and, for the fixture to be trusted, checked against the chirp it was made from).
    /// </summary>
    public async Task<Pcm> TruthAsync(string path)
    {
        switch (Ext)
        {
            case "wav": return AudioCheck.ReadWav(path);
            case "aiff": return AudioCheck.ReadAiff(path);
            default:
            {
                var pcm = await AudioCheck.DecodeWithMfAsync(path, Rate, 2, 16);
                var (corr, _) = AudioCheck.Correlate(Chirp(16), pcm, 200);
                Assert.True(corr > 0.95, $"{Id}: the fixture does not carry the chirp it was made from (correlation {corr:0.000})");
                return pcm;
            }
        }
    }
}
