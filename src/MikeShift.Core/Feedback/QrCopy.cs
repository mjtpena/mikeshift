namespace MikeShift.Core.Feedback;

/// <summary>
/// The words of the two QR code actions (Read QR code on pictures, QR code on web links and short text files), in one place so tests can check them.
/// </summary>
public static class QrCopy
{
    /// <summary>What a code holds at most with error correction M (version 40, byte mode); shown in <see cref="TooLong"/>.</summary>
    public const int MaxBytes = 2331;

    /// <summary>A picture in which no QR code was found.</summary>
    public const string NoCodeInPicture = "Couldn't find a QR code in this picture.";

    /// <summary>A link file with no <c>URL=</c> line, or an empty one.</summary>
    public const string NoAddress = "This shortcut doesn't contain a web address.";

    /// <summary>An empty text file (or one with only blanks).</summary>
    public const string NothingToEncode = "This file is empty, so there is nothing to put in a QR code.";

    /// <summary>A text file that is not text MikeShift can read as Unicode.</summary>
    public const string NotText = "This file isn't plain text, so it can't be turned into a QR code.";

    /// <summary>Text (or a file) too long for the biggest QR code at the error correction MikeShift uses.</summary>
    public static string TooLong(long bytes) =>
        $"This is {bytes:N0} bytes, which is too much for a QR code. The most one can hold is about {MaxBytes:N0}.";
}
