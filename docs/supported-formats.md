# Supported formats

MikeShift offers 41 actions across six kinds of file. The wheel and the right-click menu show only the actions that fit your selection and that your PC can do, so you never see a tile that cannot work. This page lists what can go in, what can come out, and what depends on optional Microsoft extensions.

How a file is recognised: by its content and its extension. A PDF is recognised by its `%PDF` header even if it is misnamed. Video and audio files are recognised only when Windows can actually open them. A selection of mixed kinds offers **Zip** only.

## What you can convert

| You have | Convert to | Tools |
|---|---|---|
| **Pictures**: PNG, JPG, HEIC, TIFF, GIF, WebP, BMP, AVIF, ICO, JPEG XR | PNG, JPG, TIFF, GIF, BMP, WebP, HEIC, PDF | Compress, Resize, Crop, Strip EXIF, Merge PDF |
| **PDF** | Page images (PNG or JPG), TXT | Compress, Split, Merge |
| **Video**: MP4, M4V, MOV, AVI, WMV, MKV, WebM, MPEG, 3GP, TS and others | MP4, M4V, WMV, GIF, or just the audio (M4A, MP3) | Compress, Trim |
| **Audio**: MP3, WAV, M4A, AAC, FLAC, AIFF, WMA, ALAC | M4A, WAV, AIFF, FLAC, MP3 | Compress, Trim |
| **Documents**: DOCX, RTF, ODT, HTML, TXT, Markdown, and DOC (needs Word) | PDF, DOCX, RTF, TXT, HTML, ODT | none |
| **Anything, including folders** | Zip | none |

A format is not offered for a file that is already in it (a PNG has no "PNG" tile), and a few tools appear only for the right selection:

* **Merge PDF** (for pictures) and **Merge** (for PDFs) need at least two files.
* **Resize** is named for your setting: **Resize 25%**, **Resize 50%** (the default) or **Resize 75%**.
* **Compress** is hidden for HEIC photos on a PC that cannot write HEIC, because it would have nothing to save the result as.

### Pictures

| Input extensions | `png` `jpg` `jpeg` `jpe` `jfif` `heic` `heif` `tif` `tiff` `gif` `webp` `bmp` `dib` `avif` `ico` `jxr` `wdp` |
|---|---|
| Output formats | PNG, JPG, TIFF, GIF, BMP, WebP, HEIC, and PDF (one picture per PDF) |
| Tools | **Compress** keeps the format and saves a smaller copy, only when it saves at least 5 percent. **Resize** halves (or quarters, or takes three quarters of) both sides. **Crop** opens the [crop editor](user-guide.md#crop-and-trim). **Strip EXIF** removes the camera, location and other metadata but keeps the picture upright. **Merge PDF** puts two or more pictures into one PDF. |

Pictures are converted upright: the camera's rotation is applied, so the result is not sideways. Compress, Resize and Crop refuse animated pictures and multi-page files (such as a multi-page TIFF) instead of silently dropping frames.

### PDF

| Output | What you get |
|---|---|
| PNG or JPG | One image per page, in a new folder named `<name> pages`, at up to twice the page size and no more than 8,000 pixels on the long side, on a white background. |
| TXT | The text of the PDF (no OCR: a scanned PDF has no text to extract). |
| Compress | A copy with the pictures inside recompressed, only if it comes out smaller. |
| Split | One PDF per page, in a new folder named `<name> split`. |
| Merge | `Merged.pdf`, from two or more PDFs, in the order they are listed. |

Encrypted PDFs are refused (see [limitations](limitations.md#pdf)).

### Video

| Input extensions | `mp4` `m4v` `mov` `avi` `wmv` `asf` `mkv` `webm` `mpg` `mpeg` `3gp` `3g2` `ts` `mts` `m2ts` |
|---|---|
| Output formats | MP4, M4V, WMV, GIF, M4A (audio only), MP3 (audio only) |
| Tools | **Compress** makes a 720p H.264/AAC MP4, only if smaller. **Trim** cuts a range and re-encodes it (see the [trim dialog](user-guide.md#crop-and-trim)). |

MP4 and M4V made from an H.264/AAC source are re-wrapped without re-encoding, which is fast and lossless. Everything else is transcoded.

### Audio

| Input extensions | `mp3` `wav` `m4a` `aac` `flac` `wma` `alac` `aif` `aiff` `aifc` |
|---|---|
| Output formats | M4A, WAV, AIFF, FLAC, MP3 |
| Tools | **Compress** makes a smaller AAC M4A, only if smaller. **Trim** cuts a range: WAV and AIFF are cut without any loss; other formats come out as M4A. |

### Documents

| Input extensions | `docx` `doc` `rtf` `odt` `html` `htm` `txt` `md` `markdown` |
|---|---|
| Output formats | PDF, DOCX, RTF, TXT, HTML, ODT |

A document is converted by MikeShift's own readers and writers. Text, paragraphs, headings, lists, simple tables and basic character formatting carry across. Documents that contain pictures can be converted to TXT only, because pictures do not carry across. PDFs made from documents use MikeShift's own plain layout and do not match Word's pagination; see [limitations](limitations.md#documents).

### Zip

Zip works on any selection, including folders and mixed files. One item becomes `<name>.zip`; several become `Archive.zip`. Symbolic links and junctions inside folders are skipped.

## What depends on your PC

MikeShift checks at start-up what Windows on this PC can do, and hides tiles that cannot work. Most of the optional pieces are free Microsoft Store extensions.

| You want to | You need | If it is missing |
|---|---|---|
| Open or save **HEIC** (iPhone) photos | **HEIF Image Extensions** and **HEVC Video Extensions from Device Manufacturer** (both free, from the Microsoft Store) | No HEIC tile. A HEIC photo Windows cannot open shows a **Get HEIF** tile that opens the Store page. Restart MikeShift after installing. |
| Open **AVIF** pictures | **AV1 Video Extension** (free) | A **Get AV1** tile that opens the Store page. AVIF output is never offered. |
| Open **WebP** pictures | Nothing: MikeShift reads WebP itself. The free **WebP Image Extensions** make Windows read them too. | WebP output always works. |
| Open **WebM** video | **Web Media Extensions** (free) | `.webm` files are not recognised as video (only Zip is offered). |
| Open HEVC, VP9 or AV1 video | The matching Windows video extension | The video is not recognised, or fails with "Windows can't decode the video in this file." |
| Make **MP3**, **FLAC**, **MP4/M4V** or **M4A** | Windows' built-in encoders (present on current Windows 10 and 11; FLAC needs Windows 10 version 1703 or later) | The matching tiles are hidden. |
| Convert old **.doc** files, or get Word's own PDF layout from DOCX | **Microsoft Word**, turned on in Settings | `.doc` files get only Zip. See [Word](user-guide.md#microsoft-word). |

**Settings → Format support** shows what is installed on your PC, with a **Get from Microsoft Store** link for each missing extension.

## What is not supported

* **MOV and AVIF output.** Windows has no QuickTime or AVIF writer. MOV files can be read; they convert to MP4, M4V, WMV, GIF, M4A or MP3. AVIF files can be read when the AV1 extension is installed.
* **OCR, PDF editing and PDF forms.** MikeShift converts, merges, splits and compresses PDFs; it does not edit them or read scanned text.
* **Presets, custom sizes and per-format options.** Settings has [five conversion choices](user-guide.md#conversions); there is no custom percentage, pixel size, bitrate or naming template.
* **Lossless video trim.** Video Trim always re-encodes, so the cut is exact to the frame.
* **Dithered GIFs.** GIFs use at most 256 colours per frame, 10 frames a second and 480 pixels on the long side.
* **Archives other than Zip,** and any conversion between archive types.

More detail, with the exact messages the app shows: [limitations](limitations.md).
