using MikeShift.Core.Actions;
using MikeShift.Core.Interaction;

namespace MikeShift.Core.Tests.Interaction;

/// <summary>Drives a <see cref="DragSession"/> with a fake clock and records its commands.</summary>
internal sealed class SessionHarness
{
    public long Now;
    public readonly List<DragCommand> Commands = [];
    public readonly DragSession Session;

    public SessionHarness(DragSessionOptions? options = null)
    {
        Session = new DragSession(options ?? new DragSessionOptions { DragThresholdX = 4, DragThresholdY = 4 }, () => Now);
        Session.Command += Commands.Add;
    }

    public DragState State => Session.State;
    public IEnumerable<DragCommandKind> Kinds => Commands.Select(c => c.Kind);

    public void Advance(long ms) { Now += ms; Session.Tick(); }
    public void Down(int x = 100, int y = 100) => Session.Process(InputEvent.ButtonDown(x, y));
    public void Move(int x, int y) => Session.Process(InputEvent.MouseMove(x, y));
    public void Up(int x = 100, int y = 100) => Session.Process(InputEvent.ButtonUp(x, y));
    public void Key(int vk, bool down = true) => Session.Process(down ? InputEvent.KeyDown(vk) : InputEvent.KeyUp(vk));
    public void Wheel(int delta) => Session.Process(InputEvent.Wheel(delta));
    public void Clear() => Commands.Clear();

    /// <summary>Press at (100,100), drag well past the threshold, with Shift pressed after the threshold: ends in Armed.</summary>
    public void ArmWithShiftAfter()
    {
        Down();
        Move(120, 100);
        Key(VirtualKeys.LShift);
    }
}

public class DragSessionTests
{
    // ------------------------------------------------------------------ threshold

    [Fact]
    public void StartsIdle()
    {
        var h = new SessionHarness();
        Assert.Equal(DragState.Idle, h.State);
        Assert.False(h.Session.IsActive);
        Assert.Null(h.Session.NextDeadlineMs);
    }

    [Fact]
    public void ButtonDownEntersPressed()
    {
        var h = new SessionHarness();
        h.Down(10, 20);
        Assert.Equal(DragState.Pressed, h.State);
        Assert.Equal(new PointPx(10, 20), h.Session.PressPoint);
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(-3, 0)]
    [InlineData(0, 3)]
    [InlineData(0, -3)]
    [InlineData(3, 3)]
    [InlineData(-3, -3)]
    public void MovementBelowThresholdStaysPressed(int dx, int dy)
    {
        var h = new SessionHarness();
        h.Down(100, 100);
        h.Move(100 + dx, 100 + dy);
        Assert.Equal(DragState.Pressed, h.State);
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(-4, 0)]
    [InlineData(0, 4)]
    [InlineData(0, -4)]
    [InlineData(20, 1)]
    public void MovementAtOrBeyondThresholdStartsDragging(int dx, int dy)
    {
        var h = new SessionHarness();
        h.Down(100, 100);
        h.Move(100 + dx, 100 + dy);
        Assert.Equal(DragState.Dragging, h.State);
        Assert.Empty(h.Commands); // no Shift: nothing to show
    }

    [Fact]
    public void ThresholdUsesTheSuppliedSystemMetrics()
    {
        var h = new SessionHarness(new DragSessionOptions { DragThresholdX = 10, DragThresholdY = 6 });
        h.Down(0, 0);
        h.Move(9, 5);
        Assert.Equal(DragState.Pressed, h.State);
        h.Move(0, 6);
        Assert.Equal(DragState.Dragging, h.State);
    }

    [Fact]
    public void ThresholdIsMeasuredFromThePressPointNotThePreviousMove()
    {
        var h = new SessionHarness();
        h.Down(100, 100);
        h.Move(102, 100);
        h.Move(103, 100);
        Assert.Equal(DragState.Pressed, h.State); // slow creep still below 4 from the origin
        h.Move(104, 100);
        Assert.Equal(DragState.Dragging, h.State);
    }

    [Fact]
    public void ButtonUpWithoutDraggingEndsQuietly()
    {
        var h = new SessionHarness();
        h.Down();
        h.Up();
        Assert.Equal(DragState.Idle, h.State);
        Assert.Equal([DragCommandKind.End], h.Kinds);
    }

    [Fact]
    public void MovesWithoutAPressAreIgnored()
    {
        var h = new SessionHarness();
        h.Move(0, 0);
        h.Move(500, 500);
        h.Key(VirtualKeys.LShift);
        Assert.Equal(DragState.Idle, h.State);
        Assert.Empty(h.Commands);
    }

    // ------------------------------------------------------------------ shift before / after

    [Fact]
    public void ShiftPressedAfterThresholdArmsAndShowsPendingAtTheCursor()
    {
        var h = new SessionHarness();
        h.Down(100, 100);
        h.Move(140, 120);
        Assert.Empty(h.Commands);
        h.Key(VirtualKeys.LShift);
        Assert.Equal(DragState.Armed, h.State);
        var cmd = Assert.Single(h.Commands);
        Assert.Equal(DragCommandKind.ShowPending, cmd.Kind);
        Assert.Equal(new PointPx(140, 120), cmd.Point);
        Assert.Equal(WheelPage.Format, cmd.Page);
    }

    [Fact]
    public void ShiftHeldBeforeTheClickArmsOnceThresholdIsCrossed()
    {
        var h = new SessionHarness();
        h.Key(VirtualKeys.LShift); // Shift first (Explorer range-select)
        h.Down(100, 100);
        Assert.True(h.Session.ShiftAtMouseDown);
        Assert.Equal(DragState.Pressed, h.State);
        h.Move(102, 100);
        Assert.Equal(DragState.Pressed, h.State);
        h.Move(110, 100);
        Assert.Equal(DragState.Armed, h.State);
        Assert.Equal(DragCommandKind.ShowPending, Assert.Single(h.Commands).Kind);
    }

    [Fact]
    public void ShiftHeldBeforeTheClickIsLearntFromTheKeyStateProbe()
    {
        var opts = new DragSessionOptions { QueryKeyState = () => KeyStateFlags.MK_SHIFT };
        var h = new SessionHarness(opts); // the hook never saw Shift go down
        h.Down(0, 0);
        Assert.True(h.Session.ShiftAtMouseDown);
        h.Move(50, 0);
        Assert.Equal(DragState.Armed, h.State);
    }

    [Fact]
    public void ShiftPressedBetweenDownAndThresholdArmsAtTheCrossing()
    {
        var h = new SessionHarness();
        h.Down();
        h.Key(VirtualKeys.LShift);
        Assert.False(h.Session.ShiftAtMouseDown);
        Assert.Equal(DragState.Pressed, h.State);
        h.Move(150, 100);
        Assert.Equal(DragState.Armed, h.State);
    }

    [Fact]
    public void ShiftAtMouseDownIsFalseWithoutShift()
    {
        var h = new SessionHarness();
        h.Down();
        Assert.False(h.Session.ShiftAtMouseDown);
    }

    [Fact]
    public void ShiftReleasedBeforeTheCrossingDoesNotArm()
    {
        var h = new SessionHarness();
        h.Key(VirtualKeys.LShift);
        h.Down();
        h.Key(VirtualKeys.LShift, false);
        h.Move(200, 200);
        Assert.Equal(DragState.Dragging, h.State);
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void ReleasingShiftWhileArmedKeepsTheWheel()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Key(VirtualKeys.LShift, false);
        Assert.Equal(DragState.Armed, h.State);
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void RightShiftAlsoArms()
    {
        var h = new SessionHarness();
        h.Down();
        h.Move(150, 100);
        h.Key(VirtualKeys.RShift);
        Assert.Equal(DragState.Armed, h.State);
    }

    [Fact]
    public void ShiftAutoRepeatDoesNotReArmOrToggle()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        for (var i = 0; i < 4; i++) h.Key(VirtualKeys.LShift);
        Assert.Empty(h.Commands);
    }

    // ------------------------------------------------------------------ injected

    [Fact]
    public void InjectedButtonDownIsIgnored()
    {
        var h = new SessionHarness();
        h.Session.Process(InputEvent.ButtonDown(1, 1, injected: true));
        Assert.Equal(DragState.Idle, h.State);
    }

    [Fact]
    public void InjectedMoveDoesNotCrossTheThreshold()
    {
        var h = new SessionHarness();
        h.Down();
        h.Session.Process(InputEvent.MouseMove(400, 400, injected: true));
        Assert.Equal(DragState.Pressed, h.State);
    }

    [Fact]
    public void InjectedShiftDoesNotArm()
    {
        var h = new SessionHarness();
        h.Down();
        h.Move(150, 100);
        h.Session.Process(InputEvent.KeyDown(VirtualKeys.LShift, injected: true));
        Assert.Equal(DragState.Dragging, h.State);
        Assert.False(h.Session.Tracker.Shift);
    }

    [Fact]
    public void InjectedButtonUpDoesNotEndTheSession()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Session.Process(InputEvent.ButtonUp(0, 0, injected: true));
        Assert.Equal(DragState.Armed, h.State);
    }

    [Fact]
    public void OurOwnNudgeMoveDoesNotDisturbAnArmedSession()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Session.Process(InputEvent.MouseMove(120, 100, injected: true));
        Assert.Empty(h.Commands);
        Assert.Equal(DragState.Armed, h.State);
    }

    [Fact]
    public void InjectedWheelDoesNotTogglePages()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Session.Process(InputEvent.Wheel(120, injected: true));
        Assert.Empty(h.Commands);
    }

    // ------------------------------------------------------------------ 250 ms no DragEnter

    [Fact]
    public void NoDragEnterWithin250MsHidesSilently()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Advance(249);
        Assert.Empty(h.Commands);
        h.Advance(1);
        Assert.Equal([DragCommandKind.HideSilently], h.Kinds);
        Assert.Equal(DragState.Dragging, h.State);
    }

    [Fact]
    public void DragEnterCancelsTheSilentHide()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Advance(100);
        h.Session.DragEnterArrived();
        h.Advance(1000);
        Assert.Empty(h.Commands);
        Assert.Equal(DragState.Armed, h.State);
        Assert.True(h.Session.DragEntered);
    }

    [Fact]
    public void NextDeadlineTracksThePendingTimeout()
    {
        var h = new SessionHarness();
        h.Now = 1000;
        h.ArmWithShiftAfter();
        Assert.Equal(1250, h.Session.NextDeadlineMs);
        h.Session.DragEnterArrived();
        Assert.Null(h.Session.NextDeadlineMs);
    }

    [Fact]
    public void AfterASilentHideTheSamePressDoesNotReArmEvenIfShiftStaysDown()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Advance(250);
        h.Clear();
        h.Move(300, 300);
        h.Move(310, 310);
        h.Key(VirtualKeys.LShift); // auto-repeat
        Assert.Empty(h.Commands);
        Assert.Equal(DragState.Dragging, h.State);
    }

    [Fact]
    public void AFreshShiftPressAfterASilentHideArmsAgain()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Advance(250);
        h.Clear();
        h.Key(VirtualKeys.LShift, false);
        h.Key(VirtualKeys.LShift);
        Assert.Equal(DragState.Armed, h.State);
        Assert.Equal(DragCommandKind.ShowPending, Assert.Single(h.Commands).Kind);
        h.Advance(250);
        Assert.Equal(DragCommandKind.HideSilently, h.Commands[^1].Kind); // and the timer starts over
    }

    [Fact]
    public void RangeSelectionWithShiftHeldHidesSilentlyBecauseNoDragEnterArrives()
    {
        var h = new SessionHarness();
        h.Key(VirtualKeys.LShift);
        h.Down(100, 100);
        h.Move(200, 200);
        Assert.Equal(DragState.Armed, h.State);
        h.Advance(250);
        Assert.Equal([DragCommandKind.ShowPending, DragCommandKind.HideSilently], h.Kinds);
        h.Up();
        Assert.Equal(DragState.Idle, h.State);
        Assert.Equal(DragCommandKind.End, h.Commands[^1].Kind);
    }

    [Fact]
    public void DragEnterOutsideAnArmedSessionIsIgnored()
    {
        var h = new SessionHarness();
        h.Session.DragEnterArrived();
        Assert.False(h.Session.DragEntered);
        h.Down();
        h.Move(150, 100);
        h.Session.DragEnterArrived();
        Assert.False(h.Session.DragEntered);
    }

    // ------------------------------------------------------------------ release / 400 ms dismiss

    [Fact]
    public void ReleaseWhileArmedWaitsThenDismissesAfter400Ms()
    {
        var h = new SessionHarness();
        h.Now = 5000;
        h.ArmWithShiftAfter();
        h.Session.DragEnterArrived();
        h.Clear();
        h.Up();
        Assert.Equal(DragState.Released, h.State);
        Assert.Equal(5400, h.Session.NextDeadlineMs);
        h.Advance(399);
        Assert.Empty(h.Commands);
        Assert.Equal(DragState.Released, h.State);
        h.Advance(1);
        Assert.Equal([DragCommandKind.DismissAfterRelease, DragCommandKind.End], h.Kinds);
        Assert.Equal(DragState.Idle, h.State);
    }

    [Fact]
    public void PickingBeforeTheTimeoutEndsWithoutADismiss()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Session.DragEnterArrived();
        h.Clear();
        h.Up();
        h.Advance(50);
        h.Session.Picked();
        Assert.Equal([DragCommandKind.End], h.Kinds);
        Assert.Equal(DragState.Idle, h.State);
        h.Advance(1000);
        Assert.Equal([DragCommandKind.End], h.Kinds); // no late dismiss
    }

    [Fact]
    public void ReleaseWithoutDragEnterHidesSilentlyAt250MsThenEnds()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Advance(100);
        h.Up();
        Assert.Equal(DragState.Released, h.State);
        h.Advance(149);
        Assert.Empty(h.Commands);
        h.Advance(1);
        Assert.Equal([DragCommandKind.HideSilently, DragCommandKind.End], h.Kinds);
    }

    [Fact]
    public void ReleaseWhenDragWasNeverArmedJustEnds()
    {
        var h = new SessionHarness();
        h.Down();
        h.Move(200, 200);
        h.Up();
        Assert.Equal([DragCommandKind.End], h.Kinds);
        Assert.Equal(DragState.Idle, h.State);
    }

    [Fact]
    public void ANewPressWhileWaitingToDismissDismissesTheOldWheelFirst()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Session.DragEnterArrived();
        h.Up();
        h.Clear();
        h.Down(5, 5);
        Assert.Equal([DragCommandKind.DismissAfterRelease, DragCommandKind.End], h.Kinds);
        Assert.Equal(DragState.Pressed, h.State);
    }

    [Fact]
    public void ButtonDownWithoutAnInterveningUpEndsTheOldSession()
    {
        var h = new SessionHarness();
        h.Down();
        h.Move(200, 200);
        h.Clear();
        h.Down(7, 7);
        Assert.Equal([DragCommandKind.End], h.Kinds);
        Assert.Equal(DragState.Pressed, h.State);
        Assert.Equal(new PointPx(7, 7), h.Session.PressPoint);
    }

    [Fact]
    public void PickedWhileStillArmedEndsTheSession()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Session.Picked();
        Assert.Equal([DragCommandKind.End], h.Kinds);
    }

    [Fact]
    public void PickedWhenIdleDoesNothing()
    {
        var h = new SessionHarness();
        h.Session.Picked();
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void EscapeWhileArmedDismissesImmediately()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Key(VirtualKeys.Escape);
        Assert.Equal([DragCommandKind.Dismiss, DragCommandKind.End], h.Kinds);
        Assert.Equal(DragState.Idle, h.State);
        h.Up(); // the source's cancelled drag still ends with a button-up
        Assert.Equal(2, h.Commands.Count);
    }

    [Fact]
    public void AbortEndsAnyActiveSession()
    {
        var h = new SessionHarness();
        h.Down();
        h.Session.Abort();
        Assert.Equal(DragState.Idle, h.State);
        Assert.Equal([DragCommandKind.End], h.Kinds);
        h.Session.Abort();
        Assert.Single(h.Commands);
    }

    [Fact]
    public void SessionsAreRepeatable()
    {
        var h = new SessionHarness();
        for (var round = 0; round < 3; round++)
        {
            h.Clear();
            h.ArmWithShiftAfter();
            h.Session.DragEnterArrived();
            h.Up();
            h.Advance(400);
            Assert.Equal(
                [DragCommandKind.ShowPending, DragCommandKind.DismissAfterRelease, DragCommandKind.End],
                h.Kinds);
            h.Key(VirtualKeys.LShift, false);
        }
    }

    // ------------------------------------------------------------------ missed button-up

    [Fact]
    public void ButtonReadingUpFor100MsAtRestEndsThePress()
    {
        var h = new SessionHarness();
        h.Now = 1000;
        h.Down();
        h.Move(200, 200);
        h.Now = 2000; // pointer at rest for a long time
        h.Session.PollButtonState(false);
        Assert.Equal(DragState.Dragging, h.State);
        h.Now = 2100;
        h.Session.PollButtonState(false);
        Assert.Equal(DragState.Dragging, h.State); // exactly 100 ms: not yet
        h.Now = 2101;
        h.Session.PollButtonState(false);
        Assert.Equal(DragState.Idle, h.State);
        Assert.Equal(DragCommandKind.End, h.Commands[^1].Kind);
    }

    [Fact]
    public void ButtonReadingDownResetsTheUpTimer()
    {
        var h = new SessionHarness();
        h.Now = 1000;
        h.Down();
        h.Move(200, 200);
        h.Now = 2000;
        h.Session.PollButtonState(false);
        h.Now = 2080;
        h.Session.PollButtonState(true);
        h.Now = 2150;
        h.Session.PollButtonState(false);
        h.Now = 2240;
        h.Session.PollButtonState(false);
        Assert.Equal(DragState.Dragging, h.State); // only 90 ms of continuous "up"
        h.Now = 2251;
        h.Session.PollButtonState(false);
        Assert.Equal(DragState.Idle, h.State);
    }

    [Fact]
    public void UpReadingsWhileThePointerIsStillMovingAreNotTrusted()
    {
        var h = new SessionHarness();
        h.Now = 1000;
        h.Down();
        h.Move(200, 200);
        for (var i = 0; i < 20; i++)
        {
            h.Now += 20;
            h.Move(200 + i, 200); // a real drag in progress: GetAsyncKeyState glitch must not end it
            h.Session.PollButtonState(false);
        }
        Assert.Equal(DragState.Dragging, h.State);
    }

    [Fact]
    public void MissedButtonUpWhileArmedFallsIntoTheNormalReleaseFlow()
    {
        var h = new SessionHarness();
        h.Now = 1000;
        h.ArmWithShiftAfter();
        h.Session.DragEnterArrived();
        h.Now = 3000;
        h.Session.PollButtonState(false);
        h.Now = 3101;
        h.Session.PollButtonState(false);
        Assert.Equal(DragState.Released, h.State);
        h.Advance(400);
        Assert.Equal(DragCommandKind.DismissAfterRelease, h.Commands[^2].Kind);
        Assert.Equal(DragState.Idle, h.State);
    }

    [Fact]
    public void PollingWhileIdleOrReleasedIsHarmless()
    {
        var h = new SessionHarness();
        h.Session.PollButtonState(false);
        h.Now += 1000;
        h.Session.PollButtonState(false);
        Assert.Empty(h.Commands);
    }

    // ------------------------------------------------------------------ page toggles

    [Fact]
    public void FreshCtrlSwitchesToTools()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Key(VirtualKeys.LControl);
        var cmd = Assert.Single(h.Commands);
        Assert.Equal(new DragCommand(DragCommandKind.PageToggle, new PointPx(120, 100), WheelPage.Tool), cmd);
        Assert.Equal(WheelPage.Tool, h.Session.Page);
    }

    [Fact]
    public void CtrlAutoRepeatAndRepeatedCtrlOnToolsDoNothing()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Key(VirtualKeys.LControl);
        h.Clear();
        h.Key(VirtualKeys.LControl);
        h.Key(VirtualKeys.LControl);
        h.Key(VirtualKeys.LControl, false);
        h.Key(VirtualKeys.LControl); // fresh again, but already on Tools
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void FreshShiftReturnsToFormats()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Key(VirtualKeys.LControl);
        h.Clear();
        h.Key(VirtualKeys.LShift, false);
        h.Key(VirtualKeys.LShift);
        var cmd = Assert.Single(h.Commands);
        Assert.Equal(DragCommandKind.PageToggle, cmd.Kind);
        Assert.Equal(WheelPage.Format, cmd.Page);
    }

    [Fact]
    public void CtrlBeforeArmingDoesNotSwitchPageButHeldCtrlIsNotFresh()
    {
        var h = new SessionHarness();
        h.Down();
        h.Key(VirtualKeys.LControl); // before Shift: drag semantics for Explorer, not ours
        h.Move(150, 100);
        h.Key(VirtualKeys.LShift);
        Assert.Equal([DragCommandKind.ShowPending], h.Kinds);
        Assert.Equal(WheelPage.Format, h.Session.Page);
    }

    [Fact]
    public void WheelTickTogglesInBothDirections()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Wheel(120);
        Assert.Equal(WheelPage.Tool, Assert.Single(h.Commands).Page);
        h.Wheel(-120);
        Assert.Equal(WheelPage.Format, h.Commands[^1].Page);
        h.Wheel(-120);
        Assert.Equal(WheelPage.Tool, h.Commands[^1].Page);
    }

    [Fact]
    public void HighResolutionWheelDeltasAccumulateToOneNotch()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Wheel(30);
        h.Wheel(30);
        h.Wheel(30);
        Assert.Empty(h.Commands);
        h.Wheel(30);
        Assert.Single(h.Commands);
        h.Wheel(60);
        Assert.Single(h.Commands); // accumulator restarted
    }

    [Fact]
    public void ReversingWheelDirectionDiscardsTheAccumulator()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Wheel(100);
        h.Wheel(-100);
        h.Wheel(-100);
        Assert.Single(h.Commands); // 100 up was dropped on reversal; -200 crossed the notch
    }

    [Fact]
    public void WheelIsIgnoredUnlessArmed()
    {
        var h = new SessionHarness();
        h.Wheel(120);
        h.Down();
        h.Wheel(120);
        h.Move(150, 100);
        h.Wheel(120);
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void MouseWheelOnlyIgnoresCtrlButWheelStillToggles()
    {
        var h = new SessionHarness(new DragSessionOptions { ToolsKey = ToolsKey.MouseWheelOnly });
        h.ArmWithShiftAfter();
        h.Clear();
        h.Key(VirtualKeys.LControl);
        h.Key(VirtualKeys.LMenu);
        Assert.Empty(h.Commands);
        h.Wheel(120);
        Assert.Equal(WheelPage.Tool, Assert.Single(h.Commands).Page);
    }

    [Fact]
    public void AltModeUsesAltNotCtrl()
    {
        var h = new SessionHarness(new DragSessionOptions { ToolsKey = ToolsKey.Alt });
        h.ArmWithShiftAfter();
        h.Clear();
        h.Key(VirtualKeys.LControl);
        Assert.Empty(h.Commands);
        h.Key(VirtualKeys.LMenu);
        Assert.Equal(WheelPage.Tool, Assert.Single(h.Commands).Page);
    }

    [Fact]
    public void ToolsKeyCanBeChangedAtRuntime()
    {
        var h = new SessionHarness();
        h.Session.ToolsKey = ToolsKey.Alt;
        h.ArmWithShiftAfter();
        h.Clear();
        h.Key(VirtualKeys.LControl);
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void PageToggleBeforeDragEnterIsForwarded()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Advance(50);
        h.Key(VirtualKeys.LControl);
        Assert.Single(h.Commands);
    }

    [Fact]
    public void ArmingAlwaysStartsOnFormats()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Key(VirtualKeys.LControl);
        h.Up();
        h.Advance(400);
        h.Key(VirtualKeys.LControl, false);
        h.Key(VirtualKeys.LShift, false);
        h.Clear();
        h.ArmWithShiftAfter();
        Assert.Equal(WheelPage.Format, h.Commands[0].Page);
        Assert.Equal(WheelPage.Format, h.Session.Page);
    }

    [Fact]
    public void SetPageSyncsTheSessionWithTheModelWithoutACommand()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Session.SetPage(WheelPage.Tool);
        Assert.Empty(h.Commands);
        h.Key(VirtualKeys.LControl); // already on Tool: no toggle
        Assert.Empty(h.Commands);
    }

    // ------------------------------------------------------------------ grfKeyState reconciliation through the session

    [Fact]
    public void ReconcileCatchUpDoesNotToggleThePage()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Session.ReconcileModifiers(KeyStateFlags.MK_CONTROL); // Ctrl was already held: just catching up
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void ReconcileSeesACtrlPressTheHookMissed()
    {
        var h = new SessionHarness();
        h.ArmWithShiftAfter();
        h.Session.ReconcileModifiers(KeyStateFlags.MK_SHIFT); // DragEnter: Shift held, Ctrl up
        h.Clear();
        h.Session.ReconcileModifiers(KeyStateFlags.MK_SHIFT | KeyStateFlags.MK_CONTROL); // DragOver: Ctrl went down in an elevated window
        Assert.Equal(WheelPage.Tool, Assert.Single(h.Commands).Page);
        h.Session.ReconcileModifiers(KeyStateFlags.MK_SHIFT | KeyStateFlags.MK_CONTROL);
        Assert.Single(h.Commands);
    }

    [Fact]
    public void ReconcileCanArmOnAShiftPressTheHookMissed()
    {
        var h = new SessionHarness();
        h.Down();
        h.Move(150, 100);
        h.Session.ReconcileModifiers(0); // known: Shift up
        h.Session.ReconcileModifiers(KeyStateFlags.MK_SHIFT);
        Assert.Equal(DragState.Armed, h.State);
        Assert.Equal(DragCommandKind.ShowPending, Assert.Single(h.Commands).Kind);
    }

    [Fact]
    public void ReconcileWhileIdleJustUpdatesTheTracker()
    {
        var h = new SessionHarness();
        h.Session.ReconcileModifiers(KeyStateFlags.MK_SHIFT);
        Assert.True(h.Session.Tracker.Shift);
        Assert.Empty(h.Commands);
    }

    // ------------------------------------------------------------------ the 20 s Shift-click exclusion

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] Outputs = [@"C:\w\a.png", @"C:\w\b.png"];

    [Fact]
    public void ShiftClickDropsPreviousOutputsWhenAFreshFileRemains()
    {
        string[] sel = [@"C:\w\a.png", @"C:\w\b.png", @"C:\w\new.jpg"];
        var result = DragSession.FilterSelection(sel, true, Outputs, T0, T0.AddSeconds(5));
        Assert.Equal([@"C:\w\new.jpg"], result);
    }

    [Fact]
    public void ShiftClickKeepsEverythingWhenNoFreshFileRemains()
    {
        string[] sel = [@"C:\w\a.png", @"C:\w\b.png"];
        var result = DragSession.FilterSelection(sel, true, Outputs, T0, T0.AddSeconds(5));
        Assert.Equal(sel, result);
    }

    [Fact]
    public void FilterDoesNothingWithoutShiftAtMouseDown()
    {
        string[] sel = [@"C:\w\a.png", @"C:\w\new.jpg"];
        Assert.Equal(sel, DragSession.FilterSelection(sel, false, Outputs, T0, T0.AddSeconds(5)));
    }

    [Fact]
    public void FilterDoesNothingForASingleItem()
    {
        string[] sel = [@"C:\w\a.png"];
        Assert.Equal(sel, DragSession.FilterSelection(sel, true, Outputs, T0, T0.AddSeconds(5)));
    }

    [Theory]
    [InlineData(19.999, true)]
    [InlineData(20.0, false)]
    [InlineData(45.0, false)]
    public void FilterOnlyAppliesWithinTwentySecondsOfTheLastConversion(double secondsAgo, bool filtered)
    {
        string[] sel = [@"C:\w\a.png", @"C:\w\new.jpg"];
        var result = DragSession.FilterSelection(sel, true, Outputs, T0, T0.AddSeconds(secondsAgo));
        Assert.Equal(filtered ? [@"C:\w\new.jpg"] : sel, result);
    }

    [Fact]
    public void FilterIgnoresAClockThatWentBackwards()
    {
        string[] sel = [@"C:\w\a.png", @"C:\w\new.jpg"];
        Assert.Equal(sel, DragSession.FilterSelection(sel, true, Outputs, T0, T0.AddSeconds(-3)));
    }

    [Fact]
    public void FilterDoesNothingWithoutAPreviousConversion()
    {
        string[] sel = [@"C:\w\a.png", @"C:\w\new.jpg"];
        Assert.Equal(sel, DragSession.FilterSelection(sel, true, null, null, T0));
        Assert.Equal(sel, DragSession.FilterSelection(sel, true, [], T0, T0));
        Assert.Equal(sel, DragSession.FilterSelection(sel, true, Outputs, null, T0));
    }

    [Fact]
    public void FilterComparesPathsCaseInsensitivelyAndIgnoresTrailingSeparators()
    {
        string[] outputs = [@"C:\W\A.PNG", @"C:\w\folder\"];
        string[] sel = [@"c:\w\a.png", @"C:\w\folder", @"C:\w\new.jpg"];
        var result = DragSession.FilterSelection(sel, true, outputs, T0, T0.AddSeconds(1));
        Assert.Equal([@"C:\w\new.jpg"], result);
    }

    [Fact]
    public void FilterKeepsOrderOfTheFreshFiles()
    {
        string[] sel = [@"C:\w\z.txt", @"C:\w\a.png", @"C:\w\m.txt"];
        var result = DragSession.FilterSelection(sel, true, Outputs, T0, T0.AddSeconds(1));
        Assert.Equal([@"C:\w\z.txt", @"C:\w\m.txt"], result);
    }

    [Fact]
    public void FilterDoesNotMutateItsInputs()
    {
        var sel = new List<string> { @"C:\w\a.png", @"C:\w\new.jpg" };
        var outputs = new List<string>(Outputs);
        _ = DragSession.FilterSelection(sel, true, outputs, T0, T0.AddSeconds(1));
        Assert.Equal(2, sel.Count);
        Assert.Equal(2, outputs.Count);
    }

    [Fact]
    public void ShiftAtMouseDownFeedsTheFilterEndToEnd()
    {
        var h = new SessionHarness();
        h.Key(VirtualKeys.LShift);
        h.Down();
        string[] sel = [@"C:\w\a.png", @"C:\w\new.jpg"];
        var result = DragSession.FilterSelection(sel, h.Session.ShiftAtMouseDown, Outputs, T0, T0.AddSeconds(2));
        Assert.Equal([@"C:\w\new.jpg"], result);
    }
}
