using MikeShift.Core;
using MikeShift.Core.Shell;

namespace MikeShift.Core.Tests.Shell;

public class ShellRequestTests
{
    private static ShellRequest RoundTrip(ShellRequest r)
    {
        using var ms = new MemoryStream();
        r.WriteTo(ms);
        ms.Position = 0;
        return ShellRequest.ReadFrom(ms);
    }

    [Fact]
    public void A_request_survives_the_pipe_including_unicode_and_long_paths()
    {
        var paths = new[] { @"C:\Users\Zoë\Pictures\日本語 photo.heic", @"D:\" + new string('a', 300) + ".jpg" };
        var back = RoundTrip(new ShellRequest(ShellVerb.Run, "JPG", paths));
        Assert.Equal(ShellVerb.Run, back.Verb);
        Assert.Equal("JPG", back.ActionTitle);
        Assert.Equal(paths, back.Paths);
    }

    [Fact]
    public void Wheel_and_ping_requests_round_trip()
    {
        Assert.Equal(ShellVerb.Wheel, RoundTrip(new ShellRequest(ShellVerb.Wheel, "", [@"C:\a.png"])).Verb);
        var ping = RoundTrip(new ShellRequest(ShellVerb.Ping, "", []));
        Assert.Equal(ShellVerb.Ping, ping.Verb);
        Assert.Empty(ping.Paths);
    }

    [Fact]
    public void Garbage_and_truncated_input_is_rejected_cleanly()
    {
        Assert.Throws<InvalidDataException>(() => ShellRequest.ReadFrom(new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8])));
        Assert.Throws<InvalidDataException>(() => ShellRequest.ReadFrom(new MemoryStream([])));

        using var ms = new MemoryStream();
        new ShellRequest(ShellVerb.Run, "PNG", [@"C:\a.jpg", @"C:\b.jpg"]).WriteTo(ms);
        var bytes = ms.ToArray();
        Assert.Throws<InvalidDataException>(() => ShellRequest.ReadFrom(new MemoryStream(bytes[..^3])));
    }

    [Fact]
    public void An_absurd_item_count_is_rejected_before_anything_is_allocated()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(0x3148534Du);
            w.Write((byte)ShellVerb.Run);
            w.Write("PNG");
            w.Write(int.MaxValue);
        }
        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => ShellRequest.ReadFrom(ms));
    }

    [Fact]
    public void Capabilities_cache_round_trips_every_flag()
    {
        Assert.Equal(Capabilities.All, CapabilityCache.Parse(CapabilityCache.Serialize(Capabilities.All)));
        Assert.Equal(Capabilities.None, CapabilityCache.Parse(CapabilityCache.Serialize(Capabilities.None)));
        var some = Capabilities.None with { HeifEncode = true, Mp3Encode = true, WordInstalled = true };
        Assert.Equal(some, CapabilityCache.Parse(CapabilityCache.Serialize(some)));
    }

    [Fact]
    public void Capabilities_cache_tolerates_missing_unknown_and_broken_lines()
    {
        Assert.Equal(Capabilities.None, CapabilityCache.Parse(null));
        Assert.Equal(Capabilities.None, CapabilityCache.Parse("garbage\n=\nHeifEncode=maybe\nFuture=True"));
        Assert.True(CapabilityCache.Parse("FlacEncode=True\r\nSomethingNew=True\r\n").FlacEncode);
    }
}
