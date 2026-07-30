# TypeyTypey

**Type clipboard text anywhere keyboard input works—even when paste doesn't.**

TypeyTypey is a tiny native Windows utility that simulates real keyboard typing from your clipboard instead of using Ctrl+V.

Perfect for:

- Remote Desktop (RDP)
- VM consoles
- KVM/IPMI consoles
- Legacy Windows applications
- Secure credential dialogs
- Password prompts that reject paste
- Terminal applications
- Any application that accepts keyboard input but blocks clipboard pasting

## Features

- Global hotkeys for current clipboard text, clipboard history, and stopping a typing run
- Searchable, keyboard-first clipboard-history picker
- Three typing modes: Unicode input, physical keypresses for remote consoles, or automatic
- Native keyboard simulation via Windows `SendInput`—never clipboard paste
- Configurable initial and per-character delays
- Light, Dark, or System-default theme, applied without restarting
- Quiet notification-area application with standard Windows controls
- Correct rendering at any Windows display scaling
- Single-instance operation; CLI commands communicate with the running instance
- Self-contained, single-file `win-x64` executable
- Portable: no installer or administrator privileges required
- Configurable hotkeys and 1–500 history entries
- Memory-only clipboard history with duplicate collapse
- History selection that arms the next hotkey press instead of typing immediately
- Optional administrator-mode restart, or a scheduled task that starts elevated at sign-in

## Privacy

TypeyTypey does **not**:

- Send clipboard contents anywhere
- Use telemetry
- Upload data
- Connect to cloud services
- Write clipboard history to disk
- Store passwords permanently

Clipboard history exists only in memory while TypeyTypey is running and is cleared when it exits. Settings are stored locally, but never include clipboard contents. Error messages also never include clipboard text.

## Installation

Download `TypeyTypey.exe` from the latest [GitHub Release](../../releases/latest), place it wherever you prefer, and run it. No installer and no .NET runtime are required for the release executable.

To build from source, clone this repository, install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and publish:

```powershell
dotnet publish .\TypeyTypey.csproj -c Release -r win-x64 --self-contained true
```

The executable is copied to `bin\TypeyTypey.exe` (also left in the canonical publish directory, `bin\Release\net8.0-windows\win-x64\publish\`).

## Usage

### Quick start

1. Start `TypeyTypey.exe` once. It registers its hotkeys, starts clipboard monitoring, and runs in the notification area.
2. Copy the text you need to enter, then click the destination field.
3. Press `Ctrl+Alt+V`, release the keys, and let TypeyTypey type the value. It uses keyboard input; it never sends Ctrl+V.

### Keyboard shortcuts

| Action | Default shortcut |
| --- | --- |
| Type current clipboard text | `Ctrl+Alt+V` |
| Open clipboard history | `Ctrl+Alt+Shift+V` |
| Stop typing | `Ctrl+Alt+X` |

Optional one-shot **typing-mode override hotkeys** can be enabled in Settings. They are unassigned and switched off by default.

**Type current clipboard** reads the clipboard at the moment the shortcut is pressed, waits for the modifier keys to be released, then types the text into the window you had focused. This is the normal path for a password field, console, or legacy application that accepts keystrokes but rejects paste. If the modifier keys are still held after five seconds, TypeyTypey says so rather than typing.

**Stop typing** cancels a run already under way, leaving whatever was typed so far in place. It is useful when the text is long, the wrong window was focused, or the destination is handling the keystrokes unexpectedly.

**Clipboard history** opens a searchable picker of text copied since TypeyTypey started. Type to filter, use arrow keys to navigate, press Enter (or double-click) to select, Escape to close, or Delete to remove the selected entry after confirming.

Selecting an entry does not type it. It makes that entry what `Ctrl+Alt+V` will type, and a notification confirms the choice by length — never by showing the text. Pick the entry, click the destination field, then press `Ctrl+Alt+V`. The selection stays active so it can be typed into several fields, until you copy something new, delete it from the history, or clear the history — after which `Ctrl+Alt+V` returns to typing the current clipboard. Copying wins even while clipboard monitoring is paused: TypeyTypey compares the clipboard against what it held when you picked, rather than relying on having watched it change.

### Typing mode

TypeyTypey can turn clipboard text into keystrokes two different ways, and which one works depends on what is receiving them.

| Mode | What it sends | Use it for |
| --- | --- | --- |
| **Unicode Input** | A Unicode character event per character. | Most Windows applications. Supports arbitrary Unicode—emoji, accents, non-Latin scripts. This is the default and the behaviour of every earlier version. |
| **Physical Keypresses** | Real virtual-key presses with real modifiers and scan codes, as a keyboard would send them: `A` is Shift down, A down, A up, Shift up. | iDRAC, VNC, KVM and other remote consoles hosted in a browser. |
| **Automatic** | Physical keypresses for the characters the keyboard layout can produce, Unicode for the rest, decided before typing starts. | Mixed text you want to reach a console without giving up on the characters it cannot type physically. |

Pick a mode from the tray icon's **Typing Mode** submenu or in Settings; both change the same saved setting. Upgrading from an earlier version keeps Unicode Input, so nothing about your existing setup changes until you choose otherwise.

**Why Physical Keypresses exists.** A browser-hosted console does not receive characters. It receives DOM key events and reconstructs the character from the key's identity plus the modifiers held at the time. A Unicode input event carries no virtual key and no scan code, so a console reading them can end up with the right letter in the wrong case, or nothing at all. Physical keypresses give it the key and the Shift it expects.

**Keyboard layouts.** Physical keypresses are mapped through a Windows keyboard layout, read from the window you are typing into and captured after the initial delay—so moving focus during that delay works as intended. Two limits follow. TypeyTypey maps against your **local** layout; a remote console configured for a different one will interpret the same keys differently, and nothing measurable on this machine can predict that. And characters the local layout has no key for cannot be sent physically at all.

In **Physical Keypresses**, text containing such a character is refused outright: TypeyTypey types nothing and names the position and code point of the first character it cannot produce. It will not substitute, drop, or approximate one. In **Automatic**, those characters fall back to Unicode input individually and everything else is still sent physically.

Windows accepting the keystrokes is not evidence that the remote system displayed them. TypeyTypey reports only whether the injection was accepted locally.

#### Typing-mode override hotkeys

Off by default. When enabled, you can bind a hotkey to each mode; pressing it types once in that mode without changing the saved Typing Mode. Any of them may be left unassigned. They use the same clipboard, history selection and startup delay as the normal type hotkey, and the mode is decided by which hotkey you pressed—not by whatever window had focus when you pressed it.

### Tray and settings

TypeyTypey runs in the notification area from the moment it starts; it does not open a window on launch. Double-click the tray icon to open clipboard history. Its menu also offers typing, the **Typing Mode** submenu with a tick beside the active mode, pause/resume monitoring, clearing history, settings, **Help**, About, and Exit. Closing the Settings window simply closes it — hotkeys, monitoring and the tray icon are unaffected. Use **Exit** to end the application and clear its in-memory history.

**Help** explains what TypeyTypey does, lists the hotkeys you currently have configured, and documents every command-line option. It is also available as `TypeyTypey.exe --help`, which works whether or not an instance is already running. **About** shows the product details recorded in the executable, along with the version.

Settings let you change all three hotkeys, the typing mode and its optional override hotkeys, typing delays, history size, monitoring, theme, startup behavior, and the optional **Run as administrator** mode. Defaults are 15 ms per character, a 500 ms initial delay, 50 history entries, and the **System default** theme, which follows the Windows light/dark app setting. Use a longer delay when a remote or legacy application drops characters.

### Command line

The executable remains a GUI tray application. Command-line options are a convenient way to ask the already-running instance to act; they do not create another tray icon or duplicate hotkeys.

Run them from PowerShell in the folder containing the executable, for example:

```powershell
.\TypeyTypey.exe --history
```

| Command | What it does | Useful when |
| --- | --- | --- |
| `TypeyTypey.exe --type` | Reads the current clipboard and starts the same typing flow as `Ctrl+Alt+V`. | Trigger typing from a shortcut, script, or launcher. |
| `TypeyTypey.exe --history` | Opens the searchable history picker. | Reuse a recently copied command or password without reaching for the tray icon. |
| `TypeyTypey.exe --settings` | Brings the settings window to the foreground. | Change hotkeys, delays, history limits, or startup options. |
| `TypeyTypey.exe --pause` | Stops adding new clipboard values to TypeyTypey history. Existing history remains available. | Temporarily copy sensitive or irrelevant values without recording them in the app’s memory. |
| `TypeyTypey.exe --resume` | Turns clipboard monitoring back on. | Resume collecting new text after a pause. |
| `TypeyTypey.exe --clear-history` | Clears TypeyTypey’s in-memory history only. It does not clear the Windows clipboard. | Remove copied values from the picker immediately. |
| `TypeyTypey.exe --help` | Opens the Help window. `-h` and `/?` do the same. Unlike the rest, this does not talk to the running instance — it opens, and closing it exits. | Look up a command or a configured hotkey without starting the application. |
| `TypeyTypey.exe --admin` | Restarts TypeyTypey elevated through a standard UAC prompt. Affects this run only; the saved **Run as administrator** setting is unchanged. | Type into an elevated application once, without making elevation permanent. |
| `TypeyTypey.exe --admintask` | Creates a Windows scheduled task that starts TypeyTypey with administrator rights when you sign in — with no UAC prompt. Prompts for elevation once, to create the task. | Type into elevated applications every day without approving UAC at each start. |
| `TypeyTypey.exe --admintask system` | Creates the task to run at boot as the SYSTEM account instead. | Rarely useful — see the warning below. |
| `TypeyTypey.exe --admintask off` | Removes the scheduled task. | Undo either of the above. |
| `TypeyTypey.exe --exit` | Closes the running TypeyTypey instance cleanly. | End the background app from a script or shortcut. |

If TypeyTypey is not already running, a command starts it, performs the requested action, and normally leaves it running. `--exit` is the exception: with no existing instance, it starts only long enough to exit cleanly.

`--admin` is handled by whichever instance owns the single-instance lock, so it works whether or not TypeyTypey is already running. If it is running, that instance elevates itself and restarts; declining the UAC prompt leaves it running normally. If it is already elevated, it says so and does nothing.

### Starting elevated automatically

`--admintask` is the exception to the rule above: it does not talk to the running instance. It registers a Windows scheduled task named **TypeyTypey**, raising one UAC prompt to do so, then exits. Any instance already running is left alone.

The default task starts TypeyTypey at sign-in, as your own account, with the highest privileges available — the equivalent of always answering yes to UAC, but only for TypeyTypey. It is created for whichever account runs the command, so run it from the account that will use it. Because the task replaces the ordinary startup entry, `--admintask` also turns **Start with Windows** off; otherwise two copies would start and the second would simply open Settings.

`--admintask system` instead runs TypeyTypey at boot under the SYSTEM account. **SYSTEM starts in session 0, which has no desktop**: the tray icon does not appear, the hotkeys do not reach your session, and typing cannot be delivered to your applications. It exists for the specific case where something else drives TypeyTypey in that context; it is not a way to start TypeyTypey for daily use.

Both forms are removed with `--admintask off`. Neither changes the **Run as administrator** setting, which remains the way to elevate a session on demand.

## Building

Use either the direct publish command above or the included PowerShell script:

```powershell
.\publish.ps1
```

Run the unit tests on Windows with:

```powershell
dotnet test .\TypeyTypey.Tests\TypeyTypey.Tests.csproj
```

GitHub Actions builds, tests, and publishes `TypeyTypey.exe` as a workflow artifact on pull requests, main-branch pushes, and manual runs.

## Security

TypeyTypey types clipboard-derived contents exactly as simulated keyboard input. It is **not** a password manager, clipboard replacement, macro recorder, or automation framework.

Use Bitwarden, KeePass, 1Password, or another dedicated secret manager to store credentials. TypeyTypey simply provides a dependable way to type a copied value into applications that reject paste.

Windows does not let a non-elevated process send input to an elevated application. Run TypeyTypey at the same elevation as the target if required. Some secure desktop prompts intentionally block simulated input altogether.

The **Run as administrator** setting requests a standard Windows UAC elevation and restarts TypeyTypey as the sole elevated instance. It is off by default; declining UAC leaves TypeyTypey running normally and turns the setting back off.

When TypeyTypey is running elevated it says so in two places: the tray tooltip reads **TypeyTypey (Administrator)**, and a line at the bottom of the Settings window confirms it. Neither appears when running normally, so an unexpected absence means elevation did not take effect.

## Contributing

Please read [CONTRIBUTING.md](CONTRIBUTING.md) before proposing changes. The project intentionally stays small: reliability, native Windows behavior, privacy, and minimal dependencies outrank feature count.

## License

TypeyTypey is released under the [MIT License](LICENSE).
