# Test plan

Three layers: automated tests that run anywhere Windows runs (and in CI), an automated drag harness that drives a real build with real input on a real desktop, and a manual checklist for the things only a person at real hardware can judge (several monitors, Narrator, touch, reboot behaviour).

## 1. Automated tests

```powershell
dotnet build Shiftvert.sln
dotnet test Shiftvert.sln                                   # both test projects
dotnet test tests/Shiftvert.Core.Tests --filter "FullyQualifiedName~Naming"     # one area
./scripts/build.ps1 -Configuration Release -Test            # build + test through the helper script
```

| Project | What it covers | How |
|---|---|---|
| `tests/Shiftvert.Core.Tests` | Naming and collision handling, `Kind.Of`/`Kind.CommonOf`, `ActionCatalog.For`, capability gating with fake `Capabilities`, every image / PDF / video / audio / document / Zip converter (round trips, size guards, "already small"), the crop and trim models, feedback wording (`FeedbackPlan`, `JobLedger`), `DragSession`, `ModifierTracker`, wheel geometry, placement, model and animation, easing, **hardening** (below) | In-process xUnit. Fixtures are generated at test time (`Infrastructure/Fixtures*.cs`). `OriginalsGuard` hashes every input before and after each converter test and fails if an original changed. |
| `tests/Shiftvert.Cli.Tests` | The real `shiftvert.exe` run as a child process on fixtures: exit codes, printed output paths, usage errors, Zip, image, PDF, media and document commands, corrupt-input cases | Exercises WIC and Media Foundation on CI machines too. |

Tests that need a codec the machine lacks (HEIC/HEVC encoders and the like) are skipped with `[SkipIfNoCapability]`, never deleted; skipped counts differ between machines. On the development machine (2026-10-23, after W5a): **929 Core tests and 70 CLI tests pass, 0 skipped, 0 warnings.**

**Hardening (PLAN Phase 6, "fuzz-ish inputs")** — `Core.Tests/Hardening/HardeningTests.cs` and `Cli.Tests/HardeningCliTests.cs`:

* A corpus of truncated PNG, JPG, MP4, WAV, PDF and DOCX files (cut at 0, 1, 4, 12 bytes, a quarter, a half and one byte short), misnamed files (PNG bytes named `.pdf`, PDF bytes named `.png`/`.jpg`, DOCX bytes named `.pdf`/`.png`/`.odt`/`.doc`, random bytes named `.docx`/`.doc`/`.odt`/`.rtf`/`.png`/`.jpg`/`.pdf`/`.mp4`/`.mp3`/…), and zero-byte files of 23 extensions goes through `Kind.Of`, `ActionCatalog.For` and **every action the catalog offers** via `CliRunner` (about 280 runs). Each must end with exit code 0 or 1; on 1 an `error:` line and no stack trace; within 30 s; with no stray, partial or temporary file next to the original and the original byte-identical. A guard test proves the matrix is not passing just because everything fails.
* The same for a representative 18 cases through the real `shiftvert.exe` process.
* Selections: 500 JPEGs through `ActionCatalog.For` with a cold `Kind` cache in under 2 s (about 20 ms with `Kind.CommonOf`; the per-file path measured about 225 ms for tiny JPEGs here and 2.5 s for real photos on the reviewer's machine); a 500-file mixed selection is Zip only and fast; a large selection whose last file is a misnamed PDF, or that contains a folder or a vanished file, is Zip only; small selections still classify misnamed files by content.
* Findings fixed on the way: a `.doc` made of random bytes was "converted" by Word into a garbage document (now refused before Word starts); 500-file classification (above).

**Fixtures for manual testing**

```powershell
./scripts/make-fixtures.ps1                 # writes sample PNG/JPG/GIF/TIFF/BMP/WebP, PDF, media and documents to .\fixtures (git-ignored)
./scripts/make-fixtures.ps1 -OutDir D:\fx   # elsewhere
dotnet run --project src/Shiftvert.Cli -- --list .\fixtures\sample.png
```

The script runs the `FixtureExport` test with `SHIFTVERT_FIXTURES_OUT` set, so the manual fixtures and the automated tests share one generator.

**CI** (`.github/workflows/ci.yml`, `windows-latest`): job `build-and-test` runs restore, `dotnet build Shiftvert.sln -c Release --no-restore`, `dotnet test … --no-build` and uploads `.trx` results; job `msix` runs `scripts/release.ps1` and uploads the unsigned bundle with `SHA256SUMS.txt`.

### Packaging checks done during development

| Check | Result |
|---|---|
| `dotnet build Shiftvert.sln` and `dotnet test Shiftvert.sln` | pass |
| Unsigned x64 and ARM64 MSIX build (`makeappx` validates the manifest, including the `Cli` application, its `desktop4:Subsystem="console"` alias and that `cli\shiftvert.exe` is in the package) | pass |
| Package contents: self-contained runtime present (`coreclr.dll`), `cli\shiftvert.exe` + `Shiftvert.exe` side by side, no name collision; no `onnxruntime.dll`, `DirectML.dll` or other Windows AI payload (W5a) | pass |
| `cli\shiftvert.exe --list <file>` and `--bogus` from an unpacked package run on the build machine | pass (exit codes 0 and 2) |
| Unpackaged `Shiftvert.exe --smoke-test` (self-contained Release build starts the tray and the wheel, then exits), before and after the Windows App SDK slimming | pass (exit 0) |
| `scripts/release.ps1` unsigned: `.msixbundle` + `SHA256SUMS.txt`; hash matches `sha256sum` | pass; 304.8 MB before W5a, **256.8 MB** after (Windows App SDK by component) |
| `scripts/release.ps1` signed with a throwaway certificate created by `make-dev-cert.ps1` in the CurrentUser store only; signer subject `CN=Shiftvert Dev`; certificate and PFX deleted afterwards | pass (signature present; not trusted on this machine, as expected) |
| Installing the bundle, the alias in a fresh terminal, uninstall | **not run** (needs the certificate trusted in LocalMachine\TrustedPeople; see checklist item 16). Because the Windows App SDK packages changed in W5a, install the slimmed bundle at least once before release. |

## 2. The drag harness (`tools/DragHarness`)

A console program that starts its own `Shiftvert.exe` (with `SHIFTVERT_ACCEPT_INJECTED=1` and a private log), creates a backdrop window, and performs a scripted drag with injected mouse and keyboard input, using either an OLE drag source of its own (`DoDragDrop` with a real `CF_HDROP`, a text or a URL data object) or the **real Explorer** as the source. It then checks what appeared on disk, the window state, the drop effect (COPY, never MOVE), UI Automation names and the log's stage lines, and measures the time from Shift to a visible wheel.

```powershell
dotnet build tools/DragHarness
tools\DragHarness\bin\Debug\net10.0-windows10.0.26100.0\DragHarness.exe --scenario all      # needs the desktop to itself; quit any running Shiftvert first
... --scenario copy --screenshot out.png --theme light                                       # one scenario, with a screenshot
... --app <path\to\Shiftvert.exe>                                                            # test another build (e.g. the Release output)
```

Scenarios: `copy`, `tools`, `shiftfirst`, `multi`, `esc`, `nodrop`, `text`, `nodrag`, `virtual`, `leave`, `outside`, `wheeltick`, `leftmonitor`, `edge`, `folder`, `explorer`, `click-keys`, `click-mouse`, `click-esc`, `click-blur`. Move the mouse and keyboard only after it finishes.

Result on 2026-10-23, Release self-contained build, Windows 11, two monitors (the second at negative coordinates): **all 20 scenarios pass**; wheel visible 29 to 47 ms after Shift (PLAN: under 100 ms). The same run with `SHIFTVERT_TRIM_MS=0` (graphics device released and working set trimmed before every drag, the worst case for the memory feature) gives 29 to 45 ms and also passes.

| PLAN §5.3 item | Harness coverage |
|---|---|
| 1 Shift-drag a JPG, wheel < 100 ms, drop on PNG, original unchanged and not moved | `copy` (synthetic OLE source) and `explorer` (the real Explorer window as source). Ctrl-drag and Ctrl+Shift-drag are not scripted. |
| 2 Desktop, second monitor, edge, straddling, negative coordinates | `leftmonitor` (monitor left of the primary) and `edge` (Shift within 30 px of the screen edge, clamped, tiles still hit). The Desktop as a source, mixed-DPI monitors and straddling two monitors are not covered. |
| 3 Shift before the click | `shiftfirst`. The Shift-click exclusion of the last outputs is unit-tested (`DragSession.FilterSelection`), not scripted. |
| 4 Tools/formats toggle, release over empty space, Esc, hub | `tools` (Ctrl), `wheeltick`, `esc`, `nodrop` (release on the hub), `outside` (release elsewhere: dismissed after the timeout, nothing converted), `leave` (DragLeave and back). "Explorer's own drop proceeds" is not scripted. |
| 6 Multi-file, folder | `multi` (two JPGs), `folder` (Zip). Merge PDF for 3 images and the 500-file selection are in the unit tests. |
| 7 Virtual files | `virtual` (a URL/virtual data object: "Save the file first", effect NONE) and `text` / `nodrag` (no wheel). Real Outlook, zip-folder and MTP sources are not covered. |
| 9 Convert Files… click mode | `click-keys`, `click-mouse`, `click-esc`, `click-blur`, plus a UI Automation check that tiles are `ListItem`s. Narrator itself is not covered. |

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

Run on a real Windows 11 machine (and, where noted, Windows 10) with the signed dev bundle installed. Record the outcome by moving an `x` into one column and adding notes (build/date/who). **Automated (harness)** means the scripted harness above covered the item (fully, or "partly" as the notes say) on 2026-10-23 on the development PC; the part only real hardware or a person can judge stays **Not run**.

| # | What to check | Pass | Fail | Not run | Automated (harness) 2026-10-23 | Notes |
|---|---|:-:|:-:|:-:|:-:|---|
| 1 | Shift-drag a JPG from Explorer: wheel appears under the cursor in under 100 ms; drop on PNG gives `photo.png` beside it; original unchanged; Explorer did **not** move it. Repeat with Ctrl-drag and Ctrl+Shift-drag. | | | x | x (partly) | `copy`, `explorer`: Shift-drag only, 29 to 47 ms. Ctrl and Ctrl+Shift variants not run. |
| 2a | Same from the Desktop | | | x | | |
| 2b | Same from a second monitor at a different scaling; wheel near a monitor edge; straddling two monitors; monitor to the left of the primary (negative coordinates) | | | x | x (partly) | `leftmonitor`, `edge`. Different scaling and straddling not run. |
| 3 | Shift held *before* the click (range select): wheel still works. After converting, repeat with Shift-click: the previously produced file is excluded from the selection | | | x | x (partly) | `shiftfirst`. Shift-click exclusion: unit tests only. |
| 4 | Ctrl mid-drag goes to Tools; Shift returns to Formats; a mouse-wheel tick toggles; releasing over empty space lets Explorer's own drop proceed; Esc dismisses; dropping on the hub centre does nothing | | | x | x (partly) | `tools`, `wheeltick`, `esc`, `nodrop`, `outside`, `leave`. "Explorer's own drop proceeds" and Shift-back-to-Formats not scripted. |
| 5 | With two input languages installed, the Shift-then-Ctrl gesture does not switch language or layout | | | x | | |
| 6 | Multi-file: 3 images offer Merge PDF; image + PDF offers Zip only; a folder offers Zip; a 500-file selection stays responsive | | | x | x (partly) | `multi`, `folder`. The 500-file and mixed cases are automated tests (about 20 ms), not harness. |
| 7 | Browser image, Outlook attachment, a file inside a `.zip`, a phone (MTP) file: "Save the file first", no crash, Explorer unaffected | | | x | x (partly) | `virtual`, `text`, `nodrag` with synthetic data objects. Real sources not run. |
| 8 | Elevated Explorer window: no wheel (documented limit). Shiftvert started elevated: startup warning appears | | | x | | |
| 9 | Convert Files…: click mode, keyboard navigation, Narrator announces tiles, focus loss dismisses | | | x | x (partly) | `click-keys`, `click-mouse`, `click-esc`, `click-blur`, UIA ListItem check. Narrator not run. |
| 10 | Crop and Trim editors: presets, slider and pixel-box sync, Esc / Enter, outputs correct | | | x | | Crop editor driven once by scripted mouse input on 2026-10-23 (16:9 preset, scale slider, corner and move drags, dimmed outside, screenshot); not a recorded pass. Trim not re-run. |
| 11 | Toast states: single job, several jobs ("N still working…"), partial-failure dialog, chime toggle, "in Downloads" fallback (read-only source folder) | | | x | | |
| 12 | Open at Login toggle matches Settings → Apps → Startup and survives a reboot. A second launch pops the tray menu | | | x | | |
| 13 | Light and dark theme; Animation effects off means no animation; high contrast readable; 100 / 150 / 200 % DPI | | | x | | The harness takes light and dark screenshots (`--theme`); not a recorded pass. |
| 14 | Precision touchpad drag, pen, touch; RDP session; sleep/resume mid-session; `explorer.exe` restarted while Shiftvert runs; 24 h soak without losing the hook | | | x | | |
| 15 | No network: Resource Monitor shows zero connections during a full session | | | x | | |
| 16 | Install the `.msixbundle` with the dev certificate; uninstall cleanly; SHA-256 matches `SHA256SUMS.txt`; `shiftvert --list <file>` works from a fresh terminal (and `where shiftvert` points into `WindowsApps`); Start menu shows only Shiftvert, not "Shiftvert command line" | | | x | | Re-run after W5a: the Windows App SDK packages changed. |
| 17 | Windows 10 1809+ machine or VM: install, tray, CLI, Convert Files… | | | x | | |
| 18 | ARM64 hardware (or Windows 11 on ARM VM): install the same bundle, run items 12 and 16 | | | x | | |
| 19 | Codec variants: with and without HEIF/HEVC extensions the HEIC tile appears/disappears; with and without Word the `.doc` behaviour matches [LIMITS.md](LIMITS.md) | | | x | | |

### Install steps for testers

See the README: create the certificate (`packaging/make-dev-cert.ps1`), trust the `.cer` once in an elevated PowerShell, build and sign (`scripts/release.ps1 -PfxPath …`), then `Add-AppxPackage -Path .\artifacts\Shiftvert_<version>.msixbundle`. Remove with `Get-AppxPackage Shiftvert | Remove-AppxPackage`, and remove the trust with `Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=Shiftvert Dev' | Remove-Item` when finished.
