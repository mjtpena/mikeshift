using MikeShift.Core.Images;

namespace MikeShift.Core.Tests.Images;

/// <summary>The byte-level JPEG metadata helpers behind "keep EXIF" and "Strip EXIF".</summary>
public class JpegMetaTests
{
    [Fact]
    public void Segments_lists_markers_before_the_pixel_data()
    {
        var jpeg = Fixtures.JpegWithMetadata(32, 24, xmp: true, photoshop: true);
        var markers = JpegMeta.Segments(jpeg).Select(s => s.Marker).ToList();
        Assert.Equal(0xE1, markers[0]);
        Assert.Contains((byte)0xED, markers);
        Assert.Contains((byte)0xDB, markers); // quantisation table
        Assert.DoesNotContain((byte)0xDA, markers); // SOS is where segments stop
    }

    [Fact]
    public void Exif_orientation_and_gps_are_readable()
    {
        var jpeg = Fixtures.JpegWithMetadata(32, 24, orientation: 8, gps: true);
        Assert.Equal(8, JpegMeta.ReadOrientation(jpeg));
        Assert.True(JpegMeta.HasGps(JpegMeta.ExtractExif(jpeg)!));

        var plain = Fixtures.JpegWithMetadata(32, 24, orientation: 1, gps: false);
        Assert.False(JpegMeta.HasGps(JpegMeta.ExtractExif(plain)!));
        Assert.Equal(1, JpegMeta.ReadOrientation(Fixtures.QuadrantJpeg()));
        Assert.Null(JpegMeta.ExtractExif(Fixtures.QuadrantJpeg()));
    }

    [Fact]
    public void MakeUpright_resets_orientation_updates_dimensions_and_unlinks_the_thumbnail()
    {
        var jpeg = Fixtures.JpegWithMetadata(120, 80, orientation: 6);
        var exif = JpegMeta.ExtractExif(jpeg)!;

        var upright = JpegMeta.MakeUpright(exif, 80, 120)!;

        var patched = JpegMeta.InsertExif(Fixtures.QuadrantJpeg(80, 120), upright);
        Assert.Equal(1, JpegMeta.ReadOrientation(patched));
        Assert.Equal(upright.Length, exif.Length);
        Assert.Contains(Fixtures.ExifMake, System.Text.Encoding.ASCII.GetString(upright));
        Assert.True(JpegMeta.HasGps(upright));
        // Original block untouched (it is a copy).
        Assert.Equal(6, JpegMeta.ReadOrientation(jpeg));
    }

    [Fact]
    public void MakeUpright_rejects_a_malformed_block()
    {
        Assert.Null(JpegMeta.MakeUpright("Exif\0\0garbage"u8.ToArray(), 10, 10));
        Assert.Null(JpegMeta.MakeUpright([.. "Exif\0\0II*\0"u8, 0xFF, 0xFF, 0xFF, 0x7F], 10, 10));
    }

    [Fact]
    public void InsertExif_puts_the_block_first_and_keeps_the_picture_decodable()
    {
        using var tmp = new TempDir();
        var plain = Fixtures.QuadrantJpeg(64, 48);
        var exif = JpegMeta.ExtractExif(Fixtures.JpegWithMetadata(64, 48))!;

        var withExif = JpegMeta.InsertExif(plain, exif);

        Assert.NotNull(JpegMeta.ExtractExif(withExif));
        Assert.Equal(plain.Length + exif.Length + 4, withExif.Length);
        var path = tmp.WriteFile("x.jpg", withExif);
        Assert.True(ImageProbe.CanDecode(path));
        ImageTestUtil.AssertQuadrants(ImageTestUtil.Decode(path), tol: 30);
    }

    [Fact]
    public void Metadata_scan_and_removal_find_app1_and_app13()
    {
        using var tmp = new TempDir();
        var dirty = Fixtures.JpegWithMetadata(64, 48, xmp: true, photoshop: true);
        Assert.True(JpegMeta.HasMetadataSegments(dirty));

        var clean = JpegMeta.RemoveMetadataSegments(dirty);

        Assert.False(JpegMeta.HasMetadataSegments(clean));
        Assert.True(clean.Length < dirty.Length);
        var path = tmp.WriteFile("clean.jpg", clean);
        ImageTestUtil.AssertQuadrants(ImageTestUtil.Decode(path), tol: 30);
        Assert.False(JpegMeta.HasMetadataSegments(Fixtures.QuadrantJpeg()));
    }

    [Fact]
    public void Non_jpeg_input_is_handled_gracefully()
    {
        Assert.Empty(JpegMeta.Segments(Fixtures.PngRgb()));
        Assert.Null(JpegMeta.ExtractExif(Fixtures.PngRgb()));
        Assert.False(JpegMeta.HasMetadataSegments(Fixtures.PngRgb()));
        var png = Fixtures.PngRgb();
        Assert.Same(png, JpegMeta.InsertExif(png, new byte[10]));
    }
}
