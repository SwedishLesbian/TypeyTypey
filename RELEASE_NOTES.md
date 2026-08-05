# TypeyTypey v1.0.5

TypeyTypey is a tiny native Windows utility for typing clipboard-derived text into applications where paste is unavailable or unreliable. Copy a password, command, URL, or other text; focus the destination; then let TypeyTypey simulate keyboard input.

## Typing Mode

Clipboard text could only ever be sent as Unicode input. Most Windows applications accept that, but a
remote console hosted in a browser — iDRAC, VNC, a KVM — usually does not. It never receives
characters: it receives DOM key events and rebuilds the character from the key's identity plus the
modifiers held at the time. A Unicode input event carries neither a virtual key nor a scan code, so a
console reading them could end up with the right letter in the wrong case.

There are now three modes, chosen from the tray icon's **Typing Mode** submenu or in Settings.

- **Unicode Input** — what every earlier version did, and still the default. Arbitrary Unicode.
- **Physical Keypresses** — real key presses with real modifiers and scan codes. `A` is Shift down, A
  down, A up, Shift up. This is the one for browser-hosted consoles.
- **Automatic** — physical where the keyboard layout allows it, Unicode for the rest, decided per
  character before anything is sent.

**Upgrading changes nothing.** A settings file from an earlier build has no typing mode in it and
loads as Unicode Input, so how your typing reaches its target is the same until you choose otherwise.

Physical Keypresses preflights the whole string. If any character cannot be produced by the current
keyboard layout it types nothing at all and names the position and code point of the first one —
rather than substituting, dropping, or approximating it. Automatic falls those characters back to
Unicode individually and still sends everything else physically.

Optionally, and off by default, a hotkey can be bound to each mode. Pressing it types once in that
mode without changing the saved setting.

### What this does not promise

Physical keypresses are mapped through the keyboard layout of the window being typed into, read after
the initial delay. That is your **local** layout. A remote console configured for a different one can
still produce different characters, and nothing measurable on this machine can predict it.

Windows accepting the keystrokes is also not evidence that the remote system displayed them.
TypeyTypey reports whether the injection was accepted locally, and nothing beyond that.

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

**The Typing Mode submenu was unreadable in dark mode.** The tray menu itself was themed, but a
submenu is a separate window with its own colours, so expanding Typing Mode gave dark text on a dark
background. Every submenu is now themed, along with two things the menu draws from fixed system
colours rather than the theme: the greyed-out text of a disabled item, and the little arrow beside a
submenu.

**`--admintask` could not create a scheduled task.** The task definition declared one version of the
Windows Task Scheduler format while using two settings introduced in a later one, so Windows rejected
the whole thing with a parser error and no task was created. Both settings are gone; their defaults
were what the task wanted anyway. If a future version of Windows objects to another optional setting
in the same way, TypeyTypey now drops that one setting and retries, and reports which it dropped —
but only for settings that cannot change what the task runs or the account it runs as. Anything else
still fails, with the error Windows gave.

## New in this release

**A Stop typing hotkey, `Ctrl+Alt+X` by default.** It cancels a run already under way and leaves
whatever was typed so far in place — useful when the text is long, the wrong window had focus, or
the destination is handling the keystrokes badly. It is configurable like the other two.

**A Help window.** It covers what TypeyTypey does, the hotkeys you currently have configured, and
every command-line option with an explanation. Open it from the tray menu or with `--help` (`-h` and
`/?` also work). Unlike the other command-line options it does not need a running instance.

**About now reports the executable's own details** — product, description, author, version and
copyright — instead of a fixed string, so it cannot drift from what the file's properties page says.

**Help now covers the things worth knowing before you hit them.** Which typing mode to reach for and
why, that physical keypresses can be read as keyboard shortcuts by the target application, that text
copied before TypeyTypey started can be typed but will not be in the history picker, that an
unelevated TypeyTypey cannot type into an elevated window, and that an elevated Windows Terminal can
block typing into any Windows Terminal window. All of it from what actually happened during testing.

**Settings has been redesigned.** Related options sit in grouped cards with plain-language captions
explaining what each one changes, and the Save bar stays in place instead of scrolling away with the
content. The About section has been removed from Settings, since Help and About now cover it.

## Highlights

- Global hotkeys for current clipboard text and searchable in-memory history
- Three typing modes — Unicode, physical keypresses, or automatic — with configurable initial and per-character delays
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
