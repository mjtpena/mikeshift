# Changelog

All notable changes to MikeShift are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## 1.0.0 (2026-10-26)

The first release.

### Ways to convert

* **Right-click in File Explorer.** **Convert with MikeShift** appears in Windows 11's main right-click menu for files and folders. Its submenu offers the five most likely actions for the selection, then **Zip**, then **More options…**, which opens the full wheel at the pointer.
* **The wheel.** Start dragging files in File Explorer and hold Shift (or Ctrl, your choice on first run): a wheel of formats appears under the pointer, and dropping on a tile converts. The other key, or the mouse wheel, switches to tools. Esc cancels. The wheel always answers *copy*, so originals are never moved.
* **Keyboard shortcut.** Ctrl+Alt+Shift+Space opens the wheel for the files selected in File Explorer or on the Desktop. The shortcut can be changed or turned off.
* **Convert files… from the tray.** A file picker that opens the same wheel for the files you pick.
* **Command line.** The `mikeshift` command (`--list`, `--run`) uses the same engine as the wheel, with conversion options and clear exit codes.

### What it converts

* **41 actions** across pictures, PDF, video, audio, documents and zip. Only the actions that fit the selection and work on the PC are shown.
* **Pictures:** PNG, JPG, TIFF, GIF, BMP, WebP, HEIC and PDF output; Compress, Resize, Crop, Strip EXIF and Merge PDF.
* **PDF:** pages to PNG or JPG, text to TXT; Compress, Split and Merge.
* **Video:** MP4, M4V, WMV, GIF, and audio-only M4A and MP3; Compress and Trim.
* **Audio:** M4A, WAV, AIFF, FLAC and MP3; Compress and Trim.
* **Documents:** DOCX, RTF, ODT, HTML and TXT, to PDF and between each other.
* **Zip** for anything, including folders.
* **Crop editor and Trim dialog** that open only when asked.
* **HEIC help:** when Windows cannot open HEIC or AVIF photos yet, MikeShift offers a **Get HEIF**, **Get HEVC** or **Get AV1** action that opens the right Microsoft Store page, instead of failing.

### Settings

* Open at login, a sound and a message when a file is converted, and showing the file in its folder afterwards.
* **Conversions:** picture quality, Compress strength, Resize percentage (25, 50 or 75), GIF length, and where converted files go (next to the original or a folder you choose).
* The key that opens the wheel while dragging (Shift or Ctrl), the key that switches to tools, and the keyboard shortcut.
* **Format support:** which Windows extensions are installed, with links to the missing ones.
* **Microsoft Word is optional and off by default.** Turn it on to convert old `.doc` files and to let Word lay out PDFs from DOCX.

### Privacy and safety

* **Fully local:** no network code, no account, no telemetry. The package declares no internet capability.
* **Originals are never changed or moved.** Every result is a new file written through a temporary file, so a failure leaves nothing half-written. Names never overwrite (`photo 2.png`).
* A **Show in folder** button on the message after each conversion.

### Windows integration

* WinUI 3 with Mica, light, dark and high-contrast themes, per-monitor scaling, and a wheel that stays clear of the taskbar.
* UI Automation names for the wheel and Settings, and a keyboard path through the wheel.
* x64 and ARM64 MSIX packages: a complete sideload package (about 45 MB) and a smaller Microsoft Store flavour (about 23 MB). For Windows 10 version 1809 and later.

### Known gaps in this release

* Not yet tested on Windows 10, on ARM64 hardware, or with a screen reader. See [known limitations](docs/limitations.md#what-has-and-has-not-been-tested).
* No signed release yet; see [installing](README.md#install).
