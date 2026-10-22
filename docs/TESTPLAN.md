# Test plan

Two layers: automated tests that run anywhere Windows runs (and in CI), and a manual checklist for the things only a person at a real machine can judge (the drag gesture, multiple monitors, Narrator, reboot behaviour).

## 1. Automated tests

```powershell
dotnet build Shiftvert.sln
dotnet test Shiftvert.sln                                   # both test projects
dotnet test tests/Shiftvert.Core.Tests --filter "FullyQualifiedName~Naming"     # one area
./scripts/build.ps1 -Configuration Release -Test            # build + test through the helper script
```

| Project | What it covers | How |
|---|---|---|
| `tests/Shiftvert.Core.Tests` | Naming and collision handling, `Kind.Of`, `ActionCatalog.For`, capability gating with fake `Capabilities`, every image / PDF / Zip converter (round trips, size guards, "already small"), feedback wording (`FeedbackPlan`, `JobLedger`), `DragSession`, `ModifierTracker`, wheel geometry and model, easing | In-process xUnit. Fixtures are generated at test time (`Infrastructure/Fixtures*.cs`). `OriginalsGuard` hashes every input before and after each converter test and fails if an original changed. |
| `tests/Shiftvert.Cli.Tests` | The real `shiftvert.exe` run as a child process on fixtures: exit codes, printed output paths, usage errors, Zip, image and PDF commands | Exercises WIC and Media Foundation on CI machines too. |

Tests that need a codec the machine lacks (HEIC/HEVC encoders and the like) are skipped with `[SkipIfNoCapability]`, never deleted; skipped counts are expected to differ between machines. At the time of writing (tree at `e736eb2` plus packaging): 578 Core tests and 38 CLI tests pass, 0 skipped, on the development machine. Media and document tests arrive with those converters.

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
| Package contents: self-contained runtime present (`coreclr.dll`), `cli\shiftvert.exe` + `Shiftvert.exe` side by side, no name collision | pass |
| `cli\shiftvert.exe --list <file>` and `--bogus` from an unpacked package run on the build machine | pass (exit codes 0 and 2) |
| Unpackaged `Shiftvert.exe --smoke-test` (self-contained build starts the tray and exits) | pass |
| `scripts/release.ps1` unsigned: `.msixbundle` (about 304 MB) + `SHA256SUMS.txt`; hash matches `sha256sum` | pass |
| `scripts/release.ps1` signed with a throwaway certificate created by `make-dev-cert.ps1` in the CurrentUser store only; signer subject `CN=Shiftvert Dev`; certificate and PFX deleted afterwards | pass (signature present; not trusted on this machine, as expected) |
| Installing the bundle, the alias in a fresh terminal, uninstall | **not run** (needs the certificate trusted in LocalMachine\TrustedPeople; see checklist item 16) |

## 2. Manual checklist

Run on a real Windows 11 machine (and, where noted, Windows 10) with the signed dev bundle installed. Record the outcome by moving an `x` into one column and adding notes (build/date/who). Everything starts as **Not run**. Items that need the wheel cannot be run until the wheel work package is merged.

| # | What to check | Pass | Fail | Not run | Notes |
|---|---|:-:|:-:|:-:|---|
| 1 | Shift-drag a JPG from Explorer: wheel appears under the cursor in under 100 ms; drop on PNG gives `photo.png` beside it; original unchanged; Explorer did **not** move it. Repeat with Ctrl-drag and Ctrl+Shift-drag. | | | x | |
| 2a | Same from the Desktop | | | x | |
| 2b | Same from a second monitor at a different scaling; wheel near a monitor edge; straddling two monitors; monitor to the left of the primary (negative coordinates) | | | x | |
| 3 | Shift held *before* the click (range select): wheel still works. After converting, repeat with Shift-click: the previously produced file is excluded from the selection | | | x | |
| 4 | Ctrl mid-drag goes to Tools; Shift returns to Formats; a mouse-wheel tick toggles; releasing over empty space lets Explorer's own drop proceed; Esc dismisses; dropping on the hub centre does nothing | | | x | |
| 5 | With two input languages installed, the Shift-then-Ctrl gesture does not switch language or layout | | | x | |
| 6 | Multi-file: 3 images offer Merge PDF; image + PDF offers Zip only; a folder offers Zip; a 500-file selection stays responsive | | | x | |
| 7 | Browser image, Outlook attachment, a file inside a `.zip`, a phone (MTP) file: "Save the file first", no crash, Explorer unaffected | | | x | |
| 8 | Elevated Explorer window: no wheel (documented limit). Shiftvert started elevated: startup warning appears | | | x | |
| 9 | Convert Files…: click mode, keyboard navigation, Narrator announces tiles, focus loss dismisses | | | x | |
| 10 | Crop and Trim editors: presets, slider and pixel-box sync, Esc / Enter, outputs correct | | | x | |
| 11 | Toast states: single job, several jobs ("N still working…"), partial-failure dialog, chime toggle, "in Downloads" fallback (read-only source folder) | | | x | |
| 12 | Open at Login toggle matches Settings → Apps → Startup and survives a reboot. A second launch pops the tray menu | | | x | |
| 13 | Light and dark theme; Animation effects off means no animation; high contrast readable; 100 / 150 / 200 % DPI | | | x | |
| 14 | Precision touchpad drag, pen, touch; RDP session; sleep/resume mid-session; `explorer.exe` restarted while Shiftvert runs; 24 h soak without losing the hook | | | x | |
| 15 | No network: Resource Monitor shows zero connections during a full session | | | x | |
| 16 | Install the `.msixbundle` with the dev certificate; uninstall cleanly; SHA-256 matches `SHA256SUMS.txt`; `shiftvert --list <file>` works from a fresh terminal (and `where shiftvert` points into `WindowsApps`); Start menu shows only Shiftvert, not "Shiftvert command line" | | | x | |
| 17 | Windows 10 1809+ machine or VM: install, tray, CLI, Convert Files… | | | x | |
| 18 | ARM64 hardware (or Windows 11 on ARM VM): install the same bundle, run items 12 and 16 | | | x | |
| 19 | Codec variants: with and without HEIF/HEVC extensions the HEIC tile appears/disappears; with and without Word the `.doc` behaviour matches [LIMITS.md](LIMITS.md) | | | x | |

### Install steps for testers

See the README: create the certificate (`packaging/make-dev-cert.ps1`), trust the `.cer` once in an elevated PowerShell, build and sign (`scripts/release.ps1 -PfxPath …`), then `Add-AppxPackage -Path .\artifacts\Shiftvert_<version>.msixbundle`. Remove with `Get-AppxPackage Shiftvert | Remove-AppxPackage`, and remove the trust with `Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=Shiftvert Dev' | Remove-Item` when finished.
