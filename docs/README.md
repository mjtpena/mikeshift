# Shiftvert for Windows: documentation

The project overview, install steps, CLI usage and build commands are in the [root README](../README.md).

| Document | What it is |
|---|---|
| [PLAN.md](PLAN.md) | The original specification and build plan. Describes intent; where it disagrees with the code or with the documents below, the code and these documents win. |
| [WORKPLAN.md](WORKPLAN.md) | Who built which part, and the rules every work package followed. |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Projects, threading, drag detection design, packaging, with links to the Microsoft documentation for every API involved. |
| [LIMITS.md](LIMITS.md) | Every place the Windows version differs from the macOS original or depends on the machine. |
| [TESTPLAN.md](TESTPLAN.md) | Automated test commands, the fixture script, and the manual checklist with results (all *Not run* so far). |

Conventions used in these documents: **built** means it is in the source tree and covered by the build; **planned** means it is specified but does not exist yet. Documents were written against the tree at commit `e736eb2` plus the packaging work; when a work package lands, update the *Status* tables.
