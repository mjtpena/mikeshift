using System.Globalization;
using MikeShift.Core;
using MikeShift.Core.Feedback;
using MikeShift.Core.Shell;

namespace MikeShift.Core.Tests;

/// <summary>The conversion settings record: defaults equal today's behaviour, every value is validated, parsing is strict and stored values are read leniently.</summary>
public class ConversionOptionsTests
{
    [Fact]
    public void The_defaults_are_exactly_what_MikeShift_did_before_the_settings_existed()
    {
        var d = ConversionOptions.Default;
        Assert.Equal(90, d.Quality);
        Assert.Equal(0.9, d.QualityFraction);
        Assert.Equal(CompressStrength.Medium, d.Compress);
        Assert.Equal(3000, d.CompressMaxLongSide);
        Assert.Equal(0.6, d.CompressQuality);
        Assert.Equal(50, d.ResizePercent);
        Assert.Equal(15, d.GifSeconds);
        Assert.Null(d.OutputFolder);
        Assert.Equal(d, new ConversionOptions());
    }

    [Theory]
    [InlineData(CompressStrength.Light, 4000, 0.75)]
    [InlineData(CompressStrength.Medium, 3000, 0.6)]
    [InlineData(CompressStrength.Strong, 2000, 0.45)]
    public void Each_compress_strength_has_its_size_and_quality(CompressStrength strength, int longSide, double quality)
    {
        var o = new ConversionOptions { Compress = strength };
        Assert.Equal(longSide, o.CompressMaxLongSide);
        Assert.Equal(quality, o.CompressQuality);
    }

    [Fact]
    public void Stronger_compress_means_a_smaller_picture_and_lower_quality()
    {
        Assert.True(ConversionOptions.MaxLongSideFor(CompressStrength.Light) > ConversionOptions.MaxLongSideFor(CompressStrength.Medium));
        Assert.True(ConversionOptions.MaxLongSideFor(CompressStrength.Medium) > ConversionOptions.MaxLongSideFor(CompressStrength.Strong));
        Assert.True(ConversionOptions.QualityFor(CompressStrength.Light) > ConversionOptions.QualityFor(CompressStrength.Medium));
        Assert.True(ConversionOptions.QualityFor(CompressStrength.Medium) > ConversionOptions.QualityFor(CompressStrength.Strong));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(73)]
    [InlineData(100)]
    public void Quality_inside_the_range_is_accepted(int quality) =>
        Assert.Equal(quality, new ConversionOptions { Quality = quality }.Quality);

    [Theory]
    [InlineData(49)]
    [InlineData(101)]
    [InlineData(0)]
    [InlineData(-90)]
    [InlineData(int.MaxValue)]
    public void Quality_outside_the_range_is_refused(int quality) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionOptions { Quality = quality });

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(100)]
    public void Only_the_offered_resize_percentages_are_accepted(int percent) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionOptions { ResizePercent = percent });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(60)]
    public void Only_the_offered_gif_lengths_are_accepted(int seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionOptions { GifSeconds = seconds });

    [Fact]
    public void An_unknown_compress_strength_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionOptions { Compress = (CompressStrength)7 });

    [Fact]
    public void A_copy_made_with_with_is_validated_too()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConversionOptions.Default with { Quality = 10 });
        var changed = ConversionOptions.Default with { Quality = 70 };
        Assert.Equal(70, changed.Quality);
        Assert.Equal(90, ConversionOptions.Default.Quality); // immutable: the original did not change
    }

    [Fact]
    public void The_output_folder_must_be_a_full_path_and_is_tidied()
    {
        Assert.Throws<ArgumentException>(() => new ConversionOptions { OutputFolder = @"Pictures\Converted" });
        Assert.Throws<ArgumentException>(() => new ConversionOptions { OutputFolder = "D:" });
        Assert.Null(new ConversionOptions { OutputFolder = "   " }.OutputFolder);
        Assert.Null(new ConversionOptions { OutputFolder = null }.OutputFolder);
        Assert.Equal(@"C:\Pics\Out", new ConversionOptions { OutputFolder = @"  C:\Pics\Out\  " }.OutputFolder);
        Assert.Equal(@"C:\", new ConversionOptions { OutputFolder = @"C:\" }.OutputFolder);
        Assert.Equal(@"C:\Pics", new ConversionOptions { OutputFolder = @"C:\Pics\Other\..\" }.OutputFolder);
    }

    [Fact]
    public void Resize_titles_name_the_percentage()
    {
        Assert.Equal("Resize 25%", ConversionOptions.ResizeTitle(25));
        Assert.Equal("Resize 50%", ConversionOptions.ResizeTitle(ConversionOptions.DefaultResizePercent));
        Assert.Equal([25, 50, 75], ConversionOptions.ResizeChoices);
        Assert.Equal([5, 10, 15, 30], ConversionOptions.GifSecondsChoices);
    }

    // ------------------------------------------------------------ parsing (CLI flags, stored values)

    [Theory]
    [InlineData("50", true, 50)]
    [InlineData(" 85 ", true, 85)]
    [InlineData("100", true, 100)]
    [InlineData("49", false, 0)]
    [InlineData("101", false, 0)]
    [InlineData("9.5", false, 0)]
    [InlineData("-60", false, 0)]
    [InlineData("high", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void Quality_text_is_read_strictly(string? text, bool ok, int expected)
    {
        Assert.Equal(ok, ConversionOptions.TryParseQuality(text, out var value));
        if (ok) Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("light", CompressStrength.Light)]
    [InlineData("MEDIUM", CompressStrength.Medium)]
    [InlineData(" Strong ", CompressStrength.Strong)]
    public void Compress_names_are_read_in_any_case(string text, CompressStrength expected)
    {
        Assert.True(ConversionOptions.TryParseCompress(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("heavy")]
    [InlineData("")]
    [InlineData("light,strong")]
    [InlineData(null)]
    public void Compress_numbers_and_unknown_words_are_refused(string? text) =>
        Assert.False(ConversionOptions.TryParseCompress(text, out _));

    [Theory]
    [InlineData("25", 25)]
    [InlineData("50%", 50)]
    [InlineData(" 75 % ", 75)]
    public void Resize_text_accepts_a_percent_sign(string text, int expected)
    {
        Assert.True(ConversionOptions.TryParseResizePercent(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("30")]
    [InlineData("%")]
    [InlineData("0")]
    [InlineData("half")]
    [InlineData("5.0")]
    [InlineData(null)]
    public void Resize_text_outside_the_choices_is_refused(string? text) =>
        Assert.False(ConversionOptions.TryParseResizePercent(text, out _));

    [Theory]
    [InlineData("5", true)]
    [InlineData("10", true)]
    [InlineData("15", true)]
    [InlineData("30", true)]
    [InlineData("20", false)]
    [InlineData("0", false)]
    [InlineData("-5", false)]
    [InlineData("15s", false)]
    public void Gif_seconds_are_one_of_the_four_choices(string text, bool ok) =>
        Assert.Equal(ok, ConversionOptions.TryParseGifSeconds(text, out _));

    [Fact]
    public void Number_parsing_ignores_the_current_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA"); // Arabic-Indic digits and a different calendar
            Assert.True(ConversionOptions.TryParseQuality("85", out var q));
            Assert.Equal(85, q);
            Assert.False(ConversionOptions.TryParseQuality("\u0668\u0665", out _)); // Arabic-Indic 85 is not a number here
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Stored_values_that_are_missing_or_damaged_give_the_defaults_one_by_one()
    {
        Assert.Equal(ConversionOptions.Default, ConversionOptions.FromStored(null, null, null, null, null));
        Assert.Equal(ConversionOptions.Default, ConversionOptions.FromStored("", "", "", "", ""));

        var mixed = ConversionOptions.FromStored("77", "ultra", "40", "30", @"not a full path");
        Assert.Equal(77, mixed.Quality);
        Assert.Equal(CompressStrength.Medium, mixed.Compress);
        Assert.Equal(50, mixed.ResizePercent);
        Assert.Equal(30, mixed.GifSeconds);
        Assert.Null(mixed.OutputFolder);
    }

    [Fact]
    public void An_output_folder_is_checked_when_a_person_types_it()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("a.txt", "x");
        Assert.Equal(tmp.Path, ConversionOptions.ValidateOutputFolder(tmp.Path + @"\", out var ok));
        Assert.Null(ok);

        Assert.Null(ConversionOptions.ValidateOutputFolder(tmp.Combine("missing"), out var missing));
        Assert.Contains("does not exist", missing);
        Assert.Null(ConversionOptions.ValidateOutputFolder(file, out var isFile));
        Assert.Contains("is a file", isFile);
        Assert.Null(ConversionOptions.ValidateOutputFolder("   ", out var empty));
        Assert.Contains("empty", empty);
        Assert.Null(ConversionOptions.ValidateOutputFolder(@"relative\folder", out var relative));
        Assert.NotNull(relative);
    }

    // ------------------------------------------------------------ the file the Explorer menu reads

    [Fact]
    public void The_options_cache_round_trips_what_the_menu_needs_and_leaves_the_folder_out()
    {
        var options = new ConversionOptions
        {
            Quality = 60, Compress = CompressStrength.Strong, ResizePercent = 25, GifSeconds = 5, OutputFolder = @"C:\Secret\Folder",
        };
        var text = OptionsCache.Serialize(options);
        Assert.DoesNotContain("Secret", text);
        Assert.Equal(options with { OutputFolder = null }, OptionsCache.Parse(text));
        Assert.Equal(ConversionOptions.Default, OptionsCache.Parse(OptionsCache.Serialize(ConversionOptions.Default)));
    }

    [Fact]
    public void The_options_cache_tolerates_missing_unknown_and_broken_lines()
    {
        Assert.Equal(ConversionOptions.Default, OptionsCache.Parse(null));
        Assert.Equal(ConversionOptions.Default, OptionsCache.Parse("garbage\n=\nResizePercent=banana\nFuture=1"));
        Assert.Equal(75, OptionsCache.Parse("ResizePercent=75\r\nSomethingNew=yes\r\n").ResizePercent);
        Assert.Equal(50, OptionsCache.Parse("ResizePercent=33").ResizePercent);
    }

    // ------------------------------------------------------------ the Settings wording

    [Fact]
    public void The_settings_wording_follows_the_numbers_the_converters_use()
    {
        Assert.Equal("90 (default)", ConversionCopy.QualityValue(90));
        Assert.Equal("75", ConversionCopy.QualityValue(75));
        Assert.Contains(4000.ToString("N0", CultureInfo.CurrentCulture), ConversionCopy.CompressDescription(CompressStrength.Light));
        Assert.Contains(3000.ToString("N0", CultureInfo.CurrentCulture), ConversionCopy.CompressDescription(CompressStrength.Medium));
        Assert.Contains(2000.ToString("N0", CultureInfo.CurrentCulture), ConversionCopy.CompressDescription(CompressStrength.Strong));
        Assert.Contains("\"Resize 25%\"", ConversionCopy.ResizeDescription(25));
        Assert.Equal("Next to the original", ConversionCopy.OutputSummary(null));
        Assert.Equal(@"D:\Out", ConversionCopy.OutputSummary(@"D:\Out"));
        Assert.Contains("next to the originals", ConversionCopy.RevealDescription(null));
        Assert.Contains("folder you chose", ConversionCopy.RevealDescription(@"D:\Out"));
    }

    [Fact]
    public void A_folder_that_cannot_be_used_is_explained_and_a_good_one_is_not()
    {
        using var tmp = new TempDir();
        Assert.Null(ConversionCopy.FolderProblem(tmp.Path));
        var leftovers = Directory.GetFileSystemEntries(tmp.Path).Where(p => !p.EndsWith("__downloads", StringComparison.Ordinal)).ToArray();
        Assert.True(leftovers.Length == 0, "the write test left " + string.Join(", ", leftovers));

        Assert.Contains("isn't available", ConversionCopy.FolderProblem(tmp.Combine("gone")));
        Assert.NotNull(ConversionCopy.FolderProblem(""));

        var locked = tmp.CreateDir("locked");
        using (TempDir.DenyWrites(locked))
            Assert.Contains("Downloads", ConversionCopy.FolderProblem(locked));
    }
}
