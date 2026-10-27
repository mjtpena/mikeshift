# MikeShift documentation

What MikeShift is and how to install it: the [project README](../README.md).

## For users

| Document | What it is |
|---|---|
| [User guide](user-guide.md) | How to use everything: first run, the three ways to convert, every action by file type, the tools, where files go, Settings, keyboard shortcuts, accessibility, the command line, uninstalling, and troubleshooting. |
| [Supported formats](supported-formats.md) | What can go in, what can come out, and what needs optional Microsoft extensions. |
| [Known limitations](limitations.md) | What works differently on Windows, depends on your PC, or is refused, with the app's own wording. |
| [Privacy statement](privacy.md) | What the app does and does not do with data. |
| [Changelog](../CHANGELOG.md) | What changed in each version. |

## For contributors

| Document | What it is |
|---|---|
| [Contributing](../CONTRIBUTING.md) | Prerequisites, build and test, conventions, how to run the UI harnesses, pull requests. |
| [Architecture](developer/architecture.md) | Projects, threading, drag detection, the wheel, conversion settings, packaging, the Explorer right-click entry. |
| [Host design](developer/host-design.md) | Whether to split a reusable host from MikeShift for sibling tools: boundary, plug-in model, packaging, cost, repo shape, and the recommendation. |
| [Testing](developer/testing.md) | The test suites, offline verification, coverage, the UI harnesses and the manual release checklist. |
| [Releasing](developer/releasing.md) | Flavours, `scripts/release.ps1`, signing, and the Microsoft Store submission steps. |
| [Security policy](../SECURITY.md) | How to report a vulnerability. |

## Microsoft Store material

| Document | What it is |
|---|---|
| [store/listing.md](store/listing.md) | The Store listing, ready to paste: name, descriptions, search terms, features, category, age-rating answers, system requirements, certification notes. |
| [store/privacy-policy.md](store/privacy-policy.md) | Points the Store's privacy-policy field at the [privacy statement](privacy.md). |
| [store/screenshots.md](store/screenshots.md) | Which screenshots to upload with captions, what is missing, and how to capture it. |
| [store/demo-script.md](store/demo-script.md) | A 20-second demo video storyboard. |
| [media/store/](media/store) | The Store-sized screenshots, composed from the pictures in [images/](images) by `media/compose-store-images.py`. |

## Images

[images/](images) holds the screenshots used by the README and the guides, named for what they show.
