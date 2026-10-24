using MikeShift.Core.Actions;
using MikeShift.Core.Feedback;
using MikeShift.Core.Interaction;

namespace MikeShift.Core.Tests.Feedback;

/// <summary>
/// The HEIC dead end (positioning P0-3): a HEIC/AVIF photo Windows cannot open used to be "Zip only". With a missing extension the catalog now offers
/// the one tile that fixes it, and the pill, the failure message and the welcome card say the same. Machines are faked with capability records.
/// </summary>
public class CodecHelpTests
{
    /// <summary>Everything that has an image extension is an undecodable file: what <c>Kind.Of</c> reports for a HEIC Windows cannot open.</summary>
    private static Category Undecodable(string path) => Category.Other;

    private static string[] Titles(Capabilities caps, Func<string, Category> kindOf, params string[] paths) =>
        ActionCatalog.For(paths, caps, kindOf).Select(a => a.Title).ToArray();

    // ------------------------------------------------------------------ which extension

    [Theory]
    [InlineData("heic", false, true, CodecNeed.Heif)]
    [InlineData("heif", false, false, CodecNeed.Heif)]
    [InlineData("heic", true, false, CodecNeed.Hevc)]
    [InlineData("heic", true, true, null)]
    [InlineData("avif", true, true, null)]
    [InlineData("jpg", false, false, null)]
    [InlineData("webp", false, false, null)]
    [InlineData("", false, false, null)]
    public void TheExtensionNeededDependsOnWhatIsInstalled(string ext, bool heif, bool hevc, CodecNeed? expected)
    {
        var caps = Capabilities.All with { HeifDecode = heif, HevcDecode = hevc };
        Assert.Equal(expected, CodecHelp.ForExtension(ext, caps)?.Need);
    }

    [Fact]
    public void AvifNeedsTheAv1ExtensionOnly()
    {
        Assert.Equal(CodecNeed.Av1, CodecHelp.ForExtension("avif", Capabilities.All with { AvifDecode = false })!.Need);
        Assert.Null(CodecHelp.ForExtension("avif", Capabilities.None with { AvifDecode = true }));
    }

    // ------------------------------------------------------------------ the catalog

    [Fact]
    public void AHeicWindowsCannotOpenOffersTheHeifExtensionInsteadOfZipOnly()
    {
        var caps = Capabilities.All with { HeifDecode = false };
        var tiles = ActionCatalog.For([@"C:\x\IMG_0001.heic"], caps, Undecodable);
        Assert.Equal(["Get HEIF", "Zip"], tiles.Select(t => t.Title));

        var get = tiles[0];
        Assert.Equal(WheelPage.Tool, get.Page);
        Assert.Equal("Get the free HEIF extension", get.DropDescription);
        Assert.Equal("ms-windows-store://pdp/?ProductId=9PMMSR1CGPWG", get.OpensUri);
        Assert.False(string.IsNullOrEmpty(get.Glyph));
        Assert.Null(ZipAction.Def.OpensUri);
    }

    [Fact]
    public void WithTheHeifExtensionButNoHevcDecoderTheTileAsksForHevc()
    {
        var caps = Capabilities.All with { HevcDecode = false };
        var get = ActionCatalog.For([@"C:\x\a.heic", @"C:\x\b.HEIF"], caps, Undecodable)[0];
        Assert.Equal("Get HEVC", get.Title);
        Assert.Equal("Get the HEVC video extension", get.DropDescription); // not "free": the Store sells it
        Assert.Equal("ms-windows-store://pdp/?ProductId=9N4WGH0Z6VHQ", get.OpensUri);
    }

    [Fact]
    public void AMissingAv1ExtensionOffersItForAvif()
    {
        var caps = Capabilities.All with { AvifDecode = false };
        Assert.Equal(["Get AV1", "Zip"], Titles(caps, Undecodable, @"C:\x\a.avif"));
    }

    [Fact]
    public void WithTheExtensionsInstalledAnUnreadableHeicIsJustDamagedAndOnlyZipRemains()
    {
        Assert.Equal(["Zip"], Titles(Capabilities.All, Undecodable, @"C:\x\broken.heic"));
    }

    [Fact]
    public void ADecodableHeicNeverGetsTheTile()
    {
        // The extension is "missing" per the capabilities, but the file opened (another decoder) so it is an Image: normal tiles.
        var caps = Capabilities.All with { HeifDecode = false };
        var titles = Titles(caps, _ => Category.Image, @"C:\x\a.heic");
        Assert.DoesNotContain("Get HEIF", titles);
        Assert.Contains("JPG", titles);
    }

    [Fact]
    public void MixedOrUnrelatedSelectionsKeepTheOldBehaviour()
    {
        var caps = Capabilities.None;
        Assert.Equal(["Zip"], Titles(caps, Undecodable, @"C:\x\a.heic", @"C:\x\b.pdf"));      // one file is not HEIC
        Assert.Equal(["Zip"], Titles(caps, Undecodable, @"C:\x\a.heic", @"C:\x\b.avif"));      // different extensions needed
        Assert.Equal(["Zip"], Titles(caps, Undecodable, @"C:\x\a.xyz"));
        Assert.Equal(["Zip"], Titles(caps, Undecodable, @"C:\x\a.webp"));                       // SkiaSharp opens WebP: never a Store tile
    }

    [Fact]
    public void ASelectionOfManyHeicsStaysFastAndConsistent()
    {
        var paths = Enumerable.Range(0, 5000).Select(i => $@"C:\x\{i}.heic").ToArray();
        var asked = 0;
        var titles = ActionCatalog.For(paths, Capabilities.All with { HeifDecode = false }, p => { asked++; return Category.Other; })
            .Select(a => a.Title).ToArray();
        Assert.Equal(["Get HEIF", "Zip"], titles);
        Assert.True(asked < 100, $"kindOf was called {asked} times"); // a sample, not every file
    }

    [Fact]
    public void ARealUndecodableHeicOnDisk()
    {
        using var dir = new TempDir();
        var path = dir.Combine("not-really.heic");
        File.WriteAllBytes(path, new byte[64]); // zeros: no decoder opens this, on any PC
        Assert.Equal(["Get HEIF", "Zip"], ActionCatalog.For([path], Capabilities.All with { HeifDecode = false }).Select(a => a.Title));
        Assert.Equal(["Zip"], ActionCatalog.For([path], Capabilities.All).Select(a => a.Title));
    }

    // ------------------------------------------------------------------ the pill, the failure, the dialog

    [Fact]
    public async Task RunningTheTileExplainsWhatToInstall()
    {
        var get = CodecHelp.TileFor(CodecHelp.Heif);
        var result = await get.Run([@"C:\x\a.heic"], new ActionContext(Capabilities.None), CancellationToken.None);
        Assert.True(result.AllFailed);
        Assert.StartsWith("Windows can't open HEIC photos yet. Install the free \"HEIF Image Extensions\" from the Microsoft Store", result.AllFailedMessage);
        Assert.Equal(CodecHelp.Heif.StoreUri, CodecHelp.StoreUriInMessage(result.AllFailedMessage));
    }

    [Fact]
    public async Task TheCommandLineSaysWhichExtensionToInstallForAHeicWindowsCannotOpen()
    {
        using var dir = new TempDir();
        var path = dir.Combine("IMG_0042.heic");
        await File.WriteAllBytesAsync(path, new byte[64]); // no decoder opens this, on any PC
        var caps = Capabilities.All with { HeifDecode = false };

        var list = new StringWriter();
        Assert.Equal(0, await Cli.CliRunner.RunAsync(["--list", path], list, new StringWriter(), caps));
        Assert.Equal("Get HEIF, Zip", list.ToString().Trim());

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await Cli.CliRunner.RunAsync(["--run", "JPG", path], stdout, stderr, caps);
        Assert.Equal(1, exit);
        Assert.Equal("", stdout.ToString());
        Assert.Contains("error: Windows can't open HEIC photos yet. Install the free \"HEIF Image Extensions\" from the Microsoft Store", stderr.ToString());
        Assert.Contains("note: ms-windows-store://pdp/?ProductId=9PMMSR1CGPWG", stderr.ToString());
        Assert.Equal([Path.GetFileName(path)], Directory.GetFileSystemEntries(dir.Path).Where(p => !p.EndsWith("__downloads", StringComparison.Ordinal)).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(CodecNeed.Heif, "HEIF Image Extensions", "9PMMSR1CGPWG")]
    [InlineData(CodecNeed.Hevc, "HEVC Video Extensions", "9N4WGH0Z6VHQ")]
    [InlineData(CodecNeed.Av1, "AV1 Video Extension", "9MVZQVXJBQ9V")]
    [InlineData(CodecNeed.Webp, "WebP Image Extensions", "9PG2DK419DRG")]
    public void EachFailureMessageNamesItsStorePage(CodecNeed need, string storeName, string productId)
    {
        var prompt = new[] { CodecHelp.Heif, CodecHelp.Hevc, CodecHelp.Av1, CodecHelp.Webp }.Single(p => p.Need == need);
        Assert.Contains(storeName, prompt.FailureMessage);
        Assert.Equal($"ms-windows-store://pdp/?ProductId={productId}", prompt.StoreUri);
        Assert.Equal(prompt.StoreUri, CodecHelp.StoreUriInMessage(prompt.FailureMessage));
        Assert.False(prompt.Title.Length > 10, "a wheel tile label must be short"); // "Get HEIF"
    }

    [Fact]
    public void OtherMessagesHaveNoStorePage()
    {
        Assert.Null(CodecHelp.StoreUriInMessage("This image is damaged."));
        Assert.Null(CodecHelp.StoreUriInMessage("Windows can't open this file."));
        Assert.Null(CodecHelp.StoreUriInMessage(null));
    }

    [Fact]
    public void ADecodeFailureNamesTheMissingExtensionFromLiveChecks()
    {
        Assert.Contains("HEIF Image Extensions", CodecHelp.DecodeFailureMessage("heic", hasHeifDecoder: false, () => true, hasAvifDecoder: true));
        Assert.Contains("HEVC Video Extensions", CodecHelp.DecodeFailureMessage("heic", hasHeifDecoder: true, () => false, hasAvifDecoder: true));
        Assert.Contains("AV1 Video Extension", CodecHelp.DecodeFailureMessage("avif", hasHeifDecoder: true, () => true, hasAvifDecoder: false));
        Assert.Null(CodecHelp.DecodeFailureMessage("heic", true, () => true, true)); // everything there: the file is damaged
        // The HEVC check is not even made when the HEIF decoder itself is missing.
        Assert.Contains("HEIF", CodecHelp.DecodeFailureMessage("heic", false, () => throw new InvalidOperationException(), true));
    }

    [Fact]
    public void AFailedHeicConversionShowsAGetTheExtensionButton()
    {
        var message = CodecHelp.Heif.FailureMessage;
        var dialog = FeedbackPlan.DialogFor(ActionResult.Failed(1, @"C:\x\a.heic", message))!;
        Assert.Equal(["Get the extension", "Close"], dialog.Buttons.Select(b => b.Label));
        var get = dialog.Buttons[0];
        Assert.Equal(DialogCommand.OpenStore, get.Command);
        Assert.Equal(CodecHelp.Heif.StoreUri, get.Path);
        Assert.Equal(DialogCommand.Close, dialog.Buttons[^1].Command); // the last button stays the default

        var many = FeedbackPlan.DialogFor(new ActionResult
        {
            Total = 2,
            Failures = [new ActionFailure(@"C:\x\a.heic", message), new ActionFailure(@"C:\x\b.heic", message)],
        })!;
        Assert.Equal(DialogCommand.OpenStore, many.Buttons[0].Command);
    }

    [Fact]
    public void OrdinaryFailuresKeepTheirSingleCloseButton()
    {
        var dialog = FeedbackPlan.DialogFor(ActionResult.Failed(1, @"C:\x\a.pdf", "This PDF is damaged and can't be read."))!;
        Assert.Equal(["Close"], dialog.Buttons.Select(b => b.Label));
    }

    [Fact]
    public void ThePillExplainsWhyThereIsNoFormatTile()
    {
        var caps = Capabilities.All with { HeifDecode = false };
        var paths = new[] { @"C:\x\a.heic" };
        var note = CodecHelp.NoteFor(paths, caps, Undecodable);
        Assert.Equal("HEIC photos need the free HEIF extension", note);
        Assert.Null(CodecHelp.NoteFor(paths, Capabilities.All, Undecodable));
        Assert.Null(CodecHelp.NoteFor([@"C:\x\a.jpg"], caps, Undecodable));

        var model = new WheelModel(WheelMode.Click);
        model.SetActions(ActionCatalog.For(paths, caps, Undecodable));
        var callouts = WheelCallout.ForClick(model, "a.heic", note);
        var main = callouts.Main!;
        Assert.Equal(note, main.Text);
        Assert.Equal(CalloutTone.Caution, main.Tone);
        var drag = WheelCallout.ForDrag(new WheelModel(WheelMode.Drag), false, HintEmphasis.Prominent, note);
        Assert.Null(drag); // no tiles yet: nothing to say
        var dragModel = new WheelModel(WheelMode.Drag);
        dragModel.SetActions(ActionCatalog.For(paths, caps, Undecodable));
        Assert.Equal(note, WheelCallout.ForDrag(dragModel, false, HintEmphasis.Prominent, note)!.Text);
    }

    [Fact]
    public void TheHubNamesTheTileInFull()
    {
        var model = new WheelModel(WheelMode.Click);
        model.SetActions(ActionCatalog.For([@"C:\x\a.heic"], Capabilities.All with { HeifDecode = false }, Undecodable));
        model.Highlight(0);
        Assert.Equal("Get the free HEIF extension", model.CentreTitle);
        Assert.Equal("Get the free HEIF extension", WheelText.AutomationName(model.HighlightedAction!));
        Assert.Equal("Get the free HEIF extension", model.DropDescription);
    }

    // ------------------------------------------------------------------ first run

    [Fact]
    public void FirstRunListsOnlyWhatIsMissing()
    {
        Assert.Empty(CodecHelp.Missing(Capabilities.All));
        Assert.Equal([CodecNeed.Heif, CodecNeed.Hevc, CodecNeed.Av1, CodecNeed.Webp], CodecHelp.Missing(Capabilities.None).Select(p => p.Need));
        Assert.Equal([CodecNeed.Hevc], CodecHelp.Missing(Capabilities.All with { HevcDecode = false }).Select(p => p.Need));
        Assert.Equal([CodecNeed.Heif], CodecHelp.Missing(Capabilities.All with { HeifDecode = false }).Select(p => p.Need));
    }

    [Fact]
    public void TheWelcomeCardTitleAndBodyFollowWhatIsMissing()
    {
        Assert.Null(OnboardingCopy.CodecCard(CodecHelp.Missing(Capabilities.All)));

        var both = OnboardingCopy.CodecCard(CodecHelp.Missing(Capabilities.None))!.Value;
        Assert.Equal("Optional: add support for iPhone photos (HEIC)", both.Title);
        Assert.Contains("HEIF Image Extensions", both.Body);
        Assert.Contains("HEVC Video Extensions", both.Body);

        var heifOnly = OnboardingCopy.CodecCard(CodecHelp.Missing(Capabilities.All with { HeifDecode = false }))!.Value;
        Assert.Contains("free \"HEIF Image Extensions\"", heifOnly.Body);
        Assert.DoesNotContain("HEVC", heifOnly.Body);

        var hevcOnly = OnboardingCopy.CodecCard(CodecHelp.Missing(Capabilities.All with { HevcDecode = false }))!.Value;
        Assert.Contains("HEVC Video Extensions", hevcOnly.Body);

        var av1 = OnboardingCopy.CodecCard(CodecHelp.Missing(Capabilities.All with { AvifDecode = false }))!.Value;
        Assert.Equal("Optional: add support for AVIF photos", av1.Title);

        var webp = OnboardingCopy.CodecCard(CodecHelp.Missing(Capabilities.All with { WebpEncode = false }))!.Value;
        Assert.Equal("Optional: add support for WebP images", webp.Title);
        Assert.Equal(["Get HEIF extension", "Get HEVC extension", "Get AV1 extension", "Get WebP extension"],
            CodecHelp.Missing(Capabilities.None).Select(OnboardingCopy.StoreButtonLabel));
    }

    [Fact]
    public void SettingsListsHeicReadingAsItsOwnRowWithTheRightStoreLink()
    {
        var row = CodecSummary.From(Capabilities.All with { HeifDecode = false }).Single(r => r.Name.StartsWith("HEIC photos", StringComparison.Ordinal));
        Assert.False(row.Available);
        Assert.Equal("ms-windows-store://pdp/?ProductId=9PMMSR1CGPWG", row.StoreUri);

        var hevc = CodecSummary.From(Capabilities.All with { HevcDecode = false }).Single(r => r.Name.StartsWith("HEIC photos", StringComparison.Ordinal));
        Assert.False(hevc.Available);
        Assert.Equal("ms-windows-store://pdp/?ProductId=9N4WGH0Z6VHQ", hevc.StoreUri);

        Assert.True(CodecSummary.From(Capabilities.All).Single(r => r.Name.StartsWith("HEIC photos", StringComparison.Ordinal)).Available);
    }

    // ------------------------------------------------------------------ the dev override

    [Fact]
    public void TheDevOverrideOnlyRemovesCapabilities()
    {
        var f = CapabilityOverrides.Parse("no-heif, NO-AV1")!;
        var caps = f(Capabilities.All);
        Assert.False(caps.HeifDecode);
        Assert.False(caps.HeifEncode);
        Assert.False(caps.AvifDecode);
        Assert.False(caps.Av1Decode);
        Assert.True(caps.HevcDecode);
        Assert.True(caps.WebpEncode);
        Assert.Equal(Capabilities.None, f(Capabilities.None));
        Assert.False(CapabilityOverrides.Parse("no-hevc")!(Capabilities.All).HevcDecode);
        Assert.False(CapabilityOverrides.Parse("no-webp")!(Capabilities.All).WebpEncode);
        Assert.Null(CapabilityOverrides.Parse(null));
        Assert.Null(CapabilityOverrides.Parse("nonsense, other"));
    }
}
