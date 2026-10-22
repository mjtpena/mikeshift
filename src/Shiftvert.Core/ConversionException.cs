namespace Shiftvert.Core;

/// <summary>
/// Thrown by converters for an expected, user-facing failure ("The PDF is encrypted.",
/// "Already small enough."). The <see cref="Exception.Message"/> is shown to the user verbatim,
/// so it must be a complete, plain-language sentence that does not include stack traces or HRESULT jargon.
/// <c>Each</c> catches this (and any other exception) per file and records it as a failure.
/// </summary>
public sealed class ConversionException : Exception
{
    public ConversionException(string message) : base(message) { }

    public ConversionException(string message, Exception inner) : base(message, inner) { }
}
