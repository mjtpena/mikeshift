# Changelog

All notable changes to MikeShift are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## Unreleased

### Added

* **ICO output for pictures.** A new **ICO** tile turns a logo into a Windows icon or website favicon: `logo.png` becomes `logo.ico`, with 16, 24, 32, 48, 64, 128 and 256 pixel images in one file, each scaled from the original. A picture that is not square is centred on a transparent square instead of being stretched, and transparency is kept. A picture under 256 pixels still converts, and the toast warns that the larger sizes will look soft. Also `mikeshift --run ICO logo.png`.
* **Searchable PDF for scans and pictures.** A scanned PDF gets a new **Searchable PDF** tool: `scan.pdf` becomes `scan searchable.pdf`, which looks exactly like the original but has an invisible text layer, so you can search it, select text and copy it. The original pages are kept as they are, not re-rendered. A picture gets a **Searchable PDF** tile next to **PDF**, and two or more pictures get **Merge searchable PDF**, which puts them in order into `Merged searchable.pdf`. A PDF that already has text on every page is refused with a message saying it is already searchable. It uses the same on-PC text recognition as TXT, so it is hidden on a PC without a text recognition language. Also `mikeshift --run "Searchable PDF" scan.pdf`.
* **Text from pictures and scanned PDFs.** Pictures have a new **TXT** tile that writes the text in a screenshot or a photo of a page to `photo.txt`, and PDF to **TXT** now reads scanned pages instead of refusing them; pages that already have text are read as before. It uses Windows' own text recognition, on your PC, in the languages Windows has installed. On a PC without such a language the picture tile is hidden and Settings → Format support says how to add one. Also `mikeshift --run TXT scan.png`.
* **Fit 10 MB and Fit 25 MB for videos.** Two new video tools make a copy that fits under a chat or email upload limit: `clip.mp4` becomes `clip 10 MB.mp4`, an H.264/AAC MP4 that is never larger than 10,000,000 bytes (25,000,000 for Fit 25 MB). MikeShift works out the bitrate from the length of the video, picks 1080p, 720p, 480p or 360p to suit it (never larger than the original), and checks the result: if it comes out over the limit it is encoded once more at a lower bitrate, and nothing is saved rather than a file that does not fit. A video too long to stay watchable at that size is refused straight away, with roughly how long a video can be. A video already under the size is copied into MP4 without re-encoding when it is H.264/AAC, so its quality is untouched. Fit 10 MB is on the right-click menu for videos. On the command line, `--max-size MB` (or a title such as `--run "Fit 8 MB"`) asks for another size.

### Fixed

* **PDF to TXT keeps the word order of Hebrew and Arabic lines.** The words of a right-to-left line were written from left to right, which put them in reverse order; they are now read from the right.
* **WebP to PDF, Merge PDF and the crop editor work without the WebP Image Extensions.** On a PC without the free extension (Windows Server, or a PC where it was removed), turning a WebP into a PDF said the picture "could not be read as an image", and the crop editor could not open it. They now read WebP themselves, like every other picture action.

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
