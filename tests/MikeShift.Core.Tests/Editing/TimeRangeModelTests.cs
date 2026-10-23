using MikeShift.Core.Actions;
using MikeShift.Core.Editing;

namespace MikeShift.Core.Tests.Editing;

public class TimeRangeModelTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Theory]
    [InlineData("0:00.0", 0)]
    [InlineData("0:05.5", 5.5)]
    [InlineData("1:05.5", 65.5)]
    [InlineData("1:05", 65)]
    [InlineData("12.5", 12.5)]
    [InlineData("90", 90)]
    [InlineData("90.25", 90.25)]
    [InlineData(".5", 0.5)]
    [InlineData("5.", 5)]
    [InlineData("1:2:03.4", 3723.4)]
    [InlineData("1:02:03.4", 3723.4)]
    [InlineData("0:01:00", 60)]
    [InlineData("  2:30.0  ", 150)]
    [InlineData("1:05,5", 65.5)]
    [InlineData("0:01.25", 1.25)]
    [InlineData("10:00.0", 600)]
    [InlineData("75:30", 4530)]
    public void Parse_accepts_the_forms(string text, double seconds)
    {
        Assert.True(TimeRangeModel.TryParse(text, out var v));
        Assert.Equal(seconds, v.TotalSeconds, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1:2:3:4")]
    [InlineData("1:75")]
    [InlineData("1:60.0")]
    [InlineData("1:60:00")]
    [InlineData("-5")]
    [InlineData("-0:05.0")]
    [InlineData("1.2.3")]
    [InlineData(":")]
    [InlineData(".")]
    [InlineData("1:")]
    [InlineData(":30")]
    [InlineData("1:a")]
    [InlineData("1e3")]
    [InlineData("99999999999999999999")]
    public void Parse_rejects_garbage(string? text)
    {
        Assert.False(TimeRangeModel.TryParse(text, out _));
    }

    [Theory]
    [InlineData(0, "0:00.0")]
    [InlineData(5.5, "0:05.5")]
    [InlineData(65.5, "1:05.5")]
    [InlineData(59.96, "1:00.0")]
    [InlineData(599.95, "10:00.0")]
    [InlineData(3600, "1:00:00.0")]
    [InlineData(3723.4, "1:02:03.4")]
    [InlineData(-3, "0:00.0")]
    [InlineData(12.34, "0:12.3")]
    [InlineData(12.36, "0:12.4")]
    public void Format_is_m_ss_f(double seconds, string expected)
    {
        Assert.Equal(expected, TimeRangeModel.Format(S(seconds)));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(7.3)]
    [InlineData(65.9)]
    [InlineData(3599.9)]
    [InlineData(7322.5)]
    public void Format_then_parse_round_trips_tenths(double seconds)
    {
        Assert.True(TimeRangeModel.TryParse(TimeRangeModel.Format(S(seconds)), out var v));
        Assert.Equal(seconds, v.TotalSeconds, 6);
    }

    [Fact]
    public void Starts_as_the_whole_file()
    {
        var m = new TimeRangeModel(S(30));
        Assert.Equal(TimeSpan.Zero, m.Start);
        Assert.Equal(S(30), m.End);
        Assert.Equal(S(30), m.Length);
        Assert.True(m.IsValid);
        Assert.Equal(new TimeRange(TimeSpan.Zero, S(30)), m.ToRange());
    }

    [Fact]
    public void Start_is_clamped_to_zero_and_below_end()
    {
        var m = new TimeRangeModel(S(30));
        Assert.Equal(TimeSpan.Zero, m.SetStart(S(-4)));
        m.SetEnd(S(10));
        Assert.Equal(S(9.9), m.SetStart(S(25)));
        Assert.Equal(S(10), m.End);
    }

    [Fact]
    public void End_is_clamped_to_duration_and_above_start()
    {
        var m = new TimeRangeModel(S(30));
        Assert.Equal(S(30), m.SetEnd(S(99)));
        m.SetStart(S(12));
        Assert.Equal(S(12.1), m.SetEnd(S(3)));
        Assert.Equal(S(12), m.Start);
    }

    [Fact]
    public void Minimum_length_is_a_tenth_of_a_second()
    {
        var m = new TimeRangeModel(S(30));
        m.SetStart(S(30));
        Assert.Equal(S(29.9), m.Start);
        Assert.Equal(TimeRangeModel.MinLength, m.Length);
        Assert.True(m.IsValid);
    }

    [Fact]
    public void Text_entry_updates_and_rejects()
    {
        var m = new TimeRangeModel(S(120));
        Assert.True(m.TrySetStartText("0:10.5"));
        Assert.True(m.TrySetEndText("1:30"));
        Assert.Equal(S(10.5), m.Start);
        Assert.Equal(S(90), m.End);
        Assert.False(m.TrySetStartText("nope"));
        Assert.False(m.TrySetEndText(""));
        Assert.Equal(S(10.5), m.Start);
        Assert.Equal(S(90), m.End);
        Assert.True(m.TrySetEndText("5:00")); // beyond the file: clamped
        Assert.Equal(S(120), m.End);
    }

    [Fact]
    public void Too_short_media_is_invalid()
    {
        var m = new TimeRangeModel(S(0.05));
        Assert.False(m.IsValid);
        Assert.Null(m.ToRange());
        var zero = new TimeRangeModel(TimeSpan.Zero);
        Assert.False(zero.IsValid);
    }

    [Fact]
    public void Negative_duration_becomes_zero()
    {
        var m = new TimeRangeModel(S(-5));
        Assert.Equal(TimeSpan.Zero, m.Duration);
        Assert.False(m.IsValid);
    }

    [Fact]
    public void Reset_restores_the_whole_file_and_raises_Changed()
    {
        var m = new TimeRangeModel(S(30));
        m.SetStart(S(5));
        m.SetEnd(S(6));
        var raised = 0;
        m.Changed += () => raised++;
        m.Reset();
        Assert.Equal(1, raised);
        Assert.Equal(TimeSpan.Zero, m.Start);
        Assert.Equal(S(30), m.End);
    }

    [Fact]
    public void Changed_only_fires_on_a_real_change()
    {
        var m = new TimeRangeModel(S(30));
        var raised = 0;
        m.Changed += () => raised++;
        m.SetStart(TimeSpan.Zero);
        m.SetEnd(S(30));
        Assert.Equal(0, raised);
        m.SetStart(S(1));
        Assert.Equal(1, raised);
    }
}
