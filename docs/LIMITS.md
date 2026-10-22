# Limits and Windows-specific differences

Everything that works differently from the macOS original, depends on the machine, or does not exist yet. Where a limit shows up as a message, the wording below is the app's own. "Planned" and "in progress" mean the code is not in the tree yet (see the status table in the [README](../README.md)).

## Not offered on Windows

| Item | Why |
|---|---|
| **MOV output** (video) | Media Foundation has no QuickTime muxer. MOV files can be read; they convert to MP4, M4V, WMV, GIF, M4A or MP3. |
| **AVIF output** (images) | Windows has no AVIF encoder and none is bundled. AVIF files can be read when the free "AV1 Video Extension" is installed. |
| **Folders in Convert Files…** | The Windows file dialog cannot select files and folders in one dialog (turning on folder picking turns files off), so Convert Files… picks **files** only. To convert or zip a folder, Shift-drag it from Explorer. |
| **Alt as the default tools key** | Left Alt+Shift is Windows' default input-language hotkey, and releasing Alt can send a menu-activation message to Explorer. The default is Ctrl (plus a mouse-wheel tick); Alt remains selectable in Settings with that caveat. |
| **Automatic updates, a Store listing, public signing** | Not built. "Check for Updates…" just opens a releases page in your browser (the app never goes online itself; the URL still points at the upstream macOS project and needs to be pointed at this project's releases). Sideloading needs a certificate you trust (see README). |

## Depends on what is installed on the PC

The app probes the machine at startup and hides tiles that cannot work, rather than offering them and failing.

| Capability | Needed for | What happens without it |
|---|---|---|
| **HEIF Image Extensions + HEVC Video Extensions** (Microsoft Store; HEVC may be a paid extension on some Windows editions) | HEIC output; reading HEIC photos | No **HEIC** tile. Compress falls back to PNG for sources that would otherwise become HEIC. HEIC photos that Windows cannot decode are not recognised as images. |
| **AV1 Video Extension** (free) | Reading AVIF | AVIF files are treated as unknown files (Zip only). |
| **WebP Image Extensions** (free) | Reading WebP via Windows | SkiaSharp (bundled) decodes WebP as a fallback; WebP **output** always works (SkiaSharp), a Windows-only extra. |
| **HEVC / VP9 / AV1 decoders** | Reading such videos | The video is not recognised (Zip only) or fails with a message. |
| **WebM input** | Reading `.webm` | Uncertain: depends on the installed Media Foundation codecs and extensions. The probe checks that WebM actually opens; if not, `.webm` files are unknown files. |
| **MP3 / FLAC / H.264 / AAC encoder MFTs** | MP3, FLAC, MP4/M4V, M4A, video Compress | The matching tiles are hidden. FLAC encoding needs Windows 10 1703 or later. (Video and audio conversion is in progress.) |
| **Microsoft Word** | Legacy `.doc` input; higher-fidelity PDF export from documents | Without Word, `.doc` files get no document conversions. Without Word, PDF is produced by Shiftvert's own simple text layout (below). |

## Where Windows hides things from Shiftvert

* **Elevated (administrator) windows are invisible.** Windows (UIPI) stops a normal program from seeing the mouse and keyboard events of a higher-integrity window, and from receiving its drops. Shift-drag from an elevated Explorer window or an elevated app shows no wheel. Shiftvert itself must not run as administrator for the same reason: if started elevated it shows a warning at startup. A signed `uiAccess` build is a possible future change.
* **Virtual files are refused with a hint.** Attachments dragged from Outlook, entries inside a `.zip` opened in Explorer, files on phones (MTP), and images dragged from a browser are not files on disk at drag time; their data exists only while the drop is being delivered. The wheel shows "Save the file first" and refuses the drop. *(Planned with the wheel.)*
* **Programs that do not use standard OLE drag and drop** (some games, some cross-platform toolkits, remote-desktop clients) never deliver the file list, so no wheel appears. If no file list arrives within 250 ms of Shift, the wheel hides again silently (planned with the wheel).
* **Cloud placeholders** (OneDrive "online only" files) are classified by extension only, without downloading them. Converting one downloads it first, as any program reading it would.
* **Dragging from Explorer with Shift held before clicking** also means "range select" there; Shiftvert still arms once the drag starts. After a conversion, a Shift-click (planned with the wheel; `JobRunner` already records the last outputs for it) that re-selects the files Shiftvert just produced drops those from the next selection (within 20 s), so you don't convert your own outputs.
* **The low-level hook can be removed by Windows** if the process stalls long enough to exceed the system hook timeout. Shiftvert keeps the callback minimal and re-installs the hook if it notices a drag with no events, but a very long stall (for example a debugger break) can still cost one gesture.

## Conversions: what is refused and why

Messages are shown in the toast or dialog; the CLI prints them as `error: …`.

**Images**
* Longer than 200 megapixels: "This image is too large (N megapixels). Shiftvert handles up to 200 megapixels."
* Decodes blank (usually a truncated HEIC/AVIF or a missing codec): "Couldn't read this image. It decoded as blank, so the file may be damaged or a codec is missing."
* Compress, Resize and Crop refuse animated images ("Animated images can't be … without losing the animation") and multi-page files such as multi-page TIFF ("… only the first would be kept").
* Compress keeps the result only if it is at least 5 % smaller: "This image is already small…". JPEG output is flattened on white; images with real transparency compress to PNG (or HEIC when available).

**PDF**
* Password-protected: "This PDF is password-protected." Damaged: "This PDF is damaged and can't be read." A `.pdf` without the `%PDF` header is not treated as a PDF, and a misnamed PDF is.
* PDF to image: up to 2× scale and 8000 px on the long side, white background, using the Windows PDF renderer (so rendering matches what Windows draws, not Acrobat).
* PDF to TXT: no extractable text: "probably a scan" (no OCR). Hebrew text stored in reverse is refused rather than written reversed.
* PDF Compress recompresses images only (skips images with masks/soft masks), keeps the result only if smaller, never touches encrypted files.
* Files over 2 GB are refused.

**Documents** *(in progress)*
* Legacy `.doc` needs Word (above). DOCX and ODT must start with the ZIP signature `PK`.
* Documents that contain pictures offer **TXT only**, because the converters do not carry pictures across (parity with the original).
* **PDF from documents uses Shiftvert's own text layout** (PDFsharp: fixed margins, black text, simple paragraphs, headings, lists and tables). It will not match Word's pagination, fonts or styling. With Word installed it is exported by Word instead. Very long documents are refused (2,000,000 characters; 400,000 for right-to-left text).
* RTF is read as a practical subset (text, paragraphs, basic character formatting); HTML is read tolerantly as text structure only and never fetches anything.

**Zip**
* Named pipes, sockets and device files are refused ("…is a pipe, socket or device and can't be zipped"); folder loops through reparse points are not followed.

**Video and audio** *(in progress; figures are the specification, not yet verified in code)*: first 15 s, 10 fps, max 480 px for GIF; Compress targets 720p; Trim is lossless only for WAV/AIFF audio and stream-copy-able video. AIFF is read and written by Shiftvert's own code.

## Output location

Outputs are written next to the original. If that folder is not writable (read-only share, protected folder), they go to your **Downloads** folder and the toast says "in Downloads". Names never overwrite: ` 2`, ` 3`… are appended.

## Packaging and platform

* Windows 10 version 1809 (build 17763) or later, x64 or ARM64. The ARM64 package builds in CI but has not been run on ARM64 hardware.
* The package is about 150 MB per architecture because it carries the .NET runtime and the Windows App SDK (and a second self-contained copy of the CLI, about 45 MB). `shiftvert.exe` becomes available in a new terminal after install.
* The Windows App SDK pulls in components for on-device AI features that Shiftvert does not use; they are part of the size and are covered in [NOTICE](../NOTICE).
* Wheel, drag integration and the Crop and Trim editors are **planned**; until they land, Crop and Trim work from the CLI only (`--crop`, `--range`) and the tray's Convert Files… shows a plain action list.
