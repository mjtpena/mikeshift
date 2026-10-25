# Testing

MikeShift is tested in three layers:

1. **Automated tests** that run anywhere Windows runs, including CI.
2. **UI harnesses** that drive a real build, and the real File Explorer, with real mouse and keyboard input on a real desktop.
3. **A manual checklist** for the things only a person at real hardware can judge: several monitors, a screen reader, touch, reboots.

## Automated tests

```powershell
dotnet build MikeShift.sln
dotnet test MikeShift.sln                                                     # both test projects, fast suite
dotnet test tests/MikeShift.Core.Tests --filter "FullyQualifiedName~Naming"    # one area
./scripts/build.ps1 -Configuration Release -Test                              # build and test through the helper script
```

A healthy checkout builds with **0 warnings** (warnings are errors) and passes every test.

| Project | What it covers | How |
|---|---|---|
| `tests/MikeShift.Core.Tests` | Naming and collision handling, `Kind.Of` / `Kind.CommonOf`, `ActionCatalog.For`, capability gating with fake `Capabilities`, every picture, PDF, video, audio, document and Zip converter (round trips, size guards, "already small"), the crop and trim models, feedback wording (`FeedbackPlan`, `JobLedger`), `DragSession`, `ModifierTracker`, wheel geometry, placement, model and animation, easing, the hotkey parser and conflict list, conversion settings, the Explorer context-menu planner, the CLI runner in-process, and hardening (below) | In-process xUnit. Fixtures are generated at test time (`Infrastructure/Fixtures*.cs`); nothing binary is checked in. `OriginalsGuard` hashes every input before and after each converter test and fails if an original changed. |
| `tests/MikeShift.Cli.Tests` | The real `MikeShift.Cli.exe` run as a child process on fixtures: exit codes, printed output paths, usage errors, Zip, picture, PDF, media and document commands, corrupt-input cases, the conversion flags | Exercises WIC and Media Foundation on CI machines too. Set `MIKESHIFT_CLI_EXE` to test another build of the executable (for example the one inside an unpacked package). |

Tests that need a codec the machine lacks (HEIC and HEVC encoders and the like) skip themselves with `[SkipIfNoCapability]` rather than failing, so skipped counts differ between machines. On a fully equipped PC, the default suite is 2,463 Core tests (12 skipped) and 88 CLI tests (3 skipped), and with every slow test enabled 2,517 and 91 with none skipped. These counts change as tests are added; the rule is zero failures and zero warnings.

### Fast and slow tests

The slow part (the Media Foundation matrix, video transcodes, Microsoft Word) is written with `[SlowFact]` / `[SlowTheory]` and `[Trait("Category", "Slow")]`. Without `MIKESHIFT_SLOW=1` those tests are reported as skipped, so a plain `dotnet test MikeShift.sln` is the fast suite. Run everything with:

```powershell
$env:MIKESHIFT_SLOW = '1'; dotnet test MikeShift.sln
```

(A `Category` filter alone cannot be made the default, because `dotnet test` always passes an empty filter that overrides one set in the project or in a runsettings file.) Microsoft Word tests need Word installed and run one at a time.

### Hardening tests

`Core.Tests/Hardening/HardeningTests.cs` and `Cli.Tests/HardeningCliTests.cs` feed damaged input to everything:

* A corpus of truncated PNG, JPG, MP4, WAV, PDF and DOCX files (cut at 0, 1, 4 and 12 bytes, a quarter, a half and one byte short), misnamed files (PNG bytes named `.pdf`, random bytes named `.docx`, and so on), and zero-byte files of 23 extensions goes through `Kind.Of`, `ActionCatalog.For` and **every action the catalog offers**, through `CliRunner` (about 280 runs).
* Each run must end with exit code 0 or 1; on 1 an `error:` line and no stack trace; within 30 seconds; with no stray, partial or temporary file next to the original; and with the original byte-identical. A guard test proves the matrix is not passing just because everything fails.
* The same for a representative set through the real `MikeShift.Cli.exe` process.
* Selections: 500 JPEGs through `ActionCatalog.For` with a cold `Kind` cache in under 2 seconds; a 500-file mixed selection is Zip only and fast; a large selection whose last file is a misnamed PDF, or that contains a folder or a vanished file, is Zip only; small selections still classify misnamed files by content.

### Integrity suite

`Core.Tests/Integrity/**` and `Cli.Tests/IntegrityCliTests.cs` run every action the catalog offers for every kind of file, and judge the output with decoders that are **not** the code that wrote it: SkiaSharp and WIC for pictures; PdfPig and Windows.Data.Pdf for PDF; hand-written WAV/AIFF/FLAC/MP3/MP4 parsers plus a Media Foundation decode and cross-correlation for audio and video; the Open XML SDK with `OpenXmlValidator`, System.Xml and small RTF and HTML tokenizers for documents; System.IO.Compression and Windows' `tar.exe` for Zip. Lossless outputs must be pixel- or sample-exact, lossy ones must reach a PSNR or correlation floor, originals must hash the same afterwards, and no `.mikeshift-tmp` may remain.

`MIKESHIFT_INTEGRITY_MATRIX=<file>` makes every integrity test append a row (category, input, action, validator, result, metric) to that file, which is how a results matrix is produced.

### Conversion settings tests

`ConversionOptionsTests`, `Images/ConversionSettingsTests`, `Media/GifLengthSettingTests`, `CliConversionFlagsTests`, `Hosting/ConversionPreferencesTests`, `Shell/ContextMenuSettingsTests` and `Cli.Tests/ConversionFlagsCliTests` cover: the defaults, rejection outside the range (also through `with`), strict parsing, fallback one setting at a time for damaged stored values, each converter honouring its setting (quality, Compress strength widths, exact Resize sizes, GIF frame counts), the output folder (including a missing or locked folder falling back to Downloads), the command-line flags and their usage errors, preference round trips, and the Resize wording in the Explorer menu.

### Fixtures for manual testing

```powershell
./scripts/make-fixtures.ps1                 # writes sample PNG/JPG/GIF/TIFF/BMP/WebP, PDF, media and documents to .\fixtures (git-ignored)
./scripts/make-fixtures.ps1 -OutDir D:\fx   # elsewhere
dotnet run --project src/MikeShift.Cli -- --list .\fixtures\sample.png
```

The script runs the `FixtureExport` test with `MIKESHIFT_FIXTURES_OUT` set, so the manual fixtures and the automated tests share one generator.

### Coverage

```powershell
./scripts/coverage.ps1                      # whole suite under coverlet; report in artifacts/coverage/
./scripts/coverage.ps1 -Filter "Category!=Slow"
```

It writes `index.html`, `Summary.txt`, a merged `Cobertura.xml` and `Namespaces.txt` (line and branch percentages per namespace, plus the least-covered classes). `MikeShift.Core` and `MikeShift.Cli` are measured; `MikeShift.App` (WinUI) and generated code are not. The CLI child-process tests launch the executable out of process, which coverlet cannot see; the in-process `CliRunner` tests count. CI runs this as an informational job with no threshold.

### Offline verification

MikeShift claims to have no network code. Two scripts back that up:

```powershell
./scripts/verify-offline.ps1                # runtime proof
./scripts/audit-network-imports.ps1 -Path src/MikeShift.Cli/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64   # static evidence
```

* **`verify-offline.ps1`** writes one sample of every kind of file, runs `mikeshift --list` and then `mikeshift --run <title>` for every action offered (each a separate process), then runs a `MikeShift.exe` session (idle, then the click-mode wheel, then Enter), while polling every 50 ms for any TCP connection, TCP listener or UDP endpoint owned by a MikeShift process. It also reports loaded networking libraries and the DNS client cache before and after. Microsoft Word is off by default, so the run must not start Word; add `-UseWord` to measure the opt-in path instead (Word then opens its own connections to Microsoft, which is expected and only reported). It exits 0 when no MikeShift process owned any connection or endpoint. The JSON report goes to `artifacts/verify-offline.json`. It needs no administrator rights and changes no OS setting.
* **`audit-network-imports.ps1`** scans a build or publish folder: managed assemblies for references to `System.Net.*`, `Windows.Web.*`, `Windows.Networking.*`, WebView2 and similar, and native DLLs for imports from `ws2_32`, `winhttp`, `wininet`, `dnsapi` and friends. Nothing is modified.

Run both before a release and attach the result to the release notes.

### Packaging checks

Do these on the packages you intend to ship ([releasing](releasing.md)):

* `makeappx` validates the manifest, including the `mikeshift.exe` alias to `MikeShift.Cli.exe` and its `desktop4:Subsystem="console"`.
* Package contents: the self-contained runtime is present (`coreclr.dll`), `MikeShift.Cli.exe` and `MikeShift.exe` sit side by side in the package root, and there is no `onnxruntime.dll`, `DirectML.dll` or other Windows AI payload.
* From an unpacked package: `MikeShift.Cli.exe --list`, `--run JPG`, `--run "Resize 50%"`, `--run Zip` and `--bogus` give exit codes 0 and 2; `MikeShift.exe --smoke-test` exits 0; the CLI suites pass against the trimmed executable (`MIKESHIFT_CLI_EXE`); a release build ignores `MIKESHIFT_LOG` without `--dev` and honours it with it.
* The signature's subject equals the manifest's `Publisher`, and the SHA-256 of each file matches `SHA256SUMS.txt`.

## UI harnesses

Some behaviour cannot be unit-tested: global hooks, OLE drag and drop, the tray, the Explorer context menu. Two console tools drive a real build.

> **They drive the real mouse and keyboard.** While a harness runs, it moves the pointer, presses keys and opens windows on your desktop, so do not touch the mouse or keyboard until it finishes, and close anything you care about first. Run them on a machine (or VM) you are not using. The Explorer scenarios refuse to send input unless they have really brought the File Explorer window to the front, but nothing else protects your session. Read what a scenario does before you run it, and never run the harnesses on a desktop where you have unsaved work.

### Drag harness (`tools/DragHarness`)

A console program that starts its own `MikeShift.exe` (with `--dev`, `MIKESHIFT_ACCEPT_INJECTED=1` and a private log: see [developer switches](architecture.md#8-developer-switches); only Debug builds and unpackaged `--dev` runs honour them, so the harness works against a Debug build or an unpackaged Release build, not an installed package). It creates a backdrop window and performs a scripted drag with injected mouse and keyboard input, using either an OLE drag source of its own or the real File Explorer. It checks what appeared on disk, the window state, the drop effect (copy, never move), UI Automation names and the log's stage lines, and measures the time from the key press to a visible wheel.

```powershell
dotnet build tools/DragHarness
$h = 'tools\DragHarness\bin\Debug\net10.0-windows10.0.26100.0\DragHarness.exe'
& $h --scenario all                                 # synthetic drag source; needs the desktop to itself; quit any running MikeShift first
& $h --scenario copy --screenshot out.png --theme light
& $h --app <path\to\MikeShift.exe> --scenario all   # test another build, such as the Release output
```

Scenarios of the synthetic source: `copy`, `tools`, `shiftfirst`, `multi`, `esc`, `nodrop`, `text`, `nodrag`, `virtual`, `leave`, `outside`, `wheeltick`, `leftmonitor`, `edge`, `folder`, `explorer`, `click-keys`, `click-mouse`, `click-esc`, `click-blur`.

**Scenarios with the real File Explorer as the drag source** run with `--explorer <name[,name...]|all>` (`MIKESHIFT_EXPLORER_ROOT` sets the work folder; results go to `results.txt` there):

```powershell
& $h --explorer shift-drag,tools-page,taskbar
```

| Group | Scenarios |
|---|---|
| Conversions from Explorer | `shift-drag`, `ctrl-first` (Ctrl before the click), `ctrl-shift`, `shift-range` (range select, then drag), `merge-pdf`, `zip-mixed`, `zip-folder`, `big-selection` (Ctrl+A over 500 JPEGs), `already-png`, `tools-page` |
| Placement and gesture | `edges-primary`, `edges-left`, `edges-cross` (edges, corners and the monitor boundary), `taskbar` (taskbar sweep), `toggles` (Ctrl, Shift and wheel ticks), `release-elsewhere`, `escape`, `hub`, `shift-click-rule`, `callout` |
| Other drag sources | `notepad-text` (text out of Notepad), `title-bar`, `rubber-band`, `zip-save-first` (a file inside a zip) |
| Tray and click mode | `tray-keys`, `tray-mouse`, `tray-escape`, `tray-blur`, `tray-cancel`, `tray-flyout`, `tray-keyboard`, `tray-keyboard-open` |
| Entry points | `ctrl-mode`, `ctrl-mode-shift`, `shift-mode-ctrl`, `hotkey`, `hotkey-empty`, `hotkey-none` (each with a scratch settings file via `MIKESHIFT_SETTINGS_PATH`, so your preferences are never touched) |
| Measurements | `latency-primary`, `latency-left` (key to visible wheel, idle cost) |
| Touches your files | `desktop` converts a file on your real Desktop and deletes it afterwards; run it deliberately, not as part of `all` on a machine you care about |

Every scenario also asserts that the originals are byte-identical and in place, that the app log has no `[ERROR]`, and that the wheel is hidden at the end. The scenario IDs map to the manual checklist below. The missing-HEIF tile cannot be reproduced on a PC that has the extension, so it is checked three ways: unit tests with fake capabilities (`CodecHelpTests`), the developer capability override `MIKESHIFT_CAPS=no-heif,no-hevc`, and offscreen renders (`MikeShift.exe --dev --render-wheel heic-missing-drag-dark`; `--render-wheel list` lists the scenarios).

### Context-menu harness (`tools/ContextMenuHarness`)

Checks the Explorer right-click entry ([architecture §9](architecture.md#9-explorer-right-click-entry-convert-with-mikeshift)). It needs a **registered package** (the COM server and the menu entry exist only for an installed or registered MikeShift: `Add-AppxPackage -Register <layout>\AppxManifest.xml`, with Developer Mode on) and the desktop to itself.

```powershell
dotnet build tools/ContextMenuHarness
$h = 'tools\ContextMenuHarness\bin\Debug\net10.0-windows10.0.26100.0\ContextMenuHarness.exe'
& $h probe --repeat 3 --invoke "Convert to PNG" photo.jpg        # plays Explorer: activates the COM server, builds the menu, prints timings; no real input
& $h menu <workfolder> all                                       # real File Explorer, real mouse and keyboard, UI Automation to read the menus
& $h menu <workfolder> jpg-png multi-merge folder-zip mixed heic big600 mixed-big fallback classic more
& $h menu <workfolder> timing 8                                  # right-click to menu on screen, with and without the extension registered
```

`menu` opens its own File Explorer window on the work folder (a temp folder you created containing `photo1.jpg` to `photo3.jpg`, `sample.png`, `doc.pdf`, `letter.docx`, `IMG_0001.heic`, `folder\`, `big\` with 600 JPEGs and `mixedbig\` with 18 JPEGs and 4 PDFs), selects items through the shell view, right-clicks with `SendInput`, finds the menu and its submenu through UI Automation, clicks real entries, then checks the folder: output beside the original, original SHA-256 unchanged. `HARNESS_SHOTS=<folder>` saves cropped menu screenshots (only the menu bodies are copied; the rest of the picture stays transparent). The tray app is started, and its one-time welcome window closed, first.

### Screenshots without a desktop

`MikeShift.exe --dev --scene <name>` shows one canned UI state with no real files (see [developer switches](architecture.md#8-developer-switches)), and `scripts/capture-scene.ps1` captures one of MikeShift's own windows to a PNG with `PrintWindow` without sending input to any other program. Both are used for the images under `docs/images/`; see [Store screenshots](../store/screenshots.md).

## Manual checklist

For release testing, on a real Windows 11 machine (and, where noted, Windows 10) with the signed or dev-signed package installed. Tick each item; note the Windows build. Where a UI harness scenario covers an item it is named, but a person on real hardware should still judge touch, pen, a screen reader, scaling and the like.

**Dragging**

- [ ] **1. Shift-drag a JPG from File Explorer.** The wheel appears under the pointer in under 100 ms; dropping on PNG gives `photo.png` beside it; the original is unchanged and File Explorer did **not** move it. Repeat with Ctrl-drag (Ctrl mode) and with Ctrl+Shift. *(Scenarios: `shift-drag`, `ctrl-first`, `ctrl-shift`, `tools-page`.)*
- [ ] **2a. The same from the Desktop.** *(Scenario `desktop`.)*
- [ ] **2b. Monitors.** The same from a second monitor at a different scaling; a wheel near a monitor edge; straddling two monitors; a monitor to the left of the primary (negative coordinates). *(Scenarios `edges-primary`, `edges-left`, `edges-cross`, `taskbar`.)*
- [ ] **3. Shift held before the click** (range select): the wheel still works. After converting, repeat with a Shift-click: the previously produced file is excluded from the selection. *(Scenarios `shift-range`, `shift-click-rule`.)*
- [ ] **4. Gesture keys.** The other key mid-drag goes to Tools and back; a mouse-wheel tick toggles; releasing over empty space lets Explorer's own drop proceed; Esc dismisses; dropping on the hub does nothing. *(Scenarios `toggles`, `release-elsewhere`, `escape`, `hub`.)*
- [ ] **5. Two input languages installed:** the Shift-then-Ctrl gesture does not switch language or layout.
- [ ] **6. Multi-file.** Three pictures offer Merge PDF; a picture plus a PDF offers Zip only; a folder offers Zip; a 500-file selection stays responsive. *(Scenarios `merge-pdf`, `zip-mixed`, `zip-folder`, `big-selection`, `already-png`.)*
- [ ] **7. Virtual files.** A browser image, an Outlook attachment, a file inside a `.zip` and a file on a phone (MTP) each show "Save the file first", with no crash and Explorer unaffected. *(Scenario `zip-save-first`.)*
- [ ] **8. Elevation.** An elevated File Explorer window shows no wheel (documented). MikeShift started elevated shows its startup warning.

**Click mode, editors and messages**

- [ ] **9. Convert files….** Click mode, keyboard navigation, the screen reader announces tiles, focus loss dismisses. *(Scenarios `tray-keys`, `tray-mouse`, `tray-escape`, `tray-blur`, `tray-cancel`, `tray-flyout`, `tray-keyboard`, `tray-keyboard-open`.)*
- [ ] **10. Crop and Trim editors.** Presets, slider and pixel-box sync, Esc and Enter, correct outputs.
- [ ] **11. Messages.** A single job; several jobs ("N more in progress"); the partial-failure dialog; the sound toggle; the "in Downloads" fallback (read-only source folder); **Show in folder** opens File Explorer with the output selected.
- [ ] **12. Open at login** matches Windows Settings → Apps → Startup and survives a reboot. A second launch pops the tray menu.

**Look and environment**

- [ ] **13. Themes and scaling.** Light and dark theme; Animation effects off means no animation; high contrast is readable; 100, 150 and 200 percent scaling.
- [ ] **14. Hardware and sessions.** A precision touchpad drag, pen, touch; a Remote Desktop session; sleep and resume mid-session; File Explorer restarted while MikeShift runs; a 24-hour soak without losing the hook.
- [ ] **15. No network.** Resource Monitor shows zero connections owned by MikeShift during a full session (see [offline verification](#offline-verification)).

**Packaging and platforms**

- [ ] **16. Install and uninstall.** Install the package with the signing certificate trusted; uninstall cleanly; the SHA-256 matches `SHA256SUMS.txt`; `mikeshift --list <file>` works from a fresh terminal (and `where mikeshift` points into `WindowsApps`); the Start menu shows only MikeShift, not "MikeShift command line".
- [ ] **17. Windows 10 1809 or later** (machine or VM): install, tray, command line, Convert files…, and the right-click entry.
- [ ] **18. ARM64 hardware** (or Windows 11 on ARM in a VM): install the same bundle, repeat items 12 and 16.
- [ ] **19. Codec variants.** With and without the HEIF and HEVC extensions the HEIC tile appears and disappears; with and without Word, `.doc` behaves as described in the [limitations](../limitations.md#microsoft-word).

**Right-click entry and settings**

- [ ] **20. Explorer right-click.** On a JPG, three pictures, a folder, a folder plus a file, a HEIC and 600 files: Windows 11's modern menu shows **Convert with MikeShift** with the submenu; a pick converts beside the original; **More options…** opens the wheel; **Show more options** lists it; the first click after a quiet spell is not a hang; Windows 10 shows it in its menu. *(Context-menu harness `menu` scenarios.)*
- [ ] **21. Settings → Conversions.** Change each setting, restart MikeShift, and the choices are still there; the card descriptions follow the choice; the screen reader reads each card as "name, value"; with "A folder I choose" and the folder deleted or on an unplugged drive, the warning shows and the next conversion lands in Downloads with a message that names the folder.
- [ ] **22. Right-click details.** **More options…** opens the wheel at the pointer (also when the tray app has to start first); a HEIC on a PC without the HEIF extension shows "Get the free HEIF extension" and opens the Store page; a changed Resize setting shows in the menu.

## Continuous integration

`.github/workflows/ci.yml` runs on `windows-latest`. The `build-and-test` job restores, builds in Release, runs the fast test suite and uploads the `.trx` results, then runs the coverage script (informational). The `msix` job runs `scripts/release.ps1 -Flavor all` and uploads the unsigned packages with `SHA256SUMS.txt`. CI runs neither the UI harnesses nor the slow tests.
