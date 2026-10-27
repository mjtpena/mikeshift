namespace MikeShift.Core.Media;

/// <summary>
/// The arithmetic behind the video Fit tools (<see cref="VideoConverter.FitAsync"/>): how many bits per second a video of a given length
/// may spend to end up under a size, how they are shared between picture and sound, which picture size that bitrate can carry, and the
/// shortest bitrate that is still watchable. Pure, so the numbers are unit-tested without encoding anything.
/// </summary>
internal static class SizeTarget
{
    /// <summary>Share of the size given to the streams; the rest is left for the MP4 container (indexes, headers) and encoder overshoot.</summary>
    public const double StreamShare = 0.95;

    /// <summary>Below this video bitrate even 360p falls apart into blocks: the video is refused as too long instead.</summary>
    public const uint MinWatchableVideo = 200_000;

    /// <summary>AAC bitrate for tight budgets.</summary>
    public const uint LowAudio = 96_000;

    /// <summary>AAC bitrate once the picture has enough (<see cref="HighAudioFrom"/>).</summary>
    public const uint HighAudio = 128_000;

    /// <summary>The video bitrate from which the sound gets <see cref="HighAudio"/> instead of <see cref="LowAudio"/>.</summary>
    public const uint HighAudioFrom = 1_000_000;

    /// <summary>The lowest video bitrate the retry asks for; lower than this the encoder ignores the request.</summary>
    public const uint MinRetryVideo = 100_000;

    /// <summary>The highest video bitrate ever asked for (the encoders' own limit elsewhere in MikeShift).</summary>
    public const uint MaxVideo = 25_000_000;

    /// <summary>Video and audio bits per second of one encode.</summary>
    public readonly record struct Bitrates(uint Video, uint Audio)
    {
        /// <summary>The bitrates for a second attempt after the first came out at 1 / <paramref name="ratio"/> of the limit.</summary>
        public Bitrates Scaled(double ratio)
        {
            var total = (Video + (double)Audio) * Math.Min(ratio, 1) * StreamShare;
            return this with { Video = (uint)Math.Max(MinRetryVideo, total - Audio) };
        }

        /// <summary>
        /// These bitrates lowered so the whole stays within <paramref name="totalBps"/> (unchanged when that is unknown): the sound drops to
        /// <see cref="LowAudio"/> unless the picture keeps <see cref="HighAudioFrom"/>, and the picture gets the rest. The one exception is a
        /// source below <see cref="Floor"/>, which is asked for the floor: the encoders ignore anything lower.
        /// </summary>
        public Bitrates AtMost(double totalBps)
        {
            if (totalBps <= 0 || Video + (double)Audio <= totalBps) return this;
            var audio = Audio == 0 || totalBps - Audio >= HighAudioFrom ? Audio : Math.Min(Audio, LowAudio);
            return new Bitrates((uint)Math.Max(MinRetryVideo, totalBps - audio), audio);
        }

        /// <summary>The least an encode with (<paramref name="hasAudio"/>) or without sound is ever asked for.</summary>
        public static uint Floor(bool hasAudio) => MinRetryVideo + (hasAudio ? LowAudio : 0);
    }

    /// <summary>A size in bytes: 1 MB is 1,000,000 bytes, the smaller of the two ways sizes are counted, so the result fits either way.</summary>
    public static long Bytes(int megabytes) => megabytes * 1_000_000L;

    /// <summary>The source's own bits per second over its whole length; 0 when the length is unknown.</summary>
    public static double AverageBitrate(long bytes, TimeSpan duration) =>
        duration > TimeSpan.Zero ? bytes * 8.0 / duration.TotalSeconds : 0;

    /// <summary>
    /// How a video <paramref name="duration"/> long can spend <paramref name="limitBytes"/>: <see cref="StreamShare"/> of the bits, less the
    /// sound (96 or 128 kbps AAC), go to the picture. Null when the duration is unknown. The video share may be below
    /// <see cref="MinWatchableVideo"/>; the caller decides whether that is a refusal. <paramref name="share"/> replaces <see cref="StreamShare"/>.
    /// </summary>
    public static Bitrates? Split(long limitBytes, TimeSpan duration, bool hasAudio, double share = StreamShare)
    {
        if (duration <= TimeSpan.Zero || limitBytes <= 0) return null;
        var total = limitBytes * 8.0 * share / duration.TotalSeconds;
        var audio = !hasAudio ? 0u : total - HighAudio >= HighAudioFrom ? HighAudio : LowAudio;
        var video = Math.Clamp(total - audio, 0, MaxVideo);
        return new Bitrates((uint)video, audio);
    }

    /// <summary>The longest video that still gets <see cref="MinWatchableVideo"/> within <paramref name="limitBytes"/> (for the refusal's advice).</summary>
    public static TimeSpan LongestWatchable(long limitBytes, bool hasAudio) =>
        TimeSpan.FromSeconds(limitBytes * 8.0 * StreamShare / (MinWatchableVideo + (hasAudio ? LowAudio : 0)));

    /// <summary>The picture's short side for a video bitrate: 1080, 720, 480 or 360 lines.</summary>
    public static int ShortSideFor(uint videoBps) => videoBps switch
    {
        >= 2_500_000 => 1080,
        >= 1_200_000 => 720,
        >= 600_000 => 480,
        _ => 360,
    };

    /// <summary>
    /// The <c>maxHeight</c> to hand <see cref="MediaTranscode.Mp4"/> so the SHORT side ends at <see cref="ShortSideFor"/>: a portrait phone
    /// video at 720p is 720 wide, not 720 high. <see cref="MediaTranscode.FitHeight"/> never upscales.
    /// </summary>
    public static int MaxHeightFor(int width, int height, uint videoBps)
    {
        var shortSide = ShortSideFor(videoBps);
        if (width <= 0 || height <= 0 || width >= height) return shortSide;
        return (int)Math.Round(shortSide * (double)height / width);
    }
}
