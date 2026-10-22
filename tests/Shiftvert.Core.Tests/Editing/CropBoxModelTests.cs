using Shiftvert.Core.Actions;
using Shiftvert.Core.Editing;

namespace Shiftvert.Core.Tests.Editing;

public class CropBoxModelTests
{
    private static CropBoxModel M(int w = 1000, int h = 800, double scale = 0.5) => new(w, h, scale);

    private static void AssertInside(CropBoxModel m)
    {
        var r = m.Rect;
        Assert.True(r.X >= 0 && r.Y >= 0, $"origin {r}");
        Assert.True(r.X + r.W <= m.ImageWidth && r.Y + r.H <= m.ImageHeight, $"extent {r}");
        Assert.True(r.W >= Math.Min(16, m.ImageWidth) && r.H >= Math.Min(16, m.ImageHeight), $"min {r}");
    }

    private static void AssertRatio(CropBoxModel m, double ratio)
    {
        // Integer rounding allows one pixel of slop.
        Assert.InRange(m.Rect.W, m.Rect.H * ratio - 1.01, m.Rect.H * ratio + 1.01);
    }

    private static void Drag(CropBoxModel m, CropHandle handle, double dx, double dy)
    {
        m.BeginDrag(handle);
        m.DragByImagePx(dx, dy);
        m.EndDrag();
    }

    // ---- Initial state, reset ----

    [Fact]
    public void Starts_with_the_whole_image_free_at_100_percent()
    {
        var m = M();
        Assert.Equal(new RectPx(0, 0, 1000, 800), m.Rect);
        Assert.Equal(CropAspect.Free, m.Aspect);
        Assert.Null(m.AspectRatio);
        Assert.Equal(100, m.ScalePercent);
        Assert.Equal((1000, 800), (m.OutputWidth, m.OutputHeight));
        Assert.True(m.IsValid);
        Assert.Equal(new CropChoice(new RectPx(0, 0, 1000, 800), 1000, 800), m.ToChoice());
    }

    [Fact]
    public void Reset_restores_everything()
    {
        var m = M();
        m.SetAspect(CropAspect.Square);
        m.SetScalePercent(40);
        m.Nudge(5, 5);
        m.Reset();
        Assert.Equal(new RectPx(0, 0, 1000, 800), m.Rect);
        Assert.Equal(CropAspect.Free, m.Aspect);
        Assert.Equal(100, m.ScalePercent);
        Assert.Equal((1000, 800), (m.OutputWidth, m.OutputHeight));
    }

    [Fact]
    public void Constructor_rejects_empty_images()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CropBoxModel(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CropBoxModel(10, 0));
    }

    [Fact]
    public void PreviewRect_scales_the_rect()
    {
        var m = M(scale: 0.25);
        Drag(m, CropHandle.TopLeft, 100, 40);
        var (x, y, w, h) = m.PreviewRect;
        Assert.Equal((25, 10, 225, 190), (x, y, w, h));
    }

    // ---- Presets ----

    [Theory]
    [InlineData(CropAspect.Square, 1.0)]
    [InlineData(CropAspect.FourFive, 0.8)]
    [InlineData(CropAspect.ThreeTwo, 1.5)]
    [InlineData(CropAspect.SixteenNine, 16 / 9.0)]
    [InlineData(CropAspect.NineSixteen, 9 / 16.0)]
    [InlineData(CropAspect.Original, 1000 / 800.0)]
    public void Preset_fits_the_largest_centred_box(CropAspect aspect, double ratio)
    {
        var m = M();
        m.SetAspect(aspect);
        AssertInside(m);
        AssertRatio(m, ratio);
        Assert.True(m.Rect.W == 1000 || m.Rect.H == 800, $"{m.Rect}"); // touches the image on one axis
        Assert.InRange(m.Rect.X + m.Rect.W / 2.0, 499.5, 500.5);
        Assert.InRange(m.Rect.Y + m.Rect.H / 2.0, 399.5, 400.5);
        Assert.Equal(ratio, m.AspectRatio!.Value, 6);
    }

    [Fact]
    public void Square_on_a_landscape_image_is_the_full_height()
    {
        var m = M();
        m.SetAspect(CropAspect.Square);
        Assert.Equal(new RectPx(100, 0, 800, 800), m.Rect);
    }

    [Fact]
    public void Wide_preset_on_a_portrait_image_is_the_full_width()
    {
        var m = M(600, 900);
        m.SetAspect(CropAspect.SixteenNine);
        Assert.Equal(600, m.Rect.W);
        Assert.Equal(338, m.Rect.H);
        AssertInside(m);
    }

    [Fact]
    public void Preset_fits_inside_the_current_box_centred_on_it()
    {
        var m = M();
        Drag(m, CropHandle.TopLeft, 200, 100);
        Assert.Equal(new RectPx(200, 100, 800, 700), m.Rect);
        m.SetAspect(CropAspect.Square);
        Assert.Equal(new RectPx(250, 100, 700, 700), m.Rect);
    }

    [Fact]
    public void Free_unlocks_and_keeps_the_box()
    {
        var m = M();
        m.SetAspect(CropAspect.Square);
        var before = m.Rect;
        m.SetAspect(CropAspect.Free);
        Assert.Equal(before, m.Rect);
        Assert.Null(m.AspectRatio);
    }

    [Fact]
    public void Preset_that_cannot_hold_a_minimum_box_stays_free()
    {
        var m = new CropBoxModel(20, 3000);
        m.SetAspect(CropAspect.SixteenNine); // 20 x 11 would be thinner than 16 px
        Assert.Equal(CropAspect.Free, m.Aspect);
        Assert.Equal(new RectPx(0, 0, 20, 3000), m.Rect);
    }

    [Fact]
    public void Preset_on_a_too_thin_box_falls_back_to_the_whole_image()
    {
        var m = M();
        Drag(m, CropHandle.TopLeft, 100, 100);
        Drag(m, CropHandle.BottomRight, -884, 0); // 16 wide, 700 tall
        Assert.Equal(16, m.Rect.W);
        m.SetAspect(CropAspect.SixteenNine); // 16 x 9 is below the minimum
        Assert.Equal(CropAspect.SixteenNine, m.Aspect);
        AssertInside(m);
        AssertRatio(m, 16 / 9.0);
        Assert.True(m.Rect.W > 16);
    }

    [Fact]
    public void Preset_updates_output_size()
    {
        var m = M();
        m.SetScalePercent(50);
        m.SetAspect(CropAspect.Square);
        Assert.Equal((400, 400), (m.OutputWidth, m.OutputHeight));
    }

    // ---- Moving ----

    [Fact]
    public void Move_clamps_to_the_image()
    {
        var m = M();
        m.SetAspect(CropAspect.Square); // (100,0,800,800)
        m.BeginDrag(CropHandle.Move);
        m.DragByImagePx(-500, 50);
        Assert.Equal(new RectPx(0, 0, 800, 800), m.Rect); // y clamped: box is full height
        m.DragByImagePx(5000, 5000);
        Assert.Equal(new RectPx(200, 0, 800, 800), m.Rect);
        m.EndDrag();
        AssertInside(m);
    }

    [Fact]
    public void Move_is_relative_to_the_drag_start_not_cumulative()
    {
        var m = M();
        m.SetAspect(CropAspect.Square);
        m.BeginDrag(CropHandle.Move);
        m.DragByImagePx(10, 0);
        m.DragByImagePx(10, 0);
        Assert.Equal(110, m.Rect.X);
    }

    [Fact]
    public void Preview_pixel_drag_converts_by_the_scale()
    {
        var m = M(scale: 0.5);
        m.SetAspect(CropAspect.Square);
        m.BeginDrag(CropHandle.Move);
        m.DragBy(-20, 0); // -40 image px
        Assert.Equal(60, m.Rect.X);
    }

    [Fact]
    public void Move_keeps_size_and_output()
    {
        var m = M();
        m.SetAspect(CropAspect.Square);
        m.SetScalePercent(50);
        Drag(m, CropHandle.Move, 30, 0);
        Assert.Equal(800, m.Rect.W);
        Assert.Equal((400, 400), (m.OutputWidth, m.OutputHeight));
    }

    [Fact]
    public void Nudge_moves_by_pixels_and_clamps()
    {
        var m = M();
        m.SetAspect(CropAspect.Square); // x=100
        m.Nudge(1, 0);
        Assert.Equal(101, m.Rect.X);
        m.Nudge(-10, 0);
        Assert.Equal(91, m.Rect.X);
        m.Nudge(-1000, 0);
        Assert.Equal(0, m.Rect.X);
        m.Nudge(0, -5);
        Assert.Equal(0, m.Rect.Y);
        m.Nudge(5000, 5000);
        Assert.Equal(new RectPx(200, 0, 800, 800), m.Rect);
    }

    [Fact]
    public void Nudge_without_change_does_not_raise()
    {
        var m = M();
        var n = 0;
        m.Changed += () => n++;
        m.Nudge(-1, -1);
        Assert.Equal(0, n);
    }

    // ---- Free resizing ----

    // A free box (200,100,300,250) inside a 1000x800 image.
    private static CropBoxModel Inner()
    {
        var m = M(scale: 1);
        Drag(m, CropHandle.TopLeft, 200, 100);
        Drag(m, CropHandle.BottomRight, -500, -450);
        return m;
    }

    [Fact]
    public void Inner_helper_builds_the_expected_box() => Assert.Equal(new RectPx(200, 100, 300, 250), Inner().Rect);

    public static TheoryData<CropHandle, RectPx> FreeDrags() => new()
    {
        { CropHandle.TopLeft, new RectPx(300, 150, 200, 200) },
        { CropHandle.Top, new RectPx(200, 150, 300, 200) },
        { CropHandle.TopRight, new RectPx(200, 150, 400, 200) },
        { CropHandle.Right, new RectPx(200, 100, 400, 250) },
        { CropHandle.BottomRight, new RectPx(200, 100, 400, 300) },
        { CropHandle.Bottom, new RectPx(200, 100, 300, 300) },
        { CropHandle.BottomLeft, new RectPx(300, 100, 200, 300) },
        { CropHandle.Left, new RectPx(300, 100, 200, 250) },
    };

    [Theory, MemberData(nameof(FreeDrags))]
    public void Free_handles_move_only_their_edges(CropHandle handle, RectPx expected)
    {
        var m = Inner();
        Drag(m, handle, 100, 50);
        Assert.Equal(expected, m.Rect);
        AssertInside(m);
    }

    [Theory]
    [InlineData(CropHandle.TopLeft)]
    [InlineData(CropHandle.Top)]
    [InlineData(CropHandle.TopRight)]
    [InlineData(CropHandle.Right)]
    [InlineData(CropHandle.BottomRight)]
    [InlineData(CropHandle.Bottom)]
    [InlineData(CropHandle.BottomLeft)]
    [InlineData(CropHandle.Left)]
    public void Free_handles_clamp_to_the_image_in_every_direction(CropHandle handle)
    {
        foreach (var (dx, dy) in new[] { (-9999.0, -9999.0), (9999.0, 9999.0), (-9999.0, 9999.0), (9999.0, -9999.0) })
        {
            var m = Inner();
            Drag(m, handle, dx, dy);
            AssertInside(m);
        }
    }

    [Fact]
    public void Free_resize_stops_at_the_min_box()
    {
        var m = Inner();
        Drag(m, CropHandle.TopLeft, 9999, 9999);
        Assert.Equal(new RectPx(484, 334, 16, 16), m.Rect);
        Drag(m, CropHandle.Right, -9999, 0);
        Assert.Equal(16, m.Rect.W);
    }

    [Fact]
    public void Free_resize_can_reach_the_image_edges()
    {
        var m = Inner();
        Drag(m, CropHandle.BottomRight, 9999, 9999);
        Assert.Equal(new RectPx(200, 100, 800, 700), m.Rect);
        Drag(m, CropHandle.TopLeft, -9999, -9999);
        Assert.Equal(new RectPx(0, 0, 1000, 800), m.Rect);
    }

    [Fact]
    public void Dragging_without_begin_does_nothing()
    {
        var m = M();
        m.DragByImagePx(50, 50);
        Assert.Equal(new RectPx(0, 0, 1000, 800), m.Rect);
        Assert.False(m.IsDragging);
        m.BeginDrag(CropHandle.Move);
        Assert.True(m.IsDragging);
        m.EndDrag();
        Assert.False(m.IsDragging);
    }

    // ---- Aspect-locked resizing ----

    [Theory]
    [InlineData(CropAspect.Square)]
    [InlineData(CropAspect.FourFive)]
    [InlineData(CropAspect.ThreeTwo)]
    [InlineData(CropAspect.SixteenNine)]
    [InlineData(CropAspect.NineSixteen)]
    [InlineData(CropAspect.Original)]
    public void Locked_resize_keeps_the_ratio_for_every_handle(CropAspect aspect)
    {
        var handles = new[]
        {
            CropHandle.TopLeft, CropHandle.Top, CropHandle.TopRight, CropHandle.Right,
            CropHandle.BottomRight, CropHandle.Bottom, CropHandle.BottomLeft, CropHandle.Left,
        };
        foreach (var handle in handles)
        {
            foreach (var (dx, dy) in new[] { (-120.0, -80.0), (150.0, 90.0), (-9999.0, 9999.0), (9999.0, -9999.0), (0.0, 0.0), (40.0, -300.0) })
            {
                var m = M(scale: 1);
                m.SetAspect(aspect);
                Drag(m, CropHandle.BottomRight, -300, -300); // shrink first so there is room both ways
                Drag(m, CropHandle.Move, 100, 60);
                Drag(m, handle, dx, dy);
                AssertInside(m);
                AssertRatio(m, m.AspectRatio!.Value);
            }
        }
    }

    [Fact]
    public void Locked_corner_anchors_the_opposite_corner()
    {
        var m = M(scale: 1);
        m.SetAspect(CropAspect.Square); // (100,0,800,800)
        Drag(m, CropHandle.BottomRight, -300, -300); // (100,0,500,500)
        Assert.Equal(new RectPx(100, 0, 500, 500), m.Rect);
        Drag(m, CropHandle.TopLeft, 100, 100);
        Assert.Equal(new RectPx(200, 100, 400, 400), m.Rect);
        Assert.Equal((600, 500), (m.Rect.X + m.Rect.W, m.Rect.Y + m.Rect.H));
    }

    [Fact]
    public void Locked_corner_grows_when_either_axis_grows()
    {
        var m = M(scale: 1);
        m.SetAspect(CropAspect.Square);
        Drag(m, CropHandle.BottomRight, -400, -400); // (100,0,400,400)
        Drag(m, CropHandle.BottomRight, -100, 100); // shrinks x, grows y: the larger demand wins
        Assert.Equal(500, m.Rect.W);
        Assert.Equal(500, m.Rect.H);
    }

    [Fact]
    public void Locked_corner_is_limited_by_the_nearer_image_edge()
    {
        var m = M(scale: 1);
        m.SetAspect(CropAspect.Square); // (100,0,800,800)
        Drag(m, CropHandle.BottomRight, -600, -600); // (100,0,200,200)
        Drag(m, CropHandle.BottomRight, 9999, 9999);
        Assert.Equal(new RectPx(100, 0, 800, 800), m.Rect);
    }

    [Fact]
    public void Locked_edge_resize_stays_centred_on_the_other_axis()
    {
        var m = M(scale: 1);
        m.SetAspect(CropAspect.Square);
        Drag(m, CropHandle.BottomRight, -400, -400); // (100,0,400,400)
        Drag(m, CropHandle.Move, 0, 200); // (100,200,400,400)
        Drag(m, CropHandle.Right, 100, 0); // w 500 -> h 500, centred on y = 400
        Assert.Equal(new RectPx(100, 150, 500, 500), m.Rect);
        Drag(m, CropHandle.Bottom, 0, -100); // h 400 -> w 400, centred on x = 350
        Assert.Equal(new RectPx(150, 150, 400, 400), m.Rect);
    }

    [Fact]
    public void Locked_edge_resize_respects_the_image_bounds()
    {
        var m = M(scale: 1);
        m.SetAspect(CropAspect.Square);
        Drag(m, CropHandle.BottomRight, -600, -600); // (100,0,200,200)
        Drag(m, CropHandle.Right, 9999, 0);
        AssertInside(m);
        AssertRatio(m, 1);
        Assert.True(m.Rect.W <= 800 && m.Rect.H <= 800);
    }

    [Fact]
    public void Locked_resize_respects_the_min_box_for_both_edges()
    {
        var m = M(scale: 1);
        m.SetAspect(CropAspect.NineSixteen);
        Drag(m, CropHandle.BottomRight, -9999, -9999);
        Assert.True(m.Rect.W >= 16 && m.Rect.H >= 16, $"{m.Rect}");
        AssertRatio(m, 9 / 16.0);
    }

    [Fact]
    public void Resizing_updates_the_output_size_at_the_current_scale()
    {
        var m = M(scale: 1);
        m.SetScalePercent(50);
        Drag(m, CropHandle.BottomRight, -500, -400); // (0,0,500,400)
        Assert.Equal((250, 200), (m.OutputWidth, m.OutputHeight));
    }

    // ---- Hit testing ----

    [Fact]
    public void HitTest_finds_corners_edges_and_inside()
    {
        var m = M(scale: 1);
        Drag(m, CropHandle.TopLeft, 200, 100);
        Drag(m, CropHandle.BottomRight, -200, -100); // (200,100,600,600)
        Assert.Equal(CropHandle.TopLeft, m.HitTest(202, 98, 10));
        Assert.Equal(CropHandle.TopRight, m.HitTest(800, 100, 10));
        Assert.Equal(CropHandle.BottomLeft, m.HitTest(200, 700, 10));
        Assert.Equal(CropHandle.BottomRight, m.HitTest(795, 705, 10));
        Assert.Equal(CropHandle.Top, m.HitTest(500, 104, 10));
        Assert.Equal(CropHandle.Bottom, m.HitTest(500, 696, 10));
        Assert.Equal(CropHandle.Left, m.HitTest(195, 400, 10));
        Assert.Equal(CropHandle.Right, m.HitTest(805, 400, 10));
        Assert.Equal(CropHandle.Move, m.HitTest(500, 400, 10));
        Assert.Equal(CropHandle.None, m.HitTest(50, 50, 10));
        Assert.Equal(CropHandle.None, m.HitTest(500, 760, 10));
    }

    [Fact]
    public void HitTest_uses_preview_pixels()
    {
        var m = M(scale: 0.5);
        Assert.Equal(CropHandle.BottomRight, m.HitTest(500, 400, 10));
        Assert.Equal(CropHandle.Move, m.HitTest(250, 200, 10));
    }

    [Fact]
    public void HitTest_tolerance_shrinks_for_tiny_boxes_so_the_inside_stays_grabbable()
    {
        var m = M(scale: 1);
        Drag(m, CropHandle.BottomRight, -9999, -9999); // 16 x 16 at the origin
        Assert.Equal(new RectPx(0, 0, 16, 16), m.Rect);
        Assert.Equal(CropHandle.Move, m.HitTest(8, 8, 12));
    }

    // ---- Scale and output size ----

    [Fact]
    public void Slider_percent_sets_the_output_size()
    {
        var m = M();
        m.SetScalePercent(50);
        Assert.Equal((500, 400), (m.OutputWidth, m.OutputHeight));
        Assert.Equal(50, m.ScalePercent);
        Assert.Equal(new CropChoice(new RectPx(0, 0, 1000, 800), 500, 400), m.ToChoice());
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(5, 5)]
    [InlineData(37.5, 37.5)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    [InlineData(-10, 5)]
    public void Slider_percent_is_clamped_to_5_100(double set, double expected)
    {
        var m = M();
        m.SetScalePercent(set);
        Assert.Equal(expected, m.ScalePercent, 6);
    }

    [Fact]
    public void Slider_percent_ignores_NaN()
    {
        var m = M();
        m.SetScalePercent(40);
        m.SetScalePercent(double.NaN);
        Assert.Equal(40, m.ScalePercent);
    }

    [Fact]
    public void Typing_width_sets_height_by_aspect_and_percent()
    {
        var m = M();
        Assert.True(m.TrySetOutputWidth(250));
        Assert.Equal((250, 200), (m.OutputWidth, m.OutputHeight));
        Assert.Equal(25, m.ScalePercent, 6);
    }

    [Fact]
    public void Typing_height_sets_width_by_aspect_and_percent()
    {
        var m = M();
        Assert.True(m.TrySetOutputHeight(400));
        Assert.Equal((500, 400), (m.OutputWidth, m.OutputHeight));
        Assert.Equal(50, m.ScalePercent, 6);
    }

    [Fact]
    public void Typed_size_may_go_below_5_percent_and_the_slider_pins_at_5()
    {
        var m = M();
        Assert.True(m.TrySetOutputWidth(20));
        Assert.Equal((20, 16), (m.OutputWidth, m.OutputHeight));
        Assert.Equal(2, m.ScalePercent, 6);
        Assert.Equal(5, m.SliderPercent);
        Assert.True(m.IsValid);
    }

    [Fact]
    public void Typed_size_below_5_percent_is_kept_when_the_box_changes()
    {
        var m = M();
        m.TrySetOutputWidth(20); // 2 %
        Drag(m, CropHandle.BottomRight, -500, -400); // 500 x 400
        Assert.Equal((10, 8), (m.OutputWidth, m.OutputHeight));
    }

    [Fact]
    public void Output_never_collapses_to_zero()
    {
        var m = M();
        Assert.True(m.TrySetOutputWidth(1));
        Assert.Equal(1, m.OutputWidth);
        Assert.True(m.OutputHeight >= 1);
        Drag(m, CropHandle.BottomRight, -9999, -9999);
        Assert.True(m.OutputWidth >= 1 && m.OutputHeight >= 1);
        Assert.True(m.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Typed_width_out_of_range_is_rejected_and_changes_nothing(double w)
    {
        var m = M();
        m.SetScalePercent(60);
        Assert.False(m.TrySetOutputWidth(w));
        Assert.Equal((600, 480), (m.OutputWidth, m.OutputHeight));
        Assert.Equal(60, m.ScalePercent, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(801)]
    [InlineData(double.NaN)]
    public void Typed_height_out_of_range_is_rejected(double h)
    {
        var m = M();
        Assert.False(m.TrySetOutputHeight(h));
        Assert.Equal((1000, 800), (m.OutputWidth, m.OutputHeight));
    }

    [Fact]
    public void Typed_full_width_means_100_percent()
    {
        var m = M();
        m.SetScalePercent(30);
        Assert.True(m.TrySetOutputWidth(1000));
        Assert.Equal(100, m.ScalePercent, 6);
        Assert.Equal((1000, 800), (m.OutputWidth, m.OutputHeight));
    }

    [Fact]
    public void Scale_is_relative_to_the_crop_box()
    {
        var m = M(scale: 1);
        Drag(m, CropHandle.BottomRight, -600, -400); // 400 x 400
        Assert.True(m.TrySetOutputWidth(100));
        Assert.Equal(25, m.ScalePercent, 6);
        Assert.Equal((100, 100), (m.OutputWidth, m.OutputHeight));
    }

    // ---- Validity, events ----

    [Fact]
    public void ToChoice_matches_rect_and_output()
    {
        var m = M(scale: 1);
        m.SetAspect(CropAspect.SixteenNine);
        m.SetScalePercent(25);
        var c = m.ToChoice();
        Assert.NotNull(c);
        Assert.Equal(m.Rect, c!.Rect);
        Assert.Equal(m.OutputWidth, c.OutputWidth);
        Assert.Equal(m.OutputHeight, c.OutputHeight);
    }

    [Fact]
    public void Tiny_images_get_a_smaller_minimum_box()
    {
        var m = new CropBoxModel(10, 8);
        Assert.True(m.IsValid);
        Drag(m, CropHandle.BottomRight, -100, -100);
        Assert.Equal(new RectPx(0, 0, 10, 8), m.Rect);
        AssertInside(m);
    }

    [Fact]
    public void Changed_is_raised_by_each_mutation()
    {
        var m = M();
        var n = 0;
        m.Changed += () => n++;
        m.SetAspect(CropAspect.Square);
        m.SetScalePercent(60);
        m.TrySetOutputWidth(100);
        Drag(m, CropHandle.Move, 10, 0);
        m.Reset();
        Assert.True(n >= 5);
    }

    [Fact]
    public void Random_drags_never_leave_the_model_invalid()
    {
        var rng = new Random(1234);
        var handles = Enum.GetValues<CropHandle>().Where(h => h != CropHandle.None).ToArray();
        var aspects = Enum.GetValues<CropAspect>();
        for (var i = 0; i < 2000; i++)
        {
            var m = new CropBoxModel(rng.Next(20, 5000), rng.Next(20, 5000), 0.2);
            m.SetAspect(aspects[rng.Next(aspects.Length)]);
            for (var step = 0; step < 6; step++)
            {
                Drag(m, handles[rng.Next(handles.Length)], rng.Next(-3000, 3000), rng.Next(-3000, 3000));
                Assert.True(m.IsValid, $"{m.Rect} in {m.ImageWidth}x{m.ImageHeight} {m.Aspect}");
                AssertInside(m);
            }
        }
    }
}
