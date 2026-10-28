namespace MikeShift.Core.Shell;

/// <summary>
/// The tray app's <see cref="Capabilities"/> as a small text file the Explorer extension process reads, because probing codecs takes a second
/// or more and the right-click menu must not wait for it. One <c>Name=True|False</c> line per flag; unknown lines are ignored and missing
/// ones are false, so an older or newer file never breaks the menu (it only offers a conservative subset).
/// </summary>
public static class CapabilityCache
{
    /// <summary>The file name inside the app's data folder.</summary>
    public const string FileName = "shell-capabilities.txt";

    /// <summary>Serializes the flags.</summary>
    public static string Serialize(Capabilities c) => string.Join('\n',
        $"HeifEncode={c.HeifEncode}", $"HeifDecode={c.HeifDecode}", $"WebpEncode={c.WebpEncode}", $"AvifDecode={c.AvifDecode}",
        $"Mp3Encode={c.Mp3Encode}", $"FlacEncode={c.FlacEncode}", $"H264Encode={c.H264Encode}", $"AacEncode={c.AacEncode}",
        $"HevcDecode={c.HevcDecode}", $"Vp9Decode={c.Vp9Decode}", $"Av1Decode={c.Av1Decode}", $"WebmOpen={c.WebmOpen}", $"Ocr={c.Ocr}", $"FaceDetect={c.FaceDetect}", $"AiUpscale={c.AiUpscale}",
        $"WordInstalled={c.WordInstalled}", $"WordCom={c.WordCom}") + "\n";

    /// <summary>Parses a file written by <see cref="Serialize"/>. Never throws.</summary>
    public static Capabilities Parse(string? text)
    {
        var caps = new Capabilities();
        if (string.IsNullOrWhiteSpace(text)) return caps;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0 || !bool.TryParse(line[(eq + 1)..], out var on)) continue;
            caps = line[..eq] switch
            {
                nameof(Capabilities.HeifEncode) => caps with { HeifEncode = on },
                nameof(Capabilities.HeifDecode) => caps with { HeifDecode = on },
                nameof(Capabilities.WebpEncode) => caps with { WebpEncode = on },
                nameof(Capabilities.AvifDecode) => caps with { AvifDecode = on },
                nameof(Capabilities.Mp3Encode) => caps with { Mp3Encode = on },
                nameof(Capabilities.FlacEncode) => caps with { FlacEncode = on },
                nameof(Capabilities.H264Encode) => caps with { H264Encode = on },
                nameof(Capabilities.AacEncode) => caps with { AacEncode = on },
                nameof(Capabilities.HevcDecode) => caps with { HevcDecode = on },
                nameof(Capabilities.Vp9Decode) => caps with { Vp9Decode = on },
                nameof(Capabilities.Av1Decode) => caps with { Av1Decode = on },
                nameof(Capabilities.WebmOpen) => caps with { WebmOpen = on },
                nameof(Capabilities.Ocr) => caps with { Ocr = on },
                nameof(Capabilities.FaceDetect) => caps with { FaceDetect = on },
                nameof(Capabilities.AiUpscale) => caps with { AiUpscale = on },
                nameof(Capabilities.WordInstalled) => caps with { WordInstalled = on },
                nameof(Capabilities.WordCom) => caps with { WordCom = on },
                _ => caps,
            };
        }
        return caps;
    }
}
