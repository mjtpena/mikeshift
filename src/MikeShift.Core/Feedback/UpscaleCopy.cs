namespace MikeShift.Core.Feedback;

/// <summary>The words of the Upscale 2x tool, in one place so tests can check them.</summary>
public static class UpscaleCopy
{
    /// <summary>The tile was used on a PC where the Windows image scaler is off or unsupported (it was available when MikeShift started, or the flag was simulated).</summary>
    public const string NotAvailable = "Couldn't upscale this picture because this PC doesn't have the Windows image upscaler turned on.";

    /// <summary>Windows was asked to install the image model and did not (no connection, Windows Update paused, not enough room).</summary>
    public const string ModelNotInstalled = "Couldn't upscale this picture because Windows hasn't installed its image model yet. Check Windows Update, then try again.";

    /// <summary>The engine itself went wrong on this picture.</summary>
    public const string EngineFailed = "Couldn't upscale this picture.";

    /// <summary>The engine returned a picture that is not exactly twice the size, so nothing is written.</summary>
    public const string WrongSize = "Couldn't upscale this picture: the result was not twice the size.";

    /// <summary>The output would pass the megapixel limit (200) or a side limit of 30,000 pixels; says which, with the numbers that show why.</summary>
    public static string TooLarge(long width, long height)
    {
        if (Math.Max(width, height) * 2 > SideLimit)
            return FormattableString.Invariant($"This picture is too large to upscale: its longest side would become {Math.Max(width, height) * 2:N0} pixels, and MikeShift handles up to {SideLimit:N0}.");
        var before = width * height / 1_000_000.0;
        var after = width * 2 * height * 2 / 1_000_000.0;
        return FormattableString.Invariant($"This picture is too large to upscale: {before:0.0##} megapixels would become {after:0.0##}, and MikeShift handles up to 200.");
    }

    /// <summary>The longest side an output may have.</summary>
    public const long SideLimit = 30_000;
}
