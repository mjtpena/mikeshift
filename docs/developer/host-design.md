# Separating a reusable host from MikeShift

A design note for [issue #10](https://github.com/mjtpena/mikeshift/issues/10). It asks whether the parts of MikeShift that are not converters should become a host that other offline "right-click a file, get a file back" tools could build on, for example local transcription to subtitles. A speech model is hundreds of MB, so such a tool does not belong inside MikeShift.

This note only decides. Nothing here has been built or split. Every claim below was checked against the code at the paths given; the background is in [architecture.md](architecture.md).

**Recommendation:** do not split yet. See [section 6](#6-recommendation).

## 1. Boundary

Almost everything the issue lists as "host" already contains no converter code. The trouble is that it is not separated from the converters by a project boundary, and a handful of types connect the two.

**Already host-shaped** (no knowledge of images, PDFs or codecs):

| Part | Where |
|---|---|
| Drag state machine, modifier tracking, wheel geometry, placement, animation | `src/MikeShift.Core/Interaction/` (`DragSession`, `ModifierTracker`, `WheelGeometry`, `WheelPlacement`, `WheelAnimation`) |
| Input hooks, watchdog, `IInputMonitor` | `src/MikeShift.App/Input/` |
| Wheel window, renderer, OLE drop target, UI Automation | `src/MikeShift.App/Wheel/` |
| Explorer selection and the hotkey | `src/MikeShift.Core/Hotkeys/`, `src/MikeShift.App/Selection/`, `src/MikeShift.App/Input/HotkeyService.cs` |
| Explorer right-click: COM server, pipe client, pipe listener, request format | `src/MikeShift.App/Shell/`, `src/MikeShift.Core/Shell/ShellRequest.cs` |
| Toast, failure dialogs, chime | `src/MikeShift.App/Hud/` (`IHud`, `ToastWindow`, `NoticeWindow`) |
| Output names and the "write hidden, then move into place" rule | `src/MikeShift.Core/Convert/Naming.cs` (its only MikeShift type is `ConversionException`) |
| Job bookkeeping | `src/MikeShift.Core/Feedback/JobLedger.cs`, `src/MikeShift.Core/Hosting/` |
| Trimmed MSIX build, COM interface scan | `Trimming.props`, `tools/ComInterfaceScan.cs`, the `Packaging` targets in `src/MikeShift.App/MikeShift.App.csproj` |

**Where host and product are tangled.** These are the places a split would have to cut:

| Type | Why it is not host-only |
|---|---|
| `ActionDef`, `ActionContext` (`Actions/`) | `ActionDef` is built on `Capabilities`, `Kind`, and `WheelPage` with two fixed pages (formats, tools). `ActionContext` carries `ConversionOptions`, and `IInteraction` has two methods, `ChooseCropAsync` and `ChooseRangeAsync`, which are Crop and Trim. `ActionDef.Run` also sets `Naming.OutputFolder` from `ConversionOptions.OutputFolder`. |
| `ActionCatalog` | A static class that picks the list by `Category` (Image, Pdf, Video, Audio, Document). The wheel, the Explorer bridge, the job runner and the CLI all call it directly (`WheelController`, `ShellBridge.RunAction`, `CliRunner`). |
| `ContextMenuPlanner` | The ranking table is keyed by `Category`, the top-level text is the constant "Convert with MikeShift", and it calls `CodecHelp`. |
| `DragSession`, `WheelModel`, `WheelText` | They take `WheelPage` and `ActionDef`. `WheelText` knows about Word and `.doc`. |
| `JobRunner`, `AppServices`, `Preferences` | They build an `ActionContext` from `Capabilities` and `Preferences.Conversions`, and set `Capabilities.WordAllowed`. |
| `FeedbackCopy`, `FeedbackPlan` | `AppName = "MikeShift"` and converter wording ("can't convert these files"). |
| `CliRunner` | Its options (`--quality`, `--resize`, `--compress`, `--gif-seconds`, `--max-size`) are the conversion settings. The shared part is argument parsing, the exit codes and `FastExit`. |
| Names | `AppDataPaths.FolderName` and `ShellNames.DataFolder` both hard-code "MikeShift"; `Program.InstanceKey` is "mikeshift"; `WheelUia` reports the UI Automation framework id "MikeShift". |

The one structural fact that matters most: **`MikeShift.Core` is a single assembly** (`src/MikeShift.Core/MikeShift.Core.csproj`) that holds both the host types above and the converters, and it references PDFsharp, PdfPig, DocumentFormat.OpenXml and SkiaSharp (with its native assets). A product that referenced Core as it is would take all of that along; `libSkiaSharp.dll` alone is 12 MB ([architecture §7](architecture.md#7-packaging), "Not taken"). So the first real step of any split is cutting Core in two, and the `ActionDef` family is where the cut is hard.

The issue's first guess (host: `Interaction/`, `Shell/`, `Hotkeys/`, `Hosting/`, `Naming`, `ActionDef`, `Each`, `JobLedger`, `FeedbackPlan`) is right except for `ActionDef`, `Each` and `FeedbackPlan`: each of them needs a generic version before it can move.

## 2. How a product plugs in

Runtime plugin loading is out. The package is IL-trimmed with `TrimMode=full` (`Trimming.props`), the trimmer cannot see types loaded by name, and the app avoids reflection on purpose (architecture §7, "How the hazards were handled"). The classic COM interop in the wheel also depends on `MikeShift` and `MikeShift.Core` being trim roots (`TrimmerRootAssembly` in `MikeShift.App.csproj`).

So a product would be **its own app that links the host at build time** and hands it three things in code:

1. **A catalog.** Today this is `ActionCatalog.For(paths, caps, options)` plus `ActionCatalog.Find(actions, title)`. The host would take an interface with those two operations, and a product would implement it. The Explorer bridge already resolves by title from the live catalog and never trusts the extension process (`ShellBridge.RunAction`), so this seam exists in behaviour; it is only missing in types.
2. **A way to run and a way to talk.** `ActionRun` and `ActionResult` are generic enough. `IInteraction` would become a product-defined interface (a transcription tool needs a language picker, not a crop box).
3. **Words and identity.** App name, tray text, toast copy, the top-level menu title, data folder, pipe name, CLSID, instance key.

Two limits come from how the gesture works, not from the build:

* **The drag wheel cannot be duplicated.** A Shift-drag is seen by a global `WH_MOUSE_LL` hook (`LowLevelInputMonitor`). If two resident products each ran the drag wheel, each would pop a wheel on the same drag. Only one process can own the gesture.
* **The hotkey is one chord per machine.** `HotkeyService` registers one global chord (default Ctrl+Alt+Shift+Space) and `HotkeyConflicts` exists because chords collide. A second product needs its own, or none.

For a transcription tool a wheel with one tile ("Subtitles") is also a poor fit. The natural entry for a single-action tool is the right-click menu and the CLI, with the wheel and hotkey left to MikeShift. That suggests a smaller host than the issue imagines: the Explorer entry, the pipe, `Naming`, the toast, the CLI runner and the packaging, without the input hooks.

## 3. Packaging

Each product is a separate MSIX with its own Store listing. What has to be different per product, with where it lives today:

| Item | Today | Rule |
|---|---|---|
| Package identity | `Identity Name`, `Publisher` in `Package.appxmanifest` (Partner Center values) | Own identity per product |
| Explorer command CLSID | `ShellIds.CommandClsid`, repeated in the manifest in `com:Class` and in two `desktop5:Verb` entries; `ManifestTests` checks them | New GUID. Two products sharing a CLSID would make one shadow the other |
| Pipe name | `ShellIds.PipeNameFor` gives `mikeshift-shell-<SID>` | Different prefix. With the same name the second tray app to start cannot listen, and the other product's right-click would send its files to the wrong app |
| Single-instance key | `Program.InstanceKey` ("mikeshift") | Own key |
| Data folder | `AppDataPaths.FolderName`, `ShellNames.DataFolder` | Own folder name (the cache files `shell-capabilities.txt` and `shell-options.txt` live there) |
| Startup task, alias, display names | `MikeShiftStartup`, `mikeshift.exe` alias, `MikeShift` strings in the manifest | Own |
| Icons and Store assets | `tools/IconGen`, `assets/brand/mikeshift.svg`, `docs/store/` | Own |
| Context menu entry | One top-level entry per app on Windows 11 (architecture §9) | Each product adds its own line to the right-click menu, next to "Convert with MikeShift" |

What stays the same in each: the shape of the manifest (an `App` entry, a `com:ExeServer` started with `-Embedding`, `windows.fileExplorerContextMenus`, optionally a console alias), the trimmed self-contained build, and the two flavours (sideload and store) from `scripts/release.ps1`.

Two build details a second product has to repeat or generalise. `BundleMikeShiftCli` and the related verify targets in `MikeShift.App.csproj` are written for one app plus one CLI sharing a runtime and one trim run; `Trimming.props` is already product-neutral. And every product ships its own copy of .NET and the Windows App SDK (self-contained, as MSIX cannot depend on a machine-wide .NET), so a user with two products pays that size twice and has two resident tray processes.

## 4. Cost to MikeShift

What would get worse if MikeShift were split into host plus product, even with a perfect job:

* **Churn without a customer.** The cut goes through `ActionDef`, `ActionContext`, `ActionCatalog`, `ContextMenuPlanner`, `DragSession`/`WheelModel`, `JobRunner`, `FeedbackPlan` and `CliRunner`, which are among the most edited and most tested files. The tests that exercise them (`tests/MikeShift.Core.Tests`, `tests/MikeShift.Cli.Tests`, the drag and context menu harnesses) would all need to follow.
* **Changes that span two projects.** A wheel change that needs a new tile field today touches `ActionDef` and the renderer; afterwards it would touch a host interface, the host and MikeShift's implementation. A Windows App SDK bump touches the host and the product.
* **The trimming rules get harder.** `TrimmerRootAssembly` lists `MikeShift` and `MikeShift.Core`; a new assembly holding COM interfaces has to be added or its vtables can shift (architecture §7). `tools/ComInterfaceScan.cs` already protects every `[ComImport]` interface it finds, but the root list and the CLI-bundling targets have to be kept right by hand.
* **Speed and size are the things to defend.** The wheel appears about 30 ms after Shift (budget 100 ms) and the package is about 45 MB (sideload) / 23 MB (store). Replacing static calls with an interface on the `DragEnter` path is cheap, but it is on the path the budget measures, so it needs the drag harness before and after. Size should not change if the cut is clean, because the trimmer already removes what is unused; a cut that leaves the converter packages reachable from the host would not be clean.
* **Wording.** Every user-visible string that says "MikeShift" would have to come from the product, or it stays wrong in the second product.

## 5. Repo shape

| Choice | What it gives | What it costs |
|---|---|---|
| **Shared projects in this repo** (host projects next to `MikeShift.Core` and `MikeShift.App`, each product an app project in the same solution) | One CI, one `Directory.Packages.props`, a host change and its product fix in one pull request, tests for both in one run. Matches the current layout. | The repo name and docs would no longer say what it is. Products ship together on the host's schedule unless the release script learns about several packages. MikeShift's releases get noisier. |
| **Separate host repo used as a package** | A clear contract and an independent release rhythm. | The hardest option for this code. A NuGet package cannot carry a manifest, XAML windows, `Trimming.props` targets, the COM interface scan or a CsWin32 `NativeMethods.txt` set without extra machinery. Every host fix becomes a publish, a version bump and a second pull request. The host has no tests of its own at the moment (they live in `MikeShift.Core.Tests`), so they would have to move first. |
| **Copy the host into each product** | Costs nothing today, no coordination. | The code that was hardest to get right (hooks that Windows silently removes, the vtable and trimming issues, Explorer's UI-thread limits, the 16-item sample) would drift between copies, and a fix has to be ported by hand. Acceptable for one sibling, not for several. |

If a split ever happens, shared projects in this repo is the least bad. Copying is the honest answer for a first sibling that will not be followed by others.

## 6. Recommendation

**Don't split yet.**

* No second product exists, and none has a model chosen, a size, or a Store listing. The host's real interface depends on what the first sibling needs (the analysis in section 2 suggests a single-action tool wants the right-click entry and CLI, not the wheel), and designing that without a customer is guessing.
* The split is expensive in the places MikeShift is most sensitive: the converter-facing types, the trim configuration, and the wheel latency path.
* The worst cost of waiting is small. The parts the issue lists are already separated by folder and mostly by type. Waiting does not close any door.

**What to do instead, at no cost:** keep new code on the right side of the tangles in section 1. When a change adds converter knowledge to a host-shaped type (`DragSession`, `ShellRequest`, `Naming`, `Hud/`), prefer putting it in the converter-facing side. This is a review habit, not a task.

**What would change the answer.** Any one of these means start the split:

1. A second product is committed: a named tool, a model that fits a Store package, and someone to maintain it.
2. A sibling would otherwise copy more than about one folder of host code.
3. A change to the host (a Windows 11 menu change, a hook problem) is wanted in two places at once.

**If it starts, in this order:**

1. **A host spike first, with one real sibling.** Build the sibling's right-click entry, pipe, `Naming`, toast and CLI against copies, and write down which of the section 1 types it actually needed. This is the cheapest way to find the real interface.
2. **Cut `MikeShift.Core` into a converter-free project and the converters.** `Interaction/` (with `WheelPage` and a generic tile type), `Hotkeys/`, `Hosting/`, `Shell/` (with the identity values passed in), `Naming`, `JobLedger` and the generic parts of `Feedback/` go to the first; `Convert/`, `Capabilities`, `Kind`, `ConversionOptions`, `CodecHelp` and the catalog stay. Run the existing tests after each move.
3. **Then the app side**, keeping the drag wheel and hotkey in MikeShift only.
4. **Shared projects in this repo**, with the release script building more than one package.

Open questions the spike would answer: whether a sibling can ride on MikeShift's tray process instead of having its own (for example, MikeShift's catalog starting the sibling's CLI through its app execution alias, which MikeShift already uses for its own `mikeshift.exe`; not tried here), and whether one Explorer menu line for several tools is possible, since Windows 11 allows one top-level entry per app.
