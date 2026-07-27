# TypeyTypey v1.0.5

TypeyTypey is a tiny native Windows utility for typing clipboard-derived text into applications where paste is unavailable or unreliable. Copy a password, command, URL, or other text; focus the destination; then let TypeyTypey simulate Unicode keyboard input.

## Fixes in this release

**Typing was corrupted and cut short when TypeyTypey was not running as administrator.** Stray
modifier keypresses reached the target and the text arrived truncated. TypeyTypey waits for the
hotkey to be released before it starts typing, but that wait asks Windows which keys are held, and a
program is not allowed to read the foreground window's input state when that window has more
privilege than it does. Windows reports the refusal as "no keys are held" — the same answer it gives
when the keyboard really is clear — so the wait was satisfied instantly while Ctrl and Alt were still
down, and the characters that followed were interpreted as control codes rather than text.
TypeyTypey now releases the hotkey's own modifier keys outright before typing, which does not depend
on being allowed to read them.

Two smaller corrections came with it: waiting for the modifier keys now gives up after five seconds
and says so, rather than waiting indefinitely with no indication, and the check that two hotkeys are
not set to the same combination now covers all three.

## New in this release

**A Stop typing hotkey, `Ctrl+Alt+X` by default.** It cancels a run already under way and leaves
whatever was typed so far in place — useful when the text is long, the wrong window had focus, or
the destination is handling the keystrokes badly. It is configurable like the other two.

**A Help window.** It covers what TypeyTypey does, the hotkeys you currently have configured, and
every command-line option with an explanation. Open it from the tray menu or with `--help` (`-h` and
`/?` also work). Unlike the other command-line options it does not need a running instance.

**About now reports the executable's own details** — product, description, author, version and
copyright — instead of a fixed string, so it cannot drift from what the file's properties page says.

**Settings has been redesigned.** Related options sit in grouped cards with plain-language captions
explaining what each one changes, and the Save bar stays in place instead of scrolling away with the
content. The About section has been removed from Settings, since Help and About now cover it.

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
