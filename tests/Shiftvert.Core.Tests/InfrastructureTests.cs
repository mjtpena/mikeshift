using Shiftvert.Core;
using Xunit.Sdk;

namespace Shiftvert.Core.Tests;

/// <summary>Tests of the shared test helpers themselves, plus a worked example of the capability-skip attribute.</summary>
public class InfrastructureTests
{
    [Fact]
    public void TempDir_is_unique_deleted_on_dispose_and_redirects_downloads()
    {
        string path, downloads;
        var before = Naming.DownloadsOverride;
        using (var tmp = new TempDir())
        {
            path = tmp.Path;
            downloads = tmp.Downloads;
            tmp.WriteFile("sub/x.txt", "x");
            Assert.True(File.Exists(tmp.Combine("sub", "x.txt")));
            Assert.Equal(downloads, Naming.DownloadsDirectory());
        }
        Assert.False(Directory.Exists(path));
        Assert.Equal(before, Naming.DownloadsOverride);
    }

    [Fact]
    public void OriginalsGuard_passes_when_nothing_changed()
    {
        using var tmp = new TempDir();
        var f = tmp.WriteFile("a.bin", [1, 2, 3]);
        var d = tmp.CreateDir("d");
        tmp.WriteFile("d/x.txt", "x");
        using var guard = OriginalsGuard.Watch(f, d);
        File.WriteAllText(tmp.Combine("unrelated.txt"), "new file elsewhere is fine");
        guard.AssertUnchanged();
    }

    [Fact]
    public void OriginalsGuard_detects_modification_deletion_and_additions_inside_a_watched_folder()
    {
        using var tmp = new TempDir();
        var f = tmp.WriteFile("a.bin", [1, 2, 3]);
        var d = tmp.CreateDir("d");
        tmp.WriteFile("d/x.txt", "x");

        var g1 = OriginalsGuard.Watch(f);
        File.WriteAllBytes(f, [1, 2, 4]);
        Assert.Throws<XunitException>(g1.AssertUnchanged);

        var g2 = OriginalsGuard.Watch(f);
        File.Delete(f);
        Assert.Throws<XunitException>(g2.AssertUnchanged);

        var g3 = OriginalsGuard.Watch(d);
        File.WriteAllText(tmp.Combine("d", "extra.txt"), "x");
        Assert.Throws<XunitException>(g3.AssertUnchanged);
    }

    [Fact]
    public void Capability_skip_reason_follows_the_probe_and_rejects_unknown_names()
    {
        var caps = Capabilities.Current;
        Assert.Equal(caps.WordCom, CapabilityCheck.SkipReason([nameof(Capabilities.WordCom)]) is null);
        Assert.Contains("HeifEncode", CapabilityCheck.SkipReason([nameof(Capabilities.HeifEncode), nameof(Capabilities.WordCom)]) ?? "HeifEncode");
        Assert.Null(CapabilityCheck.SkipReason([]));
        Assert.Throws<ArgumentException>(() => CapabilityCheck.SkipReason(["NoSuchCapability"]));
    }

    /// <summary>Example for the converter packages: skipped (not deleted) on machines without the HEIF encoder.</summary>
    [SkipIfNoCapability(nameof(Capabilities.HeifEncode))]
    public void Example_test_that_needs_a_heif_encoder()
    {
        Assert.True(Capabilities.Current.HeifEncode);
    }

    [Fact]
    public void Shared_fixtures_look_like_what_they_claim_to_be()
    {
        var png = Fixtures.Png(5, 3);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
        Assert.Equal("%PDF"u8.ToArray(), Fixtures.PdfStub()[..4]);
        var ts = Fixtures.MpegTs(4);
        Assert.Equal(4 * 188, ts.Length);
        Assert.All(new[] { 0, 188, 376, 564 }, i => Assert.Equal(0x47, ts[i]));
    }

    [Fact]
    public void WriteAll_creates_the_shared_set()
    {
        using var tmp = new TempDir();
        Fixtures.WriteAll(tmp.Combine("out"));
        Assert.True(File.Exists(tmp.Combine("out", "sample.png")));
        Assert.True(File.Exists(tmp.Combine("out", "minimal.pdf")));
        Assert.True(File.Exists(tmp.Combine("out", "folder", "sub", "b.txt")));
    }
}
