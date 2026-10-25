# Microsoft Store screenshots

What to upload, with captions; what is still missing; and how to capture the missing ones.

## Store requirements (check Partner Center for the current rules)

* PNG, at least 1366 x 768 (1920 x 1080 recommended), up to 10 screenshots, 16:9 landscape works everywhere. A caption of up to about 200 characters per image is optional.
* The first screenshots decide the listing. Show the right-click menu first, because that is how most people will use the app.
* No personal data, and nothing the app does not do. The screenshots below are real captures of the real UI; they are only placed on a neutral background with a caption.

## Ready to upload (in `docs/media/store/`)

None of the captures in `docs/images/` is big enough for the Store on its own, so eight 1920 x 1080 images were composed from them with `docs/media/compose-store-images.py` (Pillow; unmodified captures scaled up by at most 2x, on a light neutral gradient, with a caption; nothing is drawn over a capture). Re-run the script after replacing a source capture.

| # | File | Caption for the Store (paste) | Source captures |
|---|---|---|---|
| 1 | `01-right-click.png` | Right-click a file in File Explorer, choose Convert with MikeShift, and pick a format. The converted copy appears next to the original. | `right-click-menu.png`, `toast.png` |
| 2 | `02-drag-to-wheel.png` | Or drag a file and hold Shift (or Ctrl). A wheel of formats appears under the pointer; drop the file on one. Your original is never changed. | `wheel-formats-dark.png` |
| 3 | `03-formats-and-tools.png` | Formats on one page, tools on the other: Compress, Resize 50%, Crop, Strip EXIF and Zip. Press the other key to switch. | `wheel-formats-dark.png`, `wheel-tools-dark.png` |
| 4 | `04-merge-and-zip.png` | The menu fits what you selected: three images offer Merge PDF, a folder offers Zip. | `right-click-menu-multiple.png`, `right-click-menu-folder.png` |
| 5 | `05-keyboard-and-settings.png` | Select files in File Explorer and press Ctrl+Alt+Shift+Space to open the wheel from the keyboard. Settings choose the keys and keep Word off by default. | `wheel-keyboard-dark.png`, `settings-open-the-wheel.png` |
| 6 | `06-private-and-small.png` | Nothing is uploaded: no network code, no account, no telemetry. Originals are never moved or changed. About 23 MB to download. | `welcome.png` |
| 7 | `07-heic-guidance.png` | iPhone HEIC photos need Microsoft's free HEIF and HEVC extensions. If they are missing, MikeShift says so and opens the Store page. | `heic-extension-hint.png`, `failure-dialog.png` |
| 8 | `08-crop-and-trim.png` | Crop a photo or trim a clip in a small editor. Results are saved as copies. | `crop-editor.png`, `trim-dialog.png` |

Suggested upload order: 1, 2, 4, 3, 6, 5, 7, 8 (jobs first, then the wheel, then trust, then edge cases). Images 1 and 4 show other apps' entries in the Windows menu (Clipchamp, OneDrive, Acrobat, ChatGPT and PowerToys entries from the capture PC). That is normal for a Windows menu, but a hero shot on a clean PC (below) is cleaner and should replace images 1 and 4 when you have it. Images 2, 3 and 5 show the wheel over a synthetic backdrop, so no desktop pixels are in them.

Image 6 says "About 23 MB" and "no network code" and is true of the Store package as built (23.3 MB x64, 22.5 MB ARM64) and of the measurement described in [offline verification](../developer/testing.md#offline-verification). If a later build grows, re-check the number.

## What is missing

| Gap | Why it matters | Priority |
|---|---|---|
| **A. Hero: right-click menu in a full File Explorer window on a clean wallpaper** | The Store thumbnail is screenshot 1. A menu cropped on its own looks like a crop; a full window with photos in it shows the job. | High |
| **B. Before and after for a HEIC photo** (IMG_xxxx.heic next to IMG_xxxx.jpg, toast showing) | The number-one job should be visible as a result, not a menu. | High |
| **C. The wheel at full-screen size over a real Explorer window** | The existing wheel captures are small crops. | Medium |
| **D. Light-theme versions** of A and C | Many Windows users run light mode; all hero captures so far are dark. | Medium |
| **E. A merged PDF result** (`Merged.pdf` next to three JPGs) | Shows the PDF job. | Medium |
| **F. Windows 10 captures** | None exist. Do not add any until the app has been tried on Windows 10. | Later |
| **G. ARM64** | Nothing to show, but do not claim ARM64 hardware testing. | n/a |

## How to capture the missing ones

### Set-up for every capture (clean machine)

Use a fresh Windows 11 user account or a VM so the right-click menu shows only Windows' own entries and MikeShift, not whatever else is installed on your PC. Then:

1. Display 1920 x 1080 at 100% scale (VM: set the resolution; PC: Settings > System > Display). Do the capture once in Dark mode and once in Light mode (Settings > Personalization > Colors).
2. Wallpaper: a plain Windows default wallpaper or a solid neutral colour. No personal photos.
3. Create the demo folder `C:\Demo\Photos` (the address bar then shows no user name). Put in it a few harmless photos you have the right to use. For fixtures run `./scripts/make-fixtures.ps1` (writes sample files to `.\fixtures`); for the HEIC shot use a stock or self-made HEIC (or convert a sample with `mikeshift --run HEIC sample.jpg`, which needs the HEIF and HEVC extensions installed).
4. Install the Store (or sideloaded) package, close the welcome window with "Get started" (pick Shift), drag the tray icon onto the taskbar if you want it visible, and pin only File Explorer to the taskbar. Hide other tray icons and close every other app and notification.
5. Capture with Snipping Tool (Win+Shift+S, Full screen) or Print Screen, and save as PNG. Do not edit pixels. Check the result at 100% for names, account pictures, notification badges and the clock/date (crop or retake if anything personal shows).

### A. Hero: right-click menu

1. Open `C:\Demo\Photos` in Explorer, Large icons view, window about 1500 x 850 so the desktop wallpaper shows around it.
2. Select one JPG or PNG, right-click it, hover **Convert with MikeShift** so the submenu is open. For a HEIC hero, select the `.heic` file so the submenu starts with **Convert to JPG**.
3. The context menu closes when focus changes, so do not use Snipping Tool's own hotkey UI if it dismisses the menu: use **Print Screen** (full screen to clipboard), paste into Paint, and save as PNG. If Snipping Tool dismisses the menu, use its **Delay** (3 or 5 s), open the menu in that time and let it capture.
4. Result: 1920 x 1080, no other third-party menu entries, MikeShift's submenu visible.

### B. HEIC before and after

1. Folder with `IMG_0001.heic` only. Right-click > Convert with MikeShift > **Convert to JPG**.
2. When the toast reads "Saved IMG_0001.jpg / Next to the original" (it stays about 6 s with its action button), press Print Screen. Explorer now lists both files.

### C. The wheel over real Explorer

1. Start a drag of a file from the Explorer window (left button held), then hold Shift. The wheel opens under the pointer.
2. While still holding both, press Print Screen. If the drag image does not appear in the capture, record instead (Xbox Game Bar: Win+Alt+R) and export a frame. Do not use the drag harness (`tools/DragHarness`) on your own desktop for this: it takes over the mouse and keyboard, and its captures include whatever is on screen.
3. Press Esc to cancel; nothing is converted.

### D. Light-theme versions

Repeat A and C in Windows Light mode. MikeShift follows the Windows theme (`MIKESHIFT_THEME=light` forces it in dev runs).

### E. Merged PDF

Select three JPGs, right-click > Convert with MikeShift > **Merge PDF**. Capture when `Merged.pdf` sits next to the three images and the toast is up.

### Scenes without a desktop (no personal pixels)

For captures of MikeShift's own windows, the dev scenes show canned data, never real files:

```powershell
# Debug build (or an unpackaged Release build, which also needs --dev). Leaves a running copy alone and installs no hooks.
$env:MIKESHIFT_NO_SINGLE_INSTANCE = '1'; $env:MIKESHIFT_NO_HOOKS = '1'
$app = '.\src\MikeShift.App\bin\x64\Debug\net10.0-windows10.0.26100.0\MikeShift.exe'
& $app --dev --scene toast-success            # also: toast-progress, toast-partial, toast-downloads, dialog-single, dialog-partial, dialog-all, dialog-heic, tray-menu, picker
$env:MIKESHIFT_THEME = 'light'; & $app --dev --show-welcome     # welcome window; also --show-settings
```

`scripts/capture-scene.ps1` captures one of those windows to a PNG without touching any other program, for example:

```powershell
./scripts/capture-scene.ps1 -AppArgs '--scene','tray-menu' -Theme light -Out .\tray-light.png -Title 'MikeShift' -Backdrop
```

(`-Backdrop` puts a synthetic gradient behind translucent popups so no desktop pixels end up in the image.) To use a new capture in the Store set, drop it in `docs/images/`, add a function to `docs/media/compose-store-images.py` and run it. The wheel itself can be rendered offscreen over a synthetic desktop with `MikeShift.exe --dev --render-wheel list` (lists scenarios) and `--render-wheel <scenario> <out.png>`; that synthetic desktop is the kind of "Quarterly results" background seen in images 3 and 7.

## Accuracy checklist before uploading

* Every screenshot shows a feature that is in the submitted build (right-click, wheel, hotkey, Crop, Trim, Settings, Word off by default).
* No image says "Windows 10" or shows a Windows 10 desktop until that has been tested.
* Nothing says "signed" or "in the Microsoft Store" before it is.
* No personal names, paths with your user name, emails, notifications or other apps' account pictures. Images 1 and 4 show third-party menu entries only; replace them with clean-PC captures (A) when you can.
