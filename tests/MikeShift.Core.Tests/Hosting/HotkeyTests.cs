using MikeShift.Core.Hotkeys;

namespace MikeShift.Core.Tests.Hosting;

/// <summary>The pure parts of the "convert the current File Explorer selection" hotkey: parsing, text, the conflict list, selection filtering.</summary>
public class HotkeyTests
{
    // ------------------------------------------------------------------ parse and format

    [Fact]
    public void TheDefaultIsCtrlAltShiftSpace()
    {
        Assert.Equal("Ctrl+Alt+Shift+Space", HotkeySpec.Default.ToString());
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, HotkeySpec.Default.Modifiers);
        Assert.Equal(0x20, HotkeySpec.Default.VirtualKey);
        Assert.Equal(HotkeyVerdict.Ok, HotkeyConflicts.Check(HotkeySpec.Default).Verdict);
    }

    [Theory]
    [InlineData("Ctrl+Alt+Space", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x20)]
    [InlineData("ctrl+alt+space", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x20)]
    [InlineData("Alt+Ctrl+Space", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x20)]   // order does not matter
    [InlineData(" Ctrl + Shift + K ", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 'K')]
    [InlineData("Control+F9", HotkeyModifiers.Ctrl, 0x78)]
    [InlineData("Ctrl+Alt+F12", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x7B)]
    [InlineData("Ctrl+Shift+Alt+F24", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Alt, 0x87)]
    [InlineData("Ctrl+7", HotkeyModifiers.Ctrl, '7')]
    [InlineData("Win+Shift+S", HotkeyModifiers.Win | HotkeyModifiers.Shift, 'S')]
    [InlineData("Windows+E", HotkeyModifiers.Win, 'E')]
    [InlineData("Alt+Escape", HotkeyModifiers.Alt, 0x1B)]
    [InlineData("Ctrl+PgUp", HotkeyModifiers.Ctrl, 0x21)]
    [InlineData("Ctrl+Alt+Delete", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x2E)]
    [InlineData("Ctrl+`", HotkeyModifiers.Ctrl, 0xC0)]
    [InlineData("Alt+F1", HotkeyModifiers.Alt, 0x70)]
    [InlineData("F8", HotkeyModifiers.None, 0x77)]
    public void ParsesTheTextTheSettingsFileHolds(string text, HotkeyModifiers mods, int vk)
    {
        Assert.True(HotkeySpec.TryParse(text, out var spec));
        Assert.Equal(mods, spec.Modifiers);
        Assert.Equal(vk, spec.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("None")]
    [InlineData("Ctrl+")]
    [InlineData("+Space")]
    [InlineData("Ctrl+Alt")]               // modifiers only
    [InlineData("Ctrl+Ctrl+A")]            // repeated modifier
    [InlineData("Ctrl+A+B")]               // two keys
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl++")]
    public void RejectsTextThatIsNotAShortcut(string text) => Assert.False(HotkeySpec.TryParse(text, out _));

    [Fact]
    public void NullIsNotAShortcut() => Assert.False(HotkeySpec.TryParse(null, out _));

    [Theory]
    [InlineData("Ctrl+Alt+Shift+Space")]
    [InlineData("Ctrl+Alt+Shift+Win+Z")]
    [InlineData("Alt+F9")]
    [InlineData("Ctrl+PageDown")]
    [InlineData("Shift+Insert")]
    [InlineData("Ctrl+/")]
    [InlineData("F5")]
    [InlineData("Ctrl+Alt+Left")]
    [InlineData("Ctrl+Alt+PrintScreen")]
    [InlineData("Ctrl+Alt+7")]
    public void FormattingRoundTrips(string canonical)
    {
        Assert.True(HotkeySpec.TryParse(canonical, out var spec));
        Assert.Equal(canonical, spec.ToString());
        Assert.True(HotkeySpec.TryParse(spec.ToString(), out var again));
        Assert.Equal(spec, again);
    }

    [Fact]
    public void FormattingUsesCtrlAltShiftWinOrderWhateverTheInputOrder()
    {
        Assert.True(HotkeySpec.TryParse("Win+Shift+Alt+Ctrl+q", out var spec));
        Assert.Equal("Ctrl+Alt+Shift+Win+Q", spec.ToString());
    }

    [Fact]
    public void EveryNamedKeyRoundTrips()
    {
        foreach (var name in new[] { "Space", "Tab", "Enter", "Esc", "Backspace", "Insert", "Delete", "Home", "End", "PageUp", "PageDown", "Left", "Up", "Right", "Down", "Pause", "PrintScreen" })
        {
            Assert.True(HotkeySpec.TryParse("Ctrl+" + name, out var spec), name);
            Assert.Equal(name, spec.KeyName);
            Assert.Equal("Ctrl+" + name, spec.ToString());
        }
        for (var f = 1; f <= 24; f++)
        {
            Assert.True(HotkeySpec.TryParse($"Alt+F{f}", out var spec), "F" + f);
            Assert.Equal($"F{f}", spec.KeyName);
        }
        for (var c = 'A'; c <= 'Z'; c++)
            Assert.Equal(c.ToString(), new HotkeySpec(HotkeyModifiers.Ctrl, c).KeyName);
    }

    [Fact]
    public void AKeyTheSettingsCannotNameIsShownAsAHexCode()
    {
        var spec = new HotkeySpec(HotkeyModifiers.Ctrl, 0xE5);
        Assert.Null(spec.KeyName);
        Assert.Equal("Ctrl+0xE5", spec.ToString());
        Assert.False(HotkeySpec.TryParse(spec.ToString(), out _));
    }

    // ------------------------------------------------------------------ the stored setting

    [Fact]
    public void ASettingThatIsAbsentIsTheDefaultAndNoneIsNone()
    {
        Assert.Equal(HotkeySpec.Default, HotkeySpec.FromSetting(null));
        Assert.Null(HotkeySpec.FromSetting("None"));
        Assert.Null(HotkeySpec.FromSetting("none"));
        Assert.Null(HotkeySpec.FromSetting(""));
        Assert.Equal(HotkeySpec.Default, HotkeySpec.FromSetting("garbage"));      // damaged value: back to the default
        Assert.Equal(new HotkeySpec(HotkeyModifiers.Ctrl, 'M'), HotkeySpec.FromSetting("Ctrl+M"));
        Assert.Equal("None", HotkeySpec.ToSetting(null));
        Assert.Equal("Ctrl+Alt+Shift+Space", HotkeySpec.ToSetting(HotkeySpec.Default));
    }

    [Fact]
    public void ModifierKeysAreNeverTheMainKey()
    {
        foreach (var vk in new[] { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C })
            Assert.True(HotkeySpec.IsModifierKey(vk), vk.ToString("X"));
        Assert.False(HotkeySpec.IsModifierKey(0x20));
        Assert.False(HotkeySpec.IsModifierKey('A'));
    }

    // ------------------------------------------------------------------ conflicts

    private static HotkeyCheck Check(string text)
    {
        Assert.True(HotkeySpec.TryParse(text, out var spec), text);
        return HotkeyConflicts.Check(spec);
    }

    [Theory]
    [InlineData("Ctrl+Alt+F9")]
    [InlineData("Ctrl+Shift+Alt+M")]
    [InlineData("Ctrl+Shift+F9")]
    [InlineData("Ctrl+Alt+Shift+Space")]
    [InlineData("Alt+F9")]
    public void SafeShortcutsPass(string text) => Assert.Equal(HotkeyVerdict.Ok, Check(text).Verdict);

    [Theory]
    [InlineData("Ctrl+Shift+Space", "Word")]            // a shortcut the default avoids
    [InlineData("Ctrl+Alt+Space", "AI assistant")]      // the other one: the quick entry of AI assistant apps
    [InlineData("Ctrl+C", "Copy")]
    [InlineData("Ctrl+V", "Paste")]
    [InlineData("Ctrl+Z", "Undo")]
    [InlineData("Alt+F4", "closing the window")]
    [InlineData("Alt+Tab", "switching windows")]
    [InlineData("Alt+Space", "PowerToys Run")]
    [InlineData("Ctrl+Space", "input methods")]
    [InlineData("Ctrl+Shift+Esc", "Task Manager")]
    [InlineData("Ctrl+Esc", "Start menu")]
    [InlineData("Ctrl+Alt+Delete", "security screen")]
    public void KnownShortcutsWarnAndSayWhatTheyAreFor(string text, string meaning)
    {
        var check = Check(text);
        Assert.Equal(HotkeyVerdict.Warning, check.Verdict);
        Assert.Contains(meaning, check.Message);
        Assert.Contains("MikeShift would take it", check.Message);
    }

    [Theory]
    [InlineData("Ctrl+Alt+A")]
    [InlineData("Ctrl+Alt+Z")]
    [InlineData("Ctrl+Alt+7")]
    public void CtrlAltPlusALetterWarnsBecauseItIsAltGrOnManyKeyboards(string text)
    {
        var check = Check(text);
        Assert.Equal(HotkeyVerdict.Warning, check.Verdict);
        Assert.Contains("AltGr", check.Message);
    }

    [Fact]
    public void ABareFunctionKeyIsAllowedButWarned()
    {
        var check = Check("F8");
        Assert.Equal(HotkeyVerdict.Warning, check.Verdict);
        Assert.Contains("Add Ctrl or Alt", check.Message);
        Assert.Equal(HotkeyVerdict.Warning, Check("Shift+F8").Verdict);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Shift+A")]
    [InlineData("Space")]
    [InlineData("Shift+Space")]
    [InlineData("Enter")]
    public void ShortcutsThatWouldStopYouTypingAreRejected(string text)
    {
        var check = Check(text);
        Assert.Equal(HotkeyVerdict.Rejected, check.Verdict);
        Assert.Contains("Ctrl or Alt", check.Message);
    }

    [Theory]
    [InlineData("Win+A")]
    [InlineData("Win+Shift+S")]
    [InlineData("Ctrl+Win+Space")]
    public void WindowsKeyShortcutsAreRejectedBecauseWindowsKeepsThem(string text)
    {
        var check = Check(text);
        Assert.Equal(HotkeyVerdict.Rejected, check.Verdict);
        Assert.Contains("Windows", check.Message);
    }

    [Fact]
    public void EscAloneAndModifierKeysAreRejected()
    {
        Assert.Equal(HotkeyVerdict.Rejected, Check("Esc").Verdict);
        Assert.Equal(HotkeyVerdict.Rejected, HotkeyConflicts.Check(new HotkeySpec(HotkeyModifiers.Ctrl, 0x11)).Verdict);
        Assert.Equal(HotkeyVerdict.Rejected, HotkeyConflicts.Check(new HotkeySpec(HotkeyModifiers.Ctrl, 0xE5)).Verdict); // a key the settings cannot name
    }

    [Fact]
    public void TheKnownListIsWellFormedAndNeverContainsTheDefault()
    {
        Assert.NotEmpty(HotkeyConflicts.Known);
        Assert.DoesNotContain(HotkeySpec.Default, HotkeyConflicts.Known.Keys);
        foreach (var (spec, meaning) in HotkeyConflicts.Known)
        {
            Assert.False(string.IsNullOrWhiteSpace(meaning));
            Assert.NotNull(spec.KeyName);
            Assert.True(HotkeySpec.TryParse(spec.ToString(), out var again));
            Assert.Equal(spec, again);
        }
    }

    // ------------------------------------------------------------------ selection -> paths

    [Fact]
    public void TheSelectionKeepsRealFilePathsInOrderWithoutDuplicates()
    {
        var kept = SelectionPaths.Filter([@"C:\Users\me\a.jpg", @"c:\users\me\A.JPG", @"D:\b.png", @"C:\Users\me\a.jpg", @"\\server\share\c.pdf"]);
        Assert.Equal([@"C:\Users\me\a.jpg", @"D:\b.png", @"\\server\share\c.pdf"], kept);
    }

    [Fact]
    public void ShellItemsWithoutAFilePathAreDropped()
    {
        var kept = SelectionPaths.Filter([null, "", "   ", "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "shell:RecycleBinFolder", "Documents", @"..\a.jpg", @"C:\ok.txt", "a\0b"]);
        Assert.Equal([@"C:\ok.txt"], kept);
    }

    [Fact]
    public void FilesThatVanishedSinceTheyWereSelectedAreDropped()
    {
        var kept = SelectionPaths.Filter([@"C:\a.jpg", @"C:\gone.jpg", @"C:\b.jpg"], p => !p.Contains("gone", StringComparison.Ordinal));
        Assert.Equal([@"C:\a.jpg", @"C:\b.jpg"], kept);
    }

    [Fact]
    public void AnEmptyOrAllVirtualSelectionIsEmpty()
    {
        Assert.Empty(SelectionPaths.Filter([]));
        Assert.Empty(SelectionPaths.Filter(["::{031E4825-7B94-4DC3-B131-E946B44C8DD5}"]));
    }

    [Fact]
    public void FoldersAreKeptWhenTheyExist()
    {
        using var dir = new TempDir();
        var folder = dir.CreateDir("pics");
        var file = dir.Combine("pics", "a.jpg");
        File.WriteAllText(file, "x");
        var kept = SelectionPaths.Filter([folder, file, dir.Combine("nope.png")], p => File.Exists(p) || Directory.Exists(p));
        Assert.Equal([folder, file], kept);
    }

    [Theory]
    [InlineData("CabinetWClass", true)]
    [InlineData("ExploreWClass", true)]
    [InlineData("Progman", true)]
    [InlineData("WorkerW", true)]
    [InlineData("Notepad", false)]
    [InlineData("Chrome_WidgetWin_1", false)]
    [InlineData("MikeShiftWheel", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyExplorerWindowsAndTheDesktopAreRead(string? className, bool expected) =>
        Assert.Equal(expected, SelectionPaths.IsExplorerWindowClass(className));

    [Fact]
    public void TheToastWordsAreTheDocumentedOnes()
    {
        Assert.Equal("Select files in File Explorer first", MikeShift.Core.Feedback.FeedbackCopy.SelectFilesFirst);
        Assert.Null(MikeShift.Core.Feedback.OnboardingCopy.HotkeyHint(null));
        Assert.Equal("Select files in File Explorer and press Ctrl+Alt+Shift+Space to open the wheel from the keyboard.",
            MikeShift.Core.Feedback.OnboardingCopy.HotkeyHint(HotkeySpec.Default));
    }
}
