using Shiftvert.Core.Interaction;

namespace Shiftvert.Core.Tests.Interaction;

public class WheelAnimationTests
{
    private static WheelAnimation Shown(bool animations = true, double at = 0)
    {
        var a = new WheelAnimation(() => animations);
        a.Show(at);
        return a;
    }

    [Fact]
    public void OpensFromSixtyPercentAndSettlesAtOne()
    {
        var a = Shown();
        Assert.Equal(0.6, a.Scale(0), 3);
        Assert.Equal(0, a.Opacity(0), 3);
        Assert.True(a.IsAnimating(0.1));

        Assert.Equal(1.0, a.Scale(0.5), 3);
        Assert.Equal(1.0, a.Opacity(0.5), 3);
        Assert.False(a.IsAnimating(0.5));
    }

    [Fact]
    public void OpenOvershootsButOpacityNeverExceedsOne()
    {
        var a = Shown();
        var maxScale = 0.0;
        for (var t = 0.0; t <= 0.3; t += 0.005)
        {
            maxScale = Math.Max(maxScale, a.Scale(t));
            Assert.InRange(a.Opacity(t), 0, 1);
        }
        Assert.True(maxScale > 1.0, "the open curve overshoots");
    }

    [Fact]
    public void DisabledAnimationsAreInstant()
    {
        var a = Shown(animations: false);
        Assert.Equal(1.0, a.Scale(0), 6);
        Assert.Equal(1.0, a.Opacity(0), 6);
        a.TilesArrived(0);
        Assert.Equal(1.0, a.TilePop(7, 0), 6);
        a.HighlightChanged(0, 2);
        Assert.Equal(1.0, a.Highlight(2, 0), 6);
        Assert.False(a.IsAnimating(0));

        a.PageChanged(1);
        Assert.False(a.PageSwitching(1));
        Assert.Equal(1.0, a.IncomingOpacity(1));

        a.Pick(2, 3);
        Assert.True(a.IsFinished(2));
    }

    [Fact]
    public void TilesPopStaggeredAndNothingShowsBeforeTheyArrive()
    {
        var a = Shown();
        Assert.Equal(0, a.TilePop(0, 5));

        a.TilesArrived(1.0);
        Assert.Equal(0, a.TilePop(0, 1.0), 6);
        // tile 5 starts 50 ms after tile 0
        Assert.True(a.TilePop(0, 1.06) > a.TilePop(5, 1.06));
        Assert.Equal(1.0, a.TilePop(5, 1.5), 3);
    }

    [Fact]
    public void HighlightEasesInAndOutAndTheNewTileTakesOver()
    {
        var a = Shown();
        a.TilesArrived(0);
        a.HighlightChanged(1.0, 2);
        Assert.Equal(0, a.Highlight(2, 1.0), 6);
        Assert.InRange(a.Highlight(2, 1.05), 0.01, 0.99);
        Assert.Equal(1, a.Highlight(2, 1.2), 6);

        a.HighlightChanged(2.0, 4);
        Assert.Equal(1, a.Highlight(2, 2.0), 6);
        Assert.Equal(0, a.Highlight(2, 2.2), 6);
        Assert.Equal(1, a.Highlight(4, 2.2), 6);

        a.HighlightChanged(3.0, null);
        Assert.Equal(0, a.Highlight(4, 3.2), 6);
    }

    [Fact]
    public void PageSwitchFadesOutThenSlidesIn()
    {
        var a = Shown();
        a.TilesArrived(0);
        a.PageChanged(1.0);

        Assert.True(a.PageSwitching(1.05));
        Assert.Equal(1.0, a.OutgoingOpacity(1.0), 6);
        Assert.Equal(0.0, a.OutgoingOpacity(1.12), 6);
        Assert.Equal(0.0, a.IncomingOpacity(1.1), 6);
        Assert.Equal(-16, a.IncomingOffset(1.1), 6);

        Assert.Equal(1.0, a.IncomingOpacity(1.5), 6);
        Assert.Equal(0.0, a.IncomingOffset(1.5), 6);
        Assert.False(a.PageSwitching(1.5));
    }

    [Fact]
    public void PickPulsesAndCollapsesThenFinishes()
    {
        var a = Shown();
        a.TilesArrived(0);
        a.Pick(1.0, 2);

        Assert.Equal(WheelPhase.Collapsing, a.Phase);
        Assert.Equal(2, a.PickedIndex);
        Assert.Equal(1.0, a.Scale(1.0), 6);
        Assert.Equal(1.0, a.Opacity(1.0), 6);
        Assert.Equal(0, a.PickPush(1.0), 6);
        Assert.False(a.IsFinished(1.1));
        Assert.True(a.IsAnimating(1.1));

        Assert.Equal(WheelGeometry.PickPulse, a.PickPush(1.2), 6);
        Assert.Equal(WheelTimings.CollapseScaleTo, a.Scale(1.2), 6);
        Assert.Equal(0.0, a.Opacity(1.2), 6);
        Assert.True(a.IsFinished(1.21));
    }

    [Fact]
    public void DismissFadesOutFromTheCurrentOpacity()
    {
        var a = Shown();
        a.Dismiss(2.0);
        Assert.Equal(WheelPhase.Dismissing, a.Phase);
        Assert.Equal(1.0, a.Opacity(2.0), 6);
        Assert.InRange(a.Opacity(2.1), 0.01, 0.99);
        Assert.Equal(0.0, a.Opacity(2.2), 6);
        Assert.True(a.IsFinished(2.21));
    }

    [Fact]
    public void DismissDuringTheOpenAnimationStartsFromWhereItWas()
    {
        var a = Shown();
        var opacityAt = a.Opacity(0.05);
        a.Dismiss(0.05);
        Assert.Equal(opacityAt, a.Opacity(0.05), 6);
        Assert.True(a.Opacity(0.1) <= opacityAt);
    }

    [Fact]
    public void GenerationGuardsStaleCallbacks()
    {
        var a = new WheelAnimation();
        var first = a.Show(0);
        Assert.True(a.IsCurrent(first));

        a.Dismiss(1);
        var second = a.Show(2);
        Assert.False(a.IsCurrent(first));
        Assert.True(a.IsCurrent(second));
        Assert.Equal(WheelPhase.Visible, a.Phase);
    }

    [Fact]
    public void PickAndDismissOnlyApplyToAVisibleWheel()
    {
        var a = new WheelAnimation();
        a.Pick(0, 1);
        a.Dismiss(0);
        Assert.Equal(WheelPhase.Hidden, a.Phase);

        a.Show(0);
        a.Pick(1, 0);
        a.Dismiss(1.05); // already collapsing: ignored
        Assert.Equal(WheelPhase.Collapsing, a.Phase);

        a.Hide();
        Assert.Equal(WheelPhase.Hidden, a.Phase);
        Assert.False(a.IsAnimating(5));
    }
}
