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

    /// <summary>The output would pass the megapixel limit (200) or a side limit of 30,000 pixels.</summary>
    public static string TooLarge(long width, long height) =>
        $"This picture is too large to upscale: {width * height / 1_000_000} megapixels would become {width * 2 * height * 2 / 1_000_000}, and MikeShift handles up to 200.";
}
