> Note (2026-10-23): the product is now called MikeShift; this plan uses its original working name.

# Work plan — how PLAN.md is being delivered

`docs/PLAN.md` is the spec. This file says who builds which part, in which order, and which files each work package owns, so packages can run in parallel without stepping on each other.

Branch: `feature/windows-port`. Each package after W1 runs in its own git worktree and commits to its own branch; the project manager merges them back.

## Ground rules for every package

- Read `docs/PLAN.md` first (the sections named in your package), then this file.
- **Only edit files your package owns.** If you truly need a change in someone else's file (e.g. a signature in a W1 contract), make the smallest possible change and list it under "Cross-package edits" in your final report.
- `dotnet build Shiftvert.sln -c Debug` and `dotnet test` must be green when you finish (tests that need a codec missing on this machine are skipped via the capability-skip attribute, not deleted).
- `TreatWarningsAsErrors` is on. Fix warnings; don't suppress them globally.
- No network at runtime. No bundled third-party executables. Originals are never modified (every converter test asserts input hashes are unchanged).
- Write outputs via `Naming` (temp file in same dir → move), never directly.
- Commit your work with a clear message when green. Do not push.
- Your final report: what was built, what's stubbed/left, test counts (passed/skipped/failed), any deviations from PLAN.md and why, cross-package edits.

## Packages

| # | Package | Depends on | Owns |
|---|---|---|---|
| W1 | Scaffold + Core foundation + CLI | — | solution, `Directory.*.props`, `.github/`, all csproj files, `src/Shiftvert.Core/{Actions,Convert/Naming*,Convert/Capabilities*,Kind*}`, catalog shared code, **all converter stub files (signatures only)**, `src/Shiftvert.Cli/`, `tests/Shiftvert.Cli.Tests/`, test infrastructure (`tests/Shiftvert.Core.Tests/Infrastructure/`), zip action + tests, `scripts/make-fixtures.ps1`, `scripts/build.ps1` |
| W2a | Images | W1 | `Convert/Images/**`, `Actions/Catalog/ImageActions.cs`, `tests/.../Images/**` |
| W2b | PDF (incl. images→PDF, merge) | W1 | `Convert/Pdf/**`, `Actions/Catalog/PdfActions.cs`, `tests/.../Pdf/**` |
| W2c | Media (video + audio, AIFF, GIF) | W1 | `Convert/Media/**`, `Actions/Catalog/{Video,Audio}Actions.cs`, `tests/.../Media/**` |
| W2d | Documents | W1 | `Convert/Documents/**`, `Actions/Catalog/DocumentActions.cs`, `tests/.../Documents/**` |
| W2e | Interaction core + input monitor | W1 | `src/Shiftvert.Core/Interaction/**`, `src/Shiftvert.App/Input/**`, `tests/.../Interaction/**` |
| W2f | App shell (tray, jobs, toast, settings, single instance, first run) | W1 | `src/Shiftvert.App/{App.xaml*,Tray,Hud,Settings,Jobs,FirstRun}/**` |
| W3 | Wheel + drag integration | W2e, W2f | `src/Shiftvert.App/Wheel/**`, wiring in `App.xaml.cs` |
| W4a | Editors (Crop, Trim) | W3 | `src/Shiftvert.App/Editors/**`, interaction wiring |
| W4b | Packaging, release, docs | W3 | `packaging/**`, `scripts/release.ps1`, `docs/{README,ARCHITECTURE,LIMITS,TESTPLAN}.md`, root `README.md` |
| W5 | Review + test pass + fixes | all | anything |

Per-area CsWin32 entries go in that area's own `NativeMethods.txt` (W1 sets the project up so every `**/NativeMethods.txt` is picked up).
