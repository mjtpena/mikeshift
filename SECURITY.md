# Security policy

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Instead, open a **private GitHub security advisory** on this repository: go to the repository's **Security** tab, choose **Report a vulnerability**, and describe the problem. Only the maintainers can see it, and the report stays private until a fix is available.

Helpful details:

* the MikeShift version and Windows version (including whether it is x64 or ARM64);
* what you did, what you expected, and what happened;
* a file that triggers it, if it needs one (the advisory is private, so this is safe to share there);
* whether it needs a local account, a crafted file, or another program running.

You can expect an acknowledgement, a first assessment, and, for a confirmed problem, a fix and a note in the [changelog](CHANGELOG.md) once users can update.

## What is in scope

MikeShift runs on your PC with your permissions and processes files you choose. Reports about these are especially welcome:

* a crafted input file that crashes, hangs, exhausts memory or runs code in a converter;
* a way for another program or user to drive MikeShift (the right-click entry's named pipe, the wheel's input handling, the developer switches) or to make it write somewhere it should not;
* anything that makes MikeShift contact a network, which it is designed never to do;
* a way for a conversion to change, move or delete an original file.

## Supported versions

MikeShift has no released version yet; fixes are made on the main branch. Once there is a release, the latest release is supported.

## Designed-in protections

* No network code, and no internet capability in the package ([privacy statement](docs/privacy.md)).
* The right-click entry runs in a separate process from File Explorer, and talks to the tray app over a named pipe that only your Windows user can open; the tray app validates every path and chooses the action itself.
* Originals are never edited, moved or deleted; every result is written through a temporary file.
* Developer switches are ignored by an installed MikeShift.
