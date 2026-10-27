using MikeShift.Core.Images;

namespace MikeShift.Core.Tests.Infrastructure;

/// <summary>A picture of a QR code for <see cref="QrCodes"/>' tests and the corrupt-input corpus.</summary>
public static partial class Fixtures
{
    /// <summary>The PNG MikeShift's own QR code action writes for <paramref name="text"/>.</summary>
    public static byte[] QrCodePicture(string text) => QrCodes.EncodePngAsync(text, default).GetAwaiter().GetResult();
}
