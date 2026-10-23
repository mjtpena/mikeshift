using MikeShift.Core.Interaction;

namespace MikeShift.Core.Tests.Interaction;

public class ModifierTrackerTests
{
    [Fact]
    public void KeyDownIsFreshOnceAndAutoRepeatIsIgnored()
    {
        var t = new ModifierTracker();
        Assert.Equal(Modifiers.Shift, t.OnKey(VirtualKeys.LShift, true));
        Assert.True(t.Shift);
        for (var i = 0; i < 5; i++)
            Assert.Equal(Modifiers.None, t.OnKey(VirtualKeys.LShift, true)); // auto-repeat
        Assert.Equal(Modifiers.None, t.OnKey(VirtualKeys.LShift, false));
        Assert.False(t.Shift);
        Assert.Equal(Modifiers.Shift, t.OnKey(VirtualKeys.LShift, true)); // up then down is fresh again
    }

    [Theory]
    [InlineData(VirtualKeys.Shift, Modifiers.Shift)]
    [InlineData(VirtualKeys.LShift, Modifiers.Shift)]
    [InlineData(VirtualKeys.RShift, Modifiers.Shift)]
    [InlineData(VirtualKeys.Control, Modifiers.Ctrl)]
    [InlineData(VirtualKeys.LControl, Modifiers.Ctrl)]
    [InlineData(VirtualKeys.RControl, Modifiers.Ctrl)]
    [InlineData(VirtualKeys.Menu, Modifiers.Alt)]
    [InlineData(VirtualKeys.LMenu, Modifiers.Alt)]
    [InlineData(VirtualKeys.RMenu, Modifiers.Alt)]
    public void EveryVariantMapsToItsModifier(int vk, Modifiers expected)
    {
        var t = new ModifierTracker();
        Assert.Equal(expected, t.OnKey(vk, true));
        Assert.Equal(expected, t.Current);
        t.OnKey(vk, false);
        Assert.Equal(Modifiers.None, t.Current);
    }

    [Fact]
    public void SecondPhysicalKeyOfAHeldModifierIsNotFreshAndBothMustBeReleased()
    {
        var t = new ModifierTracker();
        t.OnKey(VirtualKeys.LShift, true);
        Assert.Equal(Modifiers.None, t.OnKey(VirtualKeys.RShift, true));
        t.OnKey(VirtualKeys.LShift, false);
        Assert.True(t.Shift);
        t.OnKey(VirtualKeys.RShift, false);
        Assert.False(t.Shift);
    }

    [Fact]
    public void NonModifierKeysAreIgnored()
    {
        var t = new ModifierTracker();
        Assert.Equal(Modifiers.None, t.OnKey(0x41, true));
        Assert.Equal(Modifiers.None, t.Current);
    }

    [Fact]
    public void OnEventHandlesKeyEventsOnly()
    {
        var t = new ModifierTracker();
        Assert.Equal(Modifiers.None, t.OnEvent(InputEvent.MouseMove(1, 1)));
        Assert.Equal(Modifiers.Ctrl, t.OnEvent(InputEvent.KeyDown(VirtualKeys.LControl)));
        Assert.True(t.Ctrl);
        t.OnEvent(InputEvent.KeyUp(VirtualKeys.LControl));
        Assert.False(t.Ctrl);
    }

    [Fact]
    public void ModifiersCombineIndependently()
    {
        var t = new ModifierTracker();
        t.OnKey(VirtualKeys.LShift, true);
        t.OnKey(VirtualKeys.LControl, true);
        Assert.Equal(Modifiers.Shift | Modifiers.Ctrl, t.Current);
        Assert.False(t.Alt);
    }

    [Fact]
    public void FirstReconcileCatchesUpWithoutFreshPress()
    {
        var t = new ModifierTracker();
        var fresh = t.Reconcile(KeyStateFlags.MK_SHIFT | KeyStateFlags.MK_CONTROL);
        Assert.Equal(Modifiers.None, fresh);
        Assert.Equal(Modifiers.Shift | Modifiers.Ctrl, t.Current);
    }

    [Fact]
    public void ReconcileReportsUpToDownAsFreshOnlyWhenPreviousStateWasKnownUp()
    {
        var t = new ModifierTracker();
        t.Reconcile(0);                                               // known: all up
        Assert.Equal(Modifiers.Ctrl, t.Reconcile(KeyStateFlags.MK_CONTROL)); // hook missed Ctrl going down
        Assert.Equal(Modifiers.None, t.Reconcile(KeyStateFlags.MK_CONTROL)); // still down: nothing new
        Assert.Equal(Modifiers.None, t.Reconcile(0));                 // released
        Assert.False(t.Ctrl);
        Assert.Equal(Modifiers.Ctrl, t.Reconcile(KeyStateFlags.MK_CONTROL)); // pressed again
    }

    [Fact]
    public void ReconcileAfterHookSawKeyUpTreatsDownAsFresh()
    {
        var t = new ModifierTracker();
        t.OnKey(VirtualKeys.LShift, true);
        t.OnKey(VirtualKeys.LShift, false); // known up
        Assert.Equal(Modifiers.Shift, t.Reconcile(KeyStateFlags.MK_SHIFT));
    }

    [Fact]
    public void ReconcileMatchingAHookObservedPressIsNotFresh()
    {
        var t = new ModifierTracker();
        t.OnKey(VirtualKeys.LShift, true);
        Assert.Equal(Modifiers.None, t.Reconcile(KeyStateFlags.MK_SHIFT));
    }

    [Fact]
    public void HookEventAfterReconcileCaughtUpTheSamePressIsNotAFreshPressTwice()
    {
        var t = new ModifierTracker();
        t.Reconcile(0);
        Assert.Equal(Modifiers.Shift, t.Reconcile(KeyStateFlags.MK_SHIFT)); // DragOver arrived first
        Assert.Equal(Modifiers.None, t.OnKey(VirtualKeys.LShift, true));    // the hook event arrives late
        Assert.True(t.Shift);
        t.OnKey(VirtualKeys.LShift, false);
        Assert.False(t.Shift);
    }

    [Fact]
    public void ReconcileClearsModifiersThatAreNoLongerDown()
    {
        var t = new ModifierTracker();
        t.OnKey(VirtualKeys.LShift, true);
        t.Reconcile(0); // the key-up happened in an elevated window the hook cannot see
        Assert.False(t.Shift);
        Assert.Equal(Modifiers.Shift, t.OnKey(VirtualKeys.LShift, true));
    }

    [Fact]
    public void CatchUpDownFromReconcileIsClearedByARealKeyUp()
    {
        var t = new ModifierTracker();
        t.Reconcile(KeyStateFlags.MK_ALT);
        Assert.True(t.Alt);
        t.OnKey(VirtualKeys.LMenu, false);
        Assert.False(t.Alt);
    }

    [Fact]
    public void MaskDecoding()
    {
        Assert.Equal(Modifiers.None, KeyStateFlags.ToModifiers(KeyStateFlags.MK_LBUTTON | KeyStateFlags.MK_RBUTTON));
        Assert.Equal(Modifiers.Shift | Modifiers.Ctrl | Modifiers.Alt,
            KeyStateFlags.ToModifiers(KeyStateFlags.MK_SHIFT | KeyStateFlags.MK_CONTROL | KeyStateFlags.MK_ALT | KeyStateFlags.MK_LBUTTON));
        Assert.Equal(0x4u, KeyStateFlags.MK_SHIFT);
        Assert.Equal(0x8u, KeyStateFlags.MK_CONTROL);
        Assert.Equal(0x20u, KeyStateFlags.MK_ALT);
    }

    [Fact]
    public void ResetForgetsEverything()
    {
        var t = new ModifierTracker();
        t.OnKey(VirtualKeys.LShift, true);
        t.Reset();
        Assert.Equal(Modifiers.None, t.Current);
        Assert.Equal(Modifiers.None, t.Reconcile(KeyStateFlags.MK_SHIFT)); // unknown again, so catching up
    }
}
