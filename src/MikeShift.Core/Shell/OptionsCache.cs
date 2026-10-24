namespace MikeShift.Core.Shell;

/// <summary>
/// The user's <see cref="ConversionOptions"/> as a small text file next to the capability cache, so the Explorer extension process can word its
/// menu lines ("Resize 25%") without reading the app's settings store. The tray app writes it whenever a conversion setting changes and runs the
/// job with the real options; the extension only ever needs the labels. One <c>Name=value</c> line per setting; unknown or unreadable lines are
/// ignored and missing ones keep their default, so an older or newer file never breaks the menu. The output folder is deliberately not written:
/// the extension has no use for a path.
/// </summary>
public static class OptionsCache
{
    /// <summary>The file name inside the app's data folder.</summary>
    public const string FileName = "shell-options.txt";

    /// <summary>Serializes the settings the menu wording depends on.</summary>
    public static string Serialize(ConversionOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return string.Join('\n',
            $"{nameof(ConversionOptions.Quality)}={o.Quality}",
            $"{nameof(ConversionOptions.Compress)}={o.Compress}",
            $"{nameof(ConversionOptions.ResizePercent)}={o.ResizePercent}",
            $"{nameof(ConversionOptions.GifSeconds)}={o.GifSeconds}") + "\n";
    }

    /// <summary>Parses a file written by <see cref="Serialize"/>. Never throws; anything unreadable is the default.</summary>
    public static ConversionOptions Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ConversionOptions.Default;
        string? quality = null, compress = null, resize = null, gif = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var value = line[(eq + 1)..];
            switch (line[..eq])
            {
                case nameof(ConversionOptions.Quality): quality = value; break;
                case nameof(ConversionOptions.Compress): compress = value; break;
                case nameof(ConversionOptions.ResizePercent): resize = value; break;
                case nameof(ConversionOptions.GifSeconds): gif = value; break;
            }
        }
        return ConversionOptions.FromStored(quality, compress, resize, gif, null);
    }
}
