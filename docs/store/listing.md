# Microsoft Store listing: MikeShift

Ready to paste into Partner Center (Product > Store listing, English). Everything between the "Paste" markers is final text; everything else is a note for the publisher. Facts come from the repository's own measurements and the [known limitations](../limitations.md). Character counts are for the text exactly as written.

Fields in Partner Center change over time. Where a limit or a drop-down value below may be out of date, it says so; the form is authoritative.

The paste blocks below avoid semicolons and the word "select". Partner Center's web firewall rejects a listing save with a bare "Access Denied" (the page only says "We are unable to save listing") when the text looks like a command or a query: a semicolon before a word such as "split" or "zip", or "select" followed later by "from". Keep that in mind when editing them.

## Before you submit (publisher checklist)

1. **Reserve the name** "MikeShift: offline file converter" (or just "MikeShift") in Partner Center, and run a trademark search first (not done; names such as ShiftDrop are close neighbours).
2. **Associate the app with the Store.** Done: the name "MikeShift" is reserved (Store ID `9PNZ1C9NRD8Z`) and `src/MikeShift.App/Package.appxmanifest` carries its Partner Center identity at version `1.1.0.0` (Store package versions need the last digit to be `0`) ([releasing](../developer/releasing.md#versioning)).
3. **Submit the "store" flavour** (about 24 MB for x64 and 23 MB for ARM64; `./scripts/release.ps1 -Flavor store`, upload the `.msixbundle` or both `_store_` `.msix` files). The Store installs the Windows App Runtime dependency itself.
4. **Host the privacy statement** ([privacy.md](../privacy.md), see [privacy-policy.md](privacy-policy.md)) at a public URL and paste the URL in Properties > Privacy policy. A public GitHub repository page or a GitHub Pages page works; the repository must be public first.
5. **Fill in the support contact and website** (suggested values below; use an email address if you prefer one).
6. **Decide the Windows 10 question.** The package declares Windows 10 1809 (build 17763) as the minimum, but the right-click entry has not been seen on Windows 10 ([limitations](../limitations.md#the-right-click-entry)). Either keep 1809 and keep the honest note in the description, or set the minimum to Windows 11 until a Windows 10 PC has been tried.
7. **Run the Windows App Certification Kit** on the Store package and fix findings.
8. **Pricing and markets:** free, all markets, like the comparable open-source tools. Your call.

---

## Paste: product name (up to 256 characters)

```
MikeShift: offline file converter
```

## Paste: short description (up to 100 characters)

Recommended (90 characters):

```
Convert HEIC, WebP, PDF, video and audio from File Explorer. Offline, nothing is uploaded.
```

Alternatives (89, 89 and 60 characters):

```
Right-click to convert photos, PDFs, video and audio. Runs on your PC; nothing leaves it.
```

```
The offline file converter for File Explorer: HEIC to JPG, WebP to PNG, merge PDFs, more.
```

```
Convert any file from File Explorer. Nothing leaves your PC.
```

## Paste: description (up to 10,000 characters)

The Store shows plain text, so this uses line breaks and bullet characters rather than Markdown.

```
Convert files without leaving File Explorer, and without uploading anything.

Right-click a photo, PDF, video, audio file, document or folder, choose "Convert with MikeShift", and pick what you want. A converted copy appears next to the original. The original is never changed.

WHAT PEOPLE USE IT FOR
• iPhone photos: HEIC to JPG or PNG
• Web images: WebP to PNG or JPG
• PDFs: merge several into one, split one apart, save pages as images, extract the text
• Video: MP4, a short GIF, or just the audio as MP3 or M4A
• Photos for email: Compress or Resize 50%, plus Crop and Strip EXIF
• Documents: DOCX, RTF, ODT, HTML and TXT to PDF and between each other
• Scans and screenshots: get the text out as TXT, or make a scanned PDF searchable
• Video for chat and email: Fit 10 MB or Fit 25 MB makes a copy that fits the upload limit
• QR codes: read one from a picture, or make one from a link or a short text
• Logos: turn a PNG into a Windows icon (ICO)
• Photos you share: blur the faces Windows detects in them
• Anything: Zip files and folders

THREE WAYS TO USE IT
1. Right-click (start here). Pick files or folders in File Explorer, right-click, and choose Convert with MikeShift. It is in Windows 11's main right-click menu. The submenu offers the most likely conversions for what you picked, and "More options" opens the full wheel.
2. The wheel. Start dragging a file, then hold Shift or Ctrl (you choose when you first run it). A wheel of formats appears under the pointer. Drop the file on a tile. Press the other key to switch to tools: Compress, Resize, Crop, Trim, Zip. Letting go elsewhere or pressing Esc cancels.
3. The keyboard. Pick files in File Explorer and press Ctrl+Alt+Shift+Space to open the wheel for them. Change or turn off the shortcut in Settings. There is also a command line tool, mikeshift, for scripts.

PRIVATE BY DESIGN
• Nothing is uploaded. The app has no network code, no account and no telemetry. In a 19-minute measurement that ran every action on every kind of file, no MikeShift process opened a network connection.
• Originals are never moved or changed. Every conversion is a new file next to the original. If that folder is not writable, the copy goes to Downloads and MikeShift tells you.
• Microsoft Word is optional and off by default. If you turn it on in Settings, Word (a separate Microsoft program) handles legacy .doc files and higher-fidelity DOCX to PDF, and Word may connect to Microsoft on its own.

SMALL AND QUIET
• A download of about 24 MB, no bundled third-party programs.
• Sits in the notification area using about 17 MB of memory when idle.
• Built with WinUI 3 for Windows 11: light, dark and high contrast themes, per-monitor scaling, a wheel that stays clear of the taskbar.

WHAT IT CONVERTS (53 actions)
• Images: PNG, JPG, TIFF, GIF, BMP, WebP, HEIC, ICO, PDF, searchable PDF, and TXT (the text in the picture). Tools: Compress, Resize 50%, Crop, Strip EXIF, Blur faces, Upscale 2x (Copilot+ PCs only), Read QR code, Merge into one PDF, Merge into one searchable PDF.
• PDF: pages to PNG or JPG, text to TXT (scans too). Tools: Compress, Split, Merge, Searchable PDF.
• Video: MP4, M4V, WMV, GIF, or audio only as M4A or MP3. Tools: Compress, Fit 10 MB, Fit 25 MB, Trim.
• Audio: M4A, WAV, AIFF, FLAC, MP3. Tools: Compress, Trim.
• Documents: PDF, DOCX, RTF, TXT, HTML, ODT. A short text file can also become a QR code.
• Internet shortcuts (.url): QR code.
• Any file or folder: Zip.
MikeShift shows only the actions that work for the files you picked on your PC.

GOOD TO KNOW
• HEIC (iPhone) photos need Microsoft's free HEIF Image Extensions and HEVC Video Extensions from the Microsoft Store. If they are missing, MikeShift tells you and offers a "Get HEIF" button that opens the Store page. Install them, then restart MikeShift.
• MikeShift focuses on the common conversions. It does not do PDF editing or MOV and AVIF output, GIFs are limited to 30 seconds (15 by default), and documents become PDFs with MikeShift's own plain layout (not Word's exact pagination). Password-protected PDFs are refused. The full list is in the project's documentation.
• Reading text from pictures and scans uses Windows' own text recognition, on your PC, in the languages Windows has installed. Without one of those languages the TXT tile for pictures and the searchable PDF tiles are hidden. PDF to TXT still works on PDFs that already have text.
• Blur faces uses Windows face detection. It finds faces looking at the camera best and can miss others, so check the result before you share it.
• Upscale 2x appears only on Copilot+ PCs. It uses the image upscaler built into Windows. The first time, Windows may download that AI model through Windows Update. That is Windows, not MikeShift, which still makes no connection, and the picture never leaves your PC.
• Windows 11 is where it was built and tested. The package also installs on Windows 10 (1809 or later), but MikeShift has not yet been tested there, including the right-click entry.
• Windows hides programs it cannot see: Explorer windows running as administrator are not supported, and files that are not real files on disk (an Outlook attachment, an image dragged from a browser) need saving first.
• Windows 11 may put the tray icon in the overflow area. The welcome window links to the taskbar setting.

OPEN SOURCE
Free, MIT licence, source code on GitHub.
```

## Paste: what's new in this version (up to 1,500 characters)

```
New in 1.1:
• Text from pictures and scanned PDFs. Pictures get a TXT tile, PDF to TXT now reads scanned pages, and Searchable PDF makes a scan searchable. It uses Windows' own text recognition on your PC.
• Fit 10 MB and Fit 25 MB make a copy of a video small enough to send in chat or email.
• Read QR code finds the codes in a picture. QR code makes one from a link or a short text.
• Blur faces covers the faces Windows detects in a photo. Check the result for missed faces before you share it.
• ICO turns a logo into a Windows icon.
• Upscale 2x doubles the size of a small picture on Copilot+ PCs. Windows may download its AI model the first time.
• Fixed: WebP pictures work in PDF and the crop editor without Microsoft's WebP extension, and Hebrew and Arabic text from PDFs keeps its word order.
Still nothing is uploaded: no network code, no account, no telemetry.
```

## Paste: product features (up to 20 bullets, up to 200 characters each)

```
Right-click any file or folder in File Explorer and choose Convert with MikeShift. The converted copy appears next to the original.
Nothing is uploaded: no network code, no account and no telemetry. Conversion happens on your PC.
Turn iPhone HEIC photos into JPG or PNG (needs Microsoft's free HEIF and HEVC extensions).
Convert WebP and other web images to PNG or JPG, one file or many at once.
Merge several images or PDFs into one PDF, split a PDF, save PDF pages as PNG or JPG, or extract PDF text.
Turn video into MP4, M4V, WMV or a short GIF, or extract the audio as MP3 or M4A.
Shrink photos for email with Compress or Resize 50%, with Crop and Strip EXIF built in.
Trim video and audio with a small editor that opens only when you ask.
Hold Shift or Ctrl while dragging a file to open a wheel of formats under the pointer, then drop to convert.
Keyboard shortcut Ctrl+Alt+Shift+Space opens the wheel for the files selected in File Explorer.
Convert documents between DOCX, RTF, ODT, HTML, TXT and PDF, and Zip any file or folder.
Originals are never moved or changed: every result is a new file, written safely so a failure leaves nothing half-written.
Small and quiet: about 24 MB to download, about 17 MB of memory when idle, no bundled third-party programs.
Windows 11 look: WinUI 3, Mica, light, dark and high contrast themes, per-monitor scaling.
A mikeshift command line tool for scripts, with the same engine as the wheel.
Microsoft Word is optional and off by default. Turn it on in Settings only if you want Word for legacy .doc files.
Free and open source under the MIT licence.
Get the text out of a screenshot, a photo of a page or a scanned PDF, or make a scan searchable. Uses Windows' own text recognition on your PC.
Make a video fit a 10 MB or 25 MB upload limit. Read a QR code from a picture, or make one from a link or a short text.
Blur detected faces in a photo (check for missed ones), make a Windows icon (ICO) from a logo, or upscale a small picture 2x on a Copilot+ PC.
```

(20 bullets; the limit is 20. The three newest are at the end; move them up if you would rather lead with them.)

## Paste: search terms

Partner Center asks for a small number of search terms (up to 7 terms of up to 30 characters each at the time of writing; check the form). Use the first seven if that is the limit; the rest are spares, ordered by priority. None is a competitor's name, which Store policy discourages.

```
file converter
heic to jpg
webp to png
merge pdf
offline converter
video to mp3
right click convert
image converter
pdf converter
video to gif
compress photo
convert heic
batch convert
audio converter
zip files
resize image
file explorer
mp4 converter
split pdf
no upload converter
```

## Category and subcategory

* **Recommended:** Utilities & tools > File management.
* **Alternative:** Photo & video (the biggest jobs are photos and video), or Productivity.
* The Store suggests a single primary category. File management matches what Explorer users search for, and the name carries "file converter". The category names may differ; use whatever the Partner Center drop-down offers that is closest.

## Age rating (IARC questionnaire answers)

Questionnaire category: **Utility, productivity, collaboration or other**. All answers are for this app as built.

| Question | Answer | Basis |
|---|---|---|
| Violence, blood, fear, sexual content, nudity, language, controlled substances, gambling | No | The app converts the user's own files; it contains no such content. |
| User-generated content (users create and share content with other users) | **No** | The app has no sharing, posting, chat or accounts. It converts files already on the user's PC and writes copies next to them. |
| Users interact or communicate with each other | No | No networking exists. |
| Shares the user's location or personal information | No | No network code, no telemetry ([privacy statement](../privacy.md)). |
| Unrestricted internet access or a web browser | No | The app has no browser. A few buttons hand a link to the user's own browser or the Store app (Check for updates, GitHub, Get HEIF). |
| In-app purchases, ads, digital goods | No | Free, no ads, no purchases. |
| Collects data from children | No | Collects no data from anyone. |

Expected result: the lowest rating (IARC 3+, ESRB Everyone, PEGI 3). IARC computes the actual result.

## Privacy policy URL

Host [privacy.md](../privacy.md) (rendered) and paste its URL. Once the repository is public: `https://github.com/mjtpena/mikeshift/blob/main/docs/privacy.md` (use GitHub Pages if you want a clean rendered page). Placeholder until then: `[PRIVACY POLICY URL]`.

## Support contact and website

| Field | Value |
|---|---|
| Support contact info (email or web page) | `https://github.com/mjtpena/mikeshift/issues` (or an email address) |
| Website | `https://github.com/mjtpena/mikeshift` |
| Copyright and trademark info | `© 2026 MikeShift contributors. MIT licence.` |
| Additional license terms | `MIT licence: https://github.com/mjtpena/mikeshift/blob/main/LICENSE.` |
| Developer / publisher display name | `[AS REGISTERED IN PARTNER CENTER]` (the manifest says "Michael John Peña") |

## System requirements text

Partner Center lists minimum and recommended hardware as free text. Use:

```
Minimum
OS: Windows 10 version 1809 (build 17763) or later, or Windows 11
Architecture: x64 or ARM64
Memory: 4 GB RAM recommended; the app uses about 17 MB when idle and about 115 MB while the wheel is on screen
Storage: about 60 MB
Other: None required. Microsoft's free HEIF Image Extensions and HEVC Video Extensions are needed to open iPhone HEIC photos. Microsoft Word is optional.

Recommended
OS: Windows 11
Architecture: x64
```

Notes: the memory and storage figures are from the project's measurements ([limitations](../limitations.md#platform-size-and-memory): about 17 MB idle, about 115 MB for the wheel in a fresh process; store package about 57 MB installed). The "4 GB RAM" line is a conventional floor, not something measured. ARM64 is built but has not been run on ARM64 hardware.

## Notes for certification (paste in Product > Submission options > Notes for certification, which allows 2,000 characters at the time of writing)

```
MikeShift is a tray app that converts files from File Explorer. No account or sign-in is needed. It has no network code and declares no internet capability.

How to test:
1. Install and launch. A welcome window opens and an icon appears in the notification area (it may be in the overflow).
2. In File Explorer, right-click a .jpg or .png, choose "Convert with MikeShift" (in the main Windows 11 menu), then "Convert to PNG" or "Convert to JPG". A copy appears next to the original and a message says so.
3. Optional: drag a file and hold Shift (or Ctrl). Drop it on a tile of the wheel that appears. Esc cancels. Ctrl+Alt+Shift+Space opens the wheel for the selected files.
4. New in 1.1 (if a tile is not in the short right-click list, choose "More options..." or run "mikeshift --list <file>"): a screenshot or scanned PDF > TXT or Searchable PDF. A video > Fit 10 MB. A picture of a QR code > Read QR code. A .url or .txt file > QR code. A photo of people > Blur faces. A .png > ICO. Tiles that need Windows text recognition or face detection are hidden where Windows lacks it.

runFullTrust: MikeShift is a WinUI 3 desktop app. It needs full trust for a low-level mouse and keyboard hook (it reads only mouse movement, the left button, the wheel and Shift, Ctrl, Alt and Esc, to run the gesture, and stores nothing), a File Explorer context-menu COM server, the files the user chooses, Media Foundation and the Windows PDF renderer.

systemAIModels: used only by "Upscale 2x" on Copilot+ PCs, which calls the image upscaler built into Windows (Microsoft.Windows.AI.Imaging). The AI model belongs to Windows. If it is missing, Windows installs it itself through Windows Update. It is not a network capability: MikeShift has no internet capability and opens no connection. On other PCs the tile is hidden, so a tester will not see it.

"Open at login" is off by default. The app adds a "mikeshift" command line alias.
```

## Other submission fields

* **Device families:** Windows desktop. **Architectures:** x64, ARM64 (use the bundle).
* **Free trial / subscription / purchases:** none.
* **Accessibility declaration** (if the form asks): do not tick screen-reader support until a Narrator pass has been done; the wheel has UI Automation names and a click-mode keyboard path, but it has not been checked with a screen reader yet.
* **Screenshots:** [screenshots.md](screenshots.md). **Trailer / demo video:** [demo-script.md](demo-script.md).
* **Store logo and tile art:** from `assets/brand/mikeshift.svg` (`scripts/make-icons.ps1`; preview [icon-sheet.png](../images/icon-sheet.png)).
