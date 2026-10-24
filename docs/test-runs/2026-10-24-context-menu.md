# Explorer right-click entry ("Convert with MikeShift"), 2026-10-24

What was built is in [ARCHITECTURE.md §9](../ARCHITECTURE.md#9-explorer-right-click-entry-convert-with-mikeshift); what it cannot do is in [LIMITS.md](../LIMITS.md#the-explorer-right-click-entry-convert-with-mikeshift). This is the evidence.

* Build under test: branch `worktree-agent-a78eb0190d43d2773` (from `feature/windows-port` @ `f691900`), **Release, IL-trimmed x64 packages made by `scripts/release.ps1 -Flavor all -Platforms x64`**: sideload 44.9 MB, store 23.4 MB (was 44.9 / 23.3 MB). Registered with `Add-AppxPackage -Register <unpacked package>\AppxManifest.xml` (Developer Mode; no certificate imported), unregistered afterwards. Debug builds were used while developing.
* Machine: Windows 11 Home build 26300, primary monitor 3440 x 1440 (taskbar at the bottom), second monitor 1920 x 1080 to its left, dark theme, Windows App Runtime 2.5.1 installed (so the store flavour could run too).
* Driver: `tools/ContextMenuHarness` (`probe` for the COM level, `menu` for the real File Explorer: its own Explorer window on a temp folder, items selected through the shell view, **real mouse and keyboard through `SendInput`**, menus read through UI Automation). The temp folder (`photo1..3.jpg`, `sample.png`, `doc.pdf`, `letter.docx`, `IMG_0001.heic`, `folder\`, `big\` with 600 JPEGs, `mixedbig\` with 18 JPEGs and 4 PDFs) was created for the run and deleted afterwards.
* Result: **every check passes on the sideload package; the store package passes the six scenarios run on it** (section 2). Nothing was converted wrongly, no original changed (SHA-256), Explorer never stalled or crashed, no `[ERROR]` line in `shell-log.txt`. One thing found along the way that changes the design assumption: Explorer hands the extension only 16 items of a big selection (section 3).
* Screenshots: `docs/screenshots/context-menu/`. Only the menu bodies were copied from the screen (the shadows and what lies between two popups stay transparent), and the classic-menu crop starts at MikeShift's own row, because the rows above it name the user's OneDrive accounts. The wheel is not screenshotted here (a see-through window); it is in the existing wheel screenshots.

## 1. Real File Explorer, sideload package (trimmed)

| Case | What was done | Result | Evidence |
|---|---|---|---|
| JPG to PNG | select `photo1.jpg`, right-click, click **Convert with MikeShift**, click **Convert to PNG** | **Pass** | modern menu shows the entry; submenu `Convert to PNG, Convert to PDF, Compress, Resize 50%, Convert to WebP, Zip, More options…` (no `Convert to JPG` for a JPG); `photo1.png` appeared 218 ms after the click, original SHA-256 identical, toast "Saved photo1.png / Next to the original / Show in folder" ([toast](../screenshots/context-menu/toast-after-convert.png)). [menu](../screenshots/context-menu/modern-menu-jpg.png) |
| Three images | select `photo1..3.jpg`, right-click on `photo2.jpg` | **Pass** | submenu `Convert to PNG, Merge PDF, Convert to PDF, Compress, Resize 50%, Zip, More options…`; **Merge PDF** wrote `Merged.pdf` 219 ms after the click. [menu](../screenshots/context-menu/modern-menu-multi-images.png) |
| A folder | select `folder`, right-click it | **Pass** | submenu `Zip, More options…`; `folder.zip` 109 ms after the click. [menu](../screenshots/context-menu/modern-menu-folder.png) |
| Folder + file (mixed) | select `folder` and `photo1.jpg` | **Pass** | the entry is on the menu (the verb is registered for `*` and `Directory`); submenu `Zip, More options…`, no conversions. [menu](../screenshots/context-menu/modern-menu-mixed.png) |
| HEIC | `IMG_0001.heic` (made by `mikeshift --run HEIC`, this PC has the HEIF extensions) | **Pass** | submenu `Convert to JPG, Convert to PNG, Convert to PDF, Compress, Resize 50%, Zip, More options…`; **Convert to JPG** wrote `IMG_0001.jpg` 218 ms after the click. [menu](../screenshots/context-menu/modern-menu-heic.png) |
| 600 files | all 600 JPEGs of `big\` selected (`SelectedItems().Count` = 600), right-click | **Pass** | menu on screen 384 ms after the right-click (one file: 735 ms in the same run, both include the harness's UI Automation polling); entry present; **Zip then packed all 600** (`Archive.zip` holds 600 entries). Explorer passed the extension **16** items while building the menu and all 600 to `Invoke` (section 3). [menu](../screenshots/context-menu/modern-menu-600-files.png) |
| 22 mixed files | 18 JPEGs and 4 PDFs selected | **Pass** | Explorer's 16-item sample held both kinds, so the submenu is `Zip, More options…`; nothing converted. [menu](../screenshots/context-menu/modern-menu-22-mixed.png) |
| Request that does not fit | a "Run PNG" request for a JPEG plus a PDF sent over the pipe (what a menu built from a sample of only JPEGs would send) | **Pass** | tray app accepted it, resolved the exact catalog (Zip only), **opened the wheel with the Zip tile** 127 ms later, converted nothing |
| Classic menu | Shift + right-click on `photo1.jpg`; and the modern menu's **Show more options** | **Pass** | on this build the classic menu lists **Convert with MikeShift** with the same submenu (`Convert to PNG … Zip, ---, More options…`; the classic menu draws the separator, the modern flyout does not); "Show more options" opens it with the entry. [classic crop](../screenshots/context-menu/classic-menu-jpg.png) |
| More options… | select two images, entry, **More options…** | **Pass** | the click-mode wheel opened, with its UI Automation tiles, 236 ms after the click |
| Fresh install | the package registered into an empty data folder, tray app not running | **Pass** | the first click started the tray app through `IApplicationActivationManager` (ready after 1.4 to 2 s); its one-time welcome window appeared (closed by the harness) |

Store flavour (`MikeShift_0.1.0.0_store_x64.msix`, shared Windows App Runtime 2.5.1 as a package dependency), `jpg-png`, `multi-merge`, `folder-zip`, `classic`, `more`, `fallback`: all pass with the same submenus and outputs.

## 2. Timing

Time from the right-click's button-up to the menu being on screen, found with UI Automation polling every few milliseconds (so each number carries 10 to 30 ms of measurement overhead; compare the columns, not the absolute values). The measured item is "Properties", which Explorer itself owns, so the same measurement works with the package unregistered. Eight right-clicks per case on an unchanged selection, trimmed sideload build:

| Selection | Without MikeShift: median / first | With MikeShift: median / first (first = extension process started cold) |
|---|---|---|
| 1 JPG | 281 / 517 ms | 357 / 533 ms |
| 3 images | 244 / 503 ms | 296 / 580 ms |
| 600 files | 252 / 522 ms | 305 / 453 ms |

So the entry costs roughly **50 ms** of menu time once the extension process is up (an earlier run with six rounds: +50, +44, +65 ms), and the very first right-click after the process has to start is in the same range as Explorer's own first menu (500 to 580 ms). Inside the extension (`shell-log.txt`, one line per menu): building a plan takes 0.3 to 1.5 ms (15 to 48 ms for the very first one, JIT); playing Explorer through COM (`probe`) the cold start is 310 to 425 ms to activate the server plus 55 to 62 ms for the first `GetState`, and a whole menu (activate, `GetState`, `GetTitle`, `GetIcon`, `GetFlags`, `EnumSubCommands`) is 4 to 8 ms once warm. A 600-item array handed to the server directly (not what Explorer does, section 3) takes 6 ms in `GetState` and shows `Zip, More options…` (the 500 cap).

The extension process: 14 MB private, 46 MB working set, 12 threads while alive; it exits after 10 idle minutes.

## 3. What Explorer does that the design had to follow

* `EnumSubCommands` receives no selection (it uses the last `GetState`'s).
* While building the menu Explorer passes **16 items** for a 600-item selection (`menu for 16 item(s)` in `shell-log.txt`, `Run Zip: 600 of 600 item(s)` at the click). The menu of a big selection is therefore built from a sample, and the tray app re-checks the whole selection at the click (falling back to the wheel). Which 16 items it picks is Explorer's choice: a name-sorted 18 JPEGs + 4 PDFs gave a sample containing both kinds.
* A `desktop9:FileExplorerClassicContextMenuHandler` pointing at the exe server fails package validation (`0x80080204`: it must name a class of a `com:SurrogateServer`, i.e. a DLL); on this build the classic menu already lists the `desktop4` entry, so that registration is not used.
* The modern flyout shows no separator for an `ECF_ISSEPARATOR` sub-command.

## 4. Builds and tests

| Check | Result |
|---|---|
| `dotnet build MikeShift.sln` | **0 warnings, 0 errors** (clean tree; a stale mix of `-p:Platform=x64` and solution builds can trip `VerifyBundledCliSharesRuntime`, a clean rebuild fixes it) |
| `dotnet test MikeShift.sln` | Core **2,136 passed**, 12 skipped (2,112 before, plus the 24 new tests); CLI **79 passed**, 3 skipped (same skips as before). New: `ContextMenuPlanTests` (14), `ShellRequestTests` (6), `ManifestTests` (4) |
| `scripts/release.ps1 -Flavor all -Platforms x64` | sideload and store build; ILLink: 0 trim warnings in `MikeShift.*`, `269 [ComImport] interfaces checked: none lost a method`; the package tooling prints its usual "mspdbcmf.exe could not be found" note (no symbol package) |
| CLI suites against the trimmed exe (`MIKESHIFT_CLI_EXE`) | sideload **79 passed / 3 skipped**; store **79 passed / 3 skipped** |
| Both trimmed packages registered and run on the real Explorer | sections 1 and 2 |

## 5. Not done

* **Windows 10 1809+**: the manifest registration is the documented one (`desktop4` 1803, `desktop5:ItemType` 1809 = the package minimum); no Windows 10 machine was available, so it was not seen working there.
* **Windows 11 release builds other than 26300**: whether "Show more options" lists the entry there was not checked (documented as modern-menu only).
* **ARM64**: built by the same script for the other flavours, not built or run for this change (the extension is the same exe).
* Narrator / keyboard use of the menu (Explorer's own accessibility; the entry's names are plain text) and a touch or pen right-click were not tried.
* An `IContextMenu` handler for the classic menu was written and then removed: Microsoft's manifest registration for it cannot point at an exe server (ARCHITECTURE §9.3).
