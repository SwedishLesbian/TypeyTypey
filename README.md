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
- Quiet notification-area application with standard Windows controls
- Single-instance operation; CLI commands communicate with the running instance
- Self-contained, single-file `win-x64` executable
- Portable: no installer or administrator privileges required
- Configurable hotkeys and 1–500 history entries
- Memory-only clipboard history with duplicate collapse

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

Start TypeyTypey once. It registers its hotkeys, begins clipboard monitoring, and runs in the notification area.

| Action | Default shortcut |
| --- | --- |
| Type current clipboard text | `Ctrl+Alt+V` |
| Open clipboard history | `Ctrl+Alt+Shift+V` |

For current clipboard text: copy text, focus the destination, press `Ctrl+Alt+V`, release the modifiers, and TypeyTypey types it after the configured delay.

For history: press `Ctrl+Alt+Shift+V`, type to filter, use arrow keys to navigate, press Enter to type, Escape to close, or Delete to remove an entry. The picker closes before any typing begins and TypeyTypey restores the captured destination window.

Double-click the tray icon to open history. Its menu offers typing, history, pause/resume monitoring, history clearing, settings, About, and Exit. Closing settings minimizes to the tray.

### Command line

The executable remains a GUI tray app. These commands relay to the already-running instance without creating duplicate tray icons or hotkeys:

```text
TypeyTypey.exe --type
TypeyTypey.exe --history
TypeyTypey.exe --settings
TypeyTypey.exe --pause
TypeyTypey.exe --resume
TypeyTypey.exe --clear-history
TypeyTypey.exe --exit
```

If TypeyTypey is not running, a command starts it, performs the requested action, and leaves it running normally.

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

## Contributing

Please read [CONTRIBUTING.md](CONTRIBUTING.md) before proposing changes. The project intentionally stays small: reliability, native Windows behavior, privacy, and minimal dependencies outrank feature count.

## License

TypeyTypey is released under the [MIT License](LICENSE).
