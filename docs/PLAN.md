> Note (2026-10-23): the product is now called MikeShift; this plan uses its original working name.

# Shiftvert for Windows — build & test handover plan

> Handover for an implementing agent (Opus). Repo: `C:\Users\mjtpe\dev\Orqentis\mikeshift` (empty, `main`, one "Initial commit"). Working name **Shiftvert for Windows**; namespace `Shiftvert`. After approval this file is copied into the repo as `docs/PLAN.md` so the implementer has it in-tree.

## Context

[Shiftvert](https://github.com/roiegold/shiftvert) is a tiny MIT-licensed macOS menu-bar utility: start dragging a file in Finder, hold **Shift**, and a radial wheel of output formats appears around the cursor; tap **Option** to switch the wheel to tools (compress, resize, crop, trim, split, merge, zip); drop on a tile and a converted *copy* appears beside the original. It has no network code and uses only OS frameworks. There is no Windows version. Goal: a fully functioning Windows 10 (1809+) / Windows 11 equivalent with the same interaction model, the same feature matrix wherever Windows can support it, and the same privacy posture (no network, originals never touched).

Decisions made by the owner:

| Decision | Choice |
|---|---|
| Converter dependencies | Windows built-ins (WIC, Media Foundation / `Windows.Media.Transcoding`, `Windows.Data.Pdf`, `System.IO.Compression`) **plus permissive-licence NuGet libraries** compiled in for PDF, documents and WebP. No bundled third-party executables, no network at runtime. |
| UI stack | **Latest .NET + latest WinUI 3** → .NET 10 LTS (10.0.x; .NET 11 ships 10 Nov 2026 as STS — upgrade is a TFM bump) + **Windows App SDK 2.5.1** (stable, 16 Sep 2026). |
| Distribution | **MSIX** (sideload now, Store-ready later). |

**Stack caveat the owner should know (verified, not a blocker):** WinUI 3 XAML windows still cannot do per-pixel transparent, non-activating popups cleanly (open issue microsoft-ui-xaml #1247 since 2019; community backdrop hacks leave borders/white-in-light-theme). The wheel is therefore rendered in a plain Win32 layered HWND with **Win2D** (Microsoft, MIT) inside the WinUI 3 process; every other surface (tray, editors, toasts, settings, first-run) is normal WinUI 3 XAML. This keeps the chosen stack and removes the biggest UI risk.

Everything below that describes the original's behaviour was extracted from `Sources/Shiftvert/{DragWatcher,RadialMenu,Actions,Converters,CropEditor,main}.swift`; the Windows API claims were verified against Microsoft docs (links in `docs/ARCHITECTURE.md` to be written by the implementer from the notes in §3).

---

## 1. Target behaviour (parity spec)

### 1.1 Interaction
- Resident tray app (no main window, no taskbar button). Tray menu: header hint, **Convert Files…** (Ctrl+O), **Open at Login** (toggle with checkmark), **Check for Updates…** (opens the releases page in the browser; the app itself never touches the network), **Version x.y.z**, **Quit**.
- **Shift-drag flow**: user begins an Explorer/Desktop drag of one or more files/folders, then holds **Shift** → wheel appears centred on the cursor, clamped to that monitor's work area, showing the **formats** page.
- **Tools page toggle = Ctrl** (default; also a mouse-wheel tick; configurable in Settings). *Not Alt*: Left Alt+Shift is Windows' default input-language hotkey, and Alt release can send `SC_KEYMENU` to Explorer. A fresh Ctrl press → tools; a fresh Shift press → formats; `Tab` toggles in keyboard mode. The hub hint reads "Ctrl  tools" / "Shift  formats".
- Drop on a highlighted tile → run the action. Release anywhere else / Esc → dismiss; Explorer's own drop proceeds untouched (we never cancel the source's drag).
- **Convert Files…** → file picker (multi-select, files + folders) → wheel opens at screen centre in *click mode* (hover highlights, mouse-down commits, arrows move highlight, Enter picks, Esc cancels, Tab toggles page; focus loss dismisses).
- Feedback: a small HUD toast ("Compress…", "photo 2.png", "3 files saved", "2 saved, 1 skipped", "N still working…", "in Downloads" when the output folder differed), optional chime, modal dialog for failures/partial failures. No system notifications.
- Explorer's drag image gets a drop description while over a tile: "Convert to **PNG**" / "Compress" (via `CFSTR_DROPDESCRIPTION`), so the user sees the pick in the native drag UI too.
- Output naming: original base name + suffix + new extension, uniquified with ` 2`, ` 3`… (`photo.heic` → `photo.png`, collision → `photo 2.png`; Compress → ` compressed`; Resize → ` 50%`; Crop → ` cropped`; Strip EXIF → ` clean`; Trim → ` trimmed`; PDF pages → folder `name pages\name 01.png…`; Split → folder `name split\`; Merge → `Merged.pdf` in the first file's folder; Zip → `name.zip`, or `Archive.zip` for multiple items). Outputs go next to the input; if that folder isn't writable, fall back to Downloads and say so in the toast.
- Multi-select rules: all items same category → that category's list; mixed or unknown → only **Zip**. Actions whose `hideFor` covers every selected extension are hidden; in mixed-extension selections such an action skips files already in the target format and the toast adds "· N already PNG".
- Batch: per-file sequential, one failure doesn't stop the rest; all fail → "None of the N files could be converted: <first reason>"; some fail → partial-failure dialog listing up to 6 failures. Cancel (crop/trim dialog) keeps files already saved.

### 1.2 Feature matrix (the Windows column is the contract)

| Category (detection) | Action | Page | Windows implementation | Notes |
|---|---|---|---|---|
| **Image** – WIC can decode it (PNG, JPG/JFIF, HEIC*, TIFF, GIF, WebP*, BMP, AVIF*, ICO…) | PNG, JPG, TIFF, GIF, BMP | Format | WIC (`Windows.Graphics.Imaging.BitmapEncoder` or `IWICBitmapEncoder` via CsWin32); JPG quality 0.9, transparency flattened on white for JPG; keep orientation/EXIF where supported; GIF rebuilt from upright pixels | 200-megapixel guard; blank-decode check (8×8 render) for truncated HEIC/AVIF |
| | HEIC | Format | Shown only if `CreateEncoder(GUID_ContainerFormatHeif)` succeeds at startup (needs Store "HEIF Image Extensions" + "HEVC Video Extensions") | Mirrors mac's "if the Mac supports it" |
| | WebP | Format | **No WIC WebP encoder exists.** Use **SkiaSharp** (MIT) `SKImage.Encode(SKEncodedImageFormat.Webp, 90)`; decode stays WIC (free WebP extension) with SkiaSharp fallback | Windows gains a format mac lacks |
| | AVIF | — | **Not offered** (no encoder in WIC; libavif not worth bundling). Decode works with the free AV1 extension. | LIMITS.md |
| | PDF | Format | PDFsharp: fit each image on A4/Letter (`RegionInfo.CurrentRegion.IsMetric`), rotate page for landscape, decode ≤3508 px wide, JPEG 0.85; each TIFF frame = page | Multi-select → "Merge PDF" |
| | Compress | Tool | Long side → 3000 px, quality 0.6; JPG (white bg) unless source is HEIC or has real alpha → HEIC if encodable else PNG; refuse animated GIF/APNG; keep only if ≥5 % smaller | ` compressed` |
| | Resize 50 % | Tool | Half long side, same format | ` 50%` |
| | Crop | Tool (interactive) | Crop editor (§3.5) → crop + optional scale, high-quality interpolation, same format | ` cropped` |
| | Strip EXIF | Tool | JPEG: re-encode pixels only, keep orientation; scan output for APP1/XMP/Photoshop segments, else re-encode at 0.95 | ` clean` |
| **Video** – MF can open it (MP4, MOV, M4V, AVI, WMV, MKV; WebM uncertain — test, open by byte-stream if the extension isn't bound) | MP4, M4V | Format | `MediaTranscoder` + `MediaEncodingProfile.CreateMp4` (H.264/AAC); raw `IMFSinkWriter` stream-copy path for same-codec re-wrap | HEVC→H.264 when re-encoding |
| | MOV | — | **Not offered** (MF has no QuickTime muxer) | LIMITS.md |
| | WMV | Format (Windows-only extra) | `CreateWmv` | cheap, keep |
| | GIF | Format | First 15 s, 10 fps, max side 480, loop; frames via `IMFSourceReader` RGB32 → WIC GIF frames with `/grctlext/Delay` | |
| | M4A (extract audio) | Format | `CreateM4a` AAC; requires an audio stream | |
| | MP3 (Windows-only extra) | Format | `CreateMp3` (MF MP3 encoder, Win8+) | mac can't, Windows can |
| | Compress (720p) | Tool | `CreateMp4(HD720p)`, keep only if smaller | |
| | Trim | Tool (interactive) | Trim dialog → `MediaTranscoder.TrimStartTime/TrimStopTime`; stream-copy where possible | ` trimmed` |
| **Audio** – MF can open it (MP3, WAV, M4A/AAC, FLAC, WMA, ALAC; AIFF via own reader) | M4A | Format | AAC 44.1 kHz 256 kbps | |
| | WAV | Format | PCM 16-bit, or 24-bit if source >16-bit (own writer or `CreateWav`) | |
| | AIFF | Format | **Own writer** (big-endian PCM container, ~100 lines) + own reader for AIFF input | parity kept |
| | FLAC | Format | `CreateFlac` (probe encoder MFT with `MFTEnumEx`; Win10 1703+) | |
| | MP3 | Format | `CreateMp3` | |
| | Compress | Tool | AAC 48 kbps × max(2, channels) | |
| | Trim | Tool (interactive) | Lossless cut for WAV/AIFF, else AAC M4A | |
| **PDF** (`%PDF` magic) | PNG, JPG (pages) | Format | `Windows.Data.Pdf.PdfDocument` render at ≤2× and ≤8000 px long side, white background | 1 page → file; n → ` pages` folder, zero-padded |
| | TXT | Format | PdfPig (Apache-2.0) text extraction; empty → "probably a scan"; Hebrew final-form heuristic → refuse as reversed | |
| | Compress | Tool | PDFsharp re-save with image recompression; skip images with SMask/Mask; refuse encrypted; keep only if smaller | ` compressed` |
| | Split | Tool | PDFsharp, one file per page into ` split` folder | |
| | Merge | Tool (multi) | PDFsharp, `Merged.pdf` beside first file | |
| **Document** (DOCX, DOC*, RTF, ODT, HTML, TXT, MD) | PDF, DOCX, RTF, TXT, HTML, ODT | Format | §3.4 pivot model; TXT/MD as plain text; files with embedded pictures → TXT only (parity). Legacy `.doc`: only via Word COM if installed, else TXT not offered | DOCX/ODT must start with `PK` |
| **Anything / folders / mixed** | Zip | Tool | `ZipFile`/`ZipArchive`, UTC timestamps, folder under its own name, multiple → `Archive.zip`; refuse pipes/sockets/reparse loops | always last tile |

`*` = depends on optional Windows codec packages; probe at startup (`Capabilities`) and hide tiles that cannot work.

Category detection (`Kind.Of(path)`): directory → Other; by extension first; `.ts/.mts` must have MPEG-TS sync bytes (0x47 every 188 bytes) else Other (TypeScript!); image only if WIC/Skia can create a decoder; audio/video only if `MFCreateSourceReaderFromURL` succeeds (lazy, cached per path for the drag's lifetime); document by extension list; cloud placeholders (`FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS`) keep the extension verdict without hydrating.

---

## 2. Solution layout

```
mikeshift/
  Shiftvert.sln
  Directory.Build.props          # net10.0-windows10.0.26100.0, SupportedOSPlatformVersion 10.0.17763.0,
                                 # LangVersion latest, Nullable, TreatWarningsAsErrors, PerMonitorV2, long paths
  Directory.Packages.props       # central package versions
  src/
    Shiftvert.Core/              # no UI. Actions, Kind, naming, converters, capability probe, CLI runner
      Actions/   (Action, ActionCatalog, Each/PartialFailure, Zip)
      Convert/   (Images, Media, Pdf, Documents, Naming, Capabilities)
      Native/    (CsWin32 P/Invoke: WIC, MF, DWM, hooks, OLE)
    Shiftvert.App/               # WinUI 3 (Windows App SDK 2.5.1), packaged MSIX
      App.xaml(.cs)              # single instance, tray, input monitor, job runner
      Tray/        (TrayIcon + menu via H.NotifyIcon.WinUI)
      Input/       (LowLevelInputMonitor, IInputMonitor, DragSession state machine, ModifierTracker)
      Wheel/       (WheelHwnd: layered HWND + Win2D renderer; WheelModel; WheelDropTarget: IDropTarget+IDropTargetHelper)
      Editors/     (CropEditorWindow, TrimDialog — WinUI XAML)
      Hud/         (ToastWindow — WinUI XAML, non-activating)
      Settings/    (SettingsWindow, Preferences in ApplicationData.LocalSettings)
    Shiftvert.Cli/               # `shiftvert --list|--run` console exe (same Core); exposed via appExecutionAlias
  tests/
    Shiftvert.Core.Tests/        # xUnit; fixtures generated at test time
    Shiftvert.Cli.Tests/         # child-process integration tests
  packaging/ (Package.appxmanifest, assets, make-dev-cert.ps1)
  scripts/   (build.ps1, make-fixtures.ps1, release.ps1 → .msixbundle + SHA-256)
  docs/      (PLAN.md, ARCHITECTURE.md, LIMITS.md, TESTPLAN.md, README.md)
```

NuGet (central-pinned to current stable at implementation time): `Microsoft.WindowsAppSDK` 2.5.1, `Microsoft.Graphics.Win2D` (wheel rendering), `Microsoft.Windows.CsWin32` (source-gen P/Invoke; add `WinRT` interop for `IDropTarget`), `H.NotifyIcon.WinUI` (tray), `WinUIEx` (window helpers for the XAML windows), `PDFsharp` 6.x (MIT), `PdfPig` (Apache-2.0), `DocumentFormat.OpenXml` (MIT), `SkiaSharp` (MIT; WebP encode), `CommunityToolkit.Mvvm` (optional), xUnit.

---

## 3. Architecture

### 3.1 Drag detection (the hard part; Phase 1 gate)

Windows has no readable global drag pasteboard: the `IDataObject` lives in the source's `DoDragDrop` loop and is only handed to a drop target. Design:

1. **Input monitor** on a dedicated thread with its own message loop: `SetWindowsHookEx(WH_MOUSE_LL)` and `SetWindowsHookEx(WH_KEYBOARD_LL)`. Callbacks copy the struct into a `Channel<InputEvent>` and `CallNextHookEx` **immediately** (allocation-free; a callback that exceeds `LowLevelHooksTimeout` gets the hook silently removed with no notification). Drop `LLMHF_INJECTED` events (our own nudges) but verify touch/pen events are not flagged injected. Watchdog: if a button is down and no events arrive for N s, re-install. Put it behind `IInputMonitor` so a Raw Input (`RIDEV_INPUTSINK`) implementation can be swapped in.
   State machine `DragSession`: `Idle → Pressed(pt0, shiftAtDown) → Dragging (moved ≥ SM_CXDRAG/SM_CYDRAG) → Armed (Shift down while Dragging) → Idle on LBUTTONUP`. Shift held *before* the click still arms once the threshold is crossed (Explorer range-select case), gated by `DragEnter` actually arriving. Missed button-up: if `GetAsyncKeyState(VK_LBUTTON)` reads up for >100 ms at rest, end. After button-up, dismiss ~400 ms later if nothing was picked.
2. **Armed → show the wheel window centred under the cursor in "pending" state** (disc only, no tiles), `SW_SHOWNOACTIVATE` / `SWP_NOACTIVATE`, then `SendInput(MOUSEEVENTF_MOVE, 0, 0)` once so OLE re-hit-tests even if the pointer is stationary. OLE calls our `IDropTarget::DragEnter` with the data object → read `CF_HDROP` (cheap `DragQueryFile`) or `CFSTR_SHELLIDLIST` → `IShellItem` file-system paths → `ActionCatalog.For(paths)` → populate tiles and animate in. **If no `DragEnter` within 250 ms of showing, hide silently** (window move, text selection, non-OLE drag). If only virtual-file formats are present (`CFSTR_FILEDESCRIPTORW`: Outlook attachments, zip-folder entries, phones) or no file formats at all (browser images/URLs) → show a single hint "Save the file first" and return `DROPEFFECT_NONE`. Never call `GetData` for heavy formats in `DragEnter`/`DragOver` (it blocks Explorer's drag loop); only in `Drop`.
3. **Hit surface**: a layered window's pixels with alpha 0 let the mouse — and OLE hit-testing — pass through. The wheel's whole 380×380 square must be painted with alpha ≥ 1 (`#01000000`) while shown. Never set `WS_EX_TRANSPARENT`. Styles: `WS_POPUP | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST`; answer `WM_MOUSEACTIVATE` with `MA_NOACTIVATE`; keep enabled (OLE skips disabled/hidden windows).
4. **Drop target**: raw `IDropTarget` registered with `RegisterDragDrop` on the wheel HWND (thread is STA, `OleInitialize` called; S_FALSE is fine). Forward every call to `IDropTargetHelper` (`CLSID_DragDropHelper`) so Explorer's drag image keeps rendering over the wheel. Set `CFSTR_DROPDESCRIPTION` (`DROPIMAGE_COPY`, "Convert to %1" / tool name) as the highlight changes. **Effects: always `DROPEFFECT_COPY & *pdwEffect` (never MOVE)**; Shift-drag means MOVE to Explorer, and a MOVE result would delete the originals. On `Drop`, also set `CFSTR_PERFORMEDDROPEFFECT = DROPEFFECT_COPY`, snapshot paths, return immediately, queue the job. A test asserts the originals exist unchanged after every drop.
5. **Modifiers**: `ModifierTracker` fed by the keyboard hook; reconciled with `grfKeyState` on every `DragOver` (OLE re-calls `DragOver` on key-state change even without motion, and this covers elevated sources the hook can't see). Page toggle: Ctrl (fresh press), mouse-wheel tick (hook sees `WM_MOUSEWHEEL`), configurable.
6. **Neutral zone** (mac parity): the point where the drag entered the wheel is neutral; nothing highlights until the pointer leaves it. Hit-test radius 52–180 px (DPI-scaled) selects the sector; sectors offset by half a step so any direction outside the hub maps to a tile; inside the hub = none.
7. **Shift-click gotcha**: if Shift was down at mouse-down, >1 item selected, and the previous conversion finished <20 s ago, drop the previously produced files from the selection, only if ≥1 fresh file remains.
8. **DPI / monitors**: PerMonitorV2 manifest; hook points and `IDropTarget` points are physical pixels; centre on the cursor, choose monitor via `MonitorFromPoint`, clamp to its work area; re-render at that monitor's DPI.
9. **Known blind spot**: elevated (admin) Explorer/app windows are invisible to medium-IL hooks (UIPI). Document in LIMITS.md; a `uiAccess` build is a possible future.

Fallback behind a feature flag (only if the Phase 1 spike shows OLE doesn't deliver `DragEnter` to a window that appears under a stationary pointer even with the nudge): a per-monitor full-work-area "sniffer" drop target shown ≤250 ms to read the file list, then shrunk to the wheel.

### 3.2 Wheel rendering (Win2D in a layered HWND)

- `WheelHwnd`: a pre-created Win32 window (hidden at startup so first show is one frame), rendered each frame with Win2D `CanvasRenderTarget` (premultiplied BGRA) → `UpdateLayeredWindow` with `ULW_ALPHA`. 380×380 logical px at 60 fps is trivial. Same class serves click mode (activatable, keyboard focus, `WM_KEYDOWN` handling) and drag mode (non-activating).
- Glass: capture the screen rect beneath the wheel just before showing (`Graphics.CopyFromScreen` / `Windows.Graphics.Capture` is overkill), apply Win2D `GaussianBlurEffect` + tint (dark: 5 % white hub, light: 45 %), draw as the disc background. Deterministic in both themes; no DWM backdrop needed.
- Geometry (logical px, from mac): window 380; disc r=150; ring inner r=56, outer r=139; tile corner radius 11, gap 6; hub circle r=50; label at r≈97.5 in a 76×40 box; highlighted tile pushed 9 px outward; pick pulse 18 px. n segments of 2π/n clockwise from 12 o'clock.
- Visual: tiles vertical gradient (dark 17→8 % white; light 97→92 %), 1 px rim; highlighted = system accent (`UISettings.GetColorValue(UIColorType.Accent)`) lightened top/darkened bottom, white label; centre pill "CONVERT"/"TOOLS" 11 px semibold with tracking (shows hovered action's name, bold); hint under pill 9.5 px; format tiles show extension 13.5 px bold (12 px if >4 chars); tool tiles show a Segoe Fluent Icons glyph 15 px + 8.5 px caption. Theme from `UISettings` / `Application.RequestedTheme`.
- Animations (durations 0 when `UISettings.AnimationsEnabled` is false): open disc 60→100 % + fade 0.28 s with overshoot easing (cubic-bezier 0.3,1.25,0.4,1); tiles pop out staggered 10 ms; page switch: fade 0.12 s out, slide in from 16 px inside over 0.3 s; collapse: 170 % + fade 0.2 s ease-in (render target is 380 px, so clamp overshoot to the window or grow the window for the collapse frame), picked tile pulses 18 px 0.16 s; dismiss fade 0.2 s; generation counter guards stale dismisses.
- Accessibility: a `UIAutomation` provider (`IRawElementProviderSimple` via CsWin32) exposing tiles as list items; arrows move highlight and raise `UIA_LiveRegionChanged`/`Narrator` announcements; Enter/Esc/Tab as in §1.1.

### 3.3 Job runner & feedback
`JobRunner` (App): assign id; non-interactive → toast immediately; interactive (Crop/Trim) → toast after editor closes. Run `Action.Run(paths, ct)` on the thread pool; marshal to UI; outcomes per §1.1; "N still working…" when others run. Toast = small non-activating topmost WinUI window at bottom-centre of the active monitor (WinUIEx helpers for no-activate/tool-window), auto-hide 2.5 s after last change. Chime = `Windows.Media.Playback.MediaPlayer` playing a bundled short wav. Preferences in `ApplicationData.Current.LocalSettings`: `chime`, `showsToast`, `toolsKey` (Ctrl default), `launched`.

### 3.4 Converters (Core) — specifics
- **Naming**: `Naming.Output(input, newExt, suffix)` → same dir, `base + suffix`; `Naming.Unique(dir, base, ext)` appends ` 2`, ` 3`…; create with `FileMode.CreateNew`; write to a temp file in the same directory, then `File.Move`, so a failed job never leaves a partial file.
- **Capabilities probe** (startup, cached): WIC encoder GUIDs (HEIF), SkiaSharp present, `MFTEnumEx` for MP3/FLAC/H.264/AAC encoders and HEVC/VP9/AV1 decoders, `.webm` openability, Word COM (`HKCR\Word.Application`).
- **Images**: WIC decode/encode; metadata copy via `BitmapPropertiesView`/`IWICMetadataBlockWriter`; SkiaSharp only for WebP encode and as decode fallback.
- **Media**: `Windows.Media.Transcoding.MediaTranscoder` with `MediaEncodingProfile.Create{Mp4,M4a,Mp3,Flac,Wav,Wmv}` and `TrimStartTime/TrimStopTime`; `HardwareAccelerationEnabled = true`; fall back to a hand-rolled `IMFSourceReader → IMFSinkWriter` path only for stream-copy re-wrap and GIF frame grabbing. AIFF reader/writer hand-written (PCM big-endian; `COMM`/`SSND` chunks).
- **PDF**: `Windows.Data.Pdf` render (works packaged); PdfPig text; PDFsharp structure ops; encrypted → refuse with clear message.
- **Documents**: pivot `DocModel` (paragraphs, runs with bold/italic/underline, headings, lists, simple tables, page breaks; flag `HasPictures`). Readers: DOCX (Open XML SDK), ODT (zip + `content.xml`), RTF (hand tokenizer for the text/paragraph/basic char-format subset), HTML (tolerant mini parser; p/h1-6/ul/ol/li/b/i/u/br/a text only; no fetching), TXT/MD (plain). Writers: DOCX (Open XML SDK), ODT, RTF, HTML, TXT, PDF (PDFsharp text layout, 54 pt margins, black text, length limits 2 000 000 chars / 400 000 for RTL). `HasPictures` → only TXT offered (parity). If Word COM exists, `DOC/DOCX → PDF` uses `ExportAsFixedFormat` for fidelity; else the pivot path. TXT encoding: BOM → UTF-8 → heuristic (runs of 3 bytes in 0xC0–0xFA → Windows-1255, else Windows-1252).
- **Hebrew/RTL check** for PDF→TXT: count final-form letters (ך ם ן ף ץ) at word end vs word start; mostly at start → refuse as reversed.
- **Zip**: `ZipArchive`, UTC entry times, skip reparse-point loops, refuse named pipes; long-path aware.

### 3.5 Editors (WinUI 3 XAML windows)
- **Crop editor**: ~640 px floating window, title "Crop — name.jpg"; downscaled preview (≤1600 px); draggable crop box with rule-of-thirds grid and corner brackets; presets Free/Original/1:1/4:5/3:2/16:9/9:16; scale slider 5–100 % synced with W/H pixel boxes (pixels may go below 5 %); min box 16 px; Reset / Cancel (Esc) / Save (Enter; commits any pending text entry; beeps if invalid). Returns `CropChoice{RectPx (top-left origin), OutputSize}`.
- **Trim dialog**: start/end `m:ss.f` fields + range slider with thumbnails (MF frame grab), duration label, Cancel/Trim. Returns `TimeRange`.

### 3.6 CLI (`Shiftvert.Cli`, exposed as `shiftvert.exe` via `windows.appExecutionAlias`)
- `--list FILES…` → comma-separated action titles, exit 0.
- `--run TITLE [--range m:ss-m:ss] FILES…` → prints each output path, exit 0; `error: …` to stderr, exit 1; unknown title lists actions, exit 1; `Trim` without `--range` exits 1 with syntax help.
- Any other `--flag` → usage, exit 2. The CLI is the backbone of automated tests.

### 3.7 Packaging / startup / single instance
- MSIX via the Windows App SDK packaged project (`WindowsPackageType=MSIX`, `WindowsAppSDKSelfContained=true`), `runFullTrust`, `windows.startupTask` for **Open at Login** (`StartupTask.GetAsync` → Enable/Disable; reflects Settings → Apps → Startup), `windows.appExecutionAlias` for `shiftvert.exe`, x64 + arm64 `.msixbundle`, SHA-256 published beside it. Long paths enabled in the manifest.
- Signing: `packaging/make-dev-cert.ps1` creates a self-signed cert for local install (document the one-time Trusted People import). Release signing is the owner's call later (Azure Trusted Signing, purchased cert, or Store).
- Single instance: `AppInstance.FindOrRegisterForKey("shiftvert")`; a second launch redirects and pops the tray menu at the cursor. First run: welcome dialog explaining Shift-drag and Ctrl, with an "Open at login" checkbox.

---

## 4. Phased build order (each phase ends green before the next)

**Phase 0 — Scaffold (½ day)**: solution, projects, `Directory.*.props`, CI (`dotnet build`, `dotnet test`, MSIX build on `windows-latest`), `scripts/make-fixtures.ps1` generating tiny PNG/JPG/GIF/TIFF/BMP/WebP, WAV/AIFF, a 2-second MP4 (via `MediaTranscoder` in a helper), a 3-page PDF (PDFsharp), DOCX/RTF/ODT/HTML/TXT/MD samples. Copy this plan to `docs/PLAN.md`.

**Phase 1 — Spikes / gates (1–2 days)**, throwaway code that must pass before anything else:
1. `spike/DragProbe`: hooks + pending layered window (`#01000000` fill) + `SendInput` nudge + raw `IDropTarget` reading `CF_HDROP` + COPY effect + `IDropTargetHelper` + drop description. Pass criteria: works from an Explorer window, the Desktop, a Library/Search folder, OneDrive placeholder; zip-folder entry and browser image give the "save first" hint; Shift before vs after click; Ctrl toggles while stationary inside the wheel (DragOver fires) and outside (hook); two monitors with different DPI; precision-touchpad drag; **originals untouched** after a drop; window-title-bar drag with Shift shows nothing (or hides within 250 ms); a 2 s UI-thread stall doesn't lose the hook.
2. `spike/WheelRender`: Win2D → `UpdateLayeredWindow` disc with blurred screen snapshot, both themes, DPI 100/150/200, over a full-screen app, 60 fps animation cost measured. (Optional 4 h side-spike: a WinUI XAML window with WinUIEx `TransparentTintBackdrop` — adopt only if it is clean in light **and** dark theme and non-activating; otherwise stay with Win2D.)

**Phase 2 — Core converters + CLI (3–4 days)**: Naming, Kind, Capabilities, ActionCatalog, images, zip, PDF, then media, then documents. Each converter lands with its xUnit tests. CLI complete.

**Phase 3 — Wheel + drag integration (2–3 days)**: `WheelHwnd` (both modes), `DragSession`, `ModifierTracker`, tiles/pages/animations, keyboard + UIA, HUD toast, job runner, Convert Files… flow, tray menu.

**Phase 4 — Editors (1–2 days)**: Crop editor, Trim dialog, wiring as interactive actions.

**Phase 5 — Packaging & polish (1–2 days)**: MSIX, startup task, exec alias, single instance, welcome dialog, settings (chime/toast/tools key), prewarm, README/LIMITS/ARCHITECTURE docs, release script with SHA-256.

**Phase 6 — Test pass & hardening (1–2 days)**: full manual checklist (§5.3), performance (wheel visible <100 ms after Shift; idle CPU ≈0 %; RSS <100 MB resident), fuzz-ish inputs (truncated/misnamed files, 500-file selections), code review.

---

## 5. Verification

### 5.1 Unit tests (`Shiftvert.Core.Tests`, `dotnet test`)
- `Naming`: suffixing, uniquing ` 2`/` 3`, extension change, temp-then-move, non-writable dir fallback.
- `Kind.Of`: every extension in the matrix; `.ts` MPEG-TS vs TypeScript; directory; cloud placeholder attribute; unknown; misnamed files (PNG bytes named `.jpg`).
- `ActionCatalog.For`: per-category lists and order; mixed → Zip only; `hideFor` filtering; Merge PDF only for ≥2 images; Zip always last; capability-dependent tiles hidden with a fake `Capabilities`.
- Images: each conversion round-trips (decode output, check dimensions/format); JPG from RGBA is opaque; WebP via Skia; Compress ≥5 % smaller or "already small"; Resize halves; Strip EXIF output has no APP1; 200 MP guard; blank-decode guard; animated GIF refused by Compress.
- Zip: single file, folder-under-own-name, multiple → Archive.zip, UTC timestamps, reparse loop refused.
- PDF: pages→PNG count and size cap, TXT extraction, Hebrew reversed heuristic, Split count, Merge page count, Compress smaller-or-skip, encrypted refused, images→PDF page size/orientation.
- Media: 2 s MP4 → M4A/MP3/GIF (15 s cap, ≤480 px, loops), Compress 720p smaller, Trim 0:00.5–0:01.5 ≈1 s, MOV→MP4, MKV→MP4 if decodable; WAV→M4A/FLAC/MP3/AIFF; AIFF→WAV round-trip bit-exact; audio Trim.
- Documents: each reader→writer pair; `HasPictures` → TXT only; encoding heuristic; `PK` validation; length limits; RTF subset.
- `Each`: partial failure aggregation (≤6 listed), cancellation keeps earlier outputs.
- Originals untouched: a helper hashes inputs before every converter test and asserts equality after.
- `DragSession` state machine (pure, event-driven): threshold, Shift-before/after, missed button-up, 400 ms dismiss, 20 s Shift-click exclusion, Ctrl fresh-press detection, injected-event filtering.
- Wheel hit-testing: sector maths at several n, neutral zone, hub dead zone, DPI scaling.

### 5.2 CLI integration tests
Run `shiftvert --list`/`--run` as a child process on every fixture; assert printed paths exist and exit codes match. This also exercises WIC/MF on `windows-latest` CI (HEIC/HEVC encoders are absent there → `[SkipIfNoCapability]`).

### 5.3 Manual / UI checklist (run on a real Windows 11 machine; results recorded in `docs/TESTPLAN.md`)
1. Shift-drag a JPG from Explorer → wheel under cursor in <100 ms; drop on PNG → `photo.png` beside it; original unchanged; Explorer did **not** move it. Repeat with Ctrl-drag and Ctrl+Shift-drag.
2. From the Desktop; from a second monitor at different scaling; wheel near a monitor edge and straddling two monitors; monitor left of primary (negative coords).
3. Shift held *before* click (range select) → wheel still works; after converting, repeat with Shift-click → previously produced file excluded.
4. Ctrl mid-drag → tools page; Shift → back; mouse-wheel tick toggles; release over empty space → Explorer's own drop proceeds; Esc dismisses; drop on hub centre → nothing.
5. Two input languages installed: the Shift-then-Ctrl gesture must not switch language/layout.
6. Multi-file: 3 images → Merge PDF; image + PDF → Zip only; folder → Zip; 500 files → responsive.
7. Browser image, Outlook attachment, file inside a .zip, phone (MTP) → "Save the file first"; no crash; Explorer unaffected.
8. Elevated Explorer window → no wheel (documented); our app accidentally elevated → startup warning.
9. Convert Files… → click mode, keyboard navigation, Narrator announces tiles; focus loss dismisses.
10. Crop and Trim editors: presets, slider/pixel sync, Esc/Enter, outputs correct.
11. Toast states: single, multiple jobs, partial-failure dialog, chime toggle, "in Downloads" fallback (read-only source folder).
12. Open at Login toggle reflects Settings → Apps → Startup; survives reboot. Second launch pops the tray menu.
13. Light/dark theme; Animation effects off → no animations; high contrast readable; 100/150/200 % DPI.
14. Precision touchpad drag, pen, touch; RDP session; sleep/resume mid-session; `explorer.exe` restarted while running; 24 h soak without hook loss.
15. No network: Resource Monitor shows zero connections during a full session.
16. Install `.msixbundle` with the dev cert; uninstall cleanly; SHA-256 matches; `shiftvert --list` works from a fresh terminal.

### 5.4 Definition of done
All §5.1/5.2 green in CI; §5.3 recorded with pass/fail; `docs/LIMITS.md` lists every Windows-specific gap (MOV/AVIF output, HEIC dependent on codec packs, WebM input uncertain, elevated windows, virtual files, legacy `.doc` needs Word); `scripts/release.ps1` produces the bundle + SHA-256.

---

## 6. Known risks & mitigations
| Risk | Mitigation |
|---|---|
| OLE may not re-target a window that appears under a stationary pointer | `SendInput` zero-move nudge; Phase 1 gate; sniffer-overlay fallback behind a flag |
| Alpha-0 pixels are hit-test holes | Whole wheel square painted `#01000000`; test in spike |
| Silent low-level-hook removal (callback too slow) | Dedicated thread, enqueue-only callbacks, watchdog, `IInputMonitor` with Raw Input alternative |
| Explorer moving files on Shift-drag | Always COPY effect + `PERFORMEDDROPEFFECT=COPY`; test asserts originals remain |
| Alt+Shift language hotkey | Ctrl is the tools key; mouse wheel as second toggle; configurable |
| WinUI 3 transparent-window limitations | Wheel is a Win2D layered HWND; XAML only for rectangular windows |
| Codec availability varies per machine (HEIC/HEVC paid, VP9/AV1/WebP optional) | Startup probe hides tiles; LIMITS.md; Settings explains |
| UIPI: elevated sources invisible | Documented; `uiAccess` build later if users ask |
| Virtual-file sources deliver streams valid only during `Drop` | Detect in `DragEnter`, refuse with hint (v1) |
| MSIX signing friction for sideload | Dev-cert script + docs; owner picks release signing |
| .NET 11 lands mid-build | Stay on .NET 10 LTS; bump TFM later |
