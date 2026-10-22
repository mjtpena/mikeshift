# Shiftvert for Windows

Start dragging a file in Explorer, hold **Shift**, and a wheel of output formats appears under the cursor. Drop the file on a tile and a converted **copy** appears next to the original. Press **Ctrl** while the wheel is up to switch it to tools (compress, resize, crop, trim, split, merge, zip).

This is a Windows 10 (1809+) / Windows 11 port of [Shiftvert for macOS](https://github.com/roiegold/shiftvert) by roiegold (MIT). It has no network code, no bundled third-party programs, and it never touches your original files.

> **Status.** Work in progress; the table below is the honest state of this tree. Anything marked *planned* is specified in [docs/PLAN.md](docs/PLAN.md) but not built yet.

| Area | State |
|---|---|
| Core: action catalog, file classification, output naming, Zip, CLI | built |
| Image conversions (PNG, JPG, TIFF, GIF, BMP, HEIC*, WebP*, PDF; Compress, Resize 50%, Strip EXIF, Crop engine, Merge PDF) | built |
| PDF conversions (pages to PNG/JPG, TXT, Compress, Split, Merge) | built |
| Video and audio conversions (Media Foundation) | in progress in a parallel work package |
| Document conversions (DOCX, RTF, ODT, HTML, TXT, PDF) | in progress in a parallel work package |
| Tray app: menu, Convert Files…, toast, chime, settings, welcome window, Open at Login, single instance | built (Convert Files… shows a temporary action list until the wheel lands) |
| Drag detection primitives: low-level input monitor, drag state machine, modifier tracking, wheel geometry | built (unit-tested) |
| The wheel itself (layered window, drop target, Explorer drag-image text), drag integration | *planned* (next work package) |
| Crop editor and Trim dialog | *planned* |
| Packaging: self-contained MSIX x64 + ARM64, bundled CLI, dev certificate, release script, CI | built |

`*` depends on optional Windows codec packages; see [docs/LIMITS.md](docs/LIMITS.md).

## Using it

* **Shift-drag**: begin dragging one or more files or folders in Explorer or on the Desktop, then hold Shift. Drop on a tile to convert. Release anywhere else (or press Esc) and Explorer's own drop proceeds as usual. Shiftvert never cancels or changes the drag, and it always asks for a *copy*, so Shift-drag does not move your files.
* **Ctrl** while the wheel is up switches between *formats* and *tools*; Shift switches back. A mouse-wheel tick toggles too. (Alt is not the default because Left Alt+Shift is Windows' input-language hotkey; it can be chosen in Settings.)
* **Convert Files…** (tray menu, Ctrl+O): pick files in a dialog instead of dragging. Folders cannot be picked there (a Windows dialog limitation); drag them instead.
* Several files of the same kind get that kind's tiles; a mixed selection or unknown files get **Zip** only.
* Outputs go next to the original: `photo.heic` becomes `photo.png` (`photo 2.png` if that exists). If that folder is not writable they go to Downloads and the toast says so.
* Tray menu: Convert Files…, Open at Login, Check for Updates… (opens the releases page in your browser; the app itself never goes online), Version, Quit. Settings: chime, toast, tools key.

## Command line

The MSIX puts `shiftvert.exe` on your PATH (an app execution alias, available in new terminals after install). It is the same engine as the wheel and is what the automated tests drive.

```powershell
shiftvert --list photo.heic                    # e.g. PNG, JPG, TIFF, GIF, BMP, WebP, PDF, Compress, Resize 50%, Crop, Strip EXIF, Zip (depends on codecs installed)
shiftvert --run PNG photo.heic                 # prints the output path, exit code 0
shiftvert --run "Resize 50%" a.jpg b.jpg
shiftvert --run Merge a.pdf b.pdf              # Merged.pdf next to a.pdf
shiftvert --run Trim --range 0:05-0:12.5 clip.mp4
shiftvert --run Crop --crop 100,50,800,600@400x300 photo.jpg
shiftvert --run Zip "My Folder"
```

Errors go to stderr as `error: …` with exit code 1; an unknown action title lists the available ones; any other flag prints usage and exits 2. Without installing: `dotnet run --project src/Shiftvert.Cli -- --list file.png`.

## Install (sideload)

The MSIX is signed with a certificate you create yourself; Windows only installs packages whose signer it trusts.

```powershell
# 1. Create a self-signed code-signing certificate (subject must match the manifest: CN=Shiftvert Dev).
#    Writes artifacts\dev-cert\ShiftvertDev.pfx (private, git-ignored) and ShiftvertDev.cer.
./packaging/make-dev-cert.ps1 -Password (Read-Host -AsSecureString 'PFX password')

# 2. Trust it once, in an ELEVATED PowerShell (the script prints this line for you; it never does it itself).
Import-Certificate -FilePath .\artifacts\dev-cert\ShiftvertDev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# 3. Build x64 + ARM64, bundle, sign, write the checksum.
./scripts/release.ps1 -PfxPath .\artifacts\dev-cert\ShiftvertDev.pfx -PfxPassword (Read-Host -AsSecureString 'PFX password')

# 4. Install (Windows picks the right architecture from the bundle).
Add-AppxPackage -Path .\artifacts\Shiftvert_0.1.0.0.msixbundle
```

Uninstall: `Get-AppxPackage Shiftvert | Remove-AppxPackage`. Verify a download: compare `Get-FileHash <bundle> -Algorithm SHA256` with `artifacts\SHA256SUMS.txt`.

The bundle is large (about 300 MB for both architectures): it carries the .NET 10 runtime and the Windows App SDK so nothing else needs installing. Release signing for public distribution (Store, Azure Trusted Signing or a purchased certificate) is a separate decision that is not made yet.

## Build from source

Needs the .NET 10 SDK and the Windows 10/11 SDK 10.0.26100 (Visual Studio is not required).

```powershell
./scripts/build.ps1 -Test                      # dotnet build Shiftvert.sln + dotnet test Shiftvert.sln
./scripts/build.ps1 -Configuration Release -Msix -Platforms x64,ARM64   # unsigned per-architecture .msix
./scripts/release.ps1                          # unsigned artifacts\Shiftvert_<version>.msixbundle + SHA256SUMS.txt
./scripts/make-fixtures.ps1                    # sample files for manual testing -> .\fixtures
```

or directly: `dotnet build Shiftvert.sln`, `dotnet test Shiftvert.sln`. CI (GitHub Actions, `windows-latest`) runs build + test and uploads the unsigned bundle. See [docs/TESTPLAN.md](docs/TESTPLAN.md).

## Privacy

* No network access anywhere in the product. No telemetry, no update check inside the app.
* Originals are never modified or moved: every conversion writes a new file (via a temporary file in the same folder, then a rename, so a failure never leaves a half-written output).
* The input hook sees only mouse movement, the left button, the wheel and the Shift/Ctrl/Alt/Esc keys, and only to run the gesture. No other key is read or stored.
* Settings (chime, toast, tools key) are stored locally in the app's settings; Open at Login is Windows' own startup-task switch.

## More

* [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) projects, threading, how drag detection works, Microsoft API references
* [docs/LIMITS.md](docs/LIMITS.md) everything that works differently on Windows
* [docs/TESTPLAN.md](docs/TESTPLAN.md) automated tests and the manual checklist
* [docs/PLAN.md](docs/PLAN.md) the original specification; [docs/WORKPLAN.md](docs/WORKPLAN.md) who built what
* [LICENSE](LICENSE) (MIT) and [NOTICE](NOTICE) (credit to the original Shiftvert and every third-party licence)
