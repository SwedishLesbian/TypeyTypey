# TypeyTypey v1.0.2

TypeyTypey is a tiny native Windows utility for typing clipboard-derived text into applications where paste is unavailable or unreliable. Copy a password, command, URL, or other text; focus the destination; then let TypeyTypey simulate Unicode keyboard input.

## Fix in this release

This release corrects the 64-bit Win32 `SendInput` structure layout. Previous builds could have Windows reject every simulated keyboard input with `ERROR_INVALID_PARAMETER`; v1.0.2 sends the full native `INPUT` structure and reports actionable Windows error details without exposing clipboard contents.

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
