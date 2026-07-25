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

- Global hotkeys for current clipboard text and clipboard history
- Searchable, keyboard-first clipboard-history picker
- Native Unicode typing via Windows `SendInput`—never clipboard paste
- Configurable initial and per-character delays
- Light, Dark, or System-default theme, applied without restarting
- Quiet notification-area application with standard Windows controls
- Correct rendering at any Windows display scaling
- Single-instance operation; CLI commands communicate with the running instance
- Self-contained, single-file `win-x64` executable
- Portable: no installer or administrator privileges required
- Configurable hotkeys and 1–500 history entries
- Memory-only clipboard history with duplicate collapse
- Optional administrator-mode restart for typing into elevated applications

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

The executable is produced at `bin\Release\net8.0-windows\win-x64\publish\TypeyTypey.exe`.

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

**Type current clipboard** reads the clipboard at the moment the shortcut is pressed, waits for the modifier keys to be released, then types the text into the window you had focused. This is the normal path for a password field, console, or legacy application that accepts keystrokes but rejects paste.

**Clipboard history** opens a searchable picker of text copied since TypeyTypey started. Type to filter, use arrow keys to navigate, press Enter to select, Escape to close, or Delete to remove the selected item. The picker closes before typing begins and TypeyTypey restores the captured destination window.

### Tray and settings

TypeyTypey runs in the notification area from the moment it starts; it does not open a window on launch. Double-click the tray icon to open clipboard history. Its menu also offers typing, pause/resume monitoring, clearing history, settings, About, and Exit. Closing the Settings window simply closes it — hotkeys, monitoring and the tray icon are unaffected. Use **Exit** to end the application and clear its in-memory history.

Settings let you change both hotkeys, typing delays, history size, monitoring, theme, startup behavior, and the optional **Run as administrator** mode. Defaults are 15 ms per character, a 500 ms initial delay, 50 history entries, and the **System default** theme, which follows the Windows light/dark app setting. Use a longer delay when a remote or legacy application drops characters.

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
| `TypeyTypey.exe --exit` | Closes the running TypeyTypey instance cleanly. | End the background app from a script or shortcut. |

If TypeyTypey is not already running, a command starts it, performs the requested action, and normally leaves it running. `--exit` is the exception: with no existing instance, it starts only long enough to exit cleanly.

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

## Contributing

Please read [CONTRIBUTING.md](CONTRIBUTING.md) before proposing changes. The project intentionally stays small: reliability, native Windows behavior, privacy, and minimal dependencies outrank feature count.

## License

TypeyTypey is released under the [MIT License](LICENSE).
