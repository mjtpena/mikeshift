# Architecture

How Shiftvert for Windows is put together. "Built" means in the source tree; "planned" means specified in [PLAN.md](PLAN.md) but not written yet. Written against commit `e736eb2` plus the packaging work.

## 1. Solution layout

```
Shiftvert.sln
Directory.Build.props      net10.0-windows10.0.26100.0, min OS 10.0.17763.0, nullable, warnings are errors
Directory.Packages.props   every NuGet version, pinned centrally (PDFsharp stays on 6.2.4: later versions are not MIT)
src/
  Shiftvert.Core/          no UI. Everything that converts, classifies, names and decides.
  Shiftvert.App/           WinUI 3 tray app, packaged as MSIX (assembly Shiftvert.exe)
  Shiftvert.Cli/           console exe (shiftvert.exe), a thin Main around Core's CliRunner
tests/
  Shiftvert.Core.Tests/    xUnit; fixtures are generated at test time
  Shiftvert.Cli.Tests/     launches the real shiftvert.exe as a child process
packaging/make-dev-cert.ps1   self-signed signing certificate for sideloading
scripts/                   build.ps1, release.ps1, make-fixtures.ps1
```

**Shiftvert.Core** (namespace `Shiftvert.Core.*`)

| Folder | Role | State |
|---|---|---|
| `Actions/` | `ActionDef`, `ActionCatalog.For(paths, capabilities)` (which tiles for a selection), `Each` (per-file batching, partial failures), `ZipAction`, `SpecialFiles` (pipes/sockets/devices are never read) | built |
| `Actions/Catalog/` | the ordered action list per category (Image, Pdf, Video, Audio, Document) | built |
| `Kind.cs`, `Capabilities.cs` | file classification; what this machine can do (HEIF encode, WebP, MP3/FLAC/H.264/AAC encoders, HEVC/VP9/AV1 decoders, WebM, Word) | built |
| `Convert/Naming.cs` | output names (`base + suffix + ext`, ` 2`, ` 3` on collision), write to a temp file in the target folder then move, Downloads fallback | built |
| `Convert/Images`, `Convert/Pdf` | image and PDF converters | built |
| `Convert/Media`, `Convert/Documents` | video/audio and document converters | in progress (parallel work packages) |
| `Interaction/` | pure, UI-free state machines for the gesture: `DragSession`, `ModifierTracker`, `WheelGeometry`, `WheelModel`, `Easing` | built, unit-tested |
| `Feedback/` | `FeedbackPlan` and `JobLedger`: what the toast and dialogs say, and the "N still working" bookkeeping | built, unit-tested |
| `Cli/CliRunner.cs` | the whole command line, callable in-process | built |

**Shiftvert.App** folders: `Tray/` (tray icon and menu, Convert Files… dialog, temporary action picker), `Jobs/` (`JobRunner`, `UiInteraction`), `Hud/` (non-activating toast window, chime, dialogs), `Settings/` (preferences, Open at Login, settings window), `FirstRun/` (welcome window), `Input/` (low-level input monitor), `Program.cs` (single-instance entry point). Planned: `Wheel/` (layered window, renderer, drop target) and `Editors/` (Crop, Trim).

Native calls use CsWin32 (source-generated P/Invoke). Every `NativeMethods.txt` in a project is picked up, so each folder lists the Win32 functions it needs next to the code that uses them.

## 2. Process and threading model

```
 hook thread (dedicated, high priority, own message loop)
   WH_MOUSE_LL + WH_KEYBOARD_LL  ->  copy struct, TryWrite to bounded Channel<InputEvent>, CallNextHookEx   (built)
                                              |
                                              v
 consumer (thread pool)  DragSession + ModifierTracker (pure state machines, no Win32) -> commands          (built; wiring planned)
                                              |
                                              v
 UI thread (STA, WinUI dispatcher)  wheel window + IDropTarget, toast, tray, dialogs, editors              (tray/toast/dialogs built)
                                              |  Drop: snapshot paths, return at once
                                              v
 thread pool  JobRunner -> ActionDef.Run -> converter (WIC / PDF / Media Foundation)   -> toast + chime on UI thread   (built)
```

* **Hook callbacks do nothing else.** A low-level hook callback that takes longer than the `LowLevelHooksTimeout` registry value is silently removed by Windows with no notification, so the callback allocates nothing and takes no locks. `LowLevelInputMonitor` also runs a watchdog: if the left button has been down for a while and no event arrived, it installs fresh hooks before removing the old ones. It sits behind `IInputMonitor` so a Raw Input implementation can replace it.
* **Only the gesture keys are ever read:** mouse move, left button, vertical wheel, Shift, Ctrl, Alt, Esc. Injected events (including our own mouse nudge) are flagged so `DragSession` can ignore them; touch and pen events are not flagged as injected.
* **The UI thread never converts.** `JobRunner.Enqueue` returns a job id immediately; the converter runs on the thread pool, then marshals the result (toast, chime, failure dialog) back. Interactive actions (Crop, Trim) call back into the UI through `IInteraction` (`UiInteraction`) and await the editor.
* **Single instance:** `Program.Main` calls `AppInstance.FindOrRegisterForKey("shiftvert")` before any XAML starts; a second launch is redirected to the first, which pops its tray menu at the cursor.
* Converters are plain async methods on `Shiftvert.Core` and take a `CancellationToken`; cancelling keeps outputs already written.

## 3. Drag detection design

Windows has no readable global "drag pasteboard". The `IDataObject` of a drag lives inside the source's `DoDragDrop` loop and is only handed to a drop target. The design therefore detects the gesture from input, then puts a drop target under the cursor to receive the file list. This section is the design; the input and state-machine parts are built, the window and drop target are planned.

1. **Input monitor** (built). `SetWindowsHookEx` with `WH_MOUSE_LL` and `WH_KEYBOARD_LL` on a dedicated thread ([SetWindowsHookExW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw), [LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc) which documents the `LowLevelHooksTimeout` behaviour, [LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)). A missed button-up is covered by polling [GetAsyncKeyState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate) (`AsyncInput`).
2. **State machine** (built, `DragSession`): `Idle → Pressed → Dragging → Armed → Released → Idle`. Pressed becomes Dragging after moving `SM_CXDRAG`/`SM_CYDRAG`; Shift while Dragging arms; Shift held before the click still arms once the threshold is crossed (Explorer range select). After button-up the wheel is dismissed 400 ms later if nothing was picked. The session emits commands (`ShowPending`, `PageToggle`, `HideSilently`, `DismissAfterRelease`, `Dismiss`, `End`) and knows nothing about windows.
3. **Pending wheel** (planned). On Arm, show the wheel window centred under the cursor with only the disc, without activating it, then send one zero-distance mouse move ([SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)) so OLE re-hit-tests even if the pointer is stationary. If no `DragEnter` arrives within 250 ms it was not a file drag (window move, text selection): hide silently.
4. **Hit surface** (planned). A layered window's fully transparent pixels let the mouse, and OLE hit-testing, pass through, so the whole wheel square is painted with alpha ≥ 1 (`#01000000`). Never `WS_EX_TRANSPARENT`. Styles `WS_POPUP | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST` ([Window features](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)), `WM_MOUSEACTIVATE` answers `MA_NOACTIVATE`.
5. **Drop target** (planned). A raw [`IDropTarget`](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nn-oleidl-idroptarget) registered with [`RegisterDragDrop`](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-registerdragdrop) on the wheel window (STA thread). Every call is forwarded to [`IDropTargetHelper`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idroptargethelper) so Explorer's drag image keeps rendering over the wheel. `DragEnter` reads the file list cheaply (`CF_HDROP` or the shell ID list; see [Shell data scenarios](https://learn.microsoft.com/en-us/windows/win32/shell/datascenarios)); heavy formats are only read in `Drop`. Virtual-file data (`CFSTR_FILEDESCRIPTORW`: Outlook attachments, zip-folder entries, phones) and non-file data (browser images) get a "Save the file first" hint and `DROPEFFECT_NONE`.
6. **Never move the user's files.** The returned effect is always `DROPEFFECT_COPY & *pdwEffect`, never MOVE: a Shift-drag means MOVE to Explorer, and a MOVE result would delete the originals. On `Drop`, `CFSTR_PERFORMEDDROPEFFECT = DROPEFFECT_COPY` is also set. Tests assert originals are unchanged.
7. **Explorer's own feedback** (planned). While a tile is highlighted, set `CFSTR_DROPDESCRIPTION` (a [DROPDESCRIPTION](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ns-shlobj_core-dropdescription) with `DROPIMAGE_COPY`, text "Convert to %1") so the native drag image reads "Convert to PNG". The format name is in the [shell clipboard formats](https://learn.microsoft.com/en-us/windows/win32/shell/clipboard).
8. **Modifiers** (built core). `ModifierTracker` is fed by the keyboard hook and reconciled with `grfKeyState` on every `DragOver` (OLE re-calls `DragOver` when key state changes even without motion, and this covers elevated sources the hook cannot see). Tools toggle: fresh Ctrl press (default), mouse-wheel tick, optional Alt.
9. **Neutral zone and hit test** (built, `WheelGeometry`/`WheelModel`). The point where the drag entered the wheel is neutral until the pointer leaves it; sectors are offset by half a step; hub = no selection. Physical pixels throughout (PerMonitorV2); monitor chosen by `MonitorFromPoint`, wheel clamped to its work area.
10. **Known blind spot.** Windows blocks input hooks from seeing higher-integrity windows (UIPI, [security overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)), so drags from an elevated Explorer or app are invisible. See [LIMITS.md](LIMITS.md).

## 4. Wheel rendering (planned)

A normal XAML window cannot be a clean per-pixel-transparent, non-activating popup, so the wheel is a plain Win32 layered window drawn with Win2D into a premultiplied BGRA surface and pushed with [`UpdateLayeredWindow`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) (`ULW_ALPHA`). Glass is a blurred snapshot of the pixels underneath plus a tint. Everything else (tray, toast, settings, editors) is ordinary WinUI 3. Geometry and animation numbers are in PLAN.md §3.2; the pure parts already exist in `Core/Interaction`.

## 5. Converters

| Area | Built on | Notes |
|---|---|---|
| Images | [Windows Imaging Component](https://learn.microsoft.com/en-us/windows/win32/wic/-wic-lh) via the WinRT `BitmapDecoder`/`BitmapEncoder`; SkiaSharp for WebP encode and as decode fallback | 200-megapixel guard, blank-decode check, orientation/EXIF handling, JPEG segment scan for Strip EXIF |
| PDF to images | [`Windows.Data.Pdf`](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf) | ≤2× scale, ≤8000 px long side, white background |
| PDF text | PdfPig | scan and reversed-Hebrew detection refuse with a message |
| PDF structure, images→PDF | PDFsharp 6.2.4 | split, merge, recompress; A4 in metric regions, Letter elsewhere |
| Video/audio (in progress) | [`MediaTranscoder`](https://learn.microsoft.com/en-us/uwp/api/windows.media.transcoding.mediatranscoder) with `MediaEncodingProfile`; Media Foundation source reader/sink writer ([MFCreateSourceReaderFromURL](https://learn.microsoft.com/en-us/windows/win32/api/mfreadwrite/nf-mfreadwrite-mfcreatesourcereaderfromurl)) for probing, stream copy and GIF frames; encoder availability probed through [MFTs](https://learn.microsoft.com/en-us/windows/win32/medfound/media-foundation-transforms); AIFF has its own reader/writer | |
| Documents (in progress) | Open XML SDK for DOCX, hand-written ODT/RTF/HTML/TXT readers and writers around a common model; PDFsharp for PDF output; Word automation when installed | |
| Zip | `System.IO.Compression` | UTC timestamps, reparse-loop and special-file refusal |

Every converter writes through `Naming` (temp file next to the target, then move), so a failed job leaves no partial output, and hashes of the inputs are asserted unchanged in tests.

## 6. Packaging

* **MSIX**, built by the Windows App SDK tooling (`WindowsPackageType=MSIX`), one package per architecture (x64, ARM64), **self-contained**: the .NET 10 runtime and the Windows App SDK ship inside the package, so there is nothing to install first. `scripts/release.ps1` combines both into one `.msixbundle` with `makeappx bundle` ([MakeAppx](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool)) and signs with `signtool` if given a certificate.
* **Two `Application` entries** in `Package.appxmanifest`: `App` (the tray app, `Shiftvert.exe`) and `Cli` (`cli\shiftvert.exe`, hidden from the Start menu). The `shiftvert.exe` command on PATH is an app execution alias ([uap5:AppExecutionAlias](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap5-appexecutionalias)) owned by the `Cli` entry with `desktop4:Subsystem="console"`, so stdout, stderr and the exit code reach the terminal.
* **Why the CLI lives in `cli\` as its own self-contained copy:** file names on Windows are case-insensitive, and the tray app's `Shiftvert.exe`/`Shiftvert.dll` would collide with the CLI's `shiftvert.exe`/`shiftvert.dll`. The app project publishes the CLI (`BundleShiftvertCli` target in `Shiftvert.App.csproj`, under the "Packaging (W4b)" comment) and adds it to the package as content. It costs roughly 45 MB per architecture; opt out with `-p:BundleCli=false` for faster inner-loop builds.
* **Open at Login:** the manifest's `windows.startupTask` (`ShiftvertStartup`, disabled by default), driven by [`StartupTask`](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask); it appears in Settings → Apps → Startup, where the user can also turn it off. Unpackaged dev runs use the HKCU Run key instead.
* **Single instance:** [`AppInstance`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.applifecycle.appinstance) (see §2).
* **Signing:** the manifest Publisher is `CN=Shiftvert Dev`; `packaging/make-dev-cert.ps1` creates a matching self-signed certificate ([creating a signing certificate](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing)). Long paths are enabled by the application manifests.
* **CI:** `.github/workflows/ci.yml` builds and tests on `windows-latest`, then runs `scripts/release.ps1` and uploads the unsigned bundle and `SHA256SUMS.txt`.
