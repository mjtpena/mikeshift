# MikeShift: documentation

What MikeShift is, how to install it and how to use it: the [root README](../README.md).

## Using and trusting it

| Document | What it is |
|---|---|
| [LIMITS.md](LIMITS.md) | Everything that works differently on Windows, depends on the PC, or is refused, with the app's own wording. Read this before relying on a feature. |
| [store/privacy-policy.md](store/privacy-policy.md) | What the app does and does not do with data. |

## Project and design

| Document | What it is |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Projects, threading, drag detection, the wheel, the Explorer right-click entry, memory, packaging, with links to the Microsoft documentation for each API. |
| [PLAN.md](PLAN.md) | The original specification. Describes intent; where it disagrees with the code or LIMITS.md, those win. |
| [WORKPLAN.md](WORKPLAN.md) | Who built which part, and the rules every work package followed. |
| [ux-review.md](ux-review.md) | The interface review. |

## Evidence

| Document | What it is |
|---|---|
| [TESTPLAN.md](TESTPLAN.md) | Automated tests, the drag and context-menu harnesses, fixtures, and the manual checklist (what ran and what is still not run). |
| [test-runs/](test-runs) | Dated runs with numbers: [offline and integrity](test-runs/2026-10-24-local-and-integrity.md), [real drags](test-runs/2026-10-24-real-drag.md), [right-click entry](test-runs/2026-10-24-context-menu.md), [package size](test-runs/2026-10-24-package-size.md), [hardening](test-runs/2026-10-24-hardening.md). |
| [research/positioning.md](research/positioning.md) | The market and Windows UX study behind the README and Store wording. |

## Microsoft Store material

| Document | What it is |
|---|---|
| [store/listing.md](store/listing.md) | The Store listing, ready to paste: name, descriptions, keywords, features, category, age rating answers, system requirements, certification notes. |
| [store/privacy-policy.md](store/privacy-policy.md) | The privacy policy to publish at the URL the Store asks for. |
| [store/screenshots.md](store/screenshots.md) | Which screenshots to upload with captions, what is still missing, and how to capture it. |
| [store/demo-script.md](store/demo-script.md) | A 20-second demo video storyboard. |
| [media/store/](media/store) | Store-sized screenshots composed from the existing captures. |

## Images

[screenshots/](screenshots) holds the captures used by the README and the test runs; [media/](media) holds composed images for the Store listing.

Conventions: **built** means the code is in the tree, covered by the build and exercised as the test plan describes. When something changes, update [LIMITS.md](LIMITS.md) and the status wording in the root README.
