# MikeShift: market position and Windows UX study

Prepared 2026-10-24. Every link was opened or returned by search on 2026-10-24 (access date for all citations). Convention: cited statements are **facts as the source reports them**; paragraphs starting **Judgement** are mine. GitHub figures come from the GitHub REST API on that date. Not retrievable: Microsoft Store ratings and download counts (Store pages return HTTP 410), Reddit (fetch blocked). Where popularity is missing below, that is why.

## 1. Executive summary

1. **Verdict: not well positioned today.** The product is technically ahead (45 MB or 23 MB, zero network, Fluent UI, 400+ checks against real Explorer), but it competes on the one thing that became a commodity this month: the hold-a-key-and-drop wheel.
2. The idea came from a paid Mac app, [Tangerine](https://www.applesfera.com/aplicaciones-os-x-1/hay-app-para-mac-que-permite-convertir-archivos-solo-arrastrarlos-pulsar-tecla) (EUR 34.99). Since 29 Sep 2026 at least a dozen free Windows clones appeared. [Filee](https://github.com/KnifeLemon/Filee) has 11.1k stars after 25 days; [ZestDrop](https://zestdrop.org/) is in the Store. Upstream [Shiftvert](https://github.com/roiegold/shiftvert) was created 20 Oct with 0 stars. MikeShift is unsigned, sideload-only and its repo is not publicly reachable (404 on `mjtpena/mikeshift`).
3. The incumbent for "right-click convert", [File Converter](https://github.com/Tichau/FileConverter) (15.5k stars), has had no working Windows 11 menu entry for five years ([#211](https://github.com/Tichau/FileConverter/issues/211)). That lane is open.
4. Windows users' common jobs (HEIC to JPG, WebP to PNG, PDF merge) are searched for and right-clicked, not discovered through a hidden gesture. MikeShift's number-one job (HEIC) also needs Microsoft's HEIF and HEVC extensions, and without them a HEIC file shows "Zip only".
5. **Single most important change: add a native Windows 11 right-click entry, "Convert with MikeShift", that opens the same wheel.** The wheel becomes the signature, not the only door. Precondition (also P0): ship through the Microsoft Store so it installs signed, with no certificate steps.
6. Explicit answers: add the right-click menu: **yes**. Keep Shift-drag as the headline gesture: **yes**, but as the accelerator, not the first-time-user entry point; offer Ctrl as an equal choice.
7. Defensible edge: smallest and safest footprint, no network at all, Windows-native and accessible, built on Windows' own codecs. Do not fight on format count (Filee 255, ZestDrop about 341).
8. Evidence gap: frequency data and Store reviews are missing, so §9 gives a 5-person test to run before building more.

## 2. Competitive landscape (current as of 2026-10-24)

### 2.1 Comparison table

| Tool | Interaction | Scope | Price / licence | Footprint | Privacy | Signals | Praise / complaints |
|---|---|---|---|---|---|---|---|
| **MikeShift** (ours) | Shift-drag wheel, Ctrl for tools; tray picker; CLI | about 41 actions: images, PDF, video, audio, docs, zip | Free, MIT | 23 MB (Store flavour) / 45 MB; idle 16.5 MB RAM ([real-drag run](../test-runs/2026-10-24-real-drag.md)) | No network, measured | none public; unsigned; no Store listing | Own tests: 288/288 conversions, originals byte-identical |
| **[Filee](https://filee.sh/)** | Hold Ctrl (configurable), drag, drop on donut; also right-click, Send To, CLI, watch folders | 255 formats; 147 work out of the box, rest need engines (FFmpeg about 100 MB, LibreOffice about 420 MB) | Free, MIT, sponsor link | Installer 128 MB, portable 155 MB ([API](https://api.github.com/repos/KnifeLemon/Filee/releases?per_page=3)); admin once | Local conversion; GitHub update check on by default; downloads engines ([README](https://raw.githubusercontent.com/KnifeLemon/Filee/main/README.md)) | **11.1k stars**, created 29 Sep; 10 releases 11 to 21 Oct ([releases](https://github.com/KnifeLemon/Filee/releases)) | Open issues: Word to PDF fidelity cluster, many format requests; SmartScreen block before signing ([#44](https://github.com/KnifeLemon/Filee/issues/44)) |
| **[ZestDrop](https://zestdrop.org/)** | Shift-drag wheel, Ctrl+Shift tools, F8/F9 | about 341 conversions, 46 tools; 145 need installed Office | Free, MIT | Portable ZIP 251 MB; Store listing | Local | 27 stars | Right-click menu only in the ZIP |
| **ShiftDrop, OrbitDrop, others** | Shift-drag (ShiftDrop: Shift formats, Ctrl tools, Alt actions); OrbitDrop adds Ctrl+Alt+Space on selection | Similar | Free, MIT | Store and installer | Local; ShiftDrop checks GitHub daily | 1 to 31 stars; [12+ more repos](https://api.github.com/search/repositories?q=drag+drop+file+converter+windows+wheel+OR+donut+OR+radial+offline&sort=stars&order=desc&per_page=15) created 18 to 22 Oct | [ShiftDrop](https://github.com/baraadb/ShiftDrop) credits Tangerine, not Shiftvert |
| **[File Converter](https://file-converter.io/)** (Tichau) | Right-click presets | FFmpeg, ImageMagick, Ghostscript; audio, video, image, PDF out; Office needs Office | Free, GPL-3.0 | MSI about 58 MB | Local | **15.5k stars**, 368 open issues/PRs, v2.2 Feb 2026; [AlternativeTo](https://alternativeto.net/software/file-converter/about/) 4.6 (19 ratings) | Praise: free, offline, simple. Complaints: menu entry vanishes ([#685](https://github.com/Tichau/FileConverter/issues/685), [#655](https://github.com/Tichau/FileConverter/issues/655), [#733](https://github.com/Tichau/FileConverter/issues/733)); Windows 11 only under "Show more options"; flashing progress window ([MakeUseOf](https://www.makeuseof.com/windows-11-image-conversion-context-menu/)); malware look-alikes ([How-To Geek](https://tech.yahoo.com/computing/articles/open-source-windows-tool-converts-123116704.html)) |
| **[PowerToys](https://github.com/microsoft/PowerToys)** | Right-click and right-drag Image Resizer; Advanced Paste window | Resize only ("not a file type conversion tool"); paste as PNG; transcode to MP3/MP4 | Free, MIT | n/a | Local (AI optional) | **139.7k stars** | [Image Resizer](https://learn.microsoft.com/en-us/windows/powertoys/image-resizer), [Advanced Paste](https://learn.microsoft.com/en-us/windows/powertoys/advanced-paste) |
| **XnConvert / IrfanView** | App with batch; IrfanView batch dialog | 500+ in / about 70 out (Xn) | Free for private use, business from EUR 15 ([XnConvert](https://www.xnview.com/en/xnconvert/)) | small | Local | IrfanView is the top HEIC answer in [Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/5569451/how-do-i-convert-heic-to-jpg-for-free-on-windows-1) | Needs plugins for HEIC; app-style workflow |
| **HandBrake / Shutter Encoder** | Apps with queues | Video (Shutter: pro codecs, AI tools) | Free, GPL-2.0 ([HandBrake](https://github.com/HandBrake/HandBrake), 24.8k stars); [Shutter](https://www.shutterencoder.com/en/) donations | Shutter 158 MB | Local | | Power-user depth |
| **Format Factory** | App | Broad | Freeware | | Local | | Installer carried [unwanted extras](https://www.techradar.com/reviews/format-factory) |
| **Windows built-ins** | Photos, Paint Save as; Clipchamp; Explorer AI actions | Paint converts one file at a time ([thread](https://techcommunity.microsoft.com/discussions/windows11/how-do-i-convert-webp-to-png-on-windows-11/4249650/replies/4249683)); AI actions are Insider-only and are not conversion ([Windows Central](https://windowscentral.com/software-apps/windows-11/windows-11-is-getting-ai-actions-in-file-explorer-heres-how-to-try-them-right-now)) | Free | n/a | Local | | HEIF extension "cannot convert"; HEVC extension complaints ([thread](https://techcommunity.microsoft.com/discussions/windows11/heic-image-extension-not-working-on-windows-11-are-there-any-heic-extension-alte/4387238/replies/4556476)) |
| **PDF24 Creator / PDFgear** | Desktop toolbox, virtual printer | PDF merge, split, compress, OCR, edit | Free | | PDF24 desktop is offline; its web tools delete uploads within 1 h ([AlternativeTo](https://alternativeto.net/software/pdf24-creator/about/)) | 156 likes, 4.6 (22 ratings) | Deeper PDF than we will ever be. PDFgear AI offline status unverified. Adobe not researched |
| **Online** (CloudConvert, Convertio, iLovePDF) | Browser | Broad | Free tiers | | **Upload**: CloudConvert deletes after conversion, 24 h outer limit ([review](https://blog.incogni.com/is-cloudconvert-safe/)); iLovePDF says 2 h ([self-reported](https://www.ilovepdf.com/pt/blog/delete-file-from-ilovepdf)); Convertio not verified | | The privacy trade-off is the reason offline tools exist |
| **Newer, right-click focused** | Image Converter 1.6.3 (Sep 2026), reaConverter | Image conversion in the Windows 11 menu (vendor claims) | not checked | | | | Confirms demand for the modern-menu entry |

ShareX is a capture tool with after-capture tasks, not a general converter; not researched further.

**Judgement.** Three clusters exist: (a) gesture wheels (Filee, ZestDrop, ShiftDrop, OrbitDrop, us), (b) right-click preset tools (File Converter), (c) deep single-purpose apps (HandBrake, PDF24, XnConvert). The wheel cluster is crowded and growing by the day; its leader is large and shipping up to several releases per week. The right-click cluster has a leader that cannot serve Windows 11. Cluster (c) is not our fight.

### 2.2 Where our footprint stands

Filee's installer is about 3 times our default package and nearly 6 times our Store package, and it can pull 100 to 420 MB of engines. ZestDrop's portable build is 251 MB. MikeShift ships no third-party executables and talks to no network; Filee, ZestDrop and ShiftDrop check GitHub for updates by default. That is real and verifiable, but it only matters to users who find us first.

## 3. Users and jobs to be done

No quantitative frequency data exists in public sources I could reach. The frequency column is my estimate.

| Job | Who | Evidence of pain | Frequency (judgement) | MikeShift fit |
|---|---|---|---|---|
| HEIC to JPG/PNG | iPhone owners using Windows | iPhone's default is HEIC ([IT help-desk guide](https://bowdoin.teamdynamix.com/TDClient/1814/Portal/KB/Article/106885/Save-iPhone-and-iPad-Photos-as-JPEG-Instead-of-HEIC)); [Microsoft Q&A, Sep 2025](https://learn.microsoft.com/en-us/answers/questions/5569451/how-do-i-convert-heic-to-jpg-for-free-on-windows-1): free offline batch asked, answers GIMP/IrfanView/FFmpeg, "the HEIF extension cannot convert" | Weekly to monthly | **Partial.** Conversion is good, but reading HEIC needs the HEIF + HEVC extensions ([LIMITS](../LIMITS.md)); without them the file is "unknown" and offers Zip only |
| WebP to PNG/JPG | Anyone saving web images | "Windows 11 doesn't come with a built-in WebP to PNG converter"; Paint one at a time ([thread](https://techcommunity.microsoft.com/discussions/windows11/how-do-i-convert-webp-to-png-on-windows-11/4249650/replies/4249683)) | Weekly | **Good** (bundled Skia fallback, batch). Browser-dragged images are virtual files: "Save the file first" |
| PDF merge, split, compress | Office, students | PDF24 4.6 stars from 22 ratings; online tools hold uploads 1 to 24 h | Monthly | **Good** for simple cases; encrypted PDFs refused; no editing |
| Video to MP4/GIF | Everyone, creators | HandBrake 24.8k stars | Monthly | **Partial.** GIF limited to 15 s, 480 px, no dithering; no MKV/MOV output; Trim re-encodes |
| Audio extraction (MP3/M4A) | Students, podcasters | PowerToys now ships transcode-to-MP3 | Monthly | **Good** |
| Shrink photos/screenshots | Email, forms | PowerToys Image Resizer presets | Weekly | **Partial.** Only "Compress" and "Resize 50%"; no custom size |
| Document export (DOCX to PDF) | Office | Filee's largest bug cluster is Word to PDF fidelity ([issues](https://github.com/KnifeLemon/Filee/issues)) | Occasional | **Poor.** Own text layout unless Word is installed ([LIMITS](../LIMITS.md)); Word is itself the better tool |
| Sensitive files (contracts, IDs, medical) | Professionals, IT | Retention windows of online tools; "works offline" is the top praise in [AlternativeTo comments](https://alternativeto.net/software/file-converter/about/) | Occasional, high stakes | **Strong, structural** |
| OCR, Office to Office, e-books, AVIF output, MKV | Niche | Filee issue list | Rare | **Not served** |

**Judgement.** Our best-served jobs (WebP, PDF, audio, privacy) are real but not the loudest. The loudest, HEIC, is the one where we depend on a Microsoft Store extension that users already complain about. Fixing that dead end matters more than adding any format.

## 4. Windows UX conventions

**4.1 Right-click (Windows 11 modern menu).** Microsoft's route is `IExplorerCommand` in a COM DLL, registered in the manifest as `windows.comServer` plus `windows.fileExplorerContextMenus` (desktop4/5), `Type="*"` for files; menu-building methods must be fast ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)). Legacy menu handlers sit under "Show more options". PowerToys dual-registers Image Resizer and PowerRename through a sparse MSIX, which yields duplicates in the legacy menu by design ([PowerToys docs](https://microsoft.github.io/PowerToys/common/context-menus/)). File Converter's maintainer says its managed SharpShell library does not support Windows 11, he wants a native rewrite, and he cannot get nested submenus working because Explorer does not support subcommands with subcommands (his citation of Microsoft documentation, [#211](https://github.com/Tichau/FileConverter/issues/211); second-hand, not verified by me). A C# `IExplorerCommand` with Native AOT and a sparse package exists as a [reference](https://github.com/2xRon/7zipExplorerExtension). MikeShift is already a full MSIX, so it can declare the verb directly.

The menu is moving: Microsoft says it will make menus "faster, simpler by default, configurable" ([Windows Latest, Jun](https://www.windowslatest.com/2026/06/04/microsoft-admits-windows-11s-right-click-menu-is-a-mess-will-let-you-customize-it-after-years-of-complaints/)); an Insider build (17 Aug) reportedly lets users choose whether app extensions appear in the main menu, a submenu, or not at all, and turns "Show more options" off by default ([OfficeDigital](https://officedigital.io/windows-11-context-menu-customization/), [Windows Latest, Aug](https://www.windowslatest.com/2026/08/20/will-windows-11-remove-the-windows-10-era-right-click-menu-microsoft-reveals-whats-next/)). Sources disagree on rollout status ([Admin365](https://admin365.blog/2026/08/15/windows-11-context-menu-overhaul-2026-full-guide/) says not in a public channel); no broad release is announced. Legacy-only handlers (File Converter) therefore get weaker over time and modern `IExplorerCommand` entries get stronger.

**4.2 Shift-drag means MOVE in Explorer.** Defaults: same drive moves, other drive copies; Ctrl forces copy, Shift forces move, Alt creates a shortcut, and right-button drag offers a menu ([Microsoft Q&A](https://learn.microsoft.com/en-us/answers/a/2790390), [Winaero](https://winaero.com/how-to-show-what-action-will-happen-when-selected-items-are-dragged-in-file-explorer/)). Our drop target always returns COPY and Explorer's cursor text reads "+ Convert to PNG" (screenshot `docs/screenshots/real-drag/01-...png`), so a drop **on the wheel** is safe. The real hazard is in our own test: with Shift held, releasing on an ordinary folder made Explorer **move the original** ([x3b in the real-drag run](../test-runs/2026-10-24-real-drag.md)). That is Explorer behaving normally, but the user who summoned a converter did not ask for a move.

**Judgement.** Shift is still the right default trigger: Shift-drag is rare in daily use (same-drive drags already move), so the wheel interferes less often than Ctrl would (Ctrl-drag is the common deliberate copy). The cost is a rare, recoverable surprise (Explorer supports undo of moves; not verified here). Ctrl is semantically cleaner ("copy" modifier, harmless stray release), which is why Filee chose it. Offer both; test the fear (§9).

**4.3 Discoverability of hidden gestures.** Research on hidden interactions finds users rely on prior experience or documentation when no visible alternative exists ([GhostUI, 2026](https://arxiv.org/pdf/2601.19258)). Our welcome window shows the gesture, but nothing in Explorer hints at it, and touch or pen users cannot hold Shift while dragging. A right-click entry is the visible alternative.

**4.4 Is the wheel a fit?** Pie menus beat linear menus on speed and errors ([Callahan et al., CHI '88](https://sites.umiacs.umd.edu/node/16962)), and marking menus let novices graduate to expert gestures ([Kurtenbach and Buxton](https://www.billbuxton.com/MMExpert.html)). On Windows, radial rings exist for peripherals ([Logitech Actions Ring](https://support.logi.com/hc/el/articles/17859365804439-What-is-the-Actions-Ring-feature-in-Logi-Options), 8 slots; forum opinion mixed: one user called it "a glorified right click menu"). **Judgement:** a power-user delight and an excellent demo, not a general-audience default. Tile positions are stable per file kind, so muscle memory can form.

**4.5 Tray and startup.** Windows 11 hides tray icons in the overflow unless the user toggles them ([guide](https://geekrewind.com/how-to-hide-or-show-system-tray-icons-in-the-taskbar-overflow-menu-on-windows-11/)); one vendor reports its icon re-hides after updates ([Discord thread](https://support.discord.com/hc/pl/community/posts/22025126316311-Honor-Win11-s-System-Tray-Setting)). Our welcome window already links the taskbar settings. Idle cost is 16.5 MB, 0.00% CPU ([real-drag run §8](../test-runs/2026-10-24-real-drag.md)); startup is the user-controlled "Open at login". Judgement: a global low-level hook plus an unsigned binary invites antivirus suspicion (Filee needed signing after [#44](https://github.com/KnifeLemon/Filee/issues/44)); signing is a trust feature, not polish.

**4.6 Notifications.** Windows app notifications persist in Notification Center and cannot be sent by elevated apps ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/)). Our HUD is faster and sits near the pointer, but it vanishes (about 6 s with an action) and leaves no history. Judgement: keep the HUD for success, add a real notification for failures and long jobs.

**4.7 Store expectations.** Individual developers can now register free; Microsoft signs and hosts MSIX apps at no cost ([Windows blog, Sep 2025](https://blogs.windows.com/windowsdeveloper/2025/09/10/free-developer-registration-for-individual-developers-on-microsoft-store/)). ZestDrop and ShiftDrop already use this. The Store flavour (23 MB, shared Windows App Runtime) is the one to submit; the Store installs the framework.

**4.8 Accessibility.** Microsoft expects accessible names, keyboard operation, high contrast and text scaling, and notes DirectX content is not accessible by default ([Learn](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-overview)). We expose a UIA list with named tiles and a click-mode keyboard path (arrows, Enter, Tab, Esc); high-contrast themes exist. **Not verified with Narrator** ([ux-review](../ux-review.md), real-drag run). The drag gesture itself is mouse-only. PowerToys Image Resizer CLI offers `--accessible` progress lines, a nice precedent.

## 5. Gap analysis

**Versus macOS Shiftvert.** At or above parity: WebP and MP3 and WMV outputs, CLI, tools, editors, UIA, themes. Intentionally missing: MOV and AVIF output, Ctrl+O ([LIMITS](../LIMITS.md)). Judgement: Shiftvert is four days old with no stars; "parity" is not a market benchmark. Tangerine is the commercial reference (20 free actions, EUR 34.99).

**Versus File Converter (closest incumbent) and Filee (closest gesture rival):**

| Capability | MikeShift | File Converter | Filee |
|---|---|---|---|
| Win 11 right-click | **No** | Legacy menu only; unreliable | Installer option (Win 11 behaviour unverified) |
| Send To / hotkey on selection | No | No | Send To, shortcuts |
| User presets and quality knobs | **None** | Core feature (custom FFmpeg presets) | Presets, watch folders |
| Format breadth | about 41 actions | FFmpeg-wide | 255 |
| Languages | English | 14+ translations named in release notes | several |
| Signed, Store | No | MSI | Signed from 1.11.0; not Store |
| No bundled engines, no network | **Yes** | Bundles GPL engines | Engines downloaded, update ping |
| Accessible, Fluent-native UI | **Strongest** (WinUI 3, UIA) | WPF | Avalonia |
| Crop/Trim editors, PDF merge | **Yes** | No | Some |

## 6. Positioning

**Statement.** For Windows users who just need a file in a different format, MikeShift is the offline converter built into File Explorer: right-click or Shift-drag, get a copy next to the original, and nothing ever leaves your PC.

**Target users.** Primary: everyday Windows 11 users with a HEIC/WebP/PDF annoyance (find it by search or Store; use right-click). Secondary: privacy-bound professionals and IT (legal, health, finance; MSIX, no network, no admin, deployable) and gesture power users (CLI, wheel, tools).

**Three defensible differentiators**
1. **Smallest, safest footprint**: 23 or 45 MB, no admin, no bundled executables, no network at all (measured, [verify-offline run](../test-runs/2026-10-24-local-and-integrity.md)). Rivals ship 128 to 251 MB or phone home by default. Durable while we hold the line.
2. **Windows-native and accessible craft**: WinUI 3, per-monitor DPI, taskbar-aware wheel, UIA, high contrast, verified in a real Explorer. Rivals use Avalonia, Python or Rust. Medium durability; it needs the Narrator audit to be a claim.
3. **Trust by construction**: copies only, byte-identical originals (288/288 and 32 drag scenarios), MIT with no GPL engines, uses Windows' own codecs so Store codec updates help us. Others can copy it, few will test it.

**Do not compete on:** format count, Office or Word fidelity, video power features (HandBrake, Shutter), PDF editing (PDF24), AI features, or the gesture itself.

**Naming.** "MikeShift" is memorable but says nothing about function and has neighbours (ShiftDrop, Shiftvert). Judgement: keep it (branding is decided), and carry the function in the Store title: "MikeShift: offline file converter". Reserve the Store name and run a trademark search before submission (not done). Tagline options: (1) "Convert any file from File Explorer. Nothing leaves your PC." (2) "Right-click or Shift-drag. Converted on your PC, never uploaded." (3) "HEIC, WebP, PDF, video: converted offline in Explorer." Avoid clone-style "Drag. Hold. Done." (Shiftvert's tagline).

**Distribution.** (1) Microsoft Store (free signing and hosting for individuals; submit the 23 MB store flavour). (2) GitHub releases with checksums, signed once Store identity or SignPath is available. (3) winget via the Store source (standard behaviour; not verified here). Publishing the repo is a prerequisite for credibility; today the project URL returns 404.

**Pricing options**
| Option | For | Against |
|---|---|---|
| **Free, MIT, donation/sponsor link** (recommended) | Matches every rival (Filee, File Converter, PowerToys); trust is the scarce asset; no network means no licence servers | No revenue |
| Free GitHub + paid Store "supporter" listing | Tangerine shows some will pay (EUR 34.99) | Hostile Store reviews next to free clones; unverified Store policy |
| Free core + Pro (presets, watch folders, OCR) | Real value for power users | Premature with no users; offline licensing adds friction and cuts against the privacy story |

Decide on money after about 1,000 installs and the usability study.

## 7. Direct answers

**Should we add a native Windows 11 right-click "Convert with MikeShift" menu? Yes, P0.** Design: one top-level command with a one-level flyout of the 4 or 5 most likely formats for the selection, plus "More options…" that opens the click-mode wheel at the pointer (reuses the existing UI; avoids the nested-submenu limit). Keep menu building trivial (decide from extension only; no file reads) and hand the work to the running tray app. Expect users to be able to hide us under the coming customisation, so the entry must earn its place.

**Should Shift-drag remain the primary gesture? Keep it as the headline gesture and demo; no longer the primary entry for new users.** Offer "Shift (default) or Ctrl" at first run with one sentence: "Ctrl copies, so letting go early is harmless." Add "press Esc to cancel" to the first-run text and the Shift-move warning. If the usability test shows two or more of five participants hesitating about moving files, switch the default to Ctrl.

## 8. Prioritised recommendations

Effort: S under 1 week, M 1 to 3 weeks, L over 3 weeks.

| # | Item | Rationale and evidence | Effort |
|---|---|---|---|
| **P0-1** | Ship signed via the Microsoft Store; publish the repo; add winget; GitHub release with checksums | README: self-signed certificate required; unsigned installer blocked Filee users ([#44](https://github.com/KnifeLemon/Filee/issues/44)); free individual registration and signing; ZestDrop/ShiftDrop already there | M (owner account, privacy policy page, listing assets) |
| **P0-2** | Windows 11 right-click entry opening the wheel/flyout (`IExplorerCommand`, MSIX manifest, x64 and ARM64; named-pipe handoff to the tray app) | File Converter's five-year gap; menu overhaul favours modern entries; PowerToys pattern; C# AOT precedent | L |
| **P0-3** | Fix the HEIC dead end: if a `.heic` is dragged and cannot be decoded, show a dedicated tile or pill "Install the free HEIF + HEVC extensions" with the Store link, plus a first-run codec check | #1 job; Microsoft Q&A and extension complaints; our file shows "Zip only" ([LIMITS](../LIMITS.md)) | S to M |
| **P0-4** | Run the 5-person usability test (§9) before further features | No frequency or reception data; Shift-move fear unmeasured | S |
| **P1-1** | "Convert current Explorer selection" global hotkey | Keyboard-only, accessibility, touch/pen; OrbitDrop (Ctrl+Alt+Space) and ZestDrop (F8/F9) already offer it | M |
| **P1-2** | First-run activation choice (Shift or Ctrl) plus "originals are never moved; Esc cancels" copy | §4.2; real-drag run x3b | S |
| **P1-3** | Narrator, keyboard-only and Windows high-contrast audit with a real assistive-tech user; fix findings; state results in README | Not verified today; defensible claim #2 depends on it | M |
| **P1-4** | Minimal knobs: custom resize (percent or pixels), JPEG quality, output folder, GIF length | PowerToys presets, File Converter presets; we have none | M |
| **P1-5** | Rewrite README/Store copy around jobs (HEIC, WebP, PDF) and "no upload", with a 20-second demo GIF and the footprint comparison | Positioning §6; rivals lead with feature counts | S |
| **P1-6** | Verify on real ARM64, multi-monitor and touch hardware before Store submission | README status line | S to M |
| **P2-1** | Real Windows notification for failures and jobs over about 10 s | §4.6 | S |
| **P2-2** | Windows 10 entry (legacy verb or Send To; Send To may be blocked by MSIX file virtualisation, unverified) | Windows 10 is 27.8% of Windows ([StatCounter, Sep 2026](https://gs.statcounter.com/windows-version-market-share/all/worldwide)) | M |
| **P2-3** | Localisation framework (File Converter has 14+ translations named in release notes) | Store reach | L |
| **P2-4** | Evaluate bundling a HEIC decoder (size, licence) | Removes the HEIC dependency | L |
| **P2-5** | Provenance note: confirm the wheel UI and README wording do not echo Tangerine's listing too closely | Applesfera quotes the Tangerine App Store text; Shiftvert's README is close; gestures are not copyrightable, wording and look can be | S |
| **P2-6** | Test on the Insider context-menu build; watch the rollout | §4.1 | S |

## 9. Five-person usability test plan

**Setup.** Five Windows 11 users, none developers. Fresh PC or VM; build installed beforehand except for task 1. 30 to 40 minutes each, think-aloud, no coaching, screen and voice recorded. Recruit: two iPhone owners with real HEIC photos; one office worker who merges PDFs; one student (web images, video to audio); one keyboard-only or Narrator user (separate session, Task 9 is theirs). Five users finds most major problems (common heuristic; not cited here). Pre-seed fixtures: HEIC, WebP, three JPGs, two PDFs, an MP4, one corrupt PDF, one password-protected PDF.

| # | Task (say only this) | Success criterion |
|---|---|---|
| 1 | "Get this app and tell me when it's running." (from Store or download page) | 4 of 5 find the tray icon in under 3 minutes unaided; record certificate or SmartScreen friction |
| 2 | "Make this iPhone photo a JPG." (no hint) | 4 of 5 get a JPG in under 60 s; log **which door they try first** (right-click, drag, tray, search) |
| 3 | "Convert it by dragging." Then ask: "What happens to your original?" and "What if you let go outside?" | 4 of 5 say original is untouched; **zero** files moved by accident; note anyone who hesitates to hold Shift |
| 4 | "Make this photo small enough to email." | 3 of 5 find Compress or Resize without the welcome tip in under 90 s |
| 5 | "Put these 3 images (then 2 PDFs) into one PDF." | 4 of 5 in under 2 minutes |
| 6 | "Pull the audio out of this video as MP3." | 4 of 5 in under 90 s |
| 7 | Corrupt PDF, protected PDF, HEIC without extensions: "What went wrong, and what would you do next?" | 4 of 5 name the cause and a next step; HEIC path must lead to the extension install |
| 8 | "Find the file you just made." | 4 of 5 in under 20 s (toast "Show in folder" or Explorer) |
| 9 | Accessibility session: Task 2 via tray and keyboard only, with Narrator | Completes with at most 1 hint; Narrator announces tile names and result |
| 10 | After: SUS questionnaire; "Would you keep this installed?"; trust 1 to 5 for an app that watches the mouse and keys | SUS 68 or more; trust 4 or more; 4 of 5 keep it |

**Overall gates:** median time to first conversion under 90 s; no destructive incident; at least 3 of 5 try right-click first (confirms P0-2) or, if not, that the wheel taught itself within the session. Report the door tally, errors, quotes and a ranked issue list to feed §8.
