namespace MikeShift.Core.Feedback;

/// <summary>The words of the Blur faces tool, in one place so tests can check them.</summary>
public static class FaceBlurCopy
{
    /// <summary>No face was found, so no file is written: the tool never quietly saves an unchanged copy.</summary>
    public const string NoFaces = "No faces found in this picture.";

    /// <summary>The tile was used on a PC where Windows face detection stopped being available after MikeShift started.</summary>
    public const string NoDetector = "Couldn't look for faces because this PC doesn't support face detection.";

    /// <summary>The detector itself went wrong, not "there were no faces".</summary>
    public const string Unreadable = "Couldn't look for faces in this picture.";

    /// <summary>The line under the toast: "Blurred 1 face", "Blurred 3 faces".</summary>
    public static string Blurred(int faces) => faces == 1 ? "Blurred 1 face" : $"Blurred {faces} faces";
}
