using MikeShift.Core.Actions;
using MikeShift.Core.Interaction;

namespace MikeShift.Core.Tests.Interaction;

/// <summary>
/// The first-run activation choice (positioning P1-2): Shift (default) or Ctrl opens the wheel while dragging; the other of the two switches to
/// Tools and the arming key goes back to Formats. Both modes keep the fresh-press rules of <see cref="ModifierTracker"/>.
/// </summary>
public class ArmKeyTests
{
    private static SessionHarness Ctrl(ToolsKey tools = ToolsKey.Ctrl) =>
        new(new DragSessionOptions { DragThresholdX = 4, DragThresholdY = 4, ArmKey = ArmKey.Ctrl, ToolsKey = tools });

    private static SessionHarness Shift(ToolsKey tools = ToolsKey.Ctrl) =>
        new(new DragSessionOptions { DragThresholdX = 4, DragThresholdY = 4, ArmKey = ArmKey.Shift, ToolsKey = tools });

    /// <summary>Press, drag past the threshold, then press the arm key: ends Armed.</summary>
    private static void ArmWithCtrlAfter(SessionHarness h)
    {
        h.Down();
        h.Move(120, 100);
        h.Key(VirtualKeys.LControl);
    }

    // ------------------------------------------------------------------ arming

    [Fact]
    public void ShiftIsTheDefaultArmKey()
    {
        Assert.Equal(ArmKey.Shift, new DragSessionOptions().ArmKey);
        var h = new SessionHarness();
        Assert.Equal(ArmKey.Shift, h.Session.ArmKey);
    }

    [Fact]
    public void CtrlPressedAfterTheThresholdArmsAndShowsPending()
    {
        var h = Ctrl();
        h.Down(100, 100);
        h.Move(140, 120);
        Assert.Empty(h.Commands);
        h.Key(VirtualKeys.LControl);
        Assert.Equal(DragState.Armed, h.State);
        var cmd = Assert.Single(h.Commands);
        Assert.Equal(DragCommandKind.ShowPending, cmd.Kind);
        Assert.Equal(new PointPx(140, 120), cmd.Point);
        Assert.Equal(WheelPage.Format, cmd.Page);
    }

    [Theory]
    [InlineData(VirtualKeys.Control)]
    [InlineData(VirtualKeys.LControl)]
    [InlineData(VirtualKeys.RControl)]
    public void EveryCtrlKeyArms(int vk)
    {
        var h = Ctrl();
        h.Down();
        h.Move(120, 100);
        h.Key(vk);
        Assert.Equal(DragState.Armed, h.State);
    }

    [Fact]
    public void ShiftDoesNotArmWhenCtrlIsTheArmKey()
    {
        var h = Ctrl();
        h.Down();
        h.Move(120, 100);
        h.Key(VirtualKeys.LShift);
        Assert.Equal(DragState.Dragging, h.State);
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void CtrlDoesNotArmWhenShiftIsTheArmKey()
    {
        var h = Shift();
        h.Down();
        h.Move(120, 100);
        h.Key(VirtualKeys.LControl);
        Assert.Equal(DragState.Dragging, h.State);
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void CtrlHeldBeforeTheClickArmsOnceTheThresholdIsCrossed()
    {
        var h = Ctrl();
        h.Key(VirtualKeys.LControl); // Ctrl first (Explorer's Ctrl-click to add to the selection)
        h.Down(100, 100);
        Assert.True(h.Session.ArmKeyAtMouseDown);
        Assert.False(h.Session.ShiftAtMouseDown);
        Assert.Equal(DragState.Pressed, h.State);
        h.Move(102, 100);
        Assert.Equal(DragState.Pressed, h.State);
        h.Move(110, 100);
        Assert.Equal(DragState.Armed, h.State);
        Assert.Equal(DragCommandKind.ShowPending, Assert.Single(h.Commands).Kind);
    }

    [Fact]
    public void CtrlHeldBeforeTheClickIsLearntFromTheKeyStateProbe()
    {
        var opts = new DragSessionOptions { ArmKey = ArmKey.Ctrl, QueryKeyState = () => KeyStateFlags.MK_CONTROL };
        var h = new SessionHarness(opts); // the hook never saw Ctrl go down
        h.Down(0, 0);
        Assert.True(h.Session.ArmKeyAtMouseDown);
        h.Move(50, 0);
        Assert.Equal(DragState.Armed, h.State);
    }

    [Fact]
    public void ShiftHeldBeforeTheClickDoesNotArmInCtrlMode()
    {
        var h = Ctrl();
        h.Key(VirtualKeys.LShift);
        h.Down();
        Assert.True(h.Session.ShiftAtMouseDown);
        Assert.False(h.Session.ArmKeyAtMouseDown);
        h.Move(150, 100);
        Assert.Equal(DragState.Dragging, h.State);
    }

    [Fact]
    public void ArmKeyAtMouseDownFollowsTheChosenKey()
    {
        var shift = Shift();
        shift.Key(VirtualKeys.LShift);
        shift.Down();
        Assert.True(shift.Session.ArmKeyAtMouseDown);

        var ctrl = Ctrl();
        ctrl.Key(VirtualKeys.LShift);
        ctrl.Down();
        Assert.False(ctrl.Session.ArmKeyAtMouseDown);
    }

    [Fact]
    public void SwitchingTheArmKeyAppliesFromTheNextPress()
    {
        var h = Shift();
        h.Session.ArmKey = ArmKey.Ctrl;
        h.Down();
        h.Move(120, 100);
        h.Key(VirtualKeys.LShift);
        Assert.Equal(DragState.Dragging, h.State);
        h.Key(VirtualKeys.LControl);
        Assert.Equal(DragState.Armed, h.State);
    }

    // ------------------------------------------------------------------ pages

    [Fact]
    public void InCtrlModeShiftSwitchesToToolsAndAFreshCtrlGoesBackToFormats()
    {
        var h = Ctrl();
        ArmWithCtrlAfter(h);
        h.Clear();

        h.Key(VirtualKeys.LShift);
        var tools = Assert.Single(h.Commands);
        Assert.Equal(DragCommandKind.PageToggle, tools.Kind);
        Assert.Equal(WheelPage.Tool, tools.Page);
        Assert.Equal(WheelPage.Tool, h.Session.Page);

        h.Clear();
        h.Key(VirtualKeys.LControl, false);
        h.Key(VirtualKeys.LControl);
        var formats = Assert.Single(h.Commands);
        Assert.Equal(WheelPage.Format, formats.Page);
    }

    [Fact]
    public void InShiftModeCtrlStillSwitchesToToolsAndShiftGoesBack()
    {
        var h = Shift();
        h.ArmWithShiftAfter();
        h.Clear();
        h.Key(VirtualKeys.LControl);
        Assert.Equal(WheelPage.Tool, Assert.Single(h.Commands).Page);
        h.Clear();
        h.Key(VirtualKeys.LShift, false);
        h.Key(VirtualKeys.LShift);
        Assert.Equal(WheelPage.Format, Assert.Single(h.Commands).Page);
    }

    [Fact]
    public void AutoRepeatOfTheToolsKeyDoesNotToggleAgain()
    {
        var h = Ctrl();
        ArmWithCtrlAfter(h);
        h.Key(VirtualKeys.LShift);
        h.Clear();
        h.Key(VirtualKeys.LShift); // key repeat while held
        h.Key(VirtualKeys.LShift);
        Assert.Empty(h.Commands);
        Assert.Equal(WheelPage.Tool, h.Session.Page);
    }

    [Fact]
    public void AHeldArmKeyThatRepeatsDoesNotGoBackToFormatsEither()
    {
        var h = Ctrl();
        ArmWithCtrlAfter(h);       // Ctrl stays down after arming
        h.Key(VirtualKeys.LShift); // Tools
        h.Clear();
        h.Key(VirtualKeys.LControl); // auto-repeat of the still-held Ctrl
        Assert.Empty(h.Commands);
        Assert.Equal(WheelPage.Tool, h.Session.Page);
    }

    [Fact]
    public void ToolsKeyCtrlMeansShiftWhenCtrlArms()
    {
        // ToolsKey.Ctrl is the default setting; with Ctrl arming it must not make Ctrl both arm and tools.
        var h = Ctrl(ToolsKey.Ctrl);
        ArmWithCtrlAfter(h);
        h.Clear();
        h.Key(VirtualKeys.LShift);
        Assert.Equal(WheelPage.Tool, Assert.Single(h.Commands).Page);
        h.Clear();
        h.Key(VirtualKeys.LControl, false);
        h.Key(VirtualKeys.LControl);
        Assert.Equal(WheelPage.Format, Assert.Single(h.Commands).Page); // Ctrl is Formats, never Tools
    }

    [Fact]
    public void ToolsKeyShiftMeansCtrlWhenShiftArms()
    {
        var h = Shift(ToolsKey.Shift);
        h.ArmWithShiftAfter();
        h.Clear();
        h.Key(VirtualKeys.LControl);
        Assert.Equal(WheelPage.Tool, Assert.Single(h.Commands).Page);
    }

    [Fact]
    public void AltCanSwitchToToolsInCtrlMode()
    {
        var h = Ctrl(ToolsKey.Alt);
        ArmWithCtrlAfter(h);
        h.Clear();
        h.Key(VirtualKeys.LShift);
        Assert.Empty(h.Commands); // Shift is not the tools key now
        h.Key(VirtualKeys.LMenu);
        Assert.Equal(WheelPage.Tool, Assert.Single(h.Commands).Page);
    }

    [Fact]
    public void MouseWheelOnlyLeavesNoToolsKeyInEitherMode()
    {
        foreach (var h in new[] { Ctrl(ToolsKey.MouseWheelOnly), Shift(ToolsKey.MouseWheelOnly) })
        {
            if (h.Session.ArmKey == ArmKey.Ctrl) ArmWithCtrlAfter(h); else h.ArmWithShiftAfter();
            h.Clear();
            h.Key(VirtualKeys.LShift);
            h.Key(VirtualKeys.LControl);
            h.Key(VirtualKeys.LMenu);
            Assert.DoesNotContain(h.Commands, c => c.Kind == DragCommandKind.PageToggle && c.Page == WheelPage.Tool);
            h.Wheel(120);
            Assert.Equal(WheelPage.Tool, h.Commands[^1].Page); // a wheel tick still toggles
        }
    }

    // ------------------------------------------------------------------ fresh-press rules

    [Fact]
    public void AfterASilentHideTheSamePressDoesNotReArmEvenIfCtrlStaysDown()
    {
        var h = Ctrl();
        ArmWithCtrlAfter(h);
        h.Advance(250);
        h.Clear();
        h.Move(300, 300);
        h.Key(VirtualKeys.LControl); // auto-repeat
        Assert.Empty(h.Commands);
        Assert.Equal(DragState.Dragging, h.State);
    }

    [Fact]
    public void AFreshCtrlPressAfterASilentHideArmsAgain()
    {
        var h = Ctrl();
        ArmWithCtrlAfter(h);
        h.Advance(250);
        h.Clear();
        h.Key(VirtualKeys.LControl, false);
        h.Key(VirtualKeys.LControl);
        Assert.Equal(DragState.Armed, h.State);
        Assert.Equal(DragCommandKind.ShowPending, Assert.Single(h.Commands).Kind);
    }

    [Fact]
    public void ACtrlPressTheHookMissedIsCaughtUpFromTheOleKeyState()
    {
        // Elevated sources hide key events from the hook; the OLE grfKeyState still says Ctrl is down.
        var h = Ctrl();
        h.Session.ReconcileModifiers(0); // Ctrl observed up
        h.Down();
        h.Move(120, 100);
        Assert.Equal(DragState.Dragging, h.State);
        h.Session.ReconcileModifiers(KeyStateFlags.MK_CONTROL);
        Assert.Equal(DragState.Armed, h.State);
    }

    [Fact]
    public void EscapeDismissesTheCtrlArmedWheel()
    {
        var h = Ctrl();
        ArmWithCtrlAfter(h);
        h.Clear();
        h.Key(VirtualKeys.Escape);
        Assert.Equal([DragCommandKind.Dismiss, DragCommandKind.End], h.Kinds);
        Assert.Equal(DragState.Idle, h.State);
    }

    [Fact]
    public void ReleasingCtrlKeepsTheWheelUpLikeReleasingShiftDoes()
    {
        var h = Ctrl();
        ArmWithCtrlAfter(h);
        h.Clear();
        h.Key(VirtualKeys.LControl, false);
        Assert.Equal(DragState.Armed, h.State);
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void DropFlowIsTheSameInCtrlMode()
    {
        var h = Ctrl();
        ArmWithCtrlAfter(h);
        h.Session.DragEnterArrived();
        h.Up();
        Assert.Equal(DragState.Released, h.State);
        h.Session.Picked();
        Assert.Equal(DragState.Idle, h.State);
    }

    // ------------------------------------------------------------------ the selection filter

    [Fact]
    public void ACtrlClickThatReSelectedOutputsDropsThemJustLikeShiftClick()
    {
        var now = DateTimeOffset.UtcNow;
        string[] picked = [@"C:\p\a.png", @"C:\p\b.jpg"];
        string[] outputs = [@"C:\p\a.png"];
        var armKeyAtDown = true; // what DragSession.ArmKeyAtMouseDown reports for a Ctrl-click in Ctrl mode
        Assert.Equal([@"C:\p\b.jpg"], DragSession.FilterSelection(picked, armKeyAtDown, outputs, now, now.AddSeconds(5)));
        Assert.Equal(picked, DragSession.FilterSelection(picked, false, outputs, now, now.AddSeconds(5)));
    }

    // ------------------------------------------------------------------ the key table

    [Theory]
    [InlineData(ArmKey.Shift, ToolsKey.Ctrl, ToolsKey.Ctrl)]
    [InlineData(ArmKey.Shift, ToolsKey.Shift, ToolsKey.Ctrl)]
    [InlineData(ArmKey.Shift, ToolsKey.Alt, ToolsKey.Alt)]
    [InlineData(ArmKey.Shift, ToolsKey.MouseWheelOnly, ToolsKey.MouseWheelOnly)]
    [InlineData(ArmKey.Ctrl, ToolsKey.Ctrl, ToolsKey.Shift)]
    [InlineData(ArmKey.Ctrl, ToolsKey.Shift, ToolsKey.Shift)]
    [InlineData(ArmKey.Ctrl, ToolsKey.Alt, ToolsKey.Alt)]
    [InlineData(ArmKey.Ctrl, ToolsKey.MouseWheelOnly, ToolsKey.MouseWheelOnly)]
    public void NormalizeNeverMakesTheArmKeyTheToolsKey(ArmKey arm, ToolsKey given, ToolsKey expected) =>
        Assert.Equal(expected, ToolsKeys.Normalize(arm, given));

    [Theory]
    [InlineData(ArmKey.Shift, ToolsKey.Ctrl, "Ctrl", "Shift")]
    [InlineData(ArmKey.Ctrl, ToolsKey.Ctrl, "Shift", "Ctrl")]
    [InlineData(ArmKey.Shift, ToolsKey.Alt, "Alt", "Shift")]
    [InlineData(ArmKey.Ctrl, ToolsKey.MouseWheelOnly, "Scroll", "Ctrl")]
    public void LabelsNameTheKeysToPress(ArmKey arm, ToolsKey tools, string toolsLabel, string armLabel)
    {
        Assert.Equal(toolsLabel, ToolsKeys.Label(arm, tools));
        Assert.Equal(armLabel, ToolsKeys.Label(arm));
    }

    [Fact]
    public void ModifiersMapToTheKeys()
    {
        Assert.Equal(Modifiers.Shift, ToolsKeys.ArmModifier(ArmKey.Shift));
        Assert.Equal(Modifiers.Ctrl, ToolsKeys.ArmModifier(ArmKey.Ctrl));
        Assert.Equal(Modifiers.Ctrl, ToolsKeys.ToolsModifier(ArmKey.Shift, ToolsKey.Ctrl));
        Assert.Equal(Modifiers.Shift, ToolsKeys.ToolsModifier(ArmKey.Ctrl, ToolsKey.Ctrl));
        Assert.Equal(Modifiers.Alt, ToolsKeys.ToolsModifier(ArmKey.Ctrl, ToolsKey.Alt));
        Assert.Equal(Modifiers.None, ToolsKeys.ToolsModifier(ArmKey.Shift, ToolsKey.MouseWheelOnly));
    }

    // ------------------------------------------------------------------ hints on the wheel

    private static ActionDef Tile(string title, WheelPage page) => new(title, page, (_, _, _) => throw new NotSupportedException());

    [Fact]
    public void TheWheelHintsNameTheChosenKeys()
    {
        var shift = new WheelModel(WheelMode.Drag, ToolsKey.Ctrl, ArmKey.Shift);
        shift.SetActions([Tile("PNG", WheelPage.Format), Tile("Zip", WheelPage.Tool)]);
        Assert.Equal("Ctrl  Tools", shift.HubHint);
        shift.TogglePage();
        Assert.Equal("Shift  Formats", shift.HubHint);

        var ctrl = new WheelModel(WheelMode.Drag, ToolsKey.Ctrl, ArmKey.Ctrl);
        ctrl.SetActions([Tile("PNG", WheelPage.Format), Tile("Zip", WheelPage.Tool)]);
        Assert.Equal("Shift  Tools", ctrl.HubHint);
        ctrl.TogglePage();
        Assert.Equal("Ctrl  Formats", ctrl.HubHint);
        Assert.Equal([new KeyHint("Ctrl", "Formats", 4)], ctrl.KeyHints);
    }

    [Fact]
    public void ChangingTheArmKeyRewritesTheHubHint()
    {
        var m = new WheelModel(WheelMode.Drag);
        m.SetActions([Tile("PNG", WheelPage.Format), Tile("Zip", WheelPage.Tool)]);
        var changes = new List<WheelChange>();
        m.Changed += changes.Add;
        m.ArmKey = ArmKey.Ctrl;
        Assert.Equal("Shift  Tools", m.HubHint);
        Assert.Contains(WheelChange.HubHint, changes);
        m.ArmKey = ArmKey.Ctrl; // unchanged: no event
        Assert.Single(changes);
    }

    [Fact]
    public void ClickModeKeepsItsTabHintInEveryMode()
    {
        var m = new WheelModel(WheelMode.Click, ToolsKey.Ctrl, ArmKey.Ctrl);
        m.SetActions([Tile("PNG", WheelPage.Format), Tile("Zip", WheelPage.Tool)]);
        Assert.Equal("Tab  Tools", m.HubHint);
    }

    [Fact]
    public void TheWelcomeCopyFollowsTheKeys()
    {
        var shift = MikeShift.Core.Feedback.OnboardingCopy.ToolsDescription(ArmKey.Shift, ToolsKey.Ctrl);
        Assert.Contains("press Ctrl or scroll", shift);
        Assert.Contains("Press Shift to go back", shift);

        var ctrl = MikeShift.Core.Feedback.OnboardingCopy.ToolsDescription(ArmKey.Ctrl, ToolsKey.Ctrl);
        Assert.Contains("press Shift or scroll", ctrl);
        Assert.Contains("Press Ctrl to go back", ctrl);

        var wheelOnly = MikeShift.Core.Feedback.OnboardingCopy.ToolsDescription(ArmKey.Ctrl, ToolsKey.MouseWheelOnly);
        Assert.StartsWith("While the wheel is open, scroll the mouse wheel", wheelOnly);

        Assert.Equal(("Press", "Shift"), MikeShift.Core.Feedback.OnboardingCopy.ToolsHeading(ArmKey.Ctrl, ToolsKey.Ctrl));
        Assert.Equal(("Use the", "Scroll wheel"), MikeShift.Core.Feedback.OnboardingCopy.ToolsHeading(ArmKey.Shift, ToolsKey.MouseWheelOnly));
        Assert.Equal("Your originals are never moved or changed. Press Esc to cancel.", MikeShift.Core.Feedback.FeedbackCopy.OriginalsSafe);
        Assert.Equal("MikeShift - Ctrl-drag files to convert", MikeShift.Core.Feedback.FeedbackCopy.TrayTooltipFor(ArmKey.Ctrl));
        Assert.Equal("Shift-drag files to convert", MikeShift.Core.Feedback.FeedbackCopy.TrayHintFor(ArmKey.Shift));
    }
}
