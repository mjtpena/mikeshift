# 20-second demo video: storyboard

One take of real use, no voice-over, burned-in captions, 20 seconds. Everything in it is something the app does today. It is for the Microsoft Store trailer slot and, cut to 15 seconds, for the README.

**Message in one line:** "Right-click a file, pick a format, done. Nothing is uploaded."
**Story:** an iPhone photo becomes a JPG (the number-one job), then the wheel (the signature), then a quick look at what else the menu does.

## Before you record

* Clean PC or VM as in [screenshots.md](screenshots.md#set-up-for-every-capture-clean-machine): 1920 x 1080, 100% scale, plain wallpaper, no personal files, no notifications (turn on Do not disturb), only File Explorer on the taskbar.
* Dark or light mode, pick one and stay in it. Dark matches the existing screenshots.
* Folder `C:\Demo\Photos` with: `IMG_0412.heic` (needs Microsoft's HEIF and HEVC extensions installed), `beach.webp`, `scan1.jpg`, `scan2.jpg`, `scan3.jpg`. Large icons view, window about 1500 x 850. Delete any `.jpg` or `.png` left over from rehearsals; the video must start with only the five source files.
* MikeShift installed, welcome window dismissed ("Get started", key = Shift), tray icon visible on the taskbar.
* Mouse: slow, deliberate movement; pause about half a second on each target; large, visible pointer (Settings > Accessibility > Mouse pointer). No keyboard overlay is needed, because the wheel shows the key hint itself.
* Recorder: Xbox Game Bar (Win+Alt+R records the focused window; the right-click menu and the wheel are separate windows, so record the **desktop** with OBS Display Capture, or Snipping Tool's screen recording, instead). 1920 x 1080, 30 fps. Record a little long and trim.
* Captions: add in any editor (Clipchamp, which ships with Windows 11, is enough). White text on a dark rounded bar, bottom centre, 40 px, Segoe UI Semibold, one line each, 2 to 3 seconds on screen. No music is needed; if you add some, use something you have rights to.

## Shots

| Time | Shot | Action on screen | Caption |
|---|---|---|---|
| 0:00 to 0:02 | **1. The problem.** Explorer folder, `IMG_0412.heic` highlighted. | Pointer rests on the file. Nothing moves for a moment so the viewer reads the file name. | iPhone photo. Need a JPG? |
| 0:02 to 0:07 | **2. Right-click.** | Right-click the HEIC; the Windows 11 menu opens; hover **Convert with MikeShift**; the submenu opens (Convert to JPG, Convert to PNG, Convert to PDF, Compress, Resize 50%, Zip, More options…); click **Convert to JPG**. | Right-click. Convert with MikeShift. |
| 0:07 to 0:10 | **3. Result.** | `IMG_0412.jpg` appears next to the HEIC; the toast "Saved IMG_0412.jpg / Next to the original" with **Show in folder**. Hold on the toast for about two seconds. | A copy appears next to the original. |
| 0:10 to 0:15 | **4. The wheel.** | Drag `beach.webp`; hold **Shift**; the wheel opens under the pointer; move to **PNG** (the tile lights, Explorer's drag label reads "Convert to PNG"); drop. `beach.png` appears. | Or hold Shift while dragging. |
| 0:15 to 0:18 | **5. More than conversion.** | Select `scan1.jpg` to `scan3.jpg` (Ctrl+A in a prepared folder or click-drag a selection box); right-click > Convert with MikeShift; the submenu now shows **Merge PDF** at the second line; hover it (do not click; or click and cut to `Merged.pdf` appearing if you want the result). | Merge pictures into one PDF. Compress, resize, crop, trim, zip. |
| 0:18 to 0:20 | **6. End card.** | Dark plain card: the MikeShift icon (`assets/brand/mikeshift.svg`) and two lines. | MikeShift. Nothing leaves your PC. / Free and open source. |

Total: 20 s. The first caption must be readable without sound and without the pointer; the end card holds two full seconds.

## Notes per shot

* **Shot 2:** the submenu list depends on the file and the PC. With the HEIF extensions installed a HEIC offers Convert to JPG first (see `docs/screenshots/context-menu/modern-menu-heic.png`). On a PC without them the menu is built from extensions only and still lists JPG; the click then shows the "Get HEIF" tile instead of converting. Rehearse on the recording PC first.
* **Shot 3:** the toast fades after a few seconds. Start counting from the click; it appears within about a quarter of a second for a small file ([context-menu test run](../test-runs/2026-10-24-context-menu.md): about 220 ms for a small JPG).
* **Shot 4:** hold Shift **before** the pointer reaches the tile, not after; the wheel appears about 22 ms after the key (median on the test PC), so there is no need to hurry. Release over the PNG tile. Do not release over an empty folder with Shift held: Explorer itself treats a Shift-drop as a move. If you prefer, choose Ctrl in the welcome window and say "hold Ctrl" in the caption instead.
* **Shot 5:** the Merge PDF line appears only when two or more images or PDFs are selected, which is the point of the shot.
* If something fails in a take (a toast disappears, a menu closes early), cut and retake that shot; shots are independent and can be recorded separately.

## Versions to export

| Version | Length | Spec | Use |
|---|---|---|---|
| Store trailer | 20 s | MP4, H.264, 1920 x 1080, 30 fps, no audio (or silent track) | Partner Center trailer (check the current size, format and poster-image rules in the form; a 1920 x 1080 poster image is asked for, use `docs/media/store/01-right-click.png`) |
| README loop | 15 s | MP4 or GIF | Cut shots 2 to 4 only. MikeShift can make the GIF itself (Video > GIF), but by default it keeps only the first 15 s (up to 30 s in Settings), at most 480 px wide, 10 fps and without dithering, so it will look rough; use a general video tool if quality matters. |
| Social clip | 10 s | MP4, 1080 x 1080 crop | Shots 2 and 4 only |

## What not to show or claim

* No "Windows 10" on screen, no "Store" badge, no "signed" until those are true.
* No competitor names or comparisons in the video.
* No upload-progress jokes, spinners or "online" imagery; the point is that there is none.
* Do not click **Check for updates** in the take (it opens the browser).
