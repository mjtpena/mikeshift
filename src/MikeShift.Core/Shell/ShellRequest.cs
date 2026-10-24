using System.Text;

namespace MikeShift.Core.Shell;

/// <summary>What the Explorer entry asks the tray app to do.</summary>
public enum ShellVerb : byte
{
    /// <summary>Run the action named <see cref="ShellRequest.ActionTitle"/> on <see cref="ShellRequest.Paths"/>, exactly as a wheel drop would.</summary>
    Run = 1,

    /// <summary>Open the wheel in click mode for <see cref="ShellRequest.Paths"/> ("More options…").</summary>
    Wheel = 2,

    /// <summary>Only make sure the tray app is running (no paths). Answered with <see cref="ShellReply.Ok"/>.</summary>
    Ping = 3,
}

/// <summary>The tray app's one-byte answer, sent once the request was accepted (not when the job is done).</summary>
public enum ShellReply : byte
{
    /// <summary>Accepted.</summary>
    Ok = 1,

    /// <summary>Refused (malformed or too large).</summary>
    Rejected = 2,
}

/// <summary>
/// One message from the Explorer extension process to the tray app over the local named pipe. A small length-prefixed binary format, so
/// neither side needs a serializer (the extension process starts cold on the first right-click; the tray app validates every field).
/// </summary>
public sealed record ShellRequest(ShellVerb Verb, string ActionTitle, IReadOnlyList<string> Paths)
{
    private const uint Magic = 0x3148534D; // "MSH1"
    private const int MaxPaths = 100_000;
    private const int MaxStringChars = 33_000;

    /// <summary>Writes the message.</summary>
    public void WriteTo(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write((byte)Verb);
        writer.Write(ActionTitle ?? "");
        writer.Write(Paths.Count);
        foreach (var p in Paths) writer.Write(p);
        writer.Flush();
    }

    /// <summary>Reads one message; throws <see cref="InvalidDataException"/> for anything malformed or out of bounds.</summary>
    public static ShellRequest ReadFrom(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Not a MikeShift shell request.");
            var verb = (ShellVerb)reader.ReadByte();
            if (!Enum.IsDefined(verb)) throw new InvalidDataException("Unknown verb.");
            var title = ReadString(reader);
            var count = reader.ReadInt32();
            if (count is < 0 or > MaxPaths) throw new InvalidDataException("Bad item count.");
            var paths = new List<string>(Math.Min(count, 4096));
            for (var i = 0; i < count; i++) paths.Add(ReadString(reader));
            return new ShellRequest(verb, title, paths);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Truncated request.", ex);
        }
    }

    private static string ReadString(BinaryReader reader)
    {
        var s = reader.ReadString();
        if (s.Length > MaxStringChars) throw new InvalidDataException("String too long.");
        return s;
    }
}
