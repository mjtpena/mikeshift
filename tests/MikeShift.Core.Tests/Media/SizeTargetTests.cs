using MikeShift.Core.Actions;
using MikeShift.Core.Actions.Catalog;
using MikeShift.Core.Feedback;
using MikeShift.Core.Interaction;
using MikeShift.Core.Media;

namespace MikeShift.Core.Tests.Media;

/// <summary>The arithmetic and the words of the video Fit tools, without encoding anything.</summary>
public class SizeTargetTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromSeconds(60);

    [Fact]
    public void A_megabyte_is_a_million_bytes_so_the_file_fits_however_a_site_counts()
    {
        Assert.Equal(10_000_000, SizeTarget.Bytes(10));
        Assert.Equal(25_000_000, SizeTarget.Bytes(25));
        Assert.Equal(2_000_000_000, SizeTarget.Bytes(2000));
    }

    [Fact]
    public void A_minute_into_ten_megabytes_leaves_five_percent_for_the_container_and_gives_the_rest_to_picture_and_sound()
    {
        var b = SizeTarget.Split(SizeTarget.Bytes(10), Minute, hasAudio: true)!.Value;

        // 10 MB x 8 x 0.95 / 60 s = 1,266,667 bps; the picture gets over 1 Mbps, so the sound gets 128 kbps.
        Assert.Equal(128_000u, b.Audio);
        Assert.InRange(b.Video, 1_138_000u, 1_139_000u);
        Assert.InRange((b.Video + (double)b.Audio) * 60 / 8, 9_499_000, 9_500_000);
    }

    [Fact]
    public void Tight_budgets_get_96_kbps_sound_and_silent_videos_spend_everything_on_the_picture()
    {
        var tight = SizeTarget.Split(SizeTarget.Bytes(5), Minute, hasAudio: true)!.Value;
        Assert.Equal(96_000u, tight.Audio);

        var silent = SizeTarget.Split(SizeTarget.Bytes(10), Minute, hasAudio: false)!.Value;
        Assert.Equal(0u, silent.Audio);
        Assert.InRange(silent.Video, 1_266_000u, 1_267_000u);
    }

    [Fact]
    public void An_unknown_length_gives_no_budget()
    {
        Assert.Null(SizeTarget.Split(SizeTarget.Bytes(10), TimeSpan.Zero, hasAudio: true));
        Assert.Null(SizeTarget.Split(0, Minute, hasAudio: true));
    }

    [Fact]
    public void Short_videos_never_ask_for_more_than_the_encoder_limit()
    {
        var b = SizeTarget.Split(SizeTarget.Bytes(25), TimeSpan.FromSeconds(2), hasAudio: true)!.Value;
        Assert.Equal(SizeTarget.MaxVideo, b.Video);
    }

    [Theory]
    [InlineData(10, true, 256)]   // about 4 minutes
    [InlineData(25, true, 641)]   // about 10 minutes
    [InlineData(10, false, 380)]
    public void The_longest_watchable_video_follows_from_the_minimum_bitrate(int megabytes, bool audio, int seconds)
    {
        var longest = SizeTarget.LongestWatchable(SizeTarget.Bytes(megabytes), audio);
        Assert.InRange(longest.TotalSeconds, seconds, seconds + 1);
        // Exactly that long is still allowed; a little longer is not.
        Assert.True(SizeTarget.Split(SizeTarget.Bytes(megabytes), longest - TimeSpan.FromSeconds(1), audio)!.Value.Video >= SizeTarget.MinWatchableVideo);
        Assert.True(SizeTarget.Split(SizeTarget.Bytes(megabytes), longest + TimeSpan.FromSeconds(1), audio)!.Value.Video < SizeTarget.MinWatchableVideo);
    }

    [Theory]
    [InlineData(5_000_000u, 1080)]
    [InlineData(2_500_000u, 1080)]
    [InlineData(2_499_999u, 720)]
    [InlineData(1_200_000u, 720)]
    [InlineData(1_137_964u, 480)] // a minute in 10 MB
    [InlineData(600_000u, 480)]
    [InlineData(599_999u, 360)]
    [InlineData(200_000u, 360)]
    public void The_picture_size_follows_the_bitrate(uint bps, int shortSide) =>
        Assert.Equal(shortSide, SizeTarget.ShortSideFor(bps));

    [Fact]
    public void The_short_side_is_what_is_capped_so_portrait_videos_are_not_squeezed_twice()
    {
        Assert.Equal(720, SizeTarget.MaxHeightFor(1920, 1080, 2_000_000));
        Assert.Equal(1280, SizeTarget.MaxHeightFor(1080, 1920, 2_000_000)); // 720 wide, 1280 high
        Assert.Equal((720u, 1280u), MediaTranscode.FitHeight(1080, 1920, SizeTarget.MaxHeightFor(1080, 1920, 2_000_000)));
        // Never upscaled: a 640x360 source stays 640x360 at a 1080p bitrate.
        Assert.Equal((640u, 360u), MediaTranscode.FitHeight(640, 360, SizeTarget.MaxHeightFor(640, 360, 5_000_000)));
    }

    [Fact]
    public void The_retry_lowers_the_bitrate_in_proportion_to_the_overshoot_and_keeps_the_sound()
    {
        var first = new SizeTarget.Bitrates(1_000_000, 128_000);

        var second = first.Scaled(10_000_000 / 11_000_000.0);

        Assert.Equal(128_000u, second.Audio);
        // (1,128,000 x 10/11 x 0.95) - 128,000 = 846,182
        Assert.InRange(second.Video, 846_000u, 846_400u);
        Assert.Equal(SizeTarget.MinRetryVideo, new SizeTarget.Bitrates(150_000, 96_000).Scaled(0.1).Video);
        Assert.True(first.Scaled(2).Video < first.Video); // a ratio above 1 never raises it
    }

    [Fact]
    public void A_small_source_that_is_encoded_spends_no_more_than_it_already_does()
    {
        var budget = new SizeTarget.Bitrates(5_000_000, 128_000);
        Assert.Equal(new SizeTarget.Bitrates(672_000, 128_000), budget.AtMost(800_000));
        Assert.Equal(budget, budget.AtMost(9_000_000));
        Assert.Equal(budget, budget.AtMost(0)); // unknown: unchanged
        Assert.Equal(800_000, SizeTarget.AverageBitrate(6_000_000, Minute));
    }

    // ------------------------------------------------------------------ words

    [Fact]
    public void Titles_names_and_toasts_say_the_size()
    {
        Assert.Equal("Fit 10 MB", SizeTargetCopy.Title(10));
        Assert.Equal(" 25 MB", SizeTargetCopy.Suffix(25));
        Assert.Equal(10, SizeTargetCopy.MegabytesIn("Fit 10 MB"));
        Assert.Equal(8, SizeTargetCopy.MegabytesIn(" fit 8mb "));
        Assert.Null(SizeTargetCopy.MegabytesIn("Fit"));
        Assert.Null(SizeTargetCopy.MegabytesIn("Fit 10 GB"));
        Assert.Null(SizeTargetCopy.MegabytesIn("Compress"));

        var fit = ActionCatalog.Find(VideoActions.All, "Fit 10 MB")!;
        Assert.Equal("Shrinking to 10 MB…", FeedbackCopy.StartTitle(fit));
        Assert.Equal("Shrinking 2 of 3…", FeedbackCopy.ProgressTitle(fit, 2, 3));
        Assert.Equal("Fit 10 MB", fit.DropDescription);
        Assert.Equal("Save a copy that is at most 10 MB, small enough to attach", WheelText.Describe(fit));
    }

    [Theory]
    [InlineData(641, "10 minutes")]
    [InlineData(256, "4 minutes")]
    [InlineData(119, "1 minute")]
    [InlineData(59, "50 seconds")]
    [InlineData(25.7, "20 seconds")]
    [InlineData(9.9, "9 seconds")]
    [InlineData(1.2, "1 second")]
    public void Lengths_are_rounded_down_into_plain_words(double seconds, string words) =>
        Assert.Equal(words, SizeTargetCopy.Length(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Refusals_say_what_happened_and_what_to_do()
    {
        var tooLong = SizeTargetCopy.TooLong(10, SizeTarget.LongestWatchable(SizeTarget.Bytes(10), hasAudio: true));
        Assert.Equal("This video is too long to fit in 10 MB and still be watchable. At that size a video can run about 4 minutes. Trim it first, then try again.", tooLong);
        Assert.Contains("couldn't", SizeTargetCopy.CouldNotFit(25));
        Assert.Contains("25 MB", SizeTargetCopy.CouldNotFit(25));
        foreach (var message in new[] { tooLong, SizeTargetCopy.CouldNotFit(10), SizeTargetCopy.UnknownLength })
        {
            Assert.True(char.IsUpper(message[0]), message);
            Assert.EndsWith(".", message);
            Assert.DoesNotContain("failed", message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
