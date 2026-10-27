# MikeShift privacy statement

Last updated: 2026-10-24. This statement applies to MikeShift 1.0.

MikeShift is a free, open-source Windows app that converts files on your PC. This page says what the app does and does not do with data. It was written against the source code and checked with the measurements described below, so you can verify it yourself.

## The short version

* MikeShift does **not** collect, send, sell or share any data about you or your files. It has **no network code**, no account, no telemetry, no analytics and no ads.
* Your files are read from, and written to, your own PC only. Originals are never changed or moved.
* The app keeps a few small files on your PC (settings and logs, below). They never leave it unless you send them yourself.
* One optional feature involves another company's program: if you turn on Microsoft Word in Settings, Word (not MikeShift) may connect to Microsoft. It is off by default.

## Who is responsible

MikeShift is published by Michael John Peña. Source code and licence (MIT): <https://github.com/mjtpena/mikeshift>. For questions about this statement, open an issue in that repository.

## What MikeShift does with your files

* When you choose a conversion, MikeShift reads the files you selected and writes a **new** file next to the original (or into the folder you chose in Settings, or into your Downloads folder if the target folder cannot be written to). It writes through a temporary hidden folder next to the result and then renames it, so a failed job leaves nothing half-written.
* It never edits, overwrites, moves or deletes the original. Converted files are named so they do not replace an existing file (`photo 2.png`).
* Files are processed on your PC. They are not uploaded, and nothing is sent to MikeShift's author or anyone else.
* If you select a OneDrive "online only" file, OneDrive downloads it first, as it would for any program that opens it. That is OneDrive, not MikeShift.

## What the app can see, and when

* **Right-click entry ("Convert with MikeShift").** Like every File Explorer menu extension, it receives the paths of the items you right-clicked. It decides what to offer from the file names' extensions without opening the files, and passes your choice to the tray app over a local named pipe that only your Windows user account can open. Its small log (`shell-log.txt`) records one line per menu with item counts, timings and errors, **not file names**.
* **Drag gesture.** To notice a Shift-drag or Ctrl-drag, MikeShift installs a Windows low-level input hook. It looks only at mouse movement, the left mouse button, the scroll wheel, and the Shift, Ctrl, Alt and Esc keys, and only to run the gesture. **No other key is read, stored or sent.** While the wheel is on screen, a small picture of the pixels under it is blurred to draw the glass effect; it stays in memory and is thrown away when the wheel hides.
* **Keyboard shortcut (default Ctrl+Alt+Shift+Space).** It is a standard Windows `RegisterHotKey` shortcut: Windows tells MikeShift only when exactly that shortcut is pressed. **Only when you press it** does MikeShift ask File Explorer (or the desktop) which files are selected. The answer is used to open the wheel for those files and is not stored. You can change the shortcut or turn it off in Settings.
* **Command line (`mikeshift`).** It runs only the files you name on the command line and prints results to your terminal.

## What MikeShift stores on your PC

All of it is in the app's own data folder, `%LOCALAPPDATA%\Packages\<MikeShift package>\LocalCache\Local\MikeShift`. **Settings → About → Open the log folder** opens it. Uninstalling the app removes it.

| What | Contents | Notes |
|---|---|---|
| Settings | Your choices: open at login, sound, message, show in folder, which key opens the wheel, the keyboard shortcut, the conversion settings, whether Microsoft Word is allowed | Stored by the app locally. "Open at login" is Windows' own startup-task switch (Settings → Apps → Startup). |
| `log.txt` | Start-up messages, counts of items, and error messages | **Never file contents.** An error message from Windows can include a file or folder path. The log is cut to its newest half when it passes 1 MB. |
| `shell-log.txt` | One line per right-click menu: item counts, timings, errors | No file names. |
| `shell-capabilities.txt`, `shell-options.txt` | Which Windows codecs this PC has (for example whether HEIC photos can be opened) and the Resize percentage | Lets the right-click entry offer only what works. No personal data. |
| Wheel hint counter | A small number counting how often the hint line has been shown | So the hints fade out as you learn the gesture. |
| In memory only | Which files MikeShift itself just made (about 20 seconds), so a quick Shift-click does not select your own outputs | Never written to disk. |

## What MikeShift sends over a network

**Nothing.** The package declares no internet capability and the code contains no networking calls. This is also measured, because a desktop app is not sandboxed by a missing capability alone: `scripts/verify-offline.ps1` runs every action on every kind of file through the command line and a tray-app session while sampling the system's network tables every 50 ms, and checks that no MikeShift process ever owns a TCP connection, a listener or a UDP endpoint. On the test PC (a 19-minute run, 14,436 samples) none did. `scripts/audit-network-imports.ps1` lists every networking API a build references. You can run both yourself ([testing](developer/testing.md#offline-verification)).

A few buttons hand a web address to **your** browser or the Microsoft Store app when you click them: **Check for updates** and **View on GitHub** (open this project's pages in your browser), and **Get HEIF**, **Get HEVC** and **Get AV1** (open the extension's Microsoft Store page). The browser or the Store then connects, under its own privacy policy. MikeShift never checks for updates by itself.

## Third parties

* **Microsoft Word (optional, off by default).** Settings → "Use Microsoft Word for Word documents", or `--use-word` on the command line for one run. When on, legacy `.doc` files and DOCX-to-PDF are handed to Word, which runs invisibly on a private copy of the file and is quit afterwards. **Word is a separate Microsoft program and is not offline**: in the offline measurement above, the Word instances MikeShift started opened HTTPS connections to Microsoft (licensing and "connected experiences"), governed by your Microsoft Office privacy settings. With the setting off, Word is never started, even if it is installed, and MikeShift's own converters are used.
* **Windows.** MikeShift uses Windows components to convert files (the Windows imaging codecs, Media Foundation, the Windows PDF renderer, and Windows text recognition to read the text in pictures and scanned PDFs, which runs on your PC). Any diagnostic data Windows itself collects is governed by your Windows privacy settings and Microsoft's policy, not by MikeShift.
* **Microsoft Store extensions** (HEIF Image Extensions, HEVC Video Extensions, AV1 Video Extension, WebP Image Extensions, Web Media Extensions) are separate programs from Microsoft that you install yourself; MikeShift only reads whether they are present.

## Children

MikeShift collects no personal information from anyone, including children, and has no accounts or online features.

## Your choices and your data

* Delete everything MikeShift stored by uninstalling it (Settings → Apps), or delete the data folder above while MikeShift is closed.
* Turn off "Open at login", the sound, the message, the keyboard shortcut and Microsoft Word in Settings.
* Nothing about you is held by the publisher, so there is nothing to request or delete there.

## Changes

If the app's data practices change (for example if a feature ever needs the network), this statement will be updated before that release, the date above will change, and the change will appear in the [changelog](../CHANGELOG.md). The history is in the project's repository.
