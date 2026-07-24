# TypeyTypey

A small Windows utility that registers a configurable global hotkey and types the current clipboard text into the active window using simulated Unicode keyboard input.

## Default behavior

- Hotkey: `Ctrl + Alt + V`
- Delay: 25 ms between characters
- Clears the clipboard after typing
- Closing the window hides it to the notification area; use the tray menu to exit
- Settings are stored at `%APPDATA%\TypeyTypey\settings.json`

## Build one executable

Install the .NET 8 SDK, then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\publish.ps1
```

The finished self-contained executable will be:

```text
bin\Release\net8.0-windows\win-x64\publish\TypeyTypey.exe
```

It does not require .NET to be installed on the destination computer.

## Notes

- Copy the password, focus the target password field, then press the configured hotkey.
- Some remote-login fields may not accept input sent too quickly. Increase the delay if characters are dropped.
- Windows blocks lower-privilege programs from injecting input into elevated programs. If the target is running as Administrator, TypeyTypey must also be run as Administrator.
- TypeyTypey does not log or store clipboard contents.
