# Test plan

Three layers: automated tests that run anywhere Windows runs (and in CI), an automated drag harness that drives a real build with real input on a real desktop, and a manual checklist for the things only a person at real hardware can judge (several monitors, Narrator, touch, reboot behaviour).

## 1. Automated tests

```powershell
dotnet build MikeShift.sln
dotnet test MikeShift.sln                                   # both test projects
dotnet test tests/MikeShift.Core.Tests --filter "FullyQualifiedName~Naming"     # one area
./scripts/build.ps1 -Configuration Release -Test            # build + test through the helper script
```

| Project | What it covers | How |
|---|---|---|
| `tests/MikeShift.Core.Tests` | Naming and collision handling, `Kind.Of`/`Kind.CommonOf`, `ActionCatalog.For`, capability gating with fake `Capabilities`, every image / PDF / video / audio / document / Zip converter (round trips, size guards, "already small"), the crop and trim models, feedback wording (`FeedbackPlan`, `JobLedger`), `DragSession`, `ModifierTracker`, wheel geometry, placement, model and animation, easing, **hardening** (below) | In-process xUnit. Fixtures are generated at test time (`Infrastructure/Fixtures*.cs`). `OriginalsGuard` hashes every input before and after each converter test and fails if an original changed. |
| `tests/MikeShift.Cli.Tests` | The real `MikeShift.Cli.exe` run as a child process on fixtures: exit codes, printed output paths, usage errors, Zip, image, PDF, media and document commands, corrupt-input cases | Exercises WIC and Media Foundation on CI machines too. |

Tests that need a codec the machine lacks (HEIC/HEVC encoders and the like) are skipped with `[SkipIfNoCapability]`, never deleted; skipped counts differ between machines. On the development machine (2026-10-23, after W5a): **929 Core tests and 70 CLI tests pass, 0 skipped, 0 warnings.**

**Hardening (PLAN Phase 6, "fuzz-ish inputs")** — `Core.Tests/Hardening/HardeningTests.cs` and `Cli.Tests/HardeningCliTests.cs`:

* A corpus of truncated PNG, JPG, MP4, WAV, PDF and DOCX files (cut at 0, 1, 4, 12 bytes, a quarter, a half and one byte short), misnamed files (PNG bytes named `.pdf`, PDF bytes named `.png`/`.jpg`, DOCX bytes named `.pdf`/`.png`/`.odt`/`.doc`, random bytes named `.docx`/`.doc`/`.odt`/`.rtf`/`.png`/`.jpg`/`.pdf`/`.mp4`/`.mp3`/…), and zero-byte files of 23 extensions goes through `Kind.Of`, `ActionCatalog.For` and **every action the catalog offers** via `CliRunner` (about 280 runs). Each must end with exit code 0 or 1; on 1 an `error:` line and no stack trace; within 30 s; with no stray, partial or temporary file next to the original and the original byte-identical. A guard test proves the matrix is not passing just because everything fails.
* The same for a representative 18 cases through the real `MikeShift.Cli.exe` process.
* Selections: 500 JPEGs through `ActionCatalog.For` with a cold `Kind` cache in under 2 s (about 20 ms with `Kind.CommonOf`; the per-file path measured about 225 ms for tiny JPEGs here and 2.5 s for real photos on the reviewer's machine); a 500-file mixed selection is Zip only and fast; a large selection whose last file is a misnamed PDF, or that contains a folder or a vanished file, is Zip only; small selections still classify misnamed files by content.
* Findings fixed on the way: a `.doc` made of random bytes was "converted" by Word into a garbage document (now refused before Word starts); 500-file classification (above).

**Integrity suite** (2026-10-24) — `Core.Tests/Integrity/**` and `Cli.Tests/IntegrityCliTests.cs`: every action the catalog offers for every kind of file, judged by decoders that are not the code that wrote the output (SkiaSharp and WIC for images; PdfPig and Windows.Data.Pdf for PDF; hand-written WAV/AIFF/FLAC/MP3/MP4 parsers plus a Media Foundation decode and cross-correlation for audio and video; the OpenXml SDK + `OpenXmlValidator`, System.Xml and small RTF/HTML tokenizers for documents; System.IO.Compression and Windows' `tar.exe` for Zip). Lossless outputs must be pixel- or sample-exact, lossy ones must reach a PSNR or correlation floor, originals must hash the same afterwards and no `.mikeshift-tmp` may remain. Results, thresholds and the bugs it found: [test-runs/2026-10-24-local-and-integrity.md](test-runs/2026-10-24-local-and-integrity.md).

* The slow part (the Media Foundation matrix, video transcodes, Word) is written with `[SlowFact]` / `[SlowTheory]` and `[Trait("Category", "Slow")]`. Without `MIKESHIFT_SLOW=1` those tests are reported as skipped, so a plain `dotnet test MikeShift.sln` is the fast suite; run everything with `$env:MIKESHIFT_SLOW = '1'; dotnet test MikeShift.sln`. (A `Category` filter alone cannot be made the default: `dotnet test` always passes an empty filter that overrides one set in the project or in a runsettings file.)
* `MIKESHIFT_INTEGRITY_MATRIX=<file>` makes every integrity test append a row (category, input, action, validator, result, metric) to that file; the report's matrix is generated from it.
* `scripts/verify-offline.ps1` runs the whole CLI matrix and a `MikeShift.exe` session while polling for network sockets owned by MikeShift processes; `scripts/audit-network-imports.ps1` lists networking APIs referenced or imported by a build folder.

**Fixtures for manual testing**

```powershell
./scripts/make-fixtures.ps1                 # writes sample PNG/JPG/GIF/TIFF/BMP/WebP, PDF, media and documents to .\fixtures (git-ignored)
./scripts/make-fixtures.ps1 -OutDir D:\fx   # elsewhere
dotnet run --project src/MikeShift.Cli -- --list .\fixtures\sample.png
```

The script runs the `FixtureExport` test with `MIKESHIFT_FIXTURES_OUT` set, so the manual fixtures and the automated tests share one generator.

**CI** (`.github/workflows/ci.yml`, `windows-latest`): job `build-and-test` runs restore, `dotnet build MikeShift.sln -c Release --no-restore`, `dotnet test … --no-build` and uploads `.trx` results; job `msix` runs `scripts/release.ps1` and uploads the unsigned bundle with `SHA256SUMS.txt`.

### Packaging checks done during development

| Check | Result |
|---|---|
| `dotnet build MikeShift.sln` and `dotnet test MikeShift.sln` | pass |
| Unsigned x64 and ARM64 MSIX build (`makeappx` validates the manifest, including the `Cli` application, its `desktop4:Subsystem="console"` alias and that `MikeShift.Cli.exe` is in the package) | pass |
| Package contents: self-contained runtime present (`coreclr.dll`), `MikeShift.Cli.exe` + `MikeShift.exe` side by side in the package root, no name collision, no `cli\` folder; no `onnxruntime.dll`, `DirectML.dll` or other Windows AI payload (W5a) | pass |
| `MikeShift.Cli.exe --list`, `--run JPG`, `--run "Resize 50%"`, `--run Zip` and `--bogus` from an unpacked x64 package run on the build machine (the CLI on the tray app's shared runtime); manifest: `Cli` application and the `mikeshift.exe` alias both point at `MikeShift.Cli.exe` | pass (exit codes 0 and 2) |
| Unpacked package: `MikeShift.exe --smoke-test` exits 0; a Release build ignores `MIKESHIFT_LOG` without `--dev` and honours it with `--dev` | pass |
| Unpackaged `MikeShift.exe --smoke-test` (self-contained Release build starts the tray and the wheel, then exits), before and after the Windows App SDK slimming | pass (exit 0) |
| `scripts/release.ps1` unsigned: `.msixbundle` + `SHA256SUMS.txt`; hash matches `sha256sum` | pass; 304.8 MB before W5a, 256.8 MB after (Windows App SDK by component), 258.5 MB measured before sharing the runtime with the CLI, 157.9 MB after (x64 80.1 MB, ARM64 77.8 MB per package), **88.4 MB** after IL trimming (x64 44.9 MB, ARM64 43.6 MB; per-architecture `.msix` files are written beside the bundle); `-Flavor store` (shared Windows App Runtime): x64 23.3 MB, ARM64 22.5 MB, bundle 45.9 MB. Every check on the trimmed packages (CLI suites against the trimmed exe with `MIKESHIFT_CLI_EXE`, scenes, real-drag harness, installed-package run) is in [test-runs/2026-10-24-package-size.md](test-runs/2026-10-24-package-size.md). |
| `scripts/release.ps1` signed with a throwaway certificate created by `make-dev-cert.ps1` in the CurrentUser store only; signer subject `CN=MikeShift Dev`; certificate and PFX deleted afterwards | pass (signature present; not trusted on this machine, as expected) |
| Installing the bundle, the alias in a fresh terminal, uninstall | **not run** (needs the certificate trusted in LocalMachine\TrustedPeople; see checklist item 16). Because the Windows App SDK packages changed in W5a, install the slimmed bundle at least once before release. |

## 2. The drag harness (`tools/DragHarness`)

A console program that starts its own `MikeShift.exe` (with `--dev`, `MIKESHIFT_ACCEPT_INJECTED=1` and a private log; see ARCHITECTURE.md §8: only Debug builds and unpackaged `--dev` runs honour these), creates a backdrop window, and performs a scripted drag with injected mouse and keyboard input, using either an OLE drag source of its own (`DoDragDrop` with a real `CF_HDROP`, a text or a URL data object) or the **real Explorer** as the source. It then checks what appeared on disk, the window state, the drop effect (COPY, never MOVE), UI Automation names and the log's stage lines, and measures the time from Shift to a visible wheel.

```powershell
dotnet build tools/DragHarness
tools\DragHarness\bin\Debug\net10.0-windows10.0.26100.0\DragHarness.exe --scenario all      # needs the desktop to itself; quit any running MikeShift first
... --scenario copy --screenshot out.png --theme light                                       # one scenario, with a screenshot
... --app <path\to\MikeShift.exe>                                                            # test another build (e.g. the Release output)
```

The Explorer-driven ("anger") scenarios run with `--anger x1a,x1j,xw,x3-taskbar,x8-primary,x5f,...` (`MIKESHIFT_ANGER_ROOT` sets the work folder; results of the 2026-10-24 run: [test-runs/2026-10-24-real-drag.md](test-runs/2026-10-24-real-drag.md)): `x1a` to `x1j` (Explorer drags: formats, tools, range select, multi-select, Zip, 500 files), `x2-primary`, `x2-left`, `x2-cross` (edges, corners, monitor boundary), `x3a` to `x3e` (toggles, release on empty space, Esc, hub, Shift-click rule), `x3-taskbar` (taskbar sweep), `x4a` to `x4d` (Notepad text, title bar, rubber band, file inside a zip), `x5a` to `x5f` (real tray, click mode, toast), `x5g` and `x5i` (tray flyout driven by the keyboard), `xw` (callout pill against the real drag thumbnail), `x8-primary` and `x8-left` (latency, idle cost).

Scenarios of the older harness-source set: `copy`, `tools`, `shiftfirst`, `multi`, `esc`, `nodrop`, `text`, `nodrag`, `virtual`, `leave`, `outside`, `wheeltick`, `leftmonitor`, `edge`, `folder`, `explorer`, `click-keys`, `click-mouse`, `click-esc`, `click-blur`. Move the mouse and keyboard only after it finishes.

Result on 2026-10-23, Release self-contained build, Windows 11, two monitors (the second at negative coordinates): **all 20 scenarios pass**; wheel visible 29 to 47 ms after Shift (PLAN: under 100 ms). The same run with `MIKESHIFT_TRIM_MS=0` (graphics device released and working set trimmed before every drag, the worst case for the memory feature) gives 29 to 45 ms and also passes.

| PLAN §5.3 item | Harness coverage |
|---|---|
| 1 Shift-drag a JPG, wheel < 100 ms, drop on PNG, original unchanged and not moved | `copy` (synthetic OLE source) and `explorer` (the real Explorer window as source). Ctrl-drag and Ctrl+Shift-drag are not scripted. |
| 2 Desktop, second monitor, edge, straddling, negative coordinates | `leftmonitor` (monitor left of the primary) and `edge` (Shift within 30 px of the screen edge, clamped, tiles still hit). The Desktop as a source, mixed-DPI monitors and straddling two monitors are not covered. |
| 3 Shift before the click | `shiftfirst`. The Shift-click exclusion of the last outputs is unit-tested (`DragSession.FilterSelection`), not scripted. |
| 4 Tools/formats toggle, release over empty space, Esc, hub | `tools` (Ctrl), `wheeltick`, `esc`, `nodrop` (release on the hub), `outside` (release elsewhere: dismissed after the timeout, nothing converted), `leave` (DragLeave and back). "Explorer's own drop proceeds" is not scripted. |
| 6 Multi-file, folder | `multi` (two JPGs), `folder` (Zip). Merge PDF for 3 images and the 500-file selection are in the unit tests. |
| 7 Virtual files | `virtual` (a URL/virtual data object: "Save the file first", effect NONE) and `text` / `nodrag` (no wheel). Real Outlook, zip-folder and MTP sources are not covered. |
| 9 Convert Files… click mode | `click-keys`, `click-mouse`, `click-esc`, `click-blur`, plus a UI Automation check that tiles are `ListItem`s. Narrator itself is not covered. |

### 2b. The context-menu harness (`tools/ContextMenuHarness`)

Checks the Explorer right-click entry ([ARCHITECTURE.md §9](ARCHITECTURE.md), results in [test-runs/2026-10-24-context-menu.md](test-runs/2026-10-24-context-menu.md)). It needs a **registered package** (the COM server and the menu entry only exist for an installed or registered MikeShift: `Add-AppxPackage -Register <layout>\AppxManifest.xml`, Developer Mode on, never a certificate import) and the desktop to itself.

```powershell
dotnet build tools/ContextMenuHarness
$h = 'tools\ContextMenuHarness\bin\Debug\net10.0-windows10.0.26100.0\ContextMenuHarness.exe'
& $h probe --repeat 3 --invoke "Convert to PNG" photo.jpg        # plays Explorer: activates the COM server, builds the menu, prints timings; no real input
& $h menu <workfolder> all                                       # real File Explorer, real mouse and keyboard, UI Automation to read the menus
& $h menu <workfolder> jpg-png multi-merge folder-zip mixed heic big600 mixed-big fallback classic more
& $h menu <workfolder> timing 8                                  # right-click to menu on screen, with and without the extension registered
```

`menu` opens its own Explorer window on the work folder (a temp folder you created: `photo1.jpg` to `photo3.jpg`, `sample.png`, `doc.pdf`, `letter.docx`, `IMG_0001.heic`, `folder\`, `big\` with 600 JPEGs, `mixedbig\` with 18 JPEGs and 4 PDFs), selects items through the shell view, right-clicks with `SendInput`, finds the menu and its submenu through UI Automation, clicks real entries, then checks the folder (output beside the original, original SHA-256 unchanged). `HARNESS_SHOTS=<folder>` saves cropped menu screenshots (only the menu bodies are copied, the rest of the picture stays transparent). The tray app is started (and its one-time welcome window closed) first.

## 3. Idle memory and latency measurements (2026-10-23, Release, self-contained)

| State | Working set | Private working set |
|---|---|---|
| Idle 20 s after start, before W5a | 198 MB | 94 MB |
| Idle 20 to 30 s after start, after W5a | about 17 MB | about 5 MB |
| Wheel shown (click mode in a fresh process), before | 216 MB | 106 MB |
| Wheel shown (click mode in a fresh process), after | 226 MB | 116 MB |
| Wheel hidden, 15 s later, after | about 15 MB | about 5 MB |
| Crop editor open / closed 6 s later, after | 326 MB / about 14 MB | 190 MB / about 4 MB |

Measured with `Get-Process` and the `Process V2\Working Set - Private` counter; the PLAN target is "RSS < 100 MB resident". Wheel latency is unchanged (above).

## 4. Manual checklist

Run on a real Windows 11 machine (and, where noted, Windows 10) with the signed dev bundle installed. **Status** is Pass, Fail, Partial or Not run. The statuses below come from the real-drag run of 2026-10-24 ([results](test-runs/2026-10-24-real-drag.md)): the real File Explorer, Notepad and tray driven by the harness's `SendInput` on the development PC (two monitors, the second at negative coordinates, both at 100 % scaling, dark theme, Debug build). "Partial" lists what was not exercised; "Not run" says why. A person on real hardware still has to judge touch, pen, Narrator, scaling and the like.

| # | What to check | Status | Notes (real-drag run 2026-10-24) |
|---|---|---|---|
| 1 | Shift-drag a JPG from Explorer: wheel appears under the cursor in under 100 ms; drop on PNG gives `photo.png` beside it; original unchanged; Explorer did **not** move it. Repeat with Ctrl-drag and Ctrl+Shift-drag. | **Pass** | `x1a`, `x1b` (Ctrl before the click), `x1c` (Ctrl+Shift): `effect=COPY`, originals SHA-256 identical, not moved. Shift to wheel 13 to 28 ms by the log (20 warm drags on each monitor; cold 29 to 31 ms). Also tools Compress and Resize 50% (`x1j`). [Section 1](test-runs/2026-10-24-real-drag.md#1-basic-conversions-real-explorer-shift-drag), [8](test-runs/2026-10-24-real-drag.md#8-latency-and-idle-cost). |
| 2a | Same from the Desktop | **Not run** | `x2-desktop` exists but creates and deletes files on the user's Desktop, which this run was not allowed to touch. |
| 2b | Same from a second monitor at a different scaling; wheel near a monitor edge; straddling two monitors; monitor to the left of the primary (negative coordinates) | **Partial** | Pass: second monitor at negative coordinates, all 8 edges, corners and the extreme pixels of both monitors, across the boundary both ways (`x2-primary`, `x2-left`, `x2-cross`), over the taskbar at 7 x positions (`x3-taskbar`). Not run: different scaling (both monitors are 100 %), straddling two monitors; the second monitor has no taskbar. [Sections 3 and 5](test-runs/2026-10-24-real-drag.md#3-taskbar). |
| 3 | Shift held *before* the click (range select): wheel still works. After converting, repeat with Shift-click: the previously produced file is excluded from the selection | **Pass** | `x1d` (range select of 3, the wheel arms at the drag threshold), `x3e` (`Shift-click rule applied: 2 -> 1 item(s)`). |
| 4 | Ctrl mid-drag goes to Tools; Shift returns to Formats; a mouse-wheel tick toggles; releasing over empty space lets Explorer's own drop proceed; Esc dismisses; dropping on the hub centre does nothing | **Pass** | `x3a` (Ctrl, fresh Shift, wheel notches, sub-notch deltas, Ctrl autorepeat inside and outside the wheel), `x3b` (Explorer's own move proceeded into another of my folders, nothing converted), `x3c` (Esc), `x3d` (hub). |
| 5 | With two input languages installed, the Shift-then-Ctrl gesture does not switch language or layout | **Not run** | Needs a second input language installed; changing OS settings was out of bounds. |
| 6 | Multi-file: 3 images offer Merge PDF; image + PDF offers Zip only; a folder offers Zip; a 500-file selection stays responsive | **Pass** | `x1e` (Merged.pdf, 3 pages), `x1f` (Zip only, the hub is the button), `x1g` (folder zip), `x1h` (500 JPEGs: tiles 0 ms after the wheel, 500 PNGs in 3.7 s), `x1i` ("already PNG"). |
| 7 | Browser image, Outlook attachment, a file inside a `.zip`, a phone (MTP) file: "Save the file first", no crash, Explorer unaffected | **Partial** | Pass: a file inside a `.zip` opened in Explorer (`x4d`): caution wheel with "Save the file first, then drag it", shell drop description set, no conversion, no crash. Not run: browser image, Outlook, MTP phone (not available). |
| 8 | Elevated Explorer window: no wheel (documented limit). MikeShift started elevated: startup warning appears | **Not run** | Needs an elevated session; not done unattended. |
| 9 | Convert Files…: click mode, keyboard navigation, Narrator announces tiles, focus loss dismisses | **Partial** | Pass with the real tray: left click opens the picker, then keyboard (arrows, Tab, Enter), mouse, Esc and focus loss (`x5a` to `x5e`); UIA focus events name the tiles; toast and Show in folder (`x5f`). Narrator itself not run. The tray flyout takes the keyboard (Esc, arrows, Enter, focus loss; O-1 fixed, `x5g`, `x5i`). [Section 7](test-runs/2026-10-24-real-drag.md#7-real-tray). |
| 10 | Crop and Trim editors: presets, slider and pixel-box sync, Esc / Enter, outputs correct | **Not run** | Not driven in this run. The crop editor was driven once by scripted input on 2026-10-23 (not a recorded pass). |
| 11 | Toast states: single job, several jobs ("N still working…"), partial-failure dialog, chime toggle, "in Downloads" fallback (read-only source folder) | **Partial** | Seen: single job with Show in folder (opens Explorer with the output selected), "Saved 500 files", "Saved 2 files … 1 already PNG". Not run: several jobs, partial-failure dialog, chime, Downloads fallback. |
| 12 | Open at Login toggle matches Settings → Apps → Startup and survives a reboot. A second launch pops the tray menu | **Not run** | Needs a reboot and changes the user's startup settings. |
| 13 | Light and dark theme; Animation effects off means no animation; high contrast readable; 100 / 150 / 200 % DPI | **Not run** | This run was dark theme at 100 % only; no OS setting was changed. |
| 14 | Precision touchpad drag, pen, touch; RDP session; sleep/resume mid-session; `explorer.exe` restarted while MikeShift runs; 24 h soak without losing the hook | **Not run** | No pen, touch, touchpad or RDP here; sleep, restarting Explorer and a 24 h soak were out of scope. |
| 15 | No network: Resource Monitor shows zero connections during a full session | **Not run** | Not measured. |
| 16 | Install the `.msixbundle` with the dev certificate; uninstall cleanly; SHA-256 matches `SHA256SUMS.txt`; `mikeshift --list <file>` works from a fresh terminal (and `where mikeshift` points into `WindowsApps`); Start menu shows only MikeShift, not "MikeShift command line" | **Not run** | Needs the trusted certificate and an install; re-run after W5a (the Windows App SDK packages changed). |
| 17 | Windows 10 1809+ machine or VM: install, tray, CLI, Convert Files… | **Not run** | No Windows 10 machine. |
| 18 | ARM64 hardware (or Windows 11 on ARM VM): install the same bundle, run items 12 and 16 | **Not run** | No ARM64 hardware. |
| 19 | Codec variants: with and without HEIF/HEVC extensions the HEIC tile appears/disappears; with and without Word the `.doc` behaviour matches [LIMITS.md](LIMITS.md) | **Not run** | Only one codec configuration here (HEIC offered). |
| 20 | Explorer right-click: on a JPG, three images, a folder, a folder + file, a HEIC, 600 files: Windows 11 modern menu shows "Convert with MikeShift" with the submenu; a pick converts beside the original; More options… opens the wheel; "Show more options" lists it; the first click after a quiet spell is not a hang; Windows 10 1809+ shows it in its menu | **Partial** | Pass on Windows 11 build 26300 through the real File Explorer, both flavours (`tools/ContextMenuHarness menu`, [results](test-runs/2026-10-24-context-menu.md)): all cases above, timings (+ about 50 ms once warm), classic menu lists the entry too on this build. Not run: Windows 10, other Windows 11 builds' classic menu, ARM64. |

Measured in the same run (Debug build): idle after 2 minutes 16.5 MB working set, 6.5 MB private, 0.00 % CPU; Shift to wheel 13 to 28 ms ([section 8](test-runs/2026-10-24-real-drag.md#8-latency-and-idle-cost)).

### Install steps for testers

See the README: create the certificate (`packaging/make-dev-cert.ps1`), trust the `.cer` once in an elevated PowerShell, build and sign (`scripts/release.ps1 -PfxPath …`), then `Add-AppxPackage -Path .\artifacts\MikeShift_<version>.msixbundle`. Remove with `Get-AppxPackage MikeShift | Remove-AppxPackage`, and remove the trust with `Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=MikeShift Dev' | Remove-Item` when finished.
