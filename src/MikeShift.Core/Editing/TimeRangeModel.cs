using System.Globalization;
using MikeShift.Core.Actions;

namespace MikeShift.Core.Editing;

/// <summary>
/// The state and rules of the trim dialog, free of any UI (PLAN §3.5): a media duration, a start and an end inside it
/// at least <see cref="MinLength"/> apart, and parsing/formatting of <c>m:ss.f</c> times.
/// </summary>
public sealed class TimeRangeModel
{
    /// <summary>The shortest allowed selection.</summary>
    public static readonly TimeSpan MinLength = TimeSpan.FromSeconds(0.1);

    private TimeSpan _start;
    private TimeSpan _end;

    public TimeRangeModel(TimeSpan duration)
    {
        Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        _end = Duration;
    }

    /// <summary>Raised after any state change.</summary>
    public event Action? Changed;

    public TimeSpan Duration { get; }

    public TimeSpan Start => _start;

    public TimeSpan End => _end;

    /// <summary>The selected length, <c>End - Start</c>.</summary>
    public TimeSpan Length => _end - _start;

    /// <summary>True when the media is long enough to hold a minimum-length selection.</summary>
    public bool IsValid => Duration >= MinLength && _start >= TimeSpan.Zero && _end <= Duration && Length >= MinLength;

    /// <summary>The whole file again.</summary>
    public void Reset()
    {
        _start = TimeSpan.Zero;
        _end = Duration;
        Changed?.Invoke();
    }

    /// <summary>Sets the start, clamped to <c>[0, End - 0.1 s]</c>. Returns the stored value.</summary>
    public TimeSpan SetStart(TimeSpan value)
    {
        var max = _end - MinLength;
        if (max < TimeSpan.Zero) max = TimeSpan.Zero;
        var v = Clamp(value, TimeSpan.Zero, max);
        if (v != _start)
        {
            _start = v;
            Changed?.Invoke();
        }

        return _start;
    }

    /// <summary>Sets the end, clamped to <c>[Start + 0.1 s, Duration]</c>. Returns the stored value.</summary>
    public TimeSpan SetEnd(TimeSpan value)
    {
        var min = _start + MinLength;
        if (min > Duration) min = Duration;
        var v = Clamp(value, min, Duration);
        if (v != _end)
        {
            _end = v;
            Changed?.Invoke();
        }

        return _end;
    }

    /// <summary>Sets start from text such as <c>1:05.5</c>; false (nothing changed) if the text cannot be parsed.</summary>
    public bool TrySetStartText(string? text)
    {
        if (!TryParse(text, out var t)) return false;
        SetStart(t);
        return true;
    }

    /// <summary>Sets end from text such as <c>1:05.5</c>; false (nothing changed) if the text cannot be parsed.</summary>
    public bool TrySetEndText(string? text)
    {
        if (!TryParse(text, out var t)) return false;
        SetEnd(t);
        return true;
    }

    /// <summary>The decision to return from the dialog, or null when not valid.</summary>
    public TimeRange? ToRange() => IsValid ? new TimeRange(_start, _end) : null;

    private static TimeSpan Clamp(TimeSpan v, TimeSpan lo, TimeSpan hi) => v < lo ? lo : v > hi ? hi : v;

    // ---- Text ----

    /// <summary>
    /// Formats as <c>m:ss.f</c> (tenths, rounded), or <c>h:mm:ss.f</c> from one hour on. Negative values show as zero.
    /// </summary>
    public static string Format(TimeSpan value)
    {
        var tenths = (long)Math.Round(Math.Max(0, value.TotalSeconds) * 10, MidpointRounding.AwayFromZero);
        var frac = tenths % 10;
        var totalSeconds = tenths / 10;
        var s = totalSeconds % 60;
        var totalMinutes = totalSeconds / 60;
        var m = totalMinutes % 60;
        var h = totalMinutes / 60;
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}.{frac}")
            : string.Create(CultureInfo.InvariantCulture, $"{m}:{s:00}.{frac}");
    }

    /// <summary>
    /// Parses <c>m:ss.f</c>, <c>ss.f</c> (any number of seconds, also plain <c>90</c>) and <c>h:mm:ss.f</c>. The fraction
    /// may have any number of digits. Minutes and seconds must be below 60 when a larger unit is given.
    /// </summary>
    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split(':');
        if (parts.Length > 3) return false;

        // Hours and minutes are whole numbers; only the last part (seconds) may carry a fraction.
        long hours = 0, minutes = 0;
        string secText = parts[^1];
        if (parts.Length == 3)
        {
            if (!TryWhole(parts[0], out hours) || !TryWhole(parts[1], out minutes)) return false;
            if (minutes >= 60) return false;
        }
        else if (parts.Length == 2)
        {
            if (!TryWhole(parts[0], out minutes)) return false;
        }

        if (!TrySeconds(secText, out var seconds)) return false;
        if (parts.Length > 1 && seconds >= 60) return false;

        // Overflow guard: absurdly large input is simply invalid.
        if (hours > 100_000 || minutes > 100_000_000 || seconds > 1_000_000_000) return false;
        value = TimeSpan.FromSeconds(hours * 3600d + minutes * 60d + seconds);
        return true;
    }

    private static bool TryWhole(string s, out long value)
    {
        value = 0;
        if (s.Length == 0 || s.Length > 9) return false;
        foreach (var c in s) if (c is < '0' or > '9') return false;
        value = long.Parse(s, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TrySeconds(string s, out double value)
    {
        value = 0;
        s = s.Replace(',', '.');
        var dot = s.IndexOf('.', StringComparison.Ordinal);
        var whole = dot < 0 ? s : s[..dot];
        var frac = dot < 0 ? string.Empty : s[(dot + 1)..];
        if (dot >= 0 && whole.Length == 0 && frac.Length == 0) return false;
        if (whole.Length == 0 && dot < 0) return false;
        long w = 0;
        if (whole.Length > 0 && !TryWhole(whole, out w)) return false;
        double f = 0;
        if (frac.Length > 0)
        {
            foreach (var c in frac) if (c is < '0' or > '9') return false;
            f = double.Parse("0." + frac, CultureInfo.InvariantCulture);
        }

        value = w + f;
        return true;
    }
}
