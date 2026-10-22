# Shiftvert for Windows: documentation

The project overview, screenshots, install steps, CLI usage and build commands are in the [root README](../README.md).

| Document | What it is |
|---|---|
| [PLAN.md](PLAN.md) | The original specification and build plan. Describes intent; where it disagrees with the code or with the documents below, the code and these documents win. |
| [WORKPLAN.md](WORKPLAN.md) | Who built which part, and the rules every work package followed. |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Projects, threading, drag detection, the wheel, memory behaviour, packaging, with links to the Microsoft documentation for every API involved. |
| [LIMITS.md](LIMITS.md) | Every place the Windows version differs from the macOS original, depends on the machine, or refuses something, with the app's own wording. |
| [TESTPLAN.md](TESTPLAN.md) | Automated test commands, the drag harness, the fixture script, and the manual checklist with results (what ran on 2026-10-23 and what is still *Not run*). |
| [screenshots/](screenshots) | The images used in the README: wheel (dark, light, tools page, click mode, "Save the file first"), crop editor, trim dialog. |

Conventions used in these documents: **built** means it is in the source tree, covered by the build and exercised as described in the test plan. Documents describe the tree after work packages W1 to W5a (everything in `docs/PLAN.md` is built); when something changes, update the *Status* tables and [LIMITS.md](LIMITS.md).
