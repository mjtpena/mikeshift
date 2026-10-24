# Package size: per-architecture downloads, IL trimming, shared Windows App Runtime (2026-10-24)

Goal: shrink the MikeShift package **without breaking any feature**. Three changes, each measured on its own and kept only if the feature set still passed on the *release layout* (the files a user gets), not just on the Debug build.

* Build: `feature/windows-port` @ `220161c` plus the commits of this branch. .NET SDK 10.0.300 (runtime 10.0.8), Windows App SDK 2.5.1 (component packages), Windows 11 26300. x64 was run; **ARM64 was built and measured but not run** (no ARM64 hardware).
* "Compressed" is the `.msix` file size (what is downloaded), "installed" is the sum of the package's files (without `[Content_Types].xml`, the block map and the signature).

## 1. Sizes

| | x64 compressed | x64 installed | ARM64 compressed | ARM64 installed | bundle (both) |
|---|---:|---:|---:|---:|---:|
| **Before** (self-contained, untrimmed; re-measured) | 80.1 MB | 208.1 MB | 77.8 MB | 221.3 MB | 157.9 MB |
| + 1 per-architecture downloads | 80.1 MB | 208.1 MB | 77.8 MB | 221.3 MB | 157.9 MB (still built) |
| + 2 IL trimming = **sideload flavour** (Windows App SDK bundled) | **44.9 MB** | 112.3 MB | **43.6 MB** | 117.3 MB | **88.4 MB** |
| + 3 shared Windows App Runtime = **store flavour** | **23.3 MB** | 56.5 MB | **22.5 MB** | 57.0 MB | **45.9 MB** |
| (for reference) shared Windows App Runtime without trimming | 58.5 MB | 152.3 MB | not built | | |

What each option saved (x64):

| Option | Effect |
|---|---|
| 1. Per-architecture `.msix` files beside the bundle | A user downloads one file, **80.1 MB instead of 157.9 MB** (-49 %), with no change to what is in it. `release.ps1` writes `MikeShift_<ver>_x64.msix`, `_arm64.msix` and the bundle, all in `SHA256SUMS.txt`. |
| 2. IL trimming (`PublishTrimmed`, `TrimMode=full`) | 80.1 -> 44.9 MB (-35.2 MB, -44 %), installed 208 -> 112 MB. Biggest: `System.Private.CoreLib` 15.3 -> 3.1 MB, `Microsoft.Windows.SDK.NET` 25.1 -> 0.8 MB, `Microsoft.WinUI` 7.0 -> 1.0 MB, `System.Private.Xml` 7.4 -> 0.7 MB, `System.Linq.Expressions` 3.5 -> 0.5 MB, OpenXml 7.6 -> 5.1 MB. |
| 3. Windows App SDK as the shared framework (`WindowsAppSDKSelfContained=false`) | on top of trimming 44.9 -> 23.3 MB (-21.6 MB); on the untrimmed package 80.1 -> 58.5 MB. WinUI's native files (`Microsoft.ui.xaml.dll` 14.6 MB, `Microsoft.UI.Xaml.Controls.dll`, `WinUIEdit`, `dcompi`, `dwmcorei`, the `.pri` files, the locale folders...) come from the framework package instead. Win2D, the WebView2 loader, SkiaSharp and the .NET runtime stay in the package. |
| Both | **x64 80.1 -> 23.3 MB (-71 %)**, ARM64 77.8 -> 22.5 MB, bundle 157.9 -> 45.9 MB. |

The four clean builds of `release.ps1 -Flavor all` take about 18 minutes on this PC.

## 2. How trimming gets into the MSIX, and what it needed

The Windows App SDK MSIX tooling packages an exe project from the *publish* item list (`__GetPublishItems` in `Microsoft.Windows.SDK.BuildTools.MSIX.Packaging.targets`) and has special handling for `PublishTrimmed`/`PublishReadyToRun` (it removes the un-trimmed inputs from the payload). So `-p:PublishTrimmed=true` on the ordinary package build (`dotnet build src/MikeShift.App -c Release -p:GenerateAppxPackageOnBuild=true ...`) runs ILLink and packages the result; nothing else is needed. `Trimming.props` sets it for package builds only (a plain `dotnet build` stays fast) and turns the trim analyzers on for every compile, so a trim problem in our own code is a build error.

**The CLI** shares the app's runtime, so it is trimmed *with* the app: the untrimmed `MikeShift.Cli.dll` is added to the app's ILLink inputs as an entry-point root (`AddBundledCliToTrimInputs`), the linked dll is what the package contains, and `MikeShift.Cli.deps.json` is derived from the app's (a first attempt with the CLI's own deps.json failed at run time: PdfPig could not load `System.Memory` because the CLI's own, different trim had dropped it from its deps.json). `VerifyBundledCliTrimmedSharesRuntime` replaces the byte-for-byte hash check in trimmed builds: every file of the CLI's own publish must be in the package, and the CLI's `runtimeconfig.json` settings must all be in the app's.

### Trim warnings found, and how each was resolved

First package build with trimming on and nothing suppressed (analyzer and linker reports counted together, duplicates included): 66 x IL2026, 60 x IL2050, 10 x IL2072, 4 x IL2104. With `-p:TrimmerSingleWarn=false` the IL2104s expand to 70 x IL2081 inside two Microsoft assemblies (row 5).

| # | Warning | Where | Resolution |
|---|---|---|---|
| 1 | IL2026 (2) `JsonSerializer` | `Settings/Preferences.cs` (settings.json) | Source-generated `JsonSerializerContext` for `Dictionary<string,string>`; same file format. (Reflection JSON is also switched off at run time in trimmed apps.) |
| 2 | IL2026 (10) C# runtime binder, IL2072 `Activator.CreateInstance(Type)` | `Core/Convert/Documents/WordInterop.cs` (`dynamic` late binding to Word) | Rewritten without `dynamic`: `CLSIDFromProgID` + `CoCreateInstance` for the object, `Type.InvokeMember` with named arguments for the calls (on a COM object that is an `IDispatch::Invoke`), `TargetInvocationException` unwrapped so `COMException` handling is unchanged. Word is installed here and the legacy `.doc` and DOCX->PDF paths pass through the trimmed CLI. One targeted `IL2075` suppression on the `InvokeMember` helper (the target is always a COM object, so no metadata lookup happens). |
| 3 | IL2072 `Activator.CreateInstance(Type.GetTypeFromCLSID(...))` | `Wheel/WheelDropTarget.cs` (`IDropTargetHelper`) | `CoCreateInstance` with the interface's IID. |
| 4 | IL2050 (60 reports, 11 distinct P/Invokes) "COM interop cannot be guaranteed after trimming" | `RegisterDragDrop`, 4 `Uia*` calls, `MFCreate*`, `SHCreateItemFromParsingName`, `SHCreateShellItemArrayFromDataObject` (hand-written and CsWin32 `allowMarshaling: true` P/Invokes with `[ComImport]` interfaces in the signature) | An advisory with no code fix short of moving every COM interface to `[GeneratedComInterface]`. Handled instead: `BuiltInComInteropSupport=true` (trimming otherwise disables classic COM interop); `MikeShift` and `MikeShift.Core` are trim roots (`TrimmerRootAssembly`), so the interfaces declared there stay whole; **the interfaces that live elsewhere are protected by the build (the last row of the next table)**; and IL2050 is suppressed, with that justification, on the five callers (`WheelAutomation`, `WheelDropTarget.Register`, `FilePicker.ItemFor`, `DataObjectInspector.ReadShellItems`, `MfInterop`) and on the two CsWin32 `PInvoke` classes only (`TrimSuppressions.cs`, ILLink reports the warning at the caller). |
| 5 | IL2104 x2 "assembly produced trim warnings" | `Microsoft.Windows.SDK.NET`, `WinRT.Runtime` (CsWinRT) | 70 x IL2081 with `TrimmerSingleWarn=false`, every one in the `ABI.*` generic-collection fallbacks (`InitFallbackCCWVTableIfNeeded`...), for generic instantiations nothing here uses. Accepted for the linker run only (`NoWarn` IL2104 in a target that runs just before `_RunILLink`, not for the compiler). The WinRT generics this app does use (`IAsyncOperation<PdfDocument>`, `MediaTranscoder` progress, `StorageFile`, `LocalSettings`) work: the CLI suites drive them through the trimmed exe. |
| 6 | IL2026 x1 | `System.Private.CoreLib` `ComActivator`: "Built-in COM support is not trim compatible" | The framework's own notice for `BuiltInComInteropSupport=true`. Accepted for the linker run only. |

Anything else the linker reports is still an error (`TreatWarningsAsErrors` stays on). **Update (hardening):** rows 5 and 6 are no longer a project-wide `NoWarn` (which also hid any new IL2026 from our own assemblies in the linker run). They are `UnconditionalSuppressMessage` attributes injected by `tools/ILLink.FrameworkSuppressions.xml` onto `ComActivator.GetClassFactoryForTypeInternal` (IL2026) and, as module-scope IL2081 suppressions, onto the two projection assemblies (no IL2081, so no IL2104). Proof: a temporary `JsonSerializer.Serialize(object)` call in MikeShift.Core, hidden from the compile-time analyzer with `#pragma warning disable IL2026`, passed the trimmed build with the old `NoWarn` and fails it now (`Trim analysis error IL2026: MikeShift.Core.TempTrimProbe.Reflective(Object)`); without it `release.ps1 -Flavor sideload -Platforms x64` builds with the same single mspdbcmf warning and the same 44.9 MB package, and the CLI suites pass 82 / 82 (`MIKESHIFT_SLOW=1`) against its unpacked `MikeShift.Cli.exe`. A package build ends with one warning, the MSIX tooling's "mspdbcmf.exe could not be found, a symbols package will not be generated", which was there before.

### Failures that no warning predicted (found by running the trimmed layout)

| Symptom | Cause | Fix |
|---|---|---|
| CLI: `error: This PDF is damaged` (`PdfCliTests`, `IntegrityCliTests`) | CLI's own deps.json lacked `System.Memory` (above) | Derive `MikeShift.Cli.deps.json` from the app's. |
| Settings window and the Trim editor never appear (`XamlParseException: Failed to assign to property ContentControl.Content`) | The linker's "remove unused interfaces" optimisation removed `IContentControlOverrides` and its relatives from the WinRT projections; `SettingsCard : ContentControl` (a managed class derived from a WinUI control) then had no such interface on its COM callable wrapper | `_TrimmerUnusedInterfaces=false` (cost: none measurable, 44.9 MB before and after). |
| **The app crashed on the first Shift-drag** (0xC0000005 in `ComTypes.IDataObject.QueryGetData`) | `[ComImport]` interfaces are called through their vtable in declaration order. The trimmer removed 5 of the 9 methods of the BCL's `IDataObject` (nothing here calls them), so `QueryGetData` moved from slot 5 to slot 2. The same would hit the 165 `[ComImport]` interfaces of WinUIEx and the 33 of `System.Private.Windows.Core`. No warning, no unit test, no scene: only the real-drag harness saw it (6 of its 8 scenarios failed) | `tools/ComInterfaceScan.cs` (a .NET 10 file-based app the build runs) lists every `[ComImport]` interface in the linker's inputs in a root descriptor with `preserve="all" required="false"`, and after the link compares each surviving interface with its input and **fails the build if one lost a method**. IDataObject has its 9 methods again; the harness then passed 8 of 8. |

## 3. Verification on the shrunk build

All of this ran on the **release layout** (`makeappx unpack` of the final `.msix` files from `release.ps1 -Flavor all`), x64.

| Check | Result |
|---|---|
| `dotnet build MikeShift.sln` | 0 warnings, 0 errors |
| `dotnet test MikeShift.sln` (Debug, untrimmed) | Core 2098 passed / 12 skipped, CLI 76 passed / 2 skipped |
| Same with `MIKESHIFT_SLOW=1` (after the last change) | Core **2153 / 2153 passed, 0 skipped**, CLI **78 / 78**, build 0 warnings |
| **CLI suites against the trimmed `MikeShift.Cli.exe`** (`MIKESHIFT_CLI_EXE`, new switch in `CliProcess`; includes the integrity CLI tests for all 41 actions, hardening, Word and the slow tests, `MIKESHIFT_SLOW=1`) | sideload layout **78 / 78 passed, 0 skipped**; store layout **78 / 78** (first trimmed attempt: 74 / 76, the two PDF failures above) |
| `scripts/verify-offline.ps1 -SkipApp` against the trimmed CLI (the app phase injects keystrokes, so it was left out) | 271 CLI processes, exit codes 227 x 0 and 16 x 1 (the same documented refusals as the untrimmed run, no unexpected failure), **0 TCP/UDP endpoints owned by MikeShift.Cli**, 266 distinct PIDs sampled every 50 ms. The script now also watches the `MikeShift.Cli` process name. |
| `MikeShift.exe --smoke-test` | exit 0 |
| `--dev --render-wheel all` (16 scenarios) | all 16 PNGs **pixel-identical** to the committed `docs/screenshots/wheel-v2/` (compared programmatically, three looked at) |
| `--dev --scene` / `--show-*` / `--crop` / `--trim`, captured with `capture-scene.ps1`: `toast-progress`, `toast-progress-batch`, `toast-success`, `toast-success-many`, `toast-downloads`, `toast-partial`, `toast-already`, `toast-long`, `dialog-single`, `dialog-partial`, `dialog-all`, `dialog-merged`, `dialog-elevated`, `tray-menu`, Settings, Welcome, Crop editor, Trim editor (18) | all 18 appear, no blank window, none logged an error; 15 pixel-identical to the same captures of the untrimmed Debug build, the other three are an animating toast, the flyout (placed at the cursor) and the trim film strip (frames load as they arrive); looked at: settings, merged dialog, tray flyout, trim editor |
| UI Automation provider (click-mode wheel read by a UIA client, no input sent) | list "MikeShift wheel: convert to" with its tiles ("Convert to JPG", "Convert to TIFF"...), same as the Debug build |
| **Installed-package check** (`Add-AppxPackage -Register <layout>\AppxManifest.xml`, Developer Mode, nothing imported into any certificate store) | **sideload** and **store** flavours: registered, started from `shell:AppsFolder\MikeShift_zatzfr9kzh5xt!App`, tray icon present ("MikeShift - Shift-drag files to convert" in the overflow flyout, found with UI Automation), Welcome window shown, no `[ERROR]` in the log, `where mikeshift` -> `%LOCALAPPDATA%\Microsoft\WindowsApps\mikeshift.exe`, `mikeshift --list` and `--run Compress` from a fresh `cmd` exit 0, then `Remove-AppxPackage`. The store flavour reported the dependency `Microsoft.WindowsAppRuntime.2` (2.5.1.0 was installed here) and loaded `Microsoft.UI.Xaml.dll` from `C:\Program Files\WindowsApps\Microsoft.WindowsAppRuntime.2_2.5.1.0_x64__8wekyb3d8bbwe\`. |
| **Real drag** (`tools/DragHarness --anger x1a,x1j,x1e,x1f,x4d,x3-taskbar,x5g,x5f` against the unpacked sideload layout started with `--dev`, `MIKESHIFT_ACCEPT_INJECTED=1`, `MIKESHIFT_NO_SINGLE_INSTANCE=1`; real Explorer windows on temp folders, nothing else injected) | **8 of 8 scenarios pass, 138 checks, nothing on stderr**: basic format drop (`x1a`), tools page Ctrl -> Compress and Resize 50% (`x1j`), Merge PDF of three selected images (`x1e`), Zip-only hub (`x1f`), virtual file out of a `.zip` (`x4d`), Shift over the taskbar at 7 positions (`x3-taskbar`), tray flyout keyboard (`x5g`), Convert files + toast + Show in folder (`x5f`). The first trimmed run failed 6 of 8 (the COM crash above). |

Not covered: ARM64 (built, not run), the store flavour as an *unpackaged* layout (it needs the framework package registered and a bootstrapper, so it was verified installed instead: tray, Welcome window, alias; the dev-switch scenes and the drag harness only run against the sideload layout), `IShellItemArray` drops (Libraries/search results; its interfaces are our own, whole) and every harness scenario outside the eight.

## 4. Recommended distribution

* **GitHub / sideload: the default files**, `MikeShift_<ver>_x64.msix` (44.9 MB) and `_arm64.msix` (43.6 MB), "download the file for your PC". Nothing to install first; this is what the README tells people. The 88.4 MB bundle is for people who do not know their architecture.
* **Microsoft Store: the `-Flavor store` bundle** (45.9 MB; the Store installs `Microsoft.WindowsAppRuntime.2` itself and keeps it patched). It is also the best sideload file for anyone who already has the Windows App Runtime 2.5+ (23 MB), but a sideloaded install without it fails: Microsoft's `WindowsAppRuntimeInstall-x64/arm64.exe` once, or `Add-AppxPackage -DependencyPath`; the dependency is declared with `MinVersion="2.5.1.0"`. LIMITS.md and the README say so.
* The default stays the self-contained flavour because it is the only one that cannot fail to install.

## 5. Things to know

* Package builds now need the .NET 10 SDK's `dotnet run --file` (file-based apps) for `tools/ComInterfaceScan.cs`; CI's `10.0.x` SDK has it.
* A release build is a clean build of the app project (about 4 minutes per flavour and architecture), because switching `WindowsAppSDKSelfContained` in a reused `obj\` mixes manifests and file lists.
* Trimming is on only when a package is built; Debug builds, tests and the dev scene scripts are unchanged. `-p:MikeShiftTrim=false` gives an untrimmed package.
* Kept as they were: the debugger/dump DLLs (`mscordaccore`, `mscordbi`, 3.8 MB installed), `libSkiaSharp.dll` (12 MB, WebP) and the WinUI locale folders. Dropping them would be a further few MB each.
