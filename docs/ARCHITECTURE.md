# Architecture

How MikeShift is put together. "Built" means in the source tree and exercised as described in [TESTPLAN.md](TESTPLAN.md); everything specified in [PLAN.md](PLAN.md) is built. Written against the tree after work package W5a.

## 1. Solution layout

```
MikeShift.sln
Directory.Build.props      net10.0-windows10.0.26100.0, min OS 10.0.17763.0, nullable, warnings are errors
Directory.Packages.props   every NuGet version, pinned centrally (PDFsharp stays on 6.2.4: later versions are not MIT)
src/
  MikeShift.Core/          no UI. Everything that converts, classifies, names and decides.
  MikeShift.App/           WinUI 3 tray app, packaged as MSIX (assembly MikeShift.exe)
  MikeShift.Cli/           console exe (mikeshift.exe), a thin Main around Core's CliRunner
tests/
  MikeShift.Core.Tests/    xUnit; fixtures are generated at test time
  MikeShift.Cli.Tests/     launches the real mikeshift.exe as a child process
tools/IconGen/            build-time icon generator: renders assets/brand/mikeshift.svg into every MSIX logo, .ico and docs/screenshots/brand/icon-sheet.png (not shipped)
tools/DragHarness/         scripted Shift-drags (real mouse/keyboard input + an OLE drag source) against a running MikeShift.exe
packaging/make-dev-cert.ps1   self-signed signing certificate for sideloading
scripts/                   build.ps1, release.ps1, make-fixtures.ps1, make-icons.ps1
assets/brand/mikeshift.svg  hand-written source of the app mark
```

**MikeShift.Core** (namespace `MikeShift.Core.*`)

| Folder | Role | State |
|---|---|---|
| `Actions/` | `ActionDef`, `ActionCatalog.For(paths, capabilities)` (which tiles for a selection), `Each` (per-file batching, partial failures), `ZipAction`, `SpecialFiles` (pipes/sockets/devices are never read) | built |
| `Actions/Catalog/` | the ordered action list per category (Image, Pdf, Video, Audio, Document) | built |
| `Kind.cs`, `Capabilities.cs` | file classification (`Kind.Of`, and `Kind.CommonOf` for whole selections); what this machine can do (HEIF encode, WebP, MP3/FLAC/H.264/AAC encoders, HEVC/VP9/AV1 decoders, WebM, Word), probed once; `Capabilities.Current` waits for the probe, `TryGetCurrent` / `IsProbed` / `CurrentAsync` never block (see §5) | built |
| `Convert/Naming.cs` | output names (`base + suffix + ext`, ` 2`, ` 3` on collision); work happens in a hidden `.name.guid.mikeshift-tmp` folder next to the target (a crash leaves nothing visible), then one move into place; Downloads fallback | built |
| `Convert/Images`, `Convert/Pdf` | image and PDF converters | built |
| `Convert/Media` | video and audio converters (Media Foundation), GIF export, AIFF reader/writer, lossless MP4 re-wrap | built |
| `Convert/Documents` | document model plus DOCX/ODT/RTF/HTML/TXT readers and writers, PDF writer, Word automation | built |
| `Interaction/` | pure, UI-free state machines for the gesture: `DragSession`, `ModifierTracker`, `WheelGeometry`, `WheelPlacement`, `WheelModel`, `WheelAnimation`, `Easing` | built, unit-tested |
| `Editing/` | the rules of the editors: `CropBoxModel` (aspect presets, scale, pixel boxes, drag handles) and `TimeRangeModel` (trim range) | built, unit-tested |
| `Feedback/` | `FeedbackPlan` and `JobLedger`: what the toast and dialogs say, and the "N still working" bookkeeping | built, unit-tested |
| `Cli/CliRunner.cs` | the whole command line, callable in-process | built |

**MikeShift.App** folders: `Tray/` (tray icon and menu, Convert Files… dialog, a plain action list used only if the wheel could not start), `Jobs/` (`JobRunner`, `UiInteraction`), `Hud/` (non-activating toast window, chime, dialogs), `Settings/` (preferences, Open at Login, settings window), `FirstRun/` (welcome window), `Input/` (low-level input monitor), `Wheel/` (the wheel window, renderer, drop target, controller, UI Automation, `DragCoordinator`), `Editors/` (Crop editor, Trim dialog, media probe for the preview), `MemoryTrim.cs`, `Program.cs` (single-instance entry point). The tray icon is monochrome and follows the taskbar theme: `tray-dark-taskbar.ico` (white glyph) or `tray-light-taskbar.ico` (dark glyph), switched live from `UISettings.ColorValuesChanged` (`Tray/TaskbarTheme.cs`, which reads `SystemUsesLightTheme`). Regenerate all icons with `scripts/make-icons.ps1`; the preview sheet is `docs/screenshots/brand/icon-sheet.png`.

Native calls use CsWin32 (source-generated P/Invoke). Every `NativeMethods.txt` in a project is picked up, so each folder lists the Win32 functions it needs next to the code that uses them.

## 2. Process and threading model

```
 hook thread (dedicated, high priority, own message loop)
   WH_MOUSE_LL + WH_KEYBOARD_LL  ->  copy struct, TryWrite to bounded Channel<InputEvent>, CallNextHookEx
                                              |
                                              v
 reader task (thread pool, DragCoordinator)  channel -> WheelController.Enqueue (drops moves while no button is down)
                                              |
                                              v
 wheel thread (dedicated STA, "MikeShift wheel")  owns the wheel window, the OLE drop target (OLE calls it through this thread's
   message loop), the Win2D renderer, DragSession, WheelModel and the animation clock. Idles in MsgWaitForMultipleObjectsEx with
   an infinite timeout; wakes for messages, posted work, or a timer it asked for (a 16 ms frame while animating, the session's
   deadlines, a 50 ms button poll while a drag is active, the idle-memory timer).
                                              |  Drop: snapshot paths, return at once
                                              v
 UI thread (STA, WinUI dispatcher)  tray, toast, settings, welcome, editors, dialogs
                                              |
 thread pool  JobRunner -> ActionDef.Run -> converter (WIC / PDF / Media Foundation / Word)  -> toast + chime on the UI thread
```

* **Hook callbacks do nothing else.** A low-level hook callback that takes longer than the `LowLevelHooksTimeout` registry value is silently removed by Windows with no notification, so the callback allocates nothing and takes no locks. `LowLevelInputMonitor` also runs a watchdog: if the left button has been down for a while and no event arrived, it installs fresh hooks before removing the old ones. It sits behind `IInputMonitor` so a Raw Input implementation can replace it.
* **Only the gesture keys are ever read:** mouse move, left button, vertical wheel, Shift, Ctrl, Alt, Esc. Injected events (including our own mouse nudge) are flagged so `DragSession` can ignore them; touch and pen events are not flagged as injected. (The dev harness sets `MIKESHIFT_ACCEPT_INJECTED=1` so its synthetic input counts; never set in normal use.)
* **The wheel thread never converts and never blocks on anything slow.** Everything it touches is its own; other threads reach it through `WheelThread.Post`/`Wake` and read an immutable `WheelSnapshot` (UI Automation does).
* **The UI thread never converts.** `JobRunner.Enqueue` returns a job id immediately; the converter runs on the thread pool, then marshals the result (toast, chime, failure dialog) back. Interactive actions (Crop, Trim) call back into the UI through `IInteraction` (`UiInteraction`) and await the editor window.
* **Single instance:** `Program.Main` calls `AppInstance.FindOrRegisterForKey("mikeshift")` before any XAML starts; a second launch is redirected to the first, which pops its tray menu at the cursor.
* Converters are plain async methods on `MikeShift.Core` and take a `CancellationToken`; cancelling keeps outputs already written.
* **Classification must be fast** because the wheel is an empty disc until it is done. `Kind.Of` caches verdicts per (path, size, mtime) for the life of a drag; `Kind.CommonOf` classifies up to 16 files exactly, confirms a larger selection whose extensions all agree by sniffing a sample of 12, and sniffs any other large selection in parallel with early exit (500 JPEGs: about 20 ms).

## 3. Drag detection and the wheel

Windows has no readable global "drag pasteboard". The `IDataObject` of a drag lives inside the source's `DoDragDrop` loop and is only handed to a drop target. The design therefore detects the gesture from input, then puts a drop target under the cursor to receive the file list.

1. **Input monitor.** `SetWindowsHookEx` with `WH_MOUSE_LL` and `WH_KEYBOARD_LL` on a dedicated thread ([SetWindowsHookExW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw), [LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc) which documents the `LowLevelHooksTimeout` behaviour, [LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)). A missed button-up is covered by polling [GetAsyncKeyState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate) (`AsyncInput`).
2. **State machine** (`DragSession`): `Idle → Pressed → Dragging → Armed → Released → Idle`. Pressed becomes Dragging after moving `SM_CXDRAG`/`SM_CYDRAG`; Shift while Dragging arms; Shift held before the click still arms once the threshold is crossed (Explorer range select). After button-up the wheel is dismissed 400 ms later if nothing was picked. The session emits commands (`ShowPending`, `PageToggle`, `HideSilently`, `DismissAfterRelease`, `Dismiss`, `End`) and knows nothing about windows.
3. **Pending wheel.** On Arm, the wheel window is shown centred under the cursor with only the disc, without activating it, then one zero-distance mouse move ([SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)) makes OLE re-hit-test even if the pointer is stationary (retried once after 90 ms). If no `DragEnter` arrives within 250 ms it was not a file drag (window move, text selection) and the wheel hides silently (so the bare disc can flash for up to 250 ms when Shift is pressed during a non-file drag).
4. **Hit surface.** A layered window's fully transparent pixels let the mouse, and OLE hit-testing, pass through, so the whole wheel square is painted with alpha ≥ 1 (`#01000000`). Never `WS_EX_TRANSPARENT`. Styles `WS_POPUP | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST` ([Window features](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)), `WM_MOUSEACTIVATE` answers `MA_NOACTIVATE`. The window is created once and reused.
5. **Drop target.** A raw [`IDropTarget`](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nn-oleidl-idroptarget) registered with [`RegisterDragDrop`](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-registerdragdrop) on the wheel window (wheel thread, STA). Every call is forwarded to [`IDropTargetHelper`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idroptargethelper) so Explorer's drag image keeps rendering over the wheel. `DragEnter` reads the file list cheaply (`CF_HDROP` or the shell ID list; see [Shell data scenarios](https://learn.microsoft.com/en-us/windows/win32/shell/datascenarios)); heavy formats are never requested. Virtual-file data (`CFSTR_FILEDESCRIPTORW`: Outlook attachments, zip-folder entries, phones) and URLs/pictures (browser images) get a "Save the file first" hint and `DROPEFFECT_NONE`; other data (selected text) gets no wheel.
6. **Never move the user's files.** The returned effect is always `DROPEFFECT_COPY & *pdwEffect`, never MOVE: a Shift-drag means MOVE to Explorer, and a MOVE result would delete the originals. On `Drop`, `CFSTR_PERFORMEDDROPEFFECT = DROPEFFECT_COPY` is also set. The harness asserts the originals are untouched, including with the real Explorer as the drag source.
7. **Explorer's own feedback.** While a tile is highlighted, `CFSTR_DROPDESCRIPTION` is set (a [DROPDESCRIPTION](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ns-shlobj_core-dropdescription) with `DROPIMAGE_COPY`, text "Convert to %1") so the native drag image reads "Convert to PNG". The format name is in the [shell clipboard formats](https://learn.microsoft.com/en-us/windows/win32/shell/clipboard).
8. **Modifiers.** `ModifierTracker` is fed by the keyboard hook and reconciled with `grfKeyState` on every `DragOver` (OLE re-calls `DragOver` when key state changes even without motion, and this covers elevated sources the hook cannot see). Tools toggle: fresh Ctrl press (default), mouse-wheel tick, optional Alt (Settings).
9. **Neutral zone and hit test** (`WheelGeometry`/`WheelModel`). The point where the drag entered the wheel is neutral until the pointer leaves it; sectors are offset by half a step; hub = no selection. Physical pixels throughout (PerMonitorV2); monitor chosen by `MonitorFromPoint`, wheel clamped to its work area (`WheelPlacement`).
10. **After a conversion**, `JobRunner` remembers the outputs; a Shift-click selection made within 20 s that includes them drops them (`DragSession.FilterSelection`), so you don't convert your own results.
11. **Click mode** (Convert Files…) is the same window and renderer, shown at the screen centre and activated: arrow keys and Enter, mouse hover and click, Tab for the page, Esc or losing focus to close. UI Automation exposes the wheel as a list whose tiles are `ListItem`s ("Convert to PNG", "Compress") with `Invoke`, and raises focus/notification events so Narrator announces the highlighted tile (`WheelAutomation`).
12. **Known blind spot.** Windows blocks input hooks from seeing higher-integrity windows (UIPI, [security overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)), so drags from an elevated Explorer or app are invisible. See [LIMITS.md](LIMITS.md).

## 4. Wheel rendering

A normal XAML window cannot be a clean per-pixel-transparent, non-activating popup, so the wheel is a plain Win32 layered window drawn with Win2D into a premultiplied BGRA `CanvasRenderTarget`, read back, and pushed with [`UpdateLayeredWindow`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) (`ULW_ALPHA`). Glass is a screen snapshot of the pixels under the wheel (a plain `BitBlt` without `CAPTUREBLT`, so neither the wheel nor Explorer's drag image is in it), blurred once with a Gaussian blur when the wheel appears, plus a tint; high-contrast mode skips the glass and uses system colours. Light/dark, accent colour and "animation effects" are read from the system at every show. Everything else (tray, toast, settings, editors) is ordinary WinUI 3. Geometry and animation numbers are in PLAN.md §3.2; the pure parts live in `Core/Interaction` and are unit-tested.

The wheel is visible about 30 ms (29 to 47 ms measured by the harness, 15 scenarios) after Shift is pressed; PLAN's budget is 100 ms.

## 5. Memory behaviour

A tray app sits resident all day, so what it holds while idle matters more than keeping everything warm.

* The wheel's Win2D device (D3D11/D2D, the driver's heaps, the effect shaders) is by far the largest allocation (about 45 MB resident). `WheelRenderer` creates it on demand; the controller **releases it 15 s after the wheel hides**, compacts the managed heap and trims the working set (`MemoryTrim`, `SetProcessWorkingSetSize`).
* **A left-button press anywhere** re-creates it if it was released (`PrewarmIfCold`: device plus one throw-away frame, about 100 ms), long before a drag has moved and Shift is pressed, so the wheel latency does not change. While the device is warm, every press pushes the release time out.
* The blurred glass snapshot is dropped when the wheel hides; the 380 px pixel buffer goes with the device.
* A few seconds after an editor closes or a job finishes, `MemoryTrim.CompactSoon` does the same compaction (an editor holds a decoded preview and a lot of XAML state).
* Toast, Settings and Welcome windows are created on first use. Codec probing (`Capabilities.Current`) runs once in the background at start-up. Nothing on the UI or OLE thread waits for it: the wheel's `DragEnter` uses `Capabilities.TryGetCurrent`, and if the probe has not finished it builds the first catalog from `Capabilities.Quick()` (no optional codec assumed, only the Word registry check) and replaces the tiles when `CurrentAsync` completes, keeping the page and re-hitting the pointer; the job runner reads the capabilities on its worker thread, and click mode builds its catalog off the UI thread.
* Measured on the development PC (Release, self-contained, 20 to 30 s after start): working set 198 MB → about 17 MB, private working set 94 MB → about 5 MB (private bytes, i.e. committed, about 57 MB). During a gesture the working set goes up again (a wheel on screen: about 115 MB private working set for a click-mode launch; an editor: about 190 MB) and is handed back afterwards. `MIKESHIFT_TRIM_MS` changes the 15 s (dev/test only).

## 6. Converters

| Area | Built on | Notes |
|---|---|---|
| Images | [Windows Imaging Component](https://learn.microsoft.com/en-us/windows/win32/wic/-wic-lh) via the WinRT `BitmapDecoder`/`BitmapEncoder`; SkiaSharp for WebP encode and as decode fallback | 200-megapixel guard, blank-decode check, orientation/EXIF handling, JPEG segment scan for Strip EXIF |
| PDF to images | [`Windows.Data.Pdf`](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf) | ≤2× scale, ≤8000 px long side, white background |
| PDF text | PdfPig | scan and reversed-Hebrew detection refuse with a message |
| PDF structure, images→PDF | PDFsharp 6.2.4 | split, merge, recompress; A4 in metric regions, Letter elsewhere |
| Video and audio | [`MediaTranscoder`](https://learn.microsoft.com/en-us/uwp/api/windows.media.transcoding.mediatranscoder) with `MediaEncodingProfile` for transcodes; Media Foundation source reader/sink writer ([MFCreateSourceReaderFromURL](https://learn.microsoft.com/en-us/windows/win32/api/mfreadwrite/nf-mfreadwrite-mfcreatesourcereaderfromurl)) for probing, the lossless MP4/M4V re-wrap and GIF frames; encoder availability probed through [MFTs](https://learn.microsoft.com/en-us/windows/win32/medfound/media-foundation-transforms); AIFF has its own reader/writer | GIF: first 15 s, 10 fps, ≤480 px; Compress targets 720p; Trim re-encodes (lossless only for WAV/AIFF) |
| Documents | Open XML SDK for DOCX; hand-written ODT/RTF/HTML/TXT readers and writers around a common `DocModel`; PDFsharp for PDF output; Word automation (COM) for legacy `.doc` and, when installed, DOCX→PDF | pictures: TXT only (parity); see LIMITS |
| Zip | `System.IO.Compression` | UTC timestamps, reparse-loop and special-file refusal |

Every converter writes through `Naming` (a hidden `.name.guid.mikeshift-tmp` folder next to the target, then a move into place), so a failed job leaves no partial output, and hashes of the inputs are asserted unchanged in tests. Corrupt input is covered by a matrix test: truncated, misnamed and empty files through every offered action must end in `error: …` (exit 1) or a valid output, never a crash, a hang over 30 s (120 s for a legacy `.doc`, which goes through Word and its own 90 s kill switch), or a stray file. Word conversions run one at a time per process (instances started together stall each other), and the process-wide list of WINWORD.EXE processes is only used to find and kill the one instance a conversion started. The CLI ends with `TerminateProcess` after flushing its output (`FastExit`): the capability probe loads the GPU encoder stack (D3D/DXGI and the vendor driver), and tearing that down at normal process exit can fail-fast inside dxgi.dll on some PCs (observed at exit of the test host), which a caller would see as a crashed child instead of the exit code.

## 7. Packaging

* **MSIX**, built by the Windows App SDK tooling (`WindowsPackageType=MSIX`), one package per architecture (x64, ARM64), **self-contained**: the .NET 10 runtime and the Windows App SDK ship inside the package, so there is nothing to install first. `scripts/release.ps1` combines both into one `.msixbundle` with `makeappx bundle` ([MakeAppx](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool)) and signs with `signtool` if given a certificate.
* **Windows App SDK by component.** The app references `Microsoft.WindowsAppSDK.Base`, `.Foundation`, `.InteractiveExperiences` and `.WinUI` (2.5.1 line) instead of the `Microsoft.WindowsAppSDK` metapackage, which also drags in the on-device AI, ML (ONNX Runtime + DirectML), Search, Widgets and DWrite components that MikeShift never calls. `H.NotifyIcon.WinUI` depends on the metapackage, so it stays in the restore graph; the metapackage and those components are referenced with `ExcludeAssets="all"` (see the comment in `MikeShift.App.csproj`), which keeps them out of the output and the MSIX. Effect: bundle 304.8 MB → 256.8 MB, per-architecture package 154.6 → 130.3 MB. When bumping the Windows App SDK, update the component versions in `Directory.Packages.props` to what the new metapackage's `.nuspec` lists, and check that no `onnxruntime.dll`/`DirectML.dll` reappears in the output.
* **Two `Application` entries** in `Package.appxmanifest`: `App` (the tray app, `MikeShift.exe`) and `Cli` (`cli\mikeshift.exe`, hidden from the Start menu). The `mikeshift.exe` command on PATH is an app execution alias ([uap5:AppExecutionAlias](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap5-appexecutionalias)) owned by the `Cli` entry with `desktop4:Subsystem="console"`, so stdout, stderr and the exit code reach the terminal.
* **Why the CLI lives in `cli\` as its own self-contained copy:** file names on Windows are case-insensitive, and the tray app's `MikeShift.exe`/`MikeShift.dll` would collide with the CLI's `mikeshift.exe`/`mikeshift.dll`. The app project publishes the CLI (`BundleMikeShiftCli` target in `MikeShift.App.csproj`, under the "Packaging (W4b)" comment) and adds it to the package as content. It costs about 51 MB per architecture (a second copy of the .NET runtime, SkiaSharp and the Windows SDK projection); opt out with `-p:BundleCli=false` for faster inner-loop builds. Sharing one runtime between the two would roughly halve this but needs a framework-dependent or single-host design that is not done.
* **Open at Login:** the manifest's `windows.startupTask` (`MikeShiftStartup`, disabled by default), driven by [`StartupTask`](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask); it appears in Settings → Apps → Startup, where the user can also turn it off. Unpackaged dev runs use the HKCU Run key instead.
* **Single instance:** [`AppInstance`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.applifecycle.appinstance) (see §2).
* **Signing:** the manifest Publisher is `CN=MikeShift Dev`; `packaging/make-dev-cert.ps1` creates a matching self-signed certificate ([creating a signing certificate](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing)). Long paths are enabled by the application manifests.
* **CI:** `.github/workflows/ci.yml` builds and tests on `windows-latest`, then runs `scripts/release.ps1` and uploads the unsigned bundle and `SHA256SUMS.txt`.
