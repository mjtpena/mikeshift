using MikeShift.Core.Interaction;

namespace MikeShift.Core.Tests.Interaction;

public class EasingTests
{
    [Fact]
    public void EndpointsAreExact()
    {
        foreach (var curve in new[] { Easing.Linear, Easing.Open, Easing.EaseIn, Easing.EaseOut, Easing.EaseInOut })
        {
            Assert.Equal(0, curve.Evaluate(0));
            Assert.Equal(1, curve.Evaluate(1));
            Assert.Equal(0, curve.Evaluate(-5));
            Assert.Equal(1, curve.Evaluate(7));
        }
    }

    [Fact]
    public void LinearIsIdentity()
    {
        for (var t = 0.0; t <= 1; t += 0.05) Assert.Equal(t, Easing.Linear.Evaluate(t), 9);
    }

    [Fact]
    public void EaseInOutIsSymmetric()
    {
        Assert.Equal(0.5, Easing.EaseInOut.Evaluate(0.5), 6);
        for (var t = 0.1; t < 0.5; t += 0.1)
            Assert.Equal(1, Easing.EaseInOut.Evaluate(t) + Easing.EaseInOut.Evaluate(1 - t), 6);
    }

    [Fact]
    public void KnownCssValues()
    {
        // cubic-bezier(0.25,0.1,0.25,1) "ease" at t=0.5 is ~0.8024 (reference values from browsers)
        Assert.Equal(0.8024, new CubicBezier(0.25, 0.1, 0.25, 1).Evaluate(0.5), 3);
        // ease-in at 0.5 ~0.3153, ease-out at 0.5 ~0.6846 (mirror images of each other)
        Assert.Equal(0.3153, Easing.EaseIn.Evaluate(0.5), 3);
        Assert.Equal(0.6846, Easing.EaseOut.Evaluate(0.5), 3);
        Assert.Equal(1, Easing.EaseIn.Evaluate(0.5) + Easing.EaseOut.Evaluate(0.5), 6);
    }

    [Fact]
    public void EaseInIsSlowerThanLinearAndEaseOutFaster()
    {
        for (var t = 0.1; t < 0.95; t += 0.1)
        {
            Assert.True(Easing.EaseIn.Evaluate(t) < t);
            Assert.True(Easing.EaseOut.Evaluate(t) > t);
        }
    }

    [Fact]
    public void OpenCurveOvershootsThenSettles()
    {
        var max = 0.0;
        for (var t = 0.0; t <= 1; t += 0.005) max = Math.Max(max, Easing.Open.Evaluate(t));
        Assert.True(max > 1.02, $"expected overshoot, max was {max}");
        Assert.True(max < 1.25);
        Assert.Equal(1, Easing.Open.Evaluate(1));
        Assert.True(Easing.Open.Evaluate(0.9) < 1.05);
    }

    [Fact]
    public void MonotonicCurvesNeverDecrease()
    {
        foreach (var curve in new[] { Easing.EaseIn, Easing.EaseOut, Easing.EaseInOut })
        {
            var prev = 0.0;
            for (var t = 0.0; t <= 1; t += 0.01)
            {
                var v = curve.Evaluate(t);
                Assert.True(v >= prev - 1e-9);
                prev = v;
            }
        }
    }

    [Fact]
    public void ControlPointXMustBeInRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezier(-0.1, 0, 0.5, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezier(0.5, 0, 1.1, 1));
    }

    [Fact]
    public void ProgressClampsAndHonoursDelay()
    {
        Assert.Equal(0, Easing.Progress(-1, 0.5));
        Assert.Equal(0.5, Easing.Progress(0.25, 0.5), 9);
        Assert.Equal(1, Easing.Progress(9, 0.5));
        Assert.Equal(0, Easing.Progress(0.05, 0.5, 0.1));
        Assert.Equal(0.5, Easing.Progress(0.35, 0.5, 0.1), 9);
    }

    [Fact]
    public void ZeroDurationFinishesImmediately()
    {
        Assert.Equal(1, Easing.Progress(0, 0));
        Assert.Equal(0, Easing.Progress(0, 0, 0.1));
        Assert.Equal(1, Easing.Progress(0.2, 0, 0.1));
    }

    [Fact]
    public void TimingConstantsMatchTheSpec()
    {
        Assert.Equal(0.28, WheelTimings.OpenSeconds);
        Assert.Equal(0.60, WheelTimings.OpenScaleFrom);
        Assert.Equal(0.010, WheelTimings.StaggerSeconds);
        Assert.Equal(0.12, WheelTimings.PageFadeSeconds);
        Assert.Equal(0.30, WheelTimings.PageSlideSeconds);
        Assert.Equal(16, WheelTimings.PageSlideDistance);
        Assert.Equal(0.20, WheelTimings.CollapseSeconds);
        Assert.Equal(1.70, WheelTimings.CollapseScaleTo);
        Assert.Equal(0.16, WheelTimings.PickPulseSeconds);
        Assert.Equal(0.20, WheelTimings.DismissSeconds);
    }

    [Fact]
    public void OpenAnimationRunsFromSixtyToOneHundredPercent()
    {
        Assert.Equal(0.6, WheelTimings.OpenScale(0), 9);
        Assert.Equal(1.0, WheelTimings.OpenScale(0.28), 9);
        Assert.Equal(1.0, WheelTimings.OpenScale(5), 9);
        Assert.True(WheelTimings.OpenScale(0.14) > 0.6);
    }

    [Fact]
    public void TilesPopOutStaggeredByTenMilliseconds()
    {
        Assert.Equal(0, WheelTimings.TilePop(3, 0.029), 9);
        Assert.True(WheelTimings.TilePop(3, 0.05) > 0);
        Assert.True(WheelTimings.TilePop(0, 0.05) > WheelTimings.TilePop(3, 0.05));
        Assert.Equal(1.0, WheelTimings.TilePop(3, 1), 9);
        Assert.Equal(0.28 + 0.07, WheelTimings.OpenTotalSeconds(8), 9);
        Assert.Equal(0.28, WheelTimings.OpenTotalSeconds(1), 9);
        Assert.Equal(0.28, WheelTimings.OpenTotalSeconds(0), 9);
    }

    [Fact]
    public void CollapseGrowsToOneSeventyPercentWhileFading()
    {
        Assert.Equal(1.0, WheelTimings.CollapseScale(0), 9);
        Assert.Equal(1.7, WheelTimings.CollapseScale(0.2), 9);
        Assert.Equal(1.0, WheelTimings.CollapseOpacity(0), 9);
        Assert.Equal(0.0, WheelTimings.CollapseOpacity(0.2), 9);
    }

    [Fact]
    public void PickPulseReachesEighteenPixels()
    {
        Assert.Equal(0, WheelTimings.PickPulseOffset(0), 9);
        Assert.Equal(18, WheelTimings.PickPulseOffset(0.16), 9);
        Assert.Equal(18, WheelTimings.PickPulseOffset(1), 9);
    }

    [Fact]
    public void PageSwitchFadesOutThenSlidesInFromSixteenPixels()
    {
        Assert.Equal(1.0, WheelTimings.PageOutOpacity(0), 9);
        Assert.Equal(0.0, WheelTimings.PageOutOpacity(0.12), 9);
        Assert.Equal(-16, WheelTimings.PageInOffset(0), 9);
        Assert.Equal(0, WheelTimings.PageInOffset(0.3), 9);
        Assert.Equal(0.0, WheelTimings.PageInOpacity(0), 9);
        Assert.Equal(1.0, WheelTimings.PageInOpacity(0.3), 9);
    }

    [Fact]
    public void DismissFadesOverTwoHundredMilliseconds()
    {
        Assert.Equal(1.0, WheelTimings.DismissOpacity(0), 9);
        Assert.Equal(0.0, WheelTimings.DismissOpacity(0.2), 9);
    }

    [Fact]
    public void DisabledAnimationsJumpStraightToTheEndState()
    {
        Assert.Equal(1.0, WheelTimings.OpenScale(0, animationsEnabled: false));
        Assert.Equal(1.0, WheelTimings.TilePop(5, 0, animationsEnabled: false));
        Assert.Equal(WheelTimings.CollapseScaleTo, WheelTimings.CollapseScale(0, animationsEnabled: false));
        Assert.Equal(0.0, WheelTimings.CollapseOpacity(0, animationsEnabled: false));
        Assert.Equal(18, WheelTimings.PickPulseOffset(0, animationsEnabled: false));
        Assert.Equal(0.0, WheelTimings.DismissOpacity(0, animationsEnabled: false));
        Assert.Equal(0, WheelTimings.PageInOffset(0, animationsEnabled: false));
        Assert.Equal(0, WheelTimings.Scaled(0.28, false));
        Assert.Equal(0.28, WheelTimings.Scaled(0.28, true));
    }
}
