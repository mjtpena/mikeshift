# Limits and Windows-specific differences

Everything that works differently from the macOS original, depends on the machine, or is refused. Where a limit shows up as a message, the wording below is the app's own (the CLI prints it as `error: …`, the app in the toast or a dialog). Everything in the PLAN is built; nothing here is "planned" any more except where a line says so.

## Not offered on Windows

| Item | Why |
|---|---|
| **MOV output** (video) | Media Foundation has no QuickTime muxer. MOV files can be read; they convert to MP4, M4V, WMV, GIF, M4A or MP3. |
| **AVIF output** (images) | Windows has no AVIF encoder and none is bundled. AVIF files can be read when the free "AV1 Video Extension" is installed. |
| **Folders in Convert Files…** | The Windows file dialog cannot select files and folders in one dialog (turning on folder picking turns files off), so Convert Files… picks **files** only. To convert or zip a folder, Shift-drag it from Explorer. |
| **Ctrl+O for Convert Files…** | The tray menu's **Convert Files…** has an access key (press the underlined letter) but no Ctrl+O shortcut. PLAN §1.1 asked for Ctrl+O, but a global Ctrl+O would hijack the Open command of whatever program has focus, so this is a deliberate deviation. |
| **Alt as the default tools key** | Left Alt+Shift is Windows' default input-language hotkey, and releasing Alt can send a menu-activation message to Explorer. The default is Ctrl (plus a mouse-wheel tick); Alt remains selectable in Settings with that caveat. |
| **Automatic updates, a Store listing, public signing** | Not built. "Check for Updates…" just opens this project's releases page (github.com/mjtpena/mikeshift/releases) in your browser; the app never goes online itself. Sideloading needs a certificate you trust (see README). |
| **Dithered GIFs** | GIF export reduces each frame to at most 256 colours (a per-frame median-cut palette) with no dithering, so gradients show banding. It is also limited to the first 15 s, 10 fps and 480 px on the long side. |
| **Lossless video Trim** | Video Trim always re-encodes (MP4 for most sources, WMV for WMV sources): cutting without re-encoding can only start on key frames, which gives the wrong start. Only WAV and AIFF audio is cut losslessly; other audio is re-encoded to M4A. |

## Depends on what is installed on the PC

The app probes the machine at startup and hides tiles that cannot work, rather than offering them and failing. The probe takes a fraction of a second on a warm PC and longer on a cold one; a Shift-drag that starts before it has finished shows a reduced set of tiles for a moment (no HEIC, WebP or media tiles) and then fills in the rest while you are still dragging.

| Capability | Needed for | What happens without it |
|---|---|---|
| **HEIF Image Extensions + HEVC Video Extensions** (Microsoft Store; HEVC may be a paid extension on some Windows editions) | HEIC **output**; reading HEIC photos | **HEIC encode depends on both extensions**: without them there is no **HEIC** tile and Compress falls back to PNG for sources that would otherwise become HEIC. HEIC photos that Windows cannot decode are not recognised as images. |
| **AV1 Video Extension** (free) | Reading AVIF | AVIF files are treated as unknown files (Zip only). |
| **WebP Image Extensions** (free) | Reading WebP via Windows | SkiaSharp (bundled) decodes WebP as a fallback; WebP **output** always works (SkiaSharp), a Windows-only extra. |
| **HEVC / VP9 / AV1 decoders** | Reading such videos | The video is not recognised (Zip only) or fails with "Windows can't decode the video in this file." |
| **Web Media Extensions** (Microsoft Store, free) | Reading `.webm` (VP8/VP9/Opus/Vorbis) | **WebM input depends on the Web Media Extensions** and on the installed Media Foundation codecs. The probe checks that WebM actually opens; if not, `.webm` files are unknown files (Zip only). |
| **MP3 / FLAC / H.264 / AAC encoder MFTs** | MP3, FLAC, MP4/M4V, M4A, video and audio Compress | The matching tiles are hidden. FLAC encoding needs Windows 10 1703 or later. |
| **Microsoft Word** | Legacy `.doc` input; higher-fidelity PDF export from DOCX | **Legacy `.doc` needs Word**: without it `.doc` files get no document conversions ("Legacy .doc files need Microsoft Word to be installed."). A `.doc` that is not an OLE2/ZIP/RTF/plain-text file is refused before Word is started ("This isn't a valid Word document (.doc); it may be damaged."). Without Word, PDF is produced by MikeShift's own text layout (below). Word runs invisibly in the background on a private copy of the file, with alerts off, and is always quit afterwards; each conversion takes a second or two longer than the built-in converters because Word has to start. Word conversions run one at a time. **Word is not part of MikeShift and is not offline**: measured on the development PC, each Word instance started for a conversion opened HTTPS connections to Microsoft addresses (port 443) while MikeShift's own processes opened none. Uninstalling or not having Word, or converting anything other than `.doc`/DOCX to PDF, avoids it. A conversion that has not finished after **90 s** (a hung Word, a dialog nobody can see) is stopped and fails with "Word didn't respond, so the conversion was stopped."; MikeShift ends only the Word process it started itself, never one you already had open, and does the same when you cancel. |

## Where Windows hides things from MikeShift

* **Elevated (administrator) windows are invisible.** Windows (UIPI) stops a normal program from seeing the mouse and keyboard events of a higher-integrity window, and from receiving its drops. Shift-drag from an elevated Explorer window or an elevated app shows no wheel. MikeShift itself must not run as administrator for the same reason: if started elevated it shows a warning at startup. A signed `uiAccess` build is a possible future change.
* **Virtual files are refused with a hint.** Attachments dragged from Outlook, entries inside a `.zip` opened in Explorer, files on phones (MTP), and images dragged from a browser are not files on disk at drag time; their data exists only while the drop is being delivered. The wheel shows **"Save the file first"** and refuses the drop (effect NONE, nothing is converted or copied). Save the file somewhere, then drag the saved file.
* **Programs that do not use standard OLE drag and drop** (some games, some cross-platform toolkits, remote-desktop clients) never deliver the file list, so no wheel appears. If no file list arrives within 250 ms of Shift, the wheel hides again silently. Consequently **a bare disc can flash for up to 250 ms** when you press Shift during a drag that is not a file drag (a window move, a text selection): the wheel is shown before Windows says what is being dragged, because that is the only way to receive the drag at all.
* **Cloud placeholders** (OneDrive "online only" files) are classified by extension only, without downloading them. Converting one downloads it first, as any program reading it would.
* **Dragging from Explorer with Shift held before clicking** also means "range select" there; MikeShift still arms once the drag starts. After a conversion, a Shift-click that re-selects the files MikeShift just produced drops those from the next selection (within 20 s), so you don't convert your own outputs.
* **The low-level hook can be removed by Windows** if the process stalls long enough to exceed the system hook timeout. MikeShift keeps the callback minimal and re-installs the hook if it notices a drag with no events, but a very long stall (for example a debugger break) can still cost one gesture.
* **Big selections are classified by sample.** Up to 16 files every file is checked by its content (a misnamed file is noticed). For a larger selection whose extensions all agree, MikeShift confirms them on a sample of 12 files so the wheel appears at once; a misnamed file that is not in the sample then fails alone with its own `error:` message when converted, and the rest are converted. A large selection with mixed extensions is checked in full.

## Conversions: what is refused and why

Messages are shown in the toast or dialog; the CLI prints them as `error: …`. A batch converts what it can and lists the files that failed.

**Images**
* Longer than 200 megapixels: "This image is too large (N megapixels). MikeShift handles up to 200 megapixels." (or "{name} is too large to convert (over 200 megapixels).")
* Decodes blank (usually a truncated HEIC/AVIF or a missing codec): "Couldn't read this image. It decoded as blank, so the file may be damaged or a codec is missing." Otherwise "Couldn't read this image. The file may be damaged." / "… or in a format Windows can't open."
* Compress, Resize and Crop refuse animated images ("Animated images can't be … without losing the animation") and multi-page files such as multi-page TIFF ("This file has N pages, and only the first would be kept, so it can't be …").
* Compress keeps the result only if it is at least 5 % smaller: "This image is already small, so compressing it wouldn't save much space." JPEG output is flattened on white; images with real transparency compress to PNG (the only output here that keeps it), so they are often "already small". HEIC output is also flattened on white, because Windows' HEIF encoder cannot store transparency.
* Crop is refused up front, before the editor opens, for animated GIF, PNG and WebP and for multi-page TIFF, so you never crop and then get an error. For HEIC photos on a PC without a HEIF encoder the **Compress** tile is hidden (it would have nothing to save the result as; see the HEIF row above).
* Crop: "The crop area is outside the image." / "The cropped picture would be too large."
* "There isn't enough memory to process this image."

**PDF**
* Password-protected: "This PDF is password-protected." Damaged: "This PDF is damaged and can't be read." / "This file isn't a valid PDF." A `.pdf` without the `%PDF` header is not treated as a PDF, and a misnamed PDF is. "This PDF has no pages." Files over 2 GB: "This PDF is too large to process."
* PDF to image: up to 2× scale and 8000 px on the long side, white background, using the Windows PDF renderer (so rendering matches what Windows draws, not Acrobat). "Page N could not be rendered."
* PDF to TXT: no extractable text: "probably a scan" (no OCR). Hebrew text stored in reverse is refused rather than written reversed.
* **Any encrypted PDF is refused** by Split, Merge, Compress, PDF to TXT and PDF to images, including one that opens without a password but carries owner-password restrictions (no printing, no copying): MikeShift does not work around restrictions the owner set.
* PDF Compress recompresses images only (skips images with masks/soft masks) and keeps the result only if smaller ("This PDF is already small — compressing it wouldn't make it any smaller.").

**Documents**
* DOCX and ODT must start with the ZIP signature `PK`: "This isn't a valid DOCX file (it may be password-protected or damaged)." Damaged packages: "This DOCX file is damaged and can't be read." / "… has no document body." RTF: "This isn't a valid RTF file." ODT: "This ODT file has no content." / "… is too large to convert."
* **Documents that contain pictures offer TXT only**, because the converters do not carry pictures across (parity with the original): "This document contains pictures, so it can only be converted to TXT."
* **PDF from documents uses MikeShift's own text layout** (PDFsharp: fixed 54 pt margins, black text, simple paragraphs, headings, lists and simple ruled tables, hard page breaks). **It will not match Word's pagination, fonts or styling** (no columns, floating objects, headers/footers or tracked changes). For DOCX with Word installed, PDF is exported by Word instead (falling back to this layout if Word fails). Very long documents are refused: "This document is too long to convert to PDF." (2,000,000 characters; 400,000 for right-to-left text).
* **Hebrew and other right-to-left text in PDF output is laid out in visual order** by a small reordering step (PDFsharp draws glyphs left to right); **there is no Arabic contextual shaping**, so Arabic letters appear in their isolated forms. Use DOCX→PDF through Word for those.
* **Non-Latin text in PDF output uses the fonts installed in Windows**: CJK in Yu Gothic, Korean in Malgun Gothic, Indic scripts in Nirmala UI, Thai and Myanmar in Leelawadee UI, symbols in Segoe UI Symbol and emoji in Segoe UI Emoji. If a font is missing on the PC, that text shows as boxes. Emoji are monochrome (no colour glyphs), and there is no complex-script shaping beyond the right-to-left reordering above, so scripts that need contextual forms or reordering of marks look plainer than in Word.
* RTF is read as a practical subset (text, paragraphs, basic character formatting); HTML is read tolerantly as text structure only and never fetches anything.

**Video and audio**
* "Windows can't open this file." / "Windows can't decode the video (or audio) in this file." (missing codec); "This file has no video." / "no audio."; "Windows Media Foundation is not available on this PC."
* Extract-audio from video offers M4A and MP3 only ("Only M4A and MP3 can be extracted from a video.").
* Compress targets 720p H.264/AAC (audio: AAC M4A) and keeps the result only if smaller: "This video is already small enough." / "This audio is already small enough."
* WAV and AIFF files are limited to 4 GB ("This audio is too long for a WAV file (over 4 GB)."); AIFF is read and written by MikeShift's own code ("This AIFF file is damaged.").
* Trim: "The trim range is empty." / "The trim range starts after the end of the file." Trim re-encodes (above).
* GIF: first 15 s, 10 fps, long side at most 480 px, no dithering (above).

**Zip**
* Named pipes, sockets and device files are refused ("… is a pipe, socket or device and can't be zipped."). Folders that are cloud placeholders (OneDrive "online only" and the like) are included (zipping downloads them), but symbolic links and junctions are skipped, so a folder loop cannot happen. "Nothing to zip." / "… no longer exists."

## Output location

Outputs are written next to the original. If that folder is not writable (read-only share, protected folder), they go to your **Downloads** folder and the toast says "in Downloads". Names never overwrite: ` 2`, ` 3`… are appended. A failed conversion leaves no output and no temporary file behind. While a job runs, its temporary output lives in a hidden folder named `.name.guid.mikeshift-tmp` next to where the result will go; it is moved into place when the job finishes. If MikeShift is killed or Windows crashes in the middle of a job, that folder can be left behind and is safe to delete.

## Packaging, memory and platform

* Windows 10 version 1809 (build 17763) or later, x64 or ARM64. The ARM64 package builds in CI but has not been run on ARM64 hardware.
* The bundle is about 158 MB for both architectures (about 80 MB per architecture) because it carries the .NET runtime and the Windows App SDK components the app uses; the command-line tool runs on the same runtime instead of bringing a second copy (that was another 51 MB per architecture). The Windows App SDK's on-device AI components (ONNX Runtime, DirectML) are **not** shipped (see ARCHITECTURE.md §7); that took the bundle from 305 MB to 257 MB, and sharing the runtime with the CLI to 158 MB. `mikeshift.exe` becomes available in a new terminal after install.
* Idle, the app holds roughly 17 MB working set (about 5 MB private) on the development PC; during a gesture, or while an editor is open, it needs more (about 115 MB private for the wheel in a fresh process, about 190 MB with the crop editor) and gives it back a few seconds later. The first Shift-drag after the app has been idle for more than 15 s re-creates the graphics device when the mouse button goes down; if the button is released and Shift pressed within ~100 ms of that, the wheel can take up to that long to appear instead of the usual ~30 ms.
* Dev-only switches (`MIKESHIFT_*` variables and flags such as `--scene`) are ignored by an installed MikeShift and by any Release build that is not started unpackaged with `--dev`; the list is in ARCHITECTURE.md §8.
