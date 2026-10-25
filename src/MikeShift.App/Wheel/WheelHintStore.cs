using MikeShift.App.Settings;
using MikeShift.Core.Interaction;

namespace MikeShift.App.Wheel;

/// <summary>
/// Remembers how many wheels have been shown, so the key hints are drawn as loud keycaps for the first
/// <see cref="WheelHints.ProminentWheels"/> wheels and quietly afterwards. One integer in <c>wheel-hints.txt</c>
/// in <see cref="AppData.Folder"/> (the dev switch <c>MIKESHIFT_HINTS_FILE</c> overrides it); deliberately not part of the preferences class. Past the limit nothing is read or
/// written again. Every failure (read-only profile, damaged file) just means "assume the hints are still needed" or "forget the count".
/// </summary>
internal sealed class WheelHintStore
{
    private readonly string _path;
    private int? _count;

    public WheelHintStore(string? path = null)
    {
        _path = path ?? DevSwitches.HintsFile ?? AppData.FilePath("wheel-hints.txt");
    }

    /// <summary>Wheels shown so far (capped at <see cref="WheelHints.ProminentWheels"/>).</summary>
    public int Count => _count ??= Load();

    /// <summary>A wheel is about to be shown: returns how loud its hints are and counts it (a tiny write, only during the first wheels).</summary>
    public HintEmphasis BeginWheel()
    {
        var before = Count;
        var emphasis = WheelHints.EmphasisFor(before);
        if (WheelHints.ShouldCount(before))
        {
            _count = before + 1;
            Save();
        }
        return emphasis;
    }

    private int Load()
    {
        try
        {
            return File.Exists(_path) ? WheelHints.Parse(File.ReadAllText(_path)) : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, WheelHints.Serialize(_count ?? 0));
        }
        catch (Exception ex)
        {
            WheelLog.Error("could not store the hint counter", ex);
        }
    }
}
