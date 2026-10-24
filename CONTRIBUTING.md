# Contributing to MikeShift

Thank you for taking an interest. MikeShift is a small, focused app: it converts the common file types from File Explorer, offline, and tries to do that well. Bug reports, fixes, tests and documentation are all welcome.

For what the app does, read the [user guide](docs/user-guide.md). For how it is built, read the [architecture](docs/developer/architecture.md).

## Reporting problems and ideas

* **A bug or a file that will not convert:** [open an issue](https://github.com/mjtpena/mikeshift/issues) with your Windows version and architecture, what you did, the message you saw (it is plain words, so copy it), and the file type. The log (Settings → About → **Open the log folder**) never contains file contents and often says what happened. Please do not attach files you would not want public.
* **A security problem:** do not open a public issue; see [SECURITY.md](SECURITY.md).
* **A feature:** open an issue first and describe the job you are trying to do. The scope is deliberate (see [known limitations](docs/limitations.md#not-offered)): MikeShift is not a format zoo with presets, and anything that needs the network is out of scope.

## Prerequisites

* **Windows 10 version 1809 or later, or Windows 11**, x64 or ARM64. The app and its tests use Windows imaging, Media Foundation and WinUI, so they build and run on Windows only.
* **The .NET 10 SDK.**
* **The Windows 10/11 SDK 10.0.26100** (for `makeappx` and `signtool` when you build packages). Visual Studio is not required.
* **PowerShell 7** for the scripts.
* Python 3 with Pillow is needed only to recompose the Store images (`docs/media/compose-store-images.py`).

## Build and test

```powershell
dotnet build MikeShift.sln                 # everything, Debug; must stay at 0 warnings
dotnet test MikeShift.sln                  # the fast suite: Core (in-process) and CLI (child processes)
./scripts/build.ps1 -Configuration Release -Test    # the same through the helper script
```

Warnings are errors (`TreatWarningsAsErrors`), including the IL-trimming analyzers, so a clean build is part of the contract. The slow tests (Media Foundation transcodes, Word) run with `$env:MIKESHIFT_SLOW = '1'`. [Testing](docs/developer/testing.md) explains the layout of the suites, the integrity suite, offline verification, coverage and the manual checklist.

To try the tray app from a checkout:

```powershell
dotnet build src/MikeShift.App -p:Platform=x64          # or ARM64
src\MikeShift.App\bin\x64\Debug\net10.0-windows10.0.26100.0\MikeShift.exe
dotnet run --project src/MikeShift.Cli -- --list .\fixtures\sample.png      # the command line
./scripts/make-fixtures.ps1                             # sample files for manual testing -> .\fixtures
```

An unpackaged run has no right-click entry (that exists only for the installed package). Developer switches (`MIKESHIFT_*` variables, `--scene`, `--dev`) are honoured by Debug builds and unpackaged `--dev` runs only; the list is in [architecture](docs/developer/architecture.md#8-developer-switches).

## Layout

```
src/MikeShift.Core/      no UI: converters, classification, naming, the action catalog, the pure state machines
src/MikeShift.App/       the WinUI 3 tray app (assembly MikeShift.exe)
src/MikeShift.Cli/       the console tool, a thin Main around Core's CliRunner
tests/                   xUnit: Core.Tests (in-process) and Cli.Tests (runs the real executable)
tools/                   DragHarness, ContextMenuHarness, IconGen, ComInterfaceScan (developer tools, not shipped)
scripts/, packaging/     build, release, coverage, offline verification, fixtures, icons, the dev certificate
docs/                    user documentation, docs/developer for contributors, docs/store for publishing
```

## Coding conventions

* **C# with nullable reference types and implicit usings; file-scoped namespaces; warnings as errors.** Match the surrounding code. Public and internal types get a short `///` summary that says what the type is *for* and why it works the way it does.
* **Keep logic out of the UI.** Anything that can be decided without a window (what the wheel shows, wording, rules, hit-testing, the context-menu plan) lives in `MikeShift.Core` and has unit tests. `MikeShift.App` should be thin glue over it.
* **Threading rules matter** ([architecture §2](docs/developer/architecture.md#2-process-and-threading-model)): the input-hook callback does nothing but copy and enqueue, the wheel thread never converts or blocks on anything slow, and the UI thread never converts.
* **Originals are never touched.** Every converter writes through `Naming` (a hidden temporary folder, then one move into place) and never modifies, moves or deletes an input. New converters get a test that uses `OriginalsGuard`.
* **No network code.** Do not add a dependency that opens a socket, and do not add the internet capability to the manifest. `scripts/audit-network-imports.ps1` and `scripts/verify-offline.ps1` check this.
* **Trimming-safe code.** The shipped packages are IL-trimmed. Do not use reflection-based JSON, `dynamic` or `Activator.CreateInstance` on types only the trimmer can see; the settings file uses a source-generated serializer. Classic `[ComImport]` interfaces in the app and Core are trim roots; new COM code should prefer `[GeneratedComInterface]`. Details: [architecture §7](docs/developer/architecture.md#7-packaging).
* **Native calls use CsWin32.** Add the function names to the `NativeMethods.txt` next to the code that uses them.
* **Dependencies** are pinned in one place, `Directory.Packages.props`. PDFsharp stays on 6.2.4 because later versions are not MIT-licensed. When you add or update a package, update [NOTICE](NOTICE) with its licence.
* **Words people read** (toasts, dialogs, Settings, the wheel) use sentence case, plain language and Windows terminology. Failures say what happened and what to do, with "couldn't" rather than "failed". The wording lives in `MikeShift.Core/Feedback` so tests can check it.
* **Tests** are xUnit, named as sentences in snake case (`Pdf_refuses_documents_beyond_the_size_limits_before_laying_anything_out`). Fixtures are generated at test time; do not check in binary files. A test that needs a codec the PC may lack uses `[SkipIfNoCapability]`; a test that takes seconds uses `[SlowFact]` or `[SlowTheory]`.

## Changing behaviour

When a change is visible to users, update the same commit's documentation:

* [CHANGELOG.md](CHANGELOG.md) for anything a user would notice;
* the [user guide](docs/user-guide.md), [supported formats](docs/supported-formats.md) or [known limitations](docs/limitations.md) if what the app does or refuses changes;
* the [privacy statement](docs/privacy.md) if anything about data handling changes (it must be updated *before* such a release);
* the [architecture](docs/developer/architecture.md) if a design decision changes.

After editing Markdown, run `./scripts/check-docs.ps1`: it checks that every relative link, image and `#section` anchor resolves.

## UI harnesses

`tools/DragHarness` and `tools/ContextMenuHarness` start the real app and drive the real File Explorer. **They take over your mouse and keyboard while they run**: they move the pointer, press keys and open windows. Run them on a machine (or a virtual machine) you are not using, with nothing unsaved on screen, and do not touch the mouse or keyboard until they finish. How to run them, and what each scenario checks, is in [testing](docs/developer/testing.md#ui-harnesses). They are not run by CI or by `dotnet test`.

## Pull requests

* Keep a change focused; separate unrelated clean-ups.
* Build with 0 warnings and run `dotnet test MikeShift.sln` before you push; say in the description what you ran, including whether you ran the slow suite or a UI harness.
* Describe the problem the change solves, not just the diff. Commit messages are short imperative summaries (`Fix Resize rounding for odd sizes`) with a paragraph of explanation when the reason is not obvious.
* CI builds and tests on `windows-latest` and builds unsigned packages for both architectures.

## Making a release

Maintainers: [releasing](docs/developer/releasing.md) covers the two flavours, `scripts/release.ps1`, signing, the checks to run first and the Microsoft Store submission steps.

## Licence

By contributing you agree that your contributions are licensed under the [MIT licence](LICENSE), like the rest of MikeShift.
