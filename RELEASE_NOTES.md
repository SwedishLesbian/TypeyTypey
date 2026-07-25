# TypeyTypey v1.0.4

TypeyTypey is a tiny native Windows utility for typing clipboard-derived text into applications where paste is unavailable or unreliable. Copy a password, command, URL, or other text; focus the destination; then let TypeyTypey simulate Unicode keyboard input.

## Fixes in this release

This release corrects two runtime defects that were still present in v1.0.3. Both v1.0.3 changes
were in the shipped binary, but neither produced the intended behaviour; v1.0.4 addresses the
underlying mechanisms.

**Global hotkeys stopped working after Settings had been opened and closed.** A global hotkey
registration belongs to a specific window handle. WinForms recreates a form's handle whenever
`ShowInTaskbar` changes, which is exactly what closing the Settings window to the tray did — so both
hotkeys were destroyed without any error. Hotkeys, clipboard monitoring, command-line relay and
typing now run on a dedicated application context with a permanently-lived message window, so no
visible window can take them down.

**The Settings window was not actually larger.** The process is DPI aware but the form applied no
scale factor, so its requested dimensions were consumed as raw device pixels — on a 150% display
that produced a 510x610 pixel window. Settings and the history picker now scale properly at any
display scaling.

## Also in this release

- TypeyTypey starts quietly in the notification area, as documented, rather than opening the Settings window on every launch.
- New **Theme** setting: System default, Light or Dark, applied immediately without restarting. Existing settings files load unchanged and use System default.
- The command pipe is restricted to the current user and LocalSystem. The Windows default granted read access to Everyone and to ANONYMOUS LOGON; see `SECURITY.md`.
- Windows startup registration failures now explain themselves instead of failing silently.
- The executable carries product, description, company and copyright metadata, and the displayed version comes from assembly metadata.

## Highlights

- Global hotkeys for current clipboard text and searchable in-memory history
- Native Unicode `SendInput` typing with configurable initial and per-character delays
- Keyboard-first history picker, duplicate collapse, and configurable capacity
- Quiet tray operation, single-instance protection, command-line relay, and optional Windows startup
- Optional UAC-backed administrator restart for input into elevated applications
- Self-contained single-file `win-x64` executable—no installer or .NET runtime required

## Privacy

Clipboard history is memory-only and clears on exit. TypeyTypey does not log or transmit clipboard contents, use telemetry, upload data, or connect to cloud services.

## Requirements

Windows 11 is the primary target; Windows 10 is supported where practical. The release executable is self-contained for 64-bit Windows.

## Release assets

The release includes `TypeyTypey.exe`, `README.md`, `LICENSE`, this release note, and the source code archive.
