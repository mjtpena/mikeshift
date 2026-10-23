# Is everything local, and are the conversions intact? (2026-10-24)

Question: "double-check that everything is local (no cloud / external service) and all the conversions work and are not corrupted".

* Build under test: `feature/windows-port` @ `02fea21` plus the fixes listed in section B4 (branch `worktree-agent-a5ea94d3abef2c72b`). Debug x64.
* Machine: Windows 11 10.0.26300 x64. Optional codecs present, so nothing was skipped for lack of a codec: HEIF encoder, WebP, H.264, AAC, MP3 and FLAC encoders; Microsoft Word is installed (the Word paths were exercised).
* Everything below was produced by code in this repository: `scripts/audit-network-imports.ps1`, `scripts/verify-offline.ps1`, `tests/MikeShift.Core.Tests/Integrity/**` and `tests/MikeShift.Cli.Tests/IntegrityCliTests.cs`.

## Verdict

**A. Local.** MikeShift's own code contains no network code, and in 14,436 samples taken every 50 ms over 19 minutes (271 `mikeshift.exe` processes running every action on every kind of file, plus a `MikeShift.exe` session that idled 60 s and then converted a file) **no MikeShift process ever owned a TCP connection, a TCP listener or a UDP endpoint**. The only outbound-capable call sites are explicit clicks that hand a link to the browser or the Microsoft Store. **One caveat that is not MikeShift's:** when Microsoft Word is installed, legacy `.doc` input and DOCX-to-PDF are handed to Word, and the Word instances MikeShift started opened HTTPS connections to Microsoft addresses (details in A3). With Word absent, or for any other conversion, nothing leaves the PC.

**B. Intact.** Every one of the 41 actions the catalog can offer was run on every kind of file it applies to and judged by decoders that are not the code that wrote the output: **288 input-by-action checks, all passing in the final run** (272 validate a produced file; 16 confirm a documented refusal such as "already small" or "animated images can't be resized" and that nothing was written). The suite found **six real defects** (silent corruption or loss in four of them, a wrong colour block in one, a failed conversion in one); all six are fixed in `src/MikeShift.Core` and covered by tests that fail without the fix.

---

# Part A: is everything local?

## A1. Static audit

### Outbound-capable call sites in `src/**`

A search of all of `src/**` for `HttpClient`, `WebRequest`, `WebClient`, `System.Net.*`, sockets, `Dns`, WinHTTP/WinINet P/Invokes (`NativeMethods.txt/json`, `DllImport`, `LibraryImport`), `Windows.Web.*`, `Windows.Networking.*`, `WebView`, `StoreContext`, telemetry/analytics/AppCenter/Sentry/Insights found **no networking code**. The only `System.Net` use is `WebUtility` (HTML entity encoding) in `HtmlReader.cs` / `HtmlWriter.cs`. The ODT reader loads XML with `DtdProcessing.Prohibit` and `XmlResolver = null`, so no external entity or schema is ever fetched; the HTML reader is a text tokenizer that never fetches anything. The call sites that can cause traffic at all are these, each only on an explicit click:

| Where | What it does | Triggered by | Who connects |
|---|---|---|---|
| `Tray/TrayController.cs:231,277` `OpenReleasesPage` | `Launcher.LaunchUriAsync("https://github.com/mjtpena/mikeshift/releases")` | tray menu "Check for updates" | the default browser |
| `Settings/SettingsWindow.xaml.cs:219` `Open(url)` | same launcher call for `AppLinks.Releases`, `Project`, `Notice`, `Shiftvert` (github.com) | About card buttons (Check for updates, GitHub, Notice, Shiftvert) | the default browser |
| `Settings/SettingsWindow.xaml.cs:122` | `Launcher.LaunchUriAsync("ms-windows-store://pdp/?ProductId=...")` (string built in `Core/Feedback/CodecSummary.cs`) | "Get from Microsoft Store" link beside a missing codec | the Microsoft Store app |
| `FirstRun/WelcomeWindow.xaml.cs:50` | `Launcher.LaunchUriAsync("ms-settings:taskbar")` | button in the welcome window | the Windows Settings app (local, no network) |
| `Settings/SettingsWindow.xaml.cs:235`, `Hud/ShellReveal.cs:23` | `explorer.exe <folder>` / `/select` | "Open logs", "show in folder" | Explorer (local) |
| `Hud/ChimePlayer.cs:22`, `Chrome/WindowChrome.cs`, `Settings/SettingsWindow.xaml.cs:48` | `MediaSource` / `BitmapImage` from a `Uri` | start-up / conversion sound, window icons | local files in the app folder |

MikeShift never makes the request itself; the link is passed to another program, and only after you click. There is no update check, telemetry, crash upload, licence check or font/asset download.

### Package manifest and dependency graph

* `Package.appxmanifest` declares one capability, `runFullTrust`; no `internetClient`, `internetClientServer` or `privateNetworkClientServer`. (A full-trust desktop process is not sandboxed, so the missing capability is a statement of intent, not enforcement; the runtime measurement below is the evidence.)
* `dotnet list package --include-transitive` for the product projects: direct packages are PDFsharp 6.2.4, PdfPig, DocumentFormat.OpenXml, SkiaSharp (+ native assets), CsWin32 (build time), the Windows App SDK by component, Win2D, H.NotifyIcon, WinUIEx, CommunityToolkit.Mvvm. Transitive: System.IO.Packaging, System.Security.Cryptography.Pkcs, System.Drawing.Common, Microsoft.Extensions.Logging/DI abstractions, `Microsoft.Web.WebView2` (a dependency of `Microsoft.WindowsAppSDK.WinUI`; MikeShift has no WebView2 control). **No telemetry, analytics, crash-reporting, AppCenter, Sentry or HTTP-client package anywhere in the graph.** The Windows AI / ML packages are restored with `ExcludeAssets=all` and are not shipped (ARCHITECTURE.md section 7).

### Binary scan (`scripts/audit-network-imports.ps1`)

The script reads the metadata of every managed DLL and the import table of every native DLL/EXE in a build folder, without running anything. Results for the CLI output (216 binaries) and the app output (498 binaries):

| Binary | Finding | Reachable from MikeShift? |
|---|---|---|
| `mikeshift.dll`, `MikeShift.dll` | **no networking type references** | n/a |
| `MikeShift.Core.dll` | only `System.Net.WebUtility` | string handling, not networking |
| `SkiaSharp.dll`, `UglyToad.PdfPig*.dll`, `DocumentFormat.OpenXml*.dll`, `PdfSharp.dll` | none | n/a |
| `PdfSharp.Cryptography.dll`, `PdfSharp.Quality.dll` | `System.Net.Http.HttpClient` (PDFsharp's digital-signature timestamp client and its QA helpers) | no: MikeShift never signs or uses the QA library; neither DLL was ever loaded in any MikeShift process (A2) |
| `Microsoft.WinUI.dll`, `Microsoft.Security.Authentication.OAuth.Projection.dll` | `Microsoft.Web.WebView2.*`, `Windows.Web.Http.*` type references (Windows App SDK) | no: no WebView2 or OAuth in MikeShift; `WebView2Loader.dll` and `System.Net.Http.dll` were never loaded |
| .NET runtime files shipped with the self-contained CLI (`System.Net.*.dll`, `msquic.dll`, `createdump.exe`) | the framework's own networking stack; `msquic.dll` and `createdump.exe` import `ws2_32` | present because the runtime is bundled; unused (never loaded) |
| `Microsoft.ui.xaml.dll` (WinUI) | imports `urlmon.dll` | WinUI's URI/MIME plumbing; the only networking-library import in the app's own binaries; no socket resulted from it (A2) |

"Present" is not "used", which is why the runtime measurement exists.

## A2. Runtime proof (`scripts/verify-offline.ps1`)

What it does: writes one sample of every kind of file (28 files plus a folder: PNG with alpha, rotated JPG, GIF, animated GIF, multi-page TIFF, BMP, WebP, HEIC, a photo as PNG and JPG, text and image PDFs, WAV 16/24, AIFF, MP3, M4A, FLAC, MP4, WMV, DOCX, RTF, ODT, HTML, TXT, MD, a legacy `.doc`), then runs `mikeshift --list` and **`mikeshift --run <title>` for every action offered for every file** (Crop and Trim get `--crop` / `--range`; Merge, Merge PDF and Zip get multiple inputs or a folder), each as its own process; then starts `MikeShift.exe` (`MIKESHIFT_NO_SINGLE_INSTANCE=1`, hooks off), lets it idle 60 s, launches a second instance with `--convert <png>` (the click-mode wheel), sends Right+Enter to pick a tile (a file was written: the conversion ran inside the app) and closes both. No administrator action, no firewall or OS setting is touched.

How it watches: a native sampler reads `GetExtendedTcpTable` / `GetExtendedUdpTable` (IPv4 and IPv6, every TCP state including LISTEN) **every 50 ms** for the PIDs of processes named `mikeshift`, `MikeShift` and `WINWORD`. Those are the tables `Get-NetTCPConnection -OwningProcess` and `Get-NetUDPEndpoint -OwningProcess` read; the script also polls those two cmdlets in a second thread as a cross-check (2,227 rounds). Loaded modules of the MikeShift processes are matched against networking libraries. The DNS client cache is diffed before and after.

Result of the final run (2026-10-24 07:32 to 07:51, 1,144 s):

| Item | Result |
|---|---|
| Sampler | 14,436 samples at 50 ms |
| `mikeshift.exe` processes observed | 264 distinct PIDs (273 launches: 271 CLI processes and 2 app instances; the 9 not seen are short runs that exited between two samples, most likely `--list` calls) |
| `MikeShift.exe` | 2 instances: 60 s idle + one conversion through the wheel |
| CLI exit codes | 227 x exit 0; 16 x exit 1, all of them documented refusals (already small, animated image, multi-page TIFF, no audio, "probably a scan"); no unexpected failure |
| **TCP connections / listeners / UDP endpoints owned by MikeShift processes** | **0** (sampler) and **0** (cmdlet cross-check) |
| Networking modules loaded by MikeShift processes | `WS2_32.dll`, `urlmon.dll` (app only), `System.Private.Uri.dll`. **Never loaded:** `System.Net.Http`, `System.Net.Sockets`, `System.Net.Quic`, `winhttp`, `wininet`, `dnsapi`, `PdfSharp.Quality`, `PdfSharp.Cryptography`, WebView2. `ws2_32` is pulled in by the .NET runtime / Windows components in nearly every process; loading it opens no socket, and no socket ever appeared. |
| DNS client cache | no entry added during the 19 minutes |

Limits of this evidence, stated plainly: it sees sockets owned by the processes. A name lookup is performed by the DNS Client service on a process's behalf and is not attributable to the process without administrator-level ETW; the cache diff (no new entry) and the absence of any socket are what stand in for it. It shows what happened on this PC in this run, not a proof over all inputs; the static audit above is what covers the rest.

## A3. Things that are not MikeShift

* **Microsoft Word.** With Word installed, legacy `.doc` files and DOCX-to-PDF run Word (invisible, on a private copy, quit afterwards). The 7 `WINWORD.EXE` processes started by the run (a Word that was already open before the run was ignored) owned **234 distinct TCP sockets, all to port 443**, in Microsoft address ranges (for example 13.x, 20.x, 23.x, 2603:1016:: / 2603:1063:: / 2620:1ec::), 163 of them reaching ESTABLISHED. That is Word's own start-up traffic (licensing, "connected experiences" and similar, governed by the Office privacy settings), not MikeShift's. The cmdlet cross-check agrees (the same 7 PIDs). README (Privacy) and LIMITS.md now say so. If "nothing leaves the PC" must hold for those two conversions as well, Word cannot be used for them; without Word MikeShift falls back to its own layout for DOCX-to-PDF and refuses `.doc`.
* **OneDrive placeholders.** Reading an "online only" file makes OneDrive download it first, as for any program (already in LIMITS.md). Not exercised here (no placeholder available).
* **Link clicks.** The browser or the Store app, started by an explicit click, of course connects.

---

# Part B: does every conversion work, and is the output intact?

## B1. How the checks work

Each action is run through the real catalog (`ActionCatalog.For` + `ActionDef.Run`, the path used by the wheel and the CLI), then the **output** is judged by a reader that is not the code that wrote it:

| Kind | Independent readers used | What must hold |
|---|---|---|
| Images | SkiaSharp (libpng, libjpeg-turbo, libgif, libwebp) **and** WIC driven directly from the test; they must agree with each other | real container (magic bytes) matches the tile; upright dimensions; PNG/TIFF/BMP/GIF-from-palette pixel-exact against the source decoded independently (BMP: alpha flattened on white); lossy targets PSNR >= 30 dB on a photograph-like picture (>= 22 dB on the four-colour hard-edged fixtures, where 4:2:0 chroma and lossy codecs lose the edges by design); Resize 50 % exactly half; Crop rectangle exact; Strip EXIF leaves no Exif/GPS/XMP/Photoshop/camera/orientation data (raw JPEG segments **and** WIC properties) and still decodes upright; Compress >= 5 % smaller and PSNR >= 25 dB; image to PDF: PdfPig page count (TIFF: one per frame), Windows.Data.Pdf renders it, picture fitted with the right aspect ratio and the right colours in each quadrant |
| PDF | PdfPig (structure, text, fonts) and Windows.Data.Pdf (render) | PNG/JPG: page count, size = 2x natural size, ink where the text was drawn, the embedded photo matches its source; TXT equals the text that was drawn **and** PdfPig's own extraction (whitespace-normalised), non-ASCII intact as strict UTF-8; Split: every part opens, 1 page, text of page *i* in file *i*, total = source; Merge: pages = sum, text order preserved, renders like the sources; Compress: smaller, every page renders (>= 25 dB per page) |
| Audio | hand-written RIFF/FORM/STREAMINFO/MPEG-frame/MP4-box parsers; Windows' shell duration; a Media Foundation decode (via `MediaTranscoder`, not the Source Reader the converters use) and cross-correlation | WAV/AIFF/FLAC sample-exact against the source PCM (FLAC: also its own STREAMINFO MD5); lossy (MP3/M4A): valid frames/boxes, duration within 0.10 s (encoder delay and padding), whole-file correlation >= 0.9 **and** correlation of the last 100 ms >= 0.9 (catches clipped tails); Trim: WAV/AIFF sample-exact slice, others correct segment (correlation >= 0.9 with the requested part, far lower with the wrong part); Compress decodes and is smaller |
| Video | MP4 box parser, ASF signature, MediaClip (duration/size), a sequential Media Foundation frame decode, GIF block parser + SkiaSharp frame compositing, audio correlation | MP4/M4V/WMV: duration +-0.15 s, size, avc1/mp4a tracks, four sampled frames >= 25 dB against the source; M4V of an ordinary MP4 is a lossless re-wrap (same sample counts, frames >= 45 dB); GIF: <= 480 px, 18-22 frames for 2 s, loops forever, frames >= 25 dB; extracted M4A/MP3 correlate with the video's own audio including the last 100 ms; Trim: duration, picture **and** audio come from the requested segment |
| Documents | OpenXml SDK + `OpenXmlValidator` (DOCX), System.Xml + ODF package rules (ODT: `mimetype` first, stored, exact; manifest; every XML part well formed), RTF and HTML tokenizers written for the suite, PdfPig (PDF) | headings, bold/italic/underline, bulleted and numbered lists, the table and every word identical to the source model for DOCX/RTF/ODT/HTML sources in every other format; TXT/MD sources in four encodings (UTF-8, UTF-8 BOM, Windows-1252, Windows-1255 Hebrew) keep every character; PDF has all words in order, headings larger than body, bold and italic faces, renders; documents with pictures offer only TXT |
| Zip | `System.IO.Compression` **and** Windows' `tar.exe` (bsdtar) | every file SHA-256 identical, structure, empty folders, Unicode names, UTF-8 flag set in the central directory, unique names for clashes, timestamps kept |
| Always | | originals SHA-256 identical before and after; no `.mikeshift-tmp` left anywhere |

Everything also runs once more through the real `mikeshift.exe` child process (`IntegrityCliTests`): `--list` equals the catalog, every action exits 0, prints absolute existing paths that validate, leaves the original byte-identical and no staging folder. Non-Latin output paths survive the pipe.

Fixtures are generated at test time with code that is independent of the converters (hand-built PNG/GIF/TIFF/BMP/WAV/AIFF/DOCX/ODT/RTF, Skia/WIC/Windows encoders for the lossy kinds). Each oracle is itself checked (for example: the independent decode of a lossless fixture must equal the pixels the generator drew; the rotated JPEG must show red top-right).

## B2. Counts

| | |
|---|---|
| Catalog actions exercised | 41 of 41 (Image 13, PDF 6, Video 8, Audio 7, Document 6, Zip 1) |
| Input-by-action checks recorded | **288** (123 image, 16 PDF, 38 audio, 17 video, 53 document, 3 zip, 38 through `mikeshift.exe`) |
| Passed in the final run | **288 / 288** (272 validate an output, 16 confirm a documented refusal with nothing written) |
| Defects found and fixed | **6** (B4) |
| Not verifiable on this PC | see B5 |
| Test totals | Before: 1,828 Core + 70 CLI. Now: 2,043 Core + 76 CLI pass in a plain `dotnet test MikeShift.sln` (12 + 2 slow tests are reported as skipped), and **2,098 Core + 78 CLI pass with `MIKESHIFT_SLOW=1`**; build: 0 warnings. The default Core run takes 1 m 10 s here versus 1 m 4 s before (about 6 s added, within run-to-run noise; enabling the slow set adds roughly another 10-20 s). |

## B3. Matrix

Each row is one input kind by one action. The "Result" column is `pass` for every row. Metrics are the measured values from the final run (PSNR in dB against the independent decode of the source; "exact" means identical). Rows named `(source)` check the fixtures themselves, not a conversion.

### Images (133 checks)

| Input | Action | Validator | Result | Metric |
|---|---|---|---|---|
| a.png + b.jpg + c.tif(2 frames) | Merge PDF | PdfPig + Windows.Data.Pdf | pass | 4 pages, orientation per picture, all render |
| alpha.png | (source) | WIC+SkiaSharp | pass | 64x48, 1 frame(s) |
| alpha.png | BMP | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact (alpha flattened on white) |
| alpha.png | Compress | refusal check | pass | refused: already small (nothing written) |
| alpha.png | Crop | WIC+SkiaSharp decode vs independent source decode | pass | rect 32x24 at (8,6) pixel-exact |
| alpha.png | GIF | WIC+SkiaSharp decode vs independent source decode | pass | exact, transparency kept |
| alpha.png | HEIC | WIC+SkiaSharp decode vs independent source decode | pass | 27.8 dB (alpha on white) |
| alpha.png | JPG | WIC+SkiaSharp decode vs independent source decode | pass | 30.0 dB (alpha on white) |
| alpha.png | PDF | PdfPig (structure) + Windows.Data.Pdf (render) | pass | 1 page(s) = 1 expected, renders, quadrant colours within 40 levels |
| alpha.png | Resize 50% | WIC+SkiaSharp decode vs independent source decode | pass | exactly 32x24 (half), exact vs ideal 2x2 box filter |
| alpha.png | Strip EXIF | WIC+SkiaSharp decode vs independent source decode | pass | exact; no Exif/GPS/XMP/camera/orientation data |
| alpha.png | TIFF | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact incl. alpha |
| alpha.png | WebP | WIC+SkiaSharp decode vs independent source decode | pass | 29.1 dB (alpha on white) |
| anim.gif | (source) | WIC+SkiaSharp | pass | 64x48, 2 frame(s) |
| anim.gif | BMP | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact |
| anim.gif | Compress | refusal check | pass | refused: Animated images can't be compressed without losing the animation. |
| anim.gif | Crop | refusal check | pass | refused: Animated images can't be cropped without losing the animation. |
| anim.gif | HEIC | WIC+SkiaSharp decode vs independent source decode | pass | 26.6 dB |
| anim.gif | JPG | WIC+SkiaSharp decode vs independent source decode | pass | 27.8 dB |
| anim.gif | PDF | PdfPig (structure) + Windows.Data.Pdf (render) | pass | 1 page(s) = 1 expected, renders, quadrant colours within 40 levels |
| anim.gif | PNG | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact |
| anim.gif | Resize 50% | refusal check | pass | refused: Animated images can't be resized without losing the animation. |
| anim.gif | Strip EXIF | refusal check | pass | refused: Animated images can't be cleaned without losing the animation. |
| anim.gif | TIFF | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact |
| anim.gif | WebP | WIC+SkiaSharp decode vs independent source decode | pass | 27.8 dB |
| c.png 64x48 | Crop 0,0,32,24 -> 32x24 | WIC+SkiaSharp | pass | pixel-exact |
| c.png 64x48 | Crop 0,0,64,48 -> 16x12 | WIC+SkiaSharp | pass | size exact, content matches |
| c.png 64x48 | Crop 10,6,40,30 -> 40x30 | WIC+SkiaSharp | pass | pixel-exact |
| c.png 64x48 | Crop 32,24,32,24 -> 64x48 | WIC+SkiaSharp | pass | size exact, content matches |
| lossless.webp | (source) | WIC+SkiaSharp | pass | 64x48, 1 frame(s) |
| lossless.webp | BMP | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact within 1 level (WIC vs libwebp rounding) |
| lossless.webp | Compress | refusal check | pass | refused: already small (nothing written) |
| lossless.webp | Crop | WIC+SkiaSharp decode vs independent source decode | pass | rect 32x24 at (8,6), 24.6 dB |
| lossless.webp | GIF | WIC+SkiaSharp decode vs independent source decode | pass | 50.5 dB |
| lossless.webp | HEIC | WIC+SkiaSharp decode vs independent source decode | pass | 26.5 dB |
| lossless.webp | JPG | WIC+SkiaSharp decode vs independent source decode | pass | 27.8 dB |
| lossless.webp | PDF | PdfPig (structure) + Windows.Data.Pdf (render) | pass | 1 page(s) = 1 expected, renders, quadrant colours within 40 levels |
| lossless.webp | PNG | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact within 1 level (WIC vs libwebp rounding) |
| lossless.webp | Resize 50% | WIC+SkiaSharp decode vs independent source decode | pass | exactly 32x24 (half), 24.8 dB vs ideal 2x2 box filter |
| lossless.webp | Strip EXIF | WIC+SkiaSharp decode vs independent source decode | pass | 27.9 dB; no Exif/GPS/XMP/camera/orientation data |
| lossless.webp | TIFF | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact within 1 level (WIC vs libwebp rounding) |
| odd.png 63x47 | Resize 50% | WIC+SkiaSharp | pass | 32x24 (rounds up), content intact |
| pages.tif | (source) | WIC+SkiaSharp | pass | 64x48, 3 frame(s) |
| pages.tif | BMP | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact |
| pages.tif | Compress | refusal check | pass | refused: This file has 3 pages, and only the first would be kept, so it can't be compressed. |
| pages.tif | Crop | refusal check | pass | refused: This file has 3 pages, and only the first would be kept, so it can't be cropped. |
| pages.tif | GIF | WIC+SkiaSharp decode vs independent source decode | pass | exact |
| pages.tif | HEIC | WIC+SkiaSharp decode vs independent source decode | pass | 26.6 dB |
| pages.tif | JPG | WIC+SkiaSharp decode vs independent source decode | pass | 27.8 dB |
| pages.tif | PDF | PdfPig (structure) + Windows.Data.Pdf (render) | pass | 3 page(s) = 3 expected, renders, quadrant colours within 40 levels |
| pages.tif | PNG | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact |
| pages.tif | Resize 50% | refusal check | pass | refused: This file has 3 pages, and only the first would be kept, so it can't be resized. |
| pages.tif | Strip EXIF | refusal check | pass | refused: This file has 3 pages, and only the first would be kept, so it can't be cleaned. |
| pages.tif | WebP | WIC+SkiaSharp decode vs independent source decode | pass | 27.8 dB |
| photo.heic | (source) | WIC+SkiaSharp | pass | 64x48, 1 frame(s) |
| photo.heic | BMP | WIC+SkiaSharp decode vs independent source decode | pass | 50.5 dB |
| photo.heic | Compress | refusal check | pass | refused: already small (nothing written) |
| photo.heic | Crop | WIC+SkiaSharp decode vs independent source decode | pass | rect 32x24 at (8,6), 28.4 dB |
| photo.heic | GIF | WIC+SkiaSharp decode vs independent source decode | pass | 44.3 dB |
| photo.heic | JPG | WIC+SkiaSharp decode vs independent source decode | pass | 28.9 dB |
| photo.heic | PDF | PdfPig (structure) + Windows.Data.Pdf (render) | pass | 1 page(s) = 1 expected, renders, quadrant colours within 40 levels |
| photo.heic | PNG | WIC+SkiaSharp decode vs independent source decode | pass | 50.5 dB |
| photo.heic | Resize 50% | WIC+SkiaSharp decode vs independent source decode | pass | exactly 32x24 (half), 26.9 dB vs ideal 2x2 box filter |
| photo.heic | Strip EXIF | WIC+SkiaSharp decode vs independent source decode | pass | 30.9 dB; no Exif/GPS/XMP/camera/orientation data |
| photo.heic | TIFF | WIC+SkiaSharp decode vs independent source decode | pass | 50.5 dB |
| photo.heic | WebP | WIC+SkiaSharp decode vs independent source decode | pass | 28.9 dB |
| photo.jpg 1200x800 (jpg) | Compress | WIC+SkiaSharp decode | pass | 531 KB -> 84 KB (jpeg), 31.8 dB |
| photo.png 1200x800 (png) | Compress | WIC+SkiaSharp decode | pass | 3756 KB -> 83 KB (jpeg), 31.9 dB |
| photo.png 1200x800 (png-alpha) | Compress | WIC+SkiaSharp decode | pass | 3756 KB -> 1385 KB (png), exact |
| plain.bmp | (source) | WIC+SkiaSharp | pass | 64x48, 1 frame(s) |
| plain.bmp | Compress | WIC+SkiaSharp decode vs independent source decode | pass | 27.3 dB |
| plain.bmp | Crop | WIC+SkiaSharp decode vs independent source decode | pass | rect 32x24 at (8,6) pixel-exact |
| plain.bmp | GIF | WIC+SkiaSharp decode vs independent source decode | pass | exact |
| plain.bmp | HEIC | WIC+SkiaSharp decode vs independent source decode | pass | 26.6 dB |
| plain.bmp | JPG | WIC+SkiaSharp decode vs independent source decode | pass | 27.8 dB |
| plain.bmp | PDF | PdfPig (structure) + Windows.Data.Pdf (render) | pass | 1 page(s) = 1 expected, renders, quadrant colours within 40 levels |
| plain.bmp | PNG | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact |
| plain.bmp | Resize 50% | WIC+SkiaSharp decode vs independent source decode | pass | exactly 32x24 (half), exact vs ideal 2x2 box filter |
| plain.bmp | Strip EXIF | WIC+SkiaSharp decode vs independent source decode | pass | exact; no Exif/GPS/XMP/camera/orientation data |
| plain.bmp | TIFF | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact |
| plain.bmp | WebP | WIC+SkiaSharp decode vs independent source decode | pass | 27.8 dB |
| r.jpg (Exif orientation 6) | Crop | WIC+SkiaSharp | pass | 30x50 box in upright coordinates is the blue quadrant |
| rotated.jpg | (source) | WIC+SkiaSharp | pass | 80x120, 1 frame(s) |
| rotated.jpg | BMP | WIC+SkiaSharp decode vs independent source decode | pass | exact |
| rotated.jpg | Compress | refusal check | pass | refused: already small (nothing written) |
| rotated.jpg | Crop | WIC+SkiaSharp decode vs independent source decode | pass | rect 40x60 at (10,15), 30.6 dB |
| rotated.jpg | GIF | WIC+SkiaSharp decode vs independent source decode | pass | 42.2 dB |
| rotated.jpg | HEIC | WIC+SkiaSharp decode vs independent source decode | pass | 32.1 dB |
| rotated.jpg | PDF | PdfPig (structure) + Windows.Data.Pdf (render) | pass | 1 page(s) = 1 expected, renders, quadrant colours within 40 levels |
| rotated.jpg | PNG | WIC+SkiaSharp decode vs independent source decode | pass | exact |
| rotated.jpg | Resize 50% | WIC+SkiaSharp decode vs independent source decode | pass | exactly 40x60 (half), 30.9 dB vs ideal 2x2 box filter |
| rotated.jpg | Strip EXIF | WIC+SkiaSharp decode vs independent source decode | pass | 36.9 dB; no Exif/GPS/XMP/camera/orientation data |
| rotated.jpg | TIFF | WIC+SkiaSharp decode vs independent source decode | pass | exact |
| rotated.jpg | WebP | WIC+SkiaSharp decode vs independent source decode | pass | 36.3 dB |
| scene.jpg | (source) | WIC+SkiaSharp | pass | 640x480, 1 frame(s) |
| scene.jpg | BMP | WIC+SkiaSharp decode vs independent source decode | pass | exact |
| scene.jpg | Compress | WIC+SkiaSharp decode vs independent source decode | pass | 38.0 dB |
| scene.jpg | Crop | WIC+SkiaSharp decode vs independent source decode | pass | rect 320x240 at (80,60), 40.2 dB |
| scene.jpg | GIF | WIC+SkiaSharp decode vs independent source decode | pass | 29.8 dB |
| scene.jpg | HEIC | WIC+SkiaSharp decode vs independent source decode | pass | 46.6 dB |
| scene.jpg | PDF | PdfPig (structure) + Windows.Data.Pdf (render) | pass | 1 page(s) = 1 expected, renders, quadrant colours within 40 levels |
| scene.jpg | PNG | WIC+SkiaSharp decode vs independent source decode | pass | exact |
| scene.jpg | Resize 50% | WIC+SkiaSharp decode vs independent source decode | pass | exactly 320x240 (half), 43.9 dB vs ideal 2x2 box filter |
| scene.jpg | Strip EXIF | WIC+SkiaSharp decode vs independent source decode | pass | 43.9 dB; no Exif/GPS/XMP/camera/orientation data |
| scene.jpg | TIFF | WIC+SkiaSharp decode vs independent source decode | pass | exact |
| scene.jpg | WebP | WIC+SkiaSharp decode vs independent source decode | pass | 40.7 dB |
| scene.png | (source) | WIC+SkiaSharp | pass | 640x480, 1 frame(s) |
| scene.png | BMP | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact |
| scene.png | Compress | WIC+SkiaSharp decode vs independent source decode | pass | 38.8 dB |
| scene.png | Crop | WIC+SkiaSharp decode vs independent source decode | pass | rect 320x240 at (80,60) pixel-exact |
| scene.png | GIF | WIC+SkiaSharp decode vs independent source decode | pass | 29.7 dB |
| scene.png | HEIC | WIC+SkiaSharp decode vs independent source decode | pass | 47.4 dB |
| scene.png | JPG | WIC+SkiaSharp decode vs independent source decode | pass | 40.9 dB |
| scene.png | PDF | PdfPig (structure) + Windows.Data.Pdf (render) | pass | 1 page(s) = 1 expected, renders, quadrant colours within 40 levels |
| scene.png | Resize 50% | WIC+SkiaSharp decode vs independent source decode | pass | exactly 320x240 (half), 51.1 dB vs ideal 2x2 box filter |
| scene.png | Strip EXIF | WIC+SkiaSharp decode vs independent source decode | pass | exact; no Exif/GPS/XMP/camera/orientation data |
| scene.png | TIFF | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact |
| scene.png | WebP | WIC+SkiaSharp decode vs independent source decode | pass | 41.0 dB |
| shot.jpg (Exif orientation 1, GPS, XMP, Photoshop) | Strip EXIF | raw JPEG markers + WIC properties | pass | no Exif/GPS/XMP/camera/orientation data |
| shot.jpg (Exif orientation 3, GPS, XMP, Photoshop) | Strip EXIF | raw JPEG markers + WIC properties | pass | no Exif/GPS/XMP/camera/orientation data |
| shot.jpg (Exif orientation 6, GPS, XMP, Photoshop) | Strip EXIF | raw JPEG markers + WIC properties | pass | no Exif/GPS/XMP/camera/orientation data |
| static.gif | (source) | WIC+SkiaSharp | pass | 64x48, 1 frame(s) |
| static.gif | BMP | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact |
| static.gif | Compress | WIC+SkiaSharp decode vs independent source decode | pass | 27.3 dB |
| static.gif | Crop | WIC+SkiaSharp decode vs independent source decode | pass | rect 32x24 at (8,6) pixel-exact |
| static.gif | HEIC | WIC+SkiaSharp decode vs independent source decode | pass | 26.6 dB |
| static.gif | JPG | WIC+SkiaSharp decode vs independent source decode | pass | 27.8 dB |
| static.gif | PDF | PdfPig (structure) + Windows.Data.Pdf (render) | pass | 1 page(s) = 1 expected, renders, quadrant colours within 40 levels |
| static.gif | PNG | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact |
| static.gif | Resize 50% | WIC+SkiaSharp decode vs independent source decode | pass | exactly 32x24 (half), exact vs ideal 2x2 box filter |
| static.gif | Strip EXIF | WIC+SkiaSharp decode vs independent source decode | pass | exact; no Exif/GPS/XMP/camera/orientation data |
| static.gif | TIFF | WIC+SkiaSharp decode vs independent source decode | pass | pixel-exact |
| static.gif | WebP | WIC+SkiaSharp decode vs independent source decode | pass | 27.8 dB |

### PDF (20 checks)

| Input | Action | Validator | Result | Metric |
|---|---|---|---|---|
| a.pdf(3) + b.pdf(2) + c.pdf(1, landscape) | Merge | PdfPig text per page + Windows.Data.Pdf | pass | 6 pages = 3+2+1, text order preserved, landscape kept, all render |
| book.pdf (4 pages) | Split | PdfPig + Windows.Data.Pdf | pass | 4 files x 1 page, text of page i in file i, all render, total = source |
| flate1.pdf | (source) | PdfPig + Windows.Data.Pdf | pass | 1 page(s) |
| flate1.pdf | JPG | Skia+WIC decode, PdfPig page sizes, ink position | pass | 1 page image(s), sizes = 2x natural (96 dpi) |
| flate1.pdf | PNG | Skia+WIC decode, PdfPig page sizes, ink position | pass | 1 page image(s), sizes = 2x natural (96 dpi) |
| heavy.pdf (3 photo pages) | Compress | PdfPig + Windows.Data.Pdf render comparison | pass | 11857 KB -> 1674 KB, 3 pages render, >= 25 dB per page |
| image3.pdf | (source) | PdfPig + Windows.Data.Pdf | pass | 3 page(s) |
| image3.pdf | JPG | Skia+WIC decode, PdfPig page sizes, ink position | pass | 3 page image(s), sizes = 2x natural (96 dpi) |
| image3.pdf | PNG | Skia+WIC decode, PdfPig page sizes, ink position | pass | 3 page image(s), sizes = 2x natural (96 dpi) |
| labelled.pdf (3 pages) | TXT | PdfPig page text + the drawn text | pass | identical after whitespace normalisation |
| landscape1.pdf | (source) | PdfPig + Windows.Data.Pdf | pass | 1 page(s) |
| landscape1.pdf | JPG | Skia+WIC decode, PdfPig page sizes, ink position | pass | 1 page image(s), sizes = 2x natural (96 dpi) |
| landscape1.pdf | PNG | Skia+WIC decode, PdfPig page sizes, ink position | pass | 1 page image(s), sizes = 2x natural (96 dpi) |
| scans.pdf (3 image pages) | Split | PdfPig + Windows.Data.Pdf render comparison | pass | 3 files, each page renders identically to the original page |
| text(2) + image(2) | Merge | Windows.Data.Pdf render comparison | pass | 4 pages, each renders like its source page (>= 40 dB) |
| text3.pdf | (source) | PdfPig + Windows.Data.Pdf | pass | 3 page(s) |
| text3.pdf | JPG | Skia+WIC decode, PdfPig page sizes, ink position | pass | 3 page image(s), sizes = 2x natural (96 dpi) |
| text3.pdf | PNG | Skia+WIC decode, PdfPig page sizes, ink position | pass | 3 page image(s), sizes = 2x natural (96 dpi) |
| unicode.pdf | TXT | strict UTF-8 decode + PdfPig | pass | accents, Greek, euro sign, curly quotes preserved |
| words.pdf (3 text pages) | Compress | refusal check | pass | refused: already small (nothing written) |

### Audio (38 checks)

| Input | Action | Validator | Result | Metric |
|---|---|---|---|---|
| mono48.wav (48 kHz mono 16-bit) | AIFF/FLAC/MP3/M4A | RIFF/FORM/STREAMINFO/MPEG/MP4 parsers | pass | rate and channel count preserved, AIFF and FLAC sample-exact |
| t.wav (2 s) | Trim past the end | RIFF parser, sample compare | pass | range after the end refused; range running past the end clamps to 0.5 s, sample-exact |
| tone.aiff | Compress | MP4 box parser + Media Foundation decode, cross-correlation | pass | 516 KB -> 36 KB, 3.019 s, correlation 0.998 |
| tone.aiff | FLAC | STREAMINFO parser + MD5 + Media Foundation decode, sample compare | pass | sample-exact after decode: 132300 frames, 16-bit, STREAMINFO MD5 matches |
| tone.aiff | M4A | MP4 box parser + Media Foundation decode, cross-correlation | pass | valid MP4 boxes, mp4a track 3.019 s vs 3.000 s source (delta +19 ms), correlation 0.998 (lag 0.0 ms) |
| tone.aiff | MP3 | MPEG frame walker + Media Foundation decode, cross-correlation | pass | 117 MPEG frames chain to EOF, 192 kbps, 3.056 s vs 3.000 s source (delta +56 ms), correlation 0.953 (lag 15.2 ms) |
| tone.aiff | Trim 0:00.5-0:02.0 | RIFF/FORM parser, sample compare with the source slice | pass | lossless: 66150 frames, sample-exact against source frames 22050..88200 |
| tone.aiff | WAV | hand-written RIFF parser, sample compare | pass | sample-exact: 132300 frames, 44100 Hz, 2 ch, 16-bit |
| tone.flac | AIFF | hand-written FORM/AIFF parser, sample compare | pass | sample-exact: 132300 frames, 44100 Hz, 2 ch, 16-bit |
| tone.flac | Compress | MP4 box parser + Media Foundation decode, cross-correlation | pass | 135 KB -> 36 KB, 3.019 s, correlation 0.998 |
| tone.flac | M4A | MP4 box parser + Media Foundation decode, cross-correlation | pass | valid MP4 boxes, mp4a track 3.019 s vs 3.000 s source (delta +19 ms), correlation 0.998 (lag 0.0 ms) |
| tone.flac | MP3 | MPEG frame walker + Media Foundation decode, cross-correlation | pass | 117 MPEG frames chain to EOF, 192 kbps, 3.056 s vs 3.000 s source (delta +56 ms), correlation 0.953 (lag 15.2 ms) |
| tone.flac | Trim 0:00.5-0:02.0 | MP4 box parser + Media Foundation decode, segment cross-correlation | pass | 1.509 s (asked 1.5), correlation 0.996 with 0.5-2.0 s (lag 0.0 ms) vs 0.001 with 0-1.5 s |
| tone.flac | WAV | hand-written RIFF parser, sample compare | pass | sample-exact: 132300 frames, 44100 Hz, 2 ch, 16-bit |
| tone.m4a | AIFF | hand-written FORM/AIFF parser, sample compare | pass | sample-exact: 133120 frames, 44100 Hz, 2 ch, 16-bit |
| tone.m4a | Compress | MP4 box parser + Media Foundation decode, cross-correlation | pass | 72 KB -> 36 KB, 3.019 s, correlation 1.000 |
| tone.m4a | FLAC | STREAMINFO parser + MD5 + Media Foundation decode, sample compare | pass | sample-exact after decode: 133120 frames, 16-bit, STREAMINFO MD5 matches |
| tone.m4a | MP3 | MPEG frame walker + Media Foundation decode, cross-correlation | pass | 118 MPEG frames chain to EOF, 192 kbps, 3.082 s vs 3.019 s source (delta +64 ms), correlation 0.953 (lag 15.2 ms) |
| tone.m4a | Trim 0:00.5-0:02.0 | MP4 box parser + Media Foundation decode, segment cross-correlation | pass | 1.509 s (asked 1.5), correlation 0.996 with 0.5-2.0 s (lag 0.0 ms) vs 0.000 with 0-1.5 s |
| tone.m4a | WAV | hand-written RIFF parser, sample compare | pass | sample-exact: 133120 frames, 44100 Hz, 2 ch, 16-bit |
| tone.mp3 | AIFF | hand-written FORM/AIFF parser, sample compare | pass | sample-exact: 134256 frames, 44100 Hz, 2 ch, 16-bit |
| tone.mp3 | Compress | MP4 box parser + Media Foundation decode, cross-correlation | pass | 71 KB -> 37 KB, 3.077 s, correlation 1.000 |
| tone.mp3 | FLAC | STREAMINFO parser + MD5 + Media Foundation decode, sample compare | pass | sample-exact after decode: 134256 frames, 16-bit, STREAMINFO MD5 matches |
| tone.mp3 | M4A | MP4 box parser + Media Foundation decode, cross-correlation | pass | valid MP4 boxes, mp4a track 3.077 s vs 3.044 s source (delta +33 ms), correlation 1.000 (lag 0.0 ms) |
| tone.mp3 | Trim 0:00.5-0:02.0 | MP4 box parser + Media Foundation decode, segment cross-correlation | pass | 1.521 s (asked 1.5), correlation 0.989 with 0.5-2.0 s (lag -0.7 ms) vs 0.000 with 0-1.5 s |
| tone.mp3 | WAV | hand-written RIFF parser, sample compare | pass | sample-exact: 134256 frames, 44100 Hz, 2 ch, 16-bit |
| tone16.wav | AIFF | hand-written FORM/AIFF parser, sample compare | pass | sample-exact: 132300 frames, 44100 Hz, 2 ch, 16-bit |
| tone16.wav | Compress | MP4 box parser + Media Foundation decode, cross-correlation | pass | 516 KB -> 36 KB, 3.019 s, correlation 0.998 |
| tone16.wav | FLAC | STREAMINFO parser + MD5 + Media Foundation decode, sample compare | pass | sample-exact after decode: 132300 frames, 16-bit, STREAMINFO MD5 matches |
| tone16.wav | M4A | MP4 box parser + Media Foundation decode, cross-correlation | pass | valid MP4 boxes, mp4a track 3.019 s vs 3.000 s source (delta +19 ms), correlation 0.998 (lag 0.0 ms) |
| tone16.wav | MP3 | MPEG frame walker + Media Foundation decode, cross-correlation | pass | 117 MPEG frames chain to EOF, 192 kbps, 3.056 s vs 3.000 s source (delta +56 ms), correlation 0.953 (lag 15.2 ms) |
| tone16.wav | Trim 0:00.5-0:02.0 | RIFF/FORM parser, sample compare with the source slice | pass | lossless: 66150 frames, sample-exact against source frames 22050..88200 |
| tone24.wav | AIFF | hand-written FORM/AIFF parser, sample compare | pass | sample-exact: 132300 frames, 44100 Hz, 2 ch, 24-bit |
| tone24.wav | Compress | MP4 box parser + Media Foundation decode, cross-correlation | pass | 775 KB -> 36 KB, 3.019 s, correlation 0.998 |
| tone24.wav | FLAC | STREAMINFO parser + MD5 + Media Foundation decode, sample compare | pass | sample-exact after decode: 132300 frames, 24-bit, STREAMINFO MD5 matches |
| tone24.wav | M4A | MP4 box parser + Media Foundation decode, cross-correlation | pass | valid MP4 boxes, mp4a track 3.019 s vs 3.000 s source (delta +19 ms), correlation 0.998 (lag 0.0 ms) |
| tone24.wav | MP3 | MPEG frame walker + Media Foundation decode, cross-correlation | pass | 117 MPEG frames chain to EOF, 192 kbps, 3.056 s vs 3.000 s source (delta +56 ms), correlation 0.953 (lag 15.2 ms) |
| tone24.wav | Trim 0:00.5-0:02.0 | RIFF/FORM parser, sample compare with the source slice | pass | lossless: 66150 frames, sample-exact against source frames 22050..88200 |

### Video (18 checks)

| Input | Action | Validator | Result | Metric |
|---|---|---|---|---|
| clean.mp4 | M4V (rewrap) | MP4 box parser + sequential Media Foundation frame decode | pass | same avc1/mp4a tracks and sample counts (61 video, 87 audio), identical pictures (>= 45 dB), size within 20% |
| clip.mp4 | Compress | MP4 box parser + sequential Media Foundation frame decode | pass | 586 KB -> 112 KB; valid boxes, avc1 640x360 + mp4a, 2.033 s vs 2.033 s; 2.033 s, 64 frames decoded, 4 sampled frames >= 52.1 dB |
| clip.mp4 | GIF | GIF block parser + SkiaSharp frame decode | pass | 21 frames at 480x270, 2.10 s, loops forever, 4 sampled frames >= 37.2 dB |
| clip.mp4 | M4A | container parser + Media Foundation decode, cross-correlation | pass | mp4a track only, 2.020 s vs 2.020 s, correlation 1.000, last 100 ms 1.000 |
| clip.mp4 | M4V | MP4/ASF parser + sequential Media Foundation frame decode | pass | valid boxes, avc1 640x360 + mp4a, 2.033 s vs 2.033 s; 2.033 s, 64 frames decoded, 4 sampled frames >= 53.7 dB |
| clip.mp4 | MP3 | container parser + Media Foundation decode, cross-correlation | pass | 79 MPEG frames, 192 kbps, 2.064 s vs 2.020 s, correlation 0.976, last 100 ms 1.000 |
| clip.mp4 | Trim 0:00.5-0:01.5 | sequential Media Foundation frame decode + audio decode | pass | 1.003 s (asked 1.0), picture and audio come from the requested segment (audio correlation 0.993 vs 0.001 for the wrong one) |
| clip.mp4 | WMV | MP4/ASF parser + sequential Media Foundation frame decode | pass | 2.061 s, 64 frames decoded, 4 sampled frames >= 45.6 dB; ASF header |
| clip.mp4 (repeated video time stamps) | M4V | sequential Media Foundation frame decode | pass | falls back to a full transcode; pictures correct at 4 sampled times |
| clip.mp4 / clip.wmv (2 s, 640x360) | (source) | MP4 box parser + sequential Media Foundation frame decode | pass | avc1 + mp4a, frames match the encoded pictures |
| clip.wmv | Compress | refusal check | pass | refused: already small enough (nothing written) |
| clip.wmv | GIF | GIF block parser + SkiaSharp frame decode | pass | 21 frames at 480x270, 2.10 s, loops forever, 4 sampled frames >= 36.5 dB |
| clip.wmv | M4A | container parser + Media Foundation decode, cross-correlation | pass | mp4a track only, 2.020 s vs 2.020 s, correlation 1.000, last 100 ms 1.000 |
| clip.wmv | M4V | MP4/ASF parser + sequential Media Foundation frame decode | pass | valid boxes, avc1 640x360 + mp4a, 2.033 s vs 2.066 s; 2.033 s, 61 frames decoded, 4 sampled frames >= 45.8 dB |
| clip.wmv | MP3 | container parser + Media Foundation decode, cross-correlation | pass | 79 MPEG frames, 192 kbps, 2.064 s vs 2.020 s, correlation 0.976, last 100 ms 1.000 |
| clip.wmv | MP4 | MP4/ASF parser + sequential Media Foundation frame decode | pass | valid boxes, avc1 640x360 + mp4a, 2.033 s vs 2.066 s; 2.033 s, 61 frames decoded, 4 sampled frames >= 45.8 dB |
| clip.wmv | Trim 0:00.5-0:01.5 | sequential Media Foundation frame decode + audio decode | pass | 1.031 s (asked 1.0), picture and audio come from the requested segment (audio correlation 1.000 vs 0.002 for the wrong one) |
| silent.mp4 | M4A / MP3 | refusal check | pass | refused: no audio (nothing written) |

### Documents (53 checks)

| Input | Action | Validator | Result | Metric |
|---|---|---|---|---|
| legacy.doc (read by Word) | DOCX | per-format reader; every word in order | pass | all words identical to the source text |
| legacy.doc (read by Word) | HTML | per-format reader; every word in order | pass | all words identical to the source text |
| legacy.doc (read by Word) | ODT | per-format reader; every word in order | pass | all words identical to the source text |
| legacy.doc (read by Word) | PDF | per-format reader; every word in order | pass | all words identical to the source text |
| legacy.doc (read by Word) | RTF | per-format reader; every word in order | pass | all words identical to the source text |
| legacy.doc (read by Word) | TXT | per-format reader; every word in order | pass | all words identical to the source text |
| notes.md (md) | DOCX | OpenXml SDK reader + OpenXmlValidator | pass | every word and character identical to the decoded source text |
| notes.md (md) | HTML | HTML tokenizer written for the suite | pass | every word and character identical to the decoded source text |
| notes.md (md) | ODT | System.Xml reader + ODF package rules | pass | every word and character identical to the decoded source text |
| notes.md (md) | PDF | PdfPig (text, fonts) + Windows.Data.Pdf render | pass | every word and character identical to the decoded source text |
| notes.md (md) | RTF | RTF tokenizer written for the suite | pass | every word and character identical to the decoded source text |
| notes.txt (hebrew-1255) | DOCX | OpenXml SDK reader + OpenXmlValidator | pass | every word and character identical to the decoded source text |
| notes.txt (hebrew-1255) | HTML | HTML tokenizer written for the suite | pass | every word and character identical to the decoded source text |
| notes.txt (hebrew-1255) | ODT | System.Xml reader + ODF package rules | pass | every word and character identical to the decoded source text |
| notes.txt (hebrew-1255) | PDF | PdfPig letters + Windows.Data.Pdf render | pass | all letters present (visual order), renders |
| notes.txt (hebrew-1255) | RTF | RTF tokenizer written for the suite | pass | every word and character identical to the decoded source text |
| notes.txt (latin-1252) | DOCX | OpenXml SDK reader + OpenXmlValidator | pass | every word and character identical to the decoded source text |
| notes.txt (latin-1252) | HTML | HTML tokenizer written for the suite | pass | every word and character identical to the decoded source text |
| notes.txt (latin-1252) | ODT | System.Xml reader + ODF package rules | pass | every word and character identical to the decoded source text |
| notes.txt (latin-1252) | PDF | PdfPig (text, fonts) + Windows.Data.Pdf render | pass | every word and character identical to the decoded source text |
| notes.txt (latin-1252) | RTF | RTF tokenizer written for the suite | pass | every word and character identical to the decoded source text |
| notes.txt (utf8) | DOCX | OpenXml SDK reader + OpenXmlValidator | pass | every word and character identical to the decoded source text |
| notes.txt (utf8) | HTML | HTML tokenizer written for the suite | pass | every word and character identical to the decoded source text |
| notes.txt (utf8) | ODT | System.Xml reader + ODF package rules | pass | every word and character identical to the decoded source text |
| notes.txt (utf8) | PDF | PdfPig (text, fonts) + Windows.Data.Pdf render | pass | every word and character identical to the decoded source text |
| notes.txt (utf8) | RTF | RTF tokenizer written for the suite | pass | every word and character identical to the decoded source text |
| notes.txt (utf8-bom) | DOCX | OpenXml SDK reader + OpenXmlValidator | pass | every word and character identical to the decoded source text |
| notes.txt (utf8-bom) | HTML | HTML tokenizer written for the suite | pass | every word and character identical to the decoded source text |
| notes.txt (utf8-bom) | ODT | System.Xml reader + ODF package rules | pass | every word and character identical to the decoded source text |
| notes.txt (utf8-bom) | PDF | PdfPig (text, fonts) + Windows.Data.Pdf render | pass | every word and character identical to the decoded source text |
| notes.txt (utf8-bom) | RTF | RTF tokenizer written for the suite | pass | every word and character identical to the decoded source text |
| pic.docx (embedded picture) | TXT (only offer) | plain text lines | pass | PDF/RTF/HTML/ODT not offered; the text is complete |
| report.docx | HTML | HTML tokenizer written for the suite | pass | outline identical (h1/h2, strong/em/u, ul/ol, table) |
| report.docx | ODT | System.Xml reader + ODF package rules | pass | outline identical; mimetype first and stored, manifest and every XML part well formed |
| report.docx | PDF | PdfPig (text, fonts) + Windows.Data.Pdf render | pass | 1 page(s), 27 words in order, heading 22 pt > body 11 pt, bold/italic faces, renders |
| report.docx | PDF (Word installed: exported by Word) | PdfPig text + Windows.Data.Pdf render | pass | 1 page(s), all words in order, renders |
| report.docx | RTF | RTF tokenizer written for the suite | pass | outline identical (headings, bold/italic/underline, bullets, numbers, table) |
| report.docx | TXT | plain text lines | pass | every line identical; bullets and numbers written out as text |
| report.html | DOCX | OpenXml SDK reader + OpenXmlValidator | pass | outline identical (headings, bold/italic/underline, bullets, numbers, table); OpenXmlValidator: 0 errors |
| report.html | ODT | System.Xml reader + ODF package rules | pass | outline identical; mimetype first and stored, manifest and every XML part well formed |
| report.html | PDF | PdfPig (text, fonts) + Windows.Data.Pdf render | pass | 1 page(s), 27 words in order, heading 22 pt > body 11 pt, bold/italic faces, renders |
| report.html | RTF | RTF tokenizer written for the suite | pass | outline identical (headings, bold/italic/underline, bullets, numbers, table) |
| report.html | TXT | plain text lines | pass | every line identical; bullets and numbers written out as text |
| report.odt | DOCX | OpenXml SDK reader + OpenXmlValidator | pass | outline identical (headings, bold/italic/underline, bullets, numbers, table); OpenXmlValidator: 0 errors |
| report.odt | HTML | HTML tokenizer written for the suite | pass | outline identical (h1/h2, strong/em/u, ul/ol, table) |
| report.odt | PDF | PdfPig (text, fonts) + Windows.Data.Pdf render | pass | 1 page(s), 27 words in order, heading 22 pt > body 11 pt, bold/italic faces, renders |
| report.odt | RTF | RTF tokenizer written for the suite | pass | outline identical (headings, bold/italic/underline, bullets, numbers, table) |
| report.odt | TXT | plain text lines | pass | every line identical; bullets and numbers written out as text |
| report.rtf | DOCX | OpenXml SDK reader + OpenXmlValidator | pass | outline identical (headings, bold/italic/underline, bullets, numbers, table); OpenXmlValidator: 0 errors |
| report.rtf | HTML | HTML tokenizer written for the suite | pass | outline identical (h1/h2, strong/em/u, ul/ol, table) |
| report.rtf | ODT | System.Xml reader + ODF package rules | pass | outline identical; mimetype first and stored, manifest and every XML part well formed |
| report.rtf | PDF | PdfPig (text, fonts) + Windows.Data.Pdf render | pass | 1 page(s), 27 words in order, heading 22 pt > body 11 pt, bold/italic faces, renders |
| report.rtf | TXT | plain text lines | pass | every line identical; bullets and numbers written out as text |

### Zip (3 checks)

| Input | Action | Validator | Result | Metric |
|---|---|---|---|---|
| 2 same-named files + 1 file + 2 same-named folders | Zip | Windows tar.exe extract + System.IO.Compression CRC check | pass | Archive.zip; names uniquified (same.txt, same 2.txt, Docs, Docs 2); every file byte-identical |
| folder with 17 entries (Unicode names, empty folder, empty file, 300 KB binary, nested 4 deep) | Zip | System.IO.Compression extract + Windows tar.exe (bsdtar) extract + central-directory parser | pass | 12 files byte-identical (SHA-256) with System.IO.Compression; tar.exe: all 12 entries whose names fit the ANSI code page identical (the 5 others are skipped by tar.exe itself, a bsdtar limit), empty folder kept, UTF-8 flag set for non-ASCII names |
| pic.jpg + doc.pdf + snd.wav + nöte.txt | Zip | Windows tar.exe extract + entry sizes | pass | all four byte-identical; compressed formats stored, WAV deflated |

### Through the real mikeshift.exe (38 checks)

| Input | Action | Validator | Result | Metric |
|---|---|---|---|---|
| My Folder | Zip | mikeshift.exe child process + zip entry CRC check | pass | exit 0, valid archive |
| clip.mp4 | Compress | mikeshift.exe child process + MP4 container 2.03 s | pass | exit 0, output valid |
| clip.mp4 | GIF | mikeshift.exe child process + 480x270 GIF, 21 frames | pass | exit 0, output valid |
| clip.mp4 | M4A | mikeshift.exe child process + MP4 container 2.02 s | pass | exit 0, output valid |
| clip.mp4 | M4V | mikeshift.exe child process + MP4 container 2.03 s | pass | exit 0, output valid |
| clip.mp4 | MP3 | mikeshift.exe child process + MP3 2.06 s | pass | exit 0, output valid |
| clip.mp4 | Trim | mikeshift.exe child process + MP4 container 1.00 s | pass | exit 0, output valid |
| clip.mp4 | WMV | mikeshift.exe child process + ASF | pass | exit 0, output valid |
| clip.mp4 | Zip | mikeshift.exe child process + zip, 1 entries | pass | exit 0, output valid |
| doc.pdf | JPG | mikeshift.exe child process + folder of 3 valid files | pass | exit 0, output valid |
| doc.pdf | PNG | mikeshift.exe child process + folder of 3 valid files | pass | exit 0, output valid |
| doc.pdf | Split | mikeshift.exe child process + folder of 3 valid files | pass | exit 0, output valid |
| doc.pdf | TXT | mikeshift.exe child process + UTF-8 text | pass | exit 0, output valid |
| doc.pdf | Zip | mikeshift.exe child process + zip, 1 entries | pass | exit 0, output valid |
| heavy.pdf | Compress | mikeshift.exe child process + 2-page PDF | pass | exit 0, output valid |
| pic é.jpg | BMP | mikeshift.exe child process + 640x480 image | pass | exit 0, output valid |
| pic é.jpg | Compress | mikeshift.exe child process + 640x480 image | pass | exit 0, output valid |
| pic é.jpg | Crop | mikeshift.exe child process + 20x16 image | pass | exit 0, output valid |
| pic é.jpg | GIF | mikeshift.exe child process + 640x480 GIF | pass | exit 0, output valid |
| pic é.jpg | PDF | mikeshift.exe child process + 1-page PDF | pass | exit 0, output valid |
| pic é.jpg | PNG | mikeshift.exe child process + 640x480 image | pass | exit 0, output valid |
| pic é.jpg | Resize 50% | mikeshift.exe child process + 320x240 image | pass | exit 0, output valid |
| pic é.jpg | Strip EXIF | mikeshift.exe child process + 640x480 image | pass | exit 0, output valid |
| pic é.jpg | TIFF | mikeshift.exe child process + 640x480 image | pass | exit 0, output valid |
| pic é.jpg | WebP | mikeshift.exe child process + 640x480 image | pass | exit 0, output valid |
| pic é.jpg | Zip | mikeshift.exe child process + zip, 1 entries | pass | exit 0, output valid |
| report.docx | HTML | mikeshift.exe child process + HTML | pass | exit 0, output valid |
| report.docx | ODT | mikeshift.exe child process + valid ODT | pass | exit 0, output valid |
| report.docx | PDF | mikeshift.exe child process + 1-page PDF | pass | exit 0, output valid |
| report.docx | RTF | mikeshift.exe child process + RTF | pass | exit 0, output valid |
| report.docx | TXT | mikeshift.exe child process + UTF-8 text | pass | exit 0, output valid |
| report.docx | Zip | mikeshift.exe child process + zip, 1 entries | pass | exit 0, output valid |
| tone.wav | AIFF | mikeshift.exe child process + AIFF 2.00 s | pass | exit 0, output valid |
| tone.wav | FLAC | mikeshift.exe child process + FLAC 2.00 s | pass | exit 0, output valid |
| tone.wav | M4A | mikeshift.exe child process + MP4 container 2.02 s | pass | exit 0, output valid |
| tone.wav | MP3 | mikeshift.exe child process + MP3 2.06 s | pass | exit 0, output valid |
| tone.wav | Trim | mikeshift.exe child process + WAV 1.00 s | pass | exit 0, output valid |
| tone.wav | Zip | mikeshift.exe child process + zip, 1 entries | pass | exit 0, output valid |

## B4. Defects found and fixed

Each was found by an integrity check failing against an independent decoder, reduced to a minimal repro, fixed at the root in `src/MikeShift.Core`, and is now covered by a test that fails without the fix.

| # | Defect | Effect on a user | Root cause | Fix | Covered by |
|---|---|---|---|---|---|
| 1 | **PNG (or any image with transparency) to HEIC** left a solid colour where the picture was transparent; **Compress** on a transparent image chose HEIC and did the same | transparent logo gets a coloured (often black) block; the LIMITS text claimed transparency was kept | Windows' HEIF encoder discards alpha (the decoder reports `alpha=Ignore`); the product fed it straight-alpha pixels with the colour that sits under transparent pixels | HEIC output is flattened on white like JPG/BMP (`ImageCodec.EncodeAsync`); Compress of a picture with real transparency now writes PNG, the only output that can keep it (`ImageConverter.CompressAsync`); LIMITS.md updated | `ImageIntegrityTests` (png-alpha to HEIC, Compress with alpha), `ImageToolTests` (two updated/new tests) |
| 2 | **Audio to MP3 lost the end of the music.** 24-bit WAV/FLAC and AAC sources were always clipped by about 50-70 ms; with 16-bit sources it happened about once in eight conversions on a busy PC (4 of 30 in a contention run) | a file that "converts fine" but ends early | Windows' transcoder drops the last frames of an MP3 encode when it has to decode first (24-bit, AAC) and, nondeterministically, under load, even from a plain 16-bit WAV | MP3 is always encoded from a 16-bit WAV staged by MikeShift (`MediaCommon.WithTranscodableSource(..., flatten16: true)`); every audio encode whose exact length is known (WAV source, staged WAV, trim range) is checked to reach that length and is redone up to 4 times, then fails loudly rather than save a clipped file (`RunAudioEncodeAsync`). 30 of 30 correct under the same load afterwards | `AudioIntegrityTests` (tail correlation, every source to MP3) |
| 3 | **Video to MP3 extraction** had the same clipped tail | same | video audio was handed to the transcoder directly | same staging (`VideoConverter.ExtractAudioAsync`) | `VideoIntegrityTests` (mp4/wmv to MP3, last 100 ms correlation) |
| 4 | **MP4 to M4V (and MP4) "lossless re-wrap" produced a broken video** for sources whose video samples repeat a time stamp (stills exported by Photos / Movie Maker, the MediaComposition fixture): the first picture stayed on screen for the rest of the clip | the converted video shows the wrong picture after the first scene | the MP4 sink writer silently drops samples whose time stamp repeats the previous one; the following inter-coded frames then decode wrongly | the copy counts the compressed video samples it wrote and compares with what it read; any loss falls back to the existing full transcode (`StreamCopy.TryRewrapToMp4`). Ordinary files still re-wrap losslessly (same sample counts, frames >= 45 dB) | `VideoIntegrityTests.MP4_to_M4V_is_a_lossless_rewrap`, `Format_conversion_is_faithful(mp4, M4V)`; the old rewrap test now uses an ordinary MP4 |
| 5 | **WMV to GIF failed** ("Windows can't decode the video in this file.") | no GIF from any WMV | `GifExporter.ConfigureRgb32` asked for RGB32 with an explicit "progressive" interlace mode; WMV streams reject that with `MF_E_INVALIDMEDIATYPE` | retry without the interlace attribute | `VideoIntegrityTests` (wmv to GIF) |
| 6 | **Trim of a WMV** put the first ~0.3 s of audio from the START of the file in front of the requested audio | trimmed clip begins with the wrong sound | `MediaTranscoder.TrimStartTime` seeks the video but not the audio of an ASF source | WMV trims go through `MediaComposition` / `MediaClip` trimming, which cuts both streams together (`MediaTranscode.RunTrimmedViaCompositionAsync`) | `VideoIntegrityTests.Trim_keeps_exactly_the_requested_part(wmv)` |

Behaviours checked and found to be correct (documented so nobody "fixes" them): a still-image conversion of an animated GIF or a multi-frame TIFF writes the first frame (TIFF to PDF writes every frame); animated and multi-page images are refused by Compress, Resize, Crop and Strip EXIF; Resize 50 % of an odd size rounds up; lossy outputs are a little longer than their source (MP3 +56 ms, AAC +19 ms: encoder delay and padding); WebP read through WIC differs from libwebp by 1 level in a lossless file; lists in a `.doc` built from the fixture RTF show "?" for the bullet because the fixture's bullet glyph is not a real character (a property of the fixture, not of MikeShift).

## B5. What could not be verified here

* **Codecs this PC lacks or inputs without a fixture:** HEVC, VP9, AV1 and WebM input, MOV input, AVIF input, genuinely OLE2 `.doc` files (the `.doc` fixture is RTF saved as `.doc`, which Word opens as a Word document), password-protected files other than the refusal paths already covered by the older tests.
* **Arabic shaping and other complex scripts in PDF output** (documented as unsupported); Hebrew was checked for letters and order only.
* **Windows' `tar.exe` cannot extract Zip entries whose names do not fit the machine's ANSI code page** (it prints "Archive entry has empty or unreadable filename"; a zip written by Python's `zipfile` fails identically). Such entries were verified with `System.IO.Compression` and by checking the UTF-8 flag in the headers instead; every other entry was extracted by both.
* **Other Word versions, ARM64, Windows 10**, very large files (the 2 GB and 200 MP guards are covered by the older tests, not re-run at size here), a OneDrive "online only" placeholder, multi-monitor behaviour.
* **Network measurements of a packaged (MSIX) install:** the run used the unpackaged Debug build and the Debug CLI; the code is the same, but the signed package itself was not installed.
* **Statistical limits of the intermittent MP3 clipping:** 30 of 30 conversions were correct after the fix under the same load that clipped 4 of 30 before; the retry makes a residual failure very unlikely, not impossible (it would then fail with a message instead of saving a clipped file).

## B6. Reproducing

```powershell
dotnet build MikeShift.sln                                       # 0 warnings
dotnet test MikeShift.sln                                        # fast suite (slow tests reported as skipped)
$env:MIKESHIFT_SLOW = '1'; dotnet test MikeShift.sln             # everything, including Media Foundation, video and Word
$env:MIKESHIFT_INTEGRITY_MATRIX = "$PWD\matrix.tsv"              # optional: one row per check, the source of the table above
./scripts/audit-network-imports.ps1 -Path src/MikeShift.App/bin/x64/Debug/net10.0-windows10.0.26100.0
./scripts/verify-offline.ps1                                     # about 20 minutes with Word installed (CLI matrix + 60 s app idle)
```
