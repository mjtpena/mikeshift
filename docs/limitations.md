# Known limitations

What MikeShift does not do, what depends on your PC, and where Windows gets in the way. Where a limit shows up as a message, the wording here is the app's own: it appears in the toast or a dialog, and the command line prints it as `error: …`.

* [Not offered](#not-offered)
* [Depends on what is installed](#depends-on-what-is-installed)
* [Where Windows hides things from MikeShift](#where-windows-hides-things-from-mikeshift)
* [The right-click entry](#the-right-click-entry)
* [Conversions that are refused](#conversions-that-are-refused)
* [Conversion settings](#conversion-settings)
* [Where converted files go](#where-converted-files-go)
* [Platform, size and memory](#platform-size-and-memory)
* [What has and has not been tested](#what-has-and-has-not-been-tested)

## Not offered

| Not offered | Why, and what to do instead |
|---|---|
| **MOV output** | Windows has no QuickTime writer. MOV files can be read; convert them to MP4, M4V, WMV, GIF, M4A or MP3. |
| **AVIF output** | Windows has no AVIF encoder and none is bundled. AVIF files can be read when the free AV1 Video Extension is installed. |
| **Folders in Convert files…** | The Windows file dialog cannot pick files and folders in one go, so **Convert files…** picks files only. To convert or zip a folder, right-click it in File Explorer, or drag it with the wheel key held. |
| **Ctrl+O for Convert files…** | A global Ctrl+O would take the Open command away from whatever program is in front. The tray menu item has an access key instead (press the underlined letter). |
| **Lossless video Trim** | Video Trim always re-encodes (MP4 for most sources, WMV for WMV sources), because cutting without re-encoding can only start on key frames and would give the wrong start. WAV and AIFF audio is cut losslessly; other audio is re-encoded to M4A. |
| **Dithered or long GIFs** | A GIF made from a video uses at most 256 colours per frame with no dithering, so gradients show banding. It runs at 10 frames a second, at most 480 pixels on the long side, for the first 15 seconds unless you choose 5, 10 or 30 in Settings. |
| **PDF editing** | PDFs cannot be edited, only converted, merged, split and compressed. |
| **Automatic updates** | MikeShift never goes online by itself. **Check for updates** opens this project's releases page in your browser. |
| **One fixed key for the wheel** | You choose Shift or Ctrl on first run (and later in Settings), because each has a cost. Shift is rarely used while dragging, but File Explorer reads a Shift-drop onto a folder as *move* (MikeShift's own wheel always answers *copy*, so the wheel is safe). Ctrl avoids that, but Ctrl-click is how people extend a selection, so a Ctrl still held when a drag starts opens the wheel. |
| **Alt as the default tools key** | Left Alt+Shift is Windows' input-language shortcut, and releasing Alt can activate a menu in Explorer. Ctrl (or Shift, if Ctrl opens the wheel) is the default; Alt is selectable in Settings with that caveat. |

### The keyboard shortcut for the files selected in Explorer

The default shortcut **Ctrl+Alt+Shift+Space** (changeable, or off) opens the wheel for the files selected in File Explorer or on the Desktop. A shortcut registered with Windows goes to MikeShift *instead of* the program in front, and only one program can own it. The shorter Ctrl+Alt+Space is used by AI assistant apps, Ctrl+Shift+Space is Word's non-breaking space, and shortcuts with the Windows key belong to Windows, which is why the default has three modifiers. If Windows refuses the shortcut, Settings says another program owns it and the shortcut does nothing until you choose another.

It reads the selection of the *foreground* File Explorer window (with tabs, the tab that is showing). "This PC", the Recycle Bin and phones (items without a file-system path) count as no selection. An Explorer window running as administrator probably cannot be asked from a normal MikeShift.

## Depends on what is installed

MikeShift checks your PC at start-up and hides tiles that cannot work instead of offering them and failing. The check takes a fraction of a second; a drag that starts before it has finished shows a reduced set of tiles for a moment (no HEIC, WebP or media tiles) and then fills in the rest while you are still dragging.

| Missing | What happens |
|---|---|
| **HEIF Image Extensions and HEVC Video Extensions from Device Manufacturer** (free, Microsoft Store) | There is no **HEIC** tile and Compress saves PNG for sources that would otherwise become HEIC. A HEIC photo Windows cannot decode shows one **Get HEIF** tile (or **Get HEVC**, when only that decoder is missing) next to Zip, with the hint "HEIC photos need the free HEIF extension". The tile opens the extension's Store page instead of converting. A conversion that reaches it anyway (command line, a batch) fails with "Windows can't open HEIC photos yet. Install the free "HEIF Image Extensions" from the Microsoft Store, then restart MikeShift.", and the failure dialog has a **Get the extension** button. The welcome window checks at first run and offers the same links. After installing, restart MikeShift, which checks once at start. If both extensions are present and a HEIC still will not open, the file is damaged and only Zip is offered. This applies to files named `.heic`, `.heif` and `.avif`; a misnamed file is not recognised. |
| **AV1 Video Extension** (free) | AVIF files Windows cannot open show a single **Get AV1** tile next to Zip, with the same advice when a conversion fails. |
| **WebP Image Extensions** (free) | MikeShift reads WebP itself, so nothing is lost. WebP output always works. |
| **Decoders for HEVC, VP9 or AV1 video** | The video is not recognised (Zip only) or fails with "Windows can't decode the video in this file." |
| **Web Media Extensions** (free) | `.webm` input depends on them and on the installed Media Foundation codecs. If WebM does not open, `.webm` files are unknown files (Zip only). |
| **MP3, FLAC, H.264 or AAC encoders** | The matching tiles are hidden (MP3, FLAC, MP4/M4V, M4A, and video and audio Compress). FLAC needs Windows 10 version 1703 or later. |
| **Microsoft Word, turned on in Settings** | See below. |

### Microsoft Word

Word is **off by default**: MikeShift is fully local, and Word is a separate program that talks to Microsoft on its own. Turn it on with **Settings → Use Microsoft Word for Word documents** (on the command line, `--use-word` for that run; without it the command line never starts Word).

* **Legacy `.doc` files need Word, turned on.** Otherwise `.doc` files get only Zip, and the wheel says "Turn on Microsoft Word in Settings to convert .doc files". If Word is not installed at all: "Legacy .doc files need Microsoft Word to be installed." A `.doc` that is not really a Word file is refused before Word starts: "This isn't a valid Word document (.doc); it may be damaged."
* **DOCX to PDF** uses Word's own export when Word is on, and MikeShift's own plain layout otherwise (see [Documents](#documents)).
* Word runs invisibly in the background on a private copy of the file, with alerts off, and is always quit afterwards. Each conversion takes a second or two longer than the built-in converters, because Word has to start, and Word conversions run one at a time.
* **Word is not offline.** In the project's own measurement, each Word instance started for a conversion opened HTTPS connections to Microsoft, while MikeShift's own processes opened none. Leaving the setting off, not having Word, or converting anything other than `.doc` and DOCX-to-PDF avoids it.
* A conversion that has not finished after 90 seconds (a hung Word, a dialog nobody can see) is stopped and fails with "Word didn't respond, so the conversion was stopped." MikeShift ends only the Word process it started itself, never one you already had open, and does the same when you cancel.

## Where Windows hides things from MikeShift

* **Elevated (administrator) windows are invisible.** Windows stops a normal program from seeing the mouse and keyboard events of a higher-privilege window, and from receiving its drops. A drag from an elevated Explorer window or an elevated app shows no wheel. MikeShift itself must not run as administrator for the same reason; if it is started elevated it shows a warning.
* **Files that are not real files on disk are refused with a hint.** Attachments dragged from Outlook, items inside a `.zip` opened in File Explorer, files on phones, and pictures dragged from a browser exist only while the drop is being delivered. The wheel shows **"Save the file first"** and refuses the drop; nothing is converted or copied. Save the file somewhere, then drag the saved file.
* **Programs that do not use standard drag and drop** (some games, some cross-platform toolkits, remote-desktop clients) never deliver the file list, so no wheel appears. If no file list arrives within a quarter of a second, the wheel hides again silently. A **bare disc can therefore flash briefly** when you press the wheel key during a drag that is not a file drag (a window move, a text selection): the wheel has to be shown before Windows says what is being dragged, because that is the only way to receive the drag at all.
* **OneDrive "online only" files** are recognised by their extension alone, without downloading them. Converting one downloads it first, as any program reading it would.
* **Holding Shift before you click** in File Explorer means "range select" there; MikeShift still opens the wheel once the drag starts. After a conversion, a Shift-click that re-selects the files MikeShift just produced drops those from the next selection (within 20 seconds), so you do not convert your own outputs.
* **Windows can remove the mouse hook** if MikeShift stalls longer than the system hook timeout. MikeShift keeps its callback minimal and re-installs the hook if it notices a drag with no events, but a very long stall (a debugger break, say) can still cost one gesture.
* **Big selections are checked by sample.** Up to 16 files, every file is checked by its content (a misnamed file is noticed). For a larger selection whose extensions all agree, MikeShift confirms them on a sample of 12 files so the wheel appears at once; a misnamed file outside the sample then fails alone with its own `error:` message when converted, and the rest are converted. A large selection with mixed extensions is checked in full.
* **Two input languages.** Windows can switch the input language with Left Alt+Shift or Ctrl+Shift. Whether the wheel's Shift-then-Ctrl gesture interferes with that has not been tested with two input languages installed.

## The right-click entry

The entry **Convert with MikeShift** is described in the [user guide](user-guide.md#right-click-in-file-explorer). Its limits:

* **Windows 11's modern menu is the supported place.** The package declares the entry the way Microsoft documents for the modern menu. In the Windows 11 build it was tried on, File Explorer also lists the same entry in the classic menu (Shift + right-click, or **Show more options**). That is not guaranteed on other Windows 11 builds: there the entry may be missing from the classic menu, and on a PC that has forced the classic menu back, there may be no entry at all. Use the wheel, the keyboard shortcut or **Convert files…** from the tray in that case.
* **Windows 10 (1809 and later) is declared but not tested.** The same registration is what Microsoft documents for Windows 10's single context menu, but MikeShift has not been seen doing it on a Windows 10 PC.
* **One level of submenu.** Explorer does not support menus inside menus, so the entry lists the five most useful actions for your selection, then **Zip**, then **More options…**, which opens the full wheel at the pointer. Everything else (the other formats, Crop, Trim, Strip EXIF) is behind **More options…**.
* **The menu is built without opening files.** The lines are chosen by file extension and by what the tray app last learned about your PC, so a misnamed file gets the menu of its extension. When you click a line, MikeShift checks the files for real; if the action does not fit, the wheel opens with what is possible (usually Zip) instead of an error.
* **HEIC, HEIF and AVIF photos Windows cannot open get one line.** When every selected file is one of these and the decoder is missing, the submenu shows **Get the free HEIF extension** (or **Get the HEVC video extension**, **Get the free AV1 extension**) and Zip. Picking it opens the extension's Store page. A mixed selection (a HEIC next to a JPG) gets the ordinary menu, and the HEIC then fails with the same advice. If you installed the extension after MikeShift started, the line stays until MikeShift is restarted; Settings offers the restart.
* **A big selection is judged from Explorer's sample.** While it builds the menu, Explorer hands the extension only 16 items of a larger selection, so a selection of mixed kinds can still be offered picture conversions; the click then falls back to the wheel. Zip and the conversions themselves run on the whole selection.
* **No entry for things that are not files on disk:** virtual folders, devices, items inside a `.zip` opened in Explorer. There is no entry on a folder's empty background or on a drive.
* **The first right-click after a quiet spell is slower.** The extension process starts on demand (about half a second) and exits after 10 idle minutes. If the tray app has been closed, the first click starts it (about 1.5 seconds) and the action runs when it is up.
* **Right after a fresh install the submenu is cautious.** Until the tray app has checked your PC's codecs once (a second or so after it starts), the menu offers only what needs no optional codec (TIFF instead of WebP, for example) and never the "Get the free HEIF extension" line.
* **The Resize line follows your setting** ("Resize 25%"). The menu catches up the next time it is drawn.
* **A package installed or updated while File Explorer is running may need Explorer restarted (or a sign-out)** before the entry shows. That is Microsoft's note for every packaged context-menu command.
* **It needs the installed package.** Unpackaged runs of `MikeShift.exe` and `dotnet run` have no right-click entry. Uninstalling the package removes it.

## Conversions that are refused

A batch converts what it can and lists the files that failed.

### Pictures

* More than 200 megapixels: "This image is too large (N megapixels). MikeShift handles up to 200 megapixels."
* Decodes blank (usually a truncated HEIC or AVIF, or a missing codec): "Couldn't read this image. It decoded as blank, so the file may be damaged or a codec is missing." Otherwise "Couldn't read this image. The file may be damaged." or "… or in a format Windows can't open."
* **Compress, Resize and Crop refuse animated pictures** ("Animated images can't be … without losing the animation") **and multi-page files** such as a multi-page TIFF ("This file has N pages, and only the first would be kept, so it can't be …"). Crop refuses these before the editor opens, so you never crop and then get an error.
* **Compress keeps its result only if it is at least 5 percent smaller:** "This image is already small, so compressing it wouldn't save much space." JPG output is flattened on white; pictures with real transparency compress to PNG (the only output here that keeps transparency), so they are often "already small". HEIC output is also flattened on white, because Windows' HEIF encoder cannot store transparency.
* Crop: "The crop area is outside the image." and "The cropped picture would be too large."
* **TXT (text in a picture)** needs a Windows language with Optical character recognition; without one the tile is hidden. It reads printed text in the languages of your Windows profile (there is no choice of language), not handwriting, and writes one line per recognised line without rebuilding columns or tables. A picture with nothing readable: "Couldn't find any text in this picture." (no empty file is written). A multi-page TIFF: "This file has N pages. Convert it to PDF first, then use TXT on the PDF to read every page." An animated picture: "Text can't be read from an animated picture." Hebrew that comes out reversed is refused rather than written backwards.
* "There isn't enough memory to process this image."

### PDF

* Password-protected: "This PDF is password-protected." Damaged: "This PDF is damaged and can't be read." or "This file isn't a valid PDF." A PDF with no pages: "This PDF has no pages." Over 2 GB: "This PDF is too large to process."
* **Any encrypted PDF is refused** by Split, Merge, Compress, PDF to TXT and PDF to pictures, including one that opens without a password but carries owner restrictions (no printing, no copying). MikeShift does not work around restrictions the owner set.
* PDF to pictures uses the Windows PDF renderer, up to twice the page size and 8,000 pixels on the long side, on white, so the result matches what Windows draws, not Acrobat. "Page N could not be rendered."
* PDF to TXT: pages with text are read as they are; pages without text (scans) are rendered at 300 dpi and read by Windows text recognition. With no text recognition language on the PC, a PDF with no extractable text is refused: "No text found — this PDF is probably a scan. To read scans, add a language with Optical character recognition in Windows Settings." If recognition finds nothing either: "Couldn't find any text in this PDF, even on its scanned pages." Hebrew text stored in reverse is refused rather than written reversed.
* PDF Compress recompresses pictures only (it skips pictures with masks) and keeps the result only if smaller: "This PDF is already small. Compressing it wouldn't make it any smaller."

### Documents

* DOCX and ODT must be real ZIP packages: "This isn't a valid DOCX file (it may be password-protected or damaged)." Also "This DOCX file is damaged and can't be read.", "… has no document body.", "This isn't a valid RTF file.", "This ODT file has no content." and "… is too large to convert."
* **Documents that contain pictures can only be converted to TXT**: "This document contains pictures, so it can only be converted to TXT." The converters do not carry pictures across.
* **PDF from a document uses MikeShift's own layout:** fixed 54 pt margins, black text, simple paragraphs, headings, lists, simple ruled tables and hard page breaks. **It will not match Word's pagination, fonts or styling**: no columns, floating objects, headers and footers, or tracked changes. For DOCX with Word installed and turned on, Word exports the PDF instead (falling back to MikeShift's layout if Word fails). Very long documents are refused: "This document is too long to convert to PDF." (2,000,000 characters; 400,000 for right-to-left text).
* **Hebrew and other right-to-left text** in PDF output is put in visual order by a small reordering step. **There is no Arabic letter shaping**, so Arabic letters appear in their isolated forms. Use DOCX to PDF through Word for those.
* **Non-Latin text in PDF output uses the fonts installed in Windows:** Yu Gothic for Chinese and Japanese, Malgun Gothic for Korean, Nirmala UI for Indic scripts, Leelawadee UI for Thai and Myanmar, Segoe UI Symbol for symbols and Segoe UI Emoji for emoji. If a font is missing on the PC, that text shows as boxes. Emoji are monochrome, and scripts that need contextual forms look plainer than in Word.
* RTF is read as a practical subset (text, paragraphs, basic character formatting). HTML is read tolerantly as text structure only and never fetches anything.

### Video and audio

* "Windows can't open this file." and "Windows can't decode the video (or audio) in this file." (a missing codec); "This file has no video." and "… no audio."; "Windows Media Foundation is not available on this PC."
* Extracting audio from a video offers M4A and MP3 only: "Only M4A and MP3 can be extracted from a video."
* Compress targets 720p H.264/AAC (audio: AAC M4A) and keeps the result only if smaller: "This video is already small enough." or "This audio is already small enough."
* WAV and AIFF are limited to 4 GB: "This audio is too long for a WAV file (over 4 GB)." and "This AIFF file is damaged."
* Trim: "The trim range is empty." and "The trim range starts after the end of the file."

### Zip

* Named pipes, sockets and device files are refused: "… is a pipe, socket or device and can't be zipped." Folders that are OneDrive placeholders are included (zipping downloads them), but symbolic links and junctions are skipped, so a folder loop cannot happen. "Nothing to zip." and "… no longer exists."

## Conversion settings

**Settings → Conversions** has five choices, and the command line has the same five as options. Without touching them, MikeShift behaves as it always did.

* **Picture quality (50 to 100, default 90)** applies to JPG, WebP and HEIC output: converting to those formats, Resize and Crop of such pictures, and PDF pages saved as JPG. PNG, TIFF, BMP and GIF are lossless or palette formats and ignore it. It does not change Compress (which has its own strength), Strip EXIF (always 95), pictures placed in a PDF, or video and audio.
* **Compress strength (Light, Medium, Strong; default Medium)** applies to the picture Compress tool only: longest side 4,000, 3,000 or 2,000 pixels, and quality 0.75, 0.6 or 0.45. PDF, video and audio Compress keep their own fixed settings. A strong setting is more likely to leave a small file as it was.
* **Resize (25, 50 or 75 percent; default 50)** is a choice of three, not a free percentage or a pixel size. The tool, its output name (`photo 25%.jpg`) and the message say which. Each side is rounded to the nearest whole pixel, and a side never goes below 1.
* **GIF length (5, 10, 15 or 30 seconds; default 15)** is the longest a GIF made from a video runs. A shorter video gives a shorter GIF. GIFs stay at 10 frames a second and at most 480 pixels wide, so 30 seconds can be a large file.
* **Where converted files go** is "next to the original" or one folder for every output, Merge PDF, Merge and Zip included.
* **The command line never reads Settings,** so a script gives the same result on every PC. An option outside its range (`--quality 40`, `--resize 60`, `--gif-seconds 20`, `--compress heavy`), an `--out` folder that does not exist, or `--run "Resize 25%"` together with `--resize 75` is a usage error (exit code 2).
* **Not offered:** a custom percentage or pixel size for Resize, per-format quality, named presets, and output naming templates.

## Where converted files go

Outputs are written next to the original, or into the folder chosen in Settings. If that folder cannot be written to (a read-only share, a protected folder, a removed drive), the files go to your **Downloads** folder and the message says so ("In Downloads (Converted isn't available)"); Settings shows a warning next to a folder that cannot be used. MikeShift does not recreate a chosen folder that has been deleted.

Names never overwrite: ` 2`, ` 3` and so on are appended. A failed conversion leaves no output and no temporary file behind. While a job runs, its temporary output lives in a hidden folder named `.<name>.<id>.mikeshift-tmp` next to where the result will go, and is moved into place when the job finishes. If MikeShift is killed or Windows crashes in the middle of a job, that folder can be left behind and is safe to delete.

## Platform, size and memory

* **Windows 10 version 1809 (build 17763) or later, x64 or ARM64.** The ARM64 package is built but has not been run on ARM64 hardware.
* **Size.** The standard (sideload) packages are about 45 MB per architecture and about 112 MB installed, because they carry the .NET runtime and the Windows App SDK; the command-line tool shares the tray app's runtime instead of bringing a second copy. The Store flavour is about 23 MB and **needs the Windows App Runtime 2.5 or later** (the framework package `Microsoft.WindowsAppRuntime.2`). The Microsoft Store installs it automatically; for a sideloaded Store-flavour package it has to be installed first, or Windows refuses to install the package and names the missing dependency. See [installing](../README.md#install).
* **Memory.** Idle, the app holds roughly 17 MB (about 5 MB private) on the test PC. During a drag, or while an editor is open, it needs more (about 115 MB for the wheel, about 190 MB with the crop editor) and gives it back a few seconds later. The first drag after the app has been idle for more than 15 seconds recreates the graphics device when the mouse button goes down; if the button is released and the key pressed within about 100 ms of that, the wheel can take up to that long to appear instead of the usual 30 ms.
* **Developer switches** (`MIKESHIFT_*` environment variables and flags such as `--scene`) are ignored by an installed MikeShift and by any release build that is not started unpackaged with `--dev`.

## What has and has not been tested

MikeShift was built and tested on a single Windows 11 x64 PC with two monitors. Automated tests cover the conversions, the settings, the wheel's logic and the command line, and a harness drove the real File Explorer with real mouse and keyboard input. These have **not** been run:

* On **Windows 10**, so the right-click entry there is declared but unseen.
* On **ARM64 hardware**.
* With a **screen reader.** The wheel and the other windows expose names to UI Automation and the wheel has a keyboard path, but they have not been checked with Narrator, and the drag gesture itself needs a mouse.
* With **touch, pen, a precision touchpad or a remote-desktop session**, with **monitors at different scaling**, or with **two input languages** installed.
* Across a **reboot** (Open at login), a 24-hour soak, or sleep and resume.

If you run into any of these, please [open an issue](https://github.com/mjtpena/mikeshift/issues).
