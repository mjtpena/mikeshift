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
    public void FluentCurvesNeverOvershoot()
    {
        foreach (var curve in new[] { Easing.Decelerate, Easing.Accelerate, Easing.Open })
        {
            for (var t = 0.0; t <= 1; t += 0.005)
            {
                var v = curve.Evaluate(t);
                Assert.InRange(v, 0, 1);
            }
            Assert.Equal(1, curve.Evaluate(1));
        }
        Assert.Same(Easing.Decelerate, Easing.Open);
    }

    [Fact]
    public void DecelerateStartsFastAndAccelerateStartsSlow()
    {
        // cubic-bezier(0,0,0,1): most of the distance is covered early; cubic-bezier(1,0,1,1): most of it late.
        Assert.True(Easing.Decelerate.Evaluate(0.25) > 0.6);
        Assert.True(Easing.Decelerate.Evaluate(0.5) > 0.85);
        Assert.True(Easing.Accelerate.Evaluate(0.25) < 0.1);
        Assert.True(Easing.Accelerate.Evaluate(0.5) < 0.4);
        for (var t = 0.05; t < 1; t += 0.05) Assert.True(Easing.Decelerate.Evaluate(t) > Easing.Accelerate.Evaluate(t));
    }

    [Fact]
    public void MonotonicCurvesNeverDecrease()
    {
        foreach (var curve in new[] { Easing.EaseIn, Easing.EaseOut, Easing.EaseInOut, Easing.Decelerate, Easing.Accelerate })
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
    public void TimingConstantsFollowFluentMotion()
    {
        // Entrances 167-250 ms (decelerate), exits 83-167 ms (accelerate).
        Assert.InRange(WheelTimings.OpenSeconds, 0.167, 0.250);
        Assert.InRange(WheelTimings.PageSlideSeconds, 0.167, 0.250);
        Assert.InRange(WheelTimings.PageFadeSeconds, 0.083, 0.167);
        Assert.InRange(WheelTimings.DismissSeconds, 0.083, 0.167);
        Assert.InRange(WheelTimings.CollapseSeconds, 0.083, 0.167);
        Assert.Equal(0.008, WheelTimings.StaggerSeconds);
        Assert.Equal(12, WheelTimings.PageSlideDistance);
        Assert.Equal(0.12, WheelTimings.PickPulseSeconds);
    }

    [Fact]
    public void NothingEverGrowsPastItsSizeAndNothingZoomsOut()
    {
        Assert.InRange(WheelTimings.OpenScaleFrom, 0.9, 0.999);
        Assert.InRange(WheelTimings.CollapseScaleTo, 0.9, 1.0);
        Assert.InRange(WheelTimings.DismissScaleTo, 0.9, 1.0);
        for (var t = 0.0; t <= 0.5; t += 0.002)
        {
            Assert.InRange(WheelTimings.OpenScale(t), WheelTimings.OpenScaleFrom - 1e-9, 1.0 + 1e-9);
            Assert.InRange(WheelTimings.OpenOpacity(t), 0, 1);
            Assert.InRange(WheelTimings.TilePop(3, t), 0, 1);
            Assert.InRange(WheelTimings.CollapseScale(t), WheelTimings.CollapseScaleTo - 1e-9, 1.0 + 1e-9);
            Assert.InRange(WheelTimings.DismissScale(t), WheelTimings.DismissScaleTo - 1e-9, 1.0 + 1e-9);
        }
    }

    [Fact]
    public void OpenAnimationDeceleratesFromNinetyFourToOneHundredPercent()
    {
        Assert.Equal(0.94, WheelTimings.OpenScale(0), 9);
        Assert.Equal(1.0, WheelTimings.OpenScale(WheelTimings.OpenSeconds), 9);
        Assert.Equal(1.0, WheelTimings.OpenScale(5), 9);
        Assert.Equal(0.0, WheelTimings.OpenOpacity(0), 9);
        Assert.Equal(1.0, WheelTimings.OpenOpacity(WheelTimings.OpenSeconds), 9);
        // decelerating: three quarters of the way there after a quarter of the time, so the wheel is readable well inside the 100 ms budget
        Assert.True(WheelTimings.OpenOpacity(0.05) > 0.6);
        Assert.True(WheelTimings.OpenOpacity(0.1) > 0.85);
    }

    [Fact]
    public void TilesAppearStaggeredByEightMilliseconds()
    {
        Assert.Equal(0, WheelTimings.TilePop(3, 0.023), 9);
        Assert.True(WheelTimings.TilePop(3, 0.05) > 0);
        Assert.True(WheelTimings.TilePop(0, 0.05) > WheelTimings.TilePop(3, 0.05));
        Assert.Equal(1.0, WheelTimings.TilePop(3, 1), 9);
        Assert.Equal(0.20 + 7 * 0.008, WheelTimings.OpenTotalSeconds(8), 9);
        Assert.Equal(0.20, WheelTimings.OpenTotalSeconds(1), 9);
        Assert.Equal(0.20, WheelTimings.OpenTotalSeconds(0), 9);
    }

    [Fact]
    public void CollapseSettlesToNinetySixPercentWhileFadingOut()
    {
        Assert.Equal(1.0, WheelTimings.CollapseScale(0), 9);
        Assert.Equal(0.96, WheelTimings.CollapseScale(WheelTimings.CollapseSeconds), 9);
        Assert.Equal(1.0, WheelTimings.CollapseOpacity(0), 9);
        Assert.Equal(0.0, WheelTimings.CollapseOpacity(WheelTimings.CollapseSeconds), 9);
        // accelerating: the picked tile stays clearly visible while it pulses
        Assert.True(WheelTimings.CollapseOpacity(0.07) > 0.85);
    }

    [Fact]
    public void PickPulseReachesEighteenPixelsAndDecelerates()
    {
        Assert.Equal(0, WheelTimings.PickPulseOffset(0), 9);
        Assert.Equal(18, WheelTimings.PickPulseOffset(0.12), 9);
        Assert.Equal(18, WheelTimings.PickPulseOffset(1), 9);
        Assert.True(WheelTimings.PickPulseOffset(0.03) > 9);   // most of the movement happens early
        for (var t = 0.0; t <= 0.2; t += 0.002) Assert.InRange(WheelTimings.PickPulseOffset(t), 0, 18 + 1e-9);   // no overshoot
    }

    [Fact]
    public void PageSwitchFadesOutQuicklyThenSlidesInFromTwelvePixels()
    {
        Assert.Equal(1.0, WheelTimings.PageOutOpacity(0), 9);
        Assert.Equal(0.0, WheelTimings.PageOutOpacity(WheelTimings.PageFadeSeconds), 9);
        Assert.Equal(-12, WheelTimings.PageInOffset(0), 9);
        Assert.Equal(0, WheelTimings.PageInOffset(WheelTimings.PageSlideSeconds), 9);
        Assert.Equal(0.0, WheelTimings.PageInOpacity(0), 9);
        Assert.Equal(1.0, WheelTimings.PageInOpacity(WheelTimings.PageSlideSeconds), 9);
        for (var t = 0.0; t <= 0.3; t += 0.002) Assert.InRange(WheelTimings.PageInOffset(t), -12 - 1e-9, 1e-9);
    }

    [Fact]
    public void DismissFadesAndSettlesOverOneHundredMilliseconds()
    {
        Assert.Equal(1.0, WheelTimings.DismissOpacity(0), 9);
        Assert.Equal(0.0, WheelTimings.DismissOpacity(WheelTimings.DismissSeconds), 9);
        Assert.Equal(1.0, WheelTimings.DismissScale(0), 9);
        Assert.Equal(0.96, WheelTimings.DismissScale(WheelTimings.DismissSeconds), 9);
    }

    [Fact]
    public void DisabledAnimationsJumpStraightToTheEndState()
    {
        Assert.Equal(1.0, WheelTimings.OpenScale(0, animationsEnabled: false));
        Assert.Equal(1.0, WheelTimings.OpenOpacity(0, animationsEnabled: false));
        Assert.Equal(1.0, WheelTimings.TilePop(5, 0, animationsEnabled: false));
        Assert.Equal(WheelTimings.CollapseScaleTo, WheelTimings.CollapseScale(0, animationsEnabled: false));
        Assert.Equal(0.0, WheelTimings.CollapseOpacity(0, animationsEnabled: false));
        Assert.Equal(18, WheelTimings.PickPulseOffset(0, animationsEnabled: false));
        Assert.Equal(0.0, WheelTimings.DismissOpacity(0, animationsEnabled: false));
        Assert.Equal(WheelTimings.DismissScaleTo, WheelTimings.DismissScale(0, animationsEnabled: false));
        Assert.Equal(0, WheelTimings.PageInOffset(0, animationsEnabled: false));
        Assert.Equal(0, WheelTimings.Scaled(0.2, false));
        Assert.Equal(0.2, WheelTimings.Scaled(0.2, true));
    }
}
