# Changelog

All notable changes to TypeyTypey are documented here.

## 1.0.4 - 2026-07-27

Fixes two runtime defects that remained present in v1.0.3. The v1.0.3 changes were in the shipped
binary but did not produce the intended behaviour, so both are corrected here at the mechanism.

- **Global hotkeys no longer stop working once the Settings window has been opened and closed.**
  Hotkey registrations belong to a window handle, and WinForms recreates a form's handle whenever
  `ShowInTaskbar` changes — which closing Settings to the tray did every time. Both hotkeys were
  destroyed silently. Hotkeys, clipboard monitoring, IPC and typing now live on a dedicated
  application context with a permanently-lived message window, independent of any visible window.
- **The Settings window is now genuinely larger.** The v1.0.3 size increase had no visible effect
  because the process is DPI aware while the form applied no scale factor, so the requested
  dimensions were consumed as raw device pixels — a 510x610 window on a 150% display. Settings and
  the history picker now scale correctly at any display scaling.
- The application starts quietly in the notification area, as documented, instead of opening the
  Settings window on every launch.
- Added a **Theme** setting with System default, Light and Dark, applied without restarting.
  Existing settings files load unchanged and use System default.
- The command pipe is now restricted to the current user and LocalSystem. The Windows default
  granted read access to Everyone and to ANONYMOUS LOGON.
- Failure to register the Windows startup entry now reports an actionable reason instead of being
  silently swallowed.
- The displayed version is read from assembly metadata; the executable carries product, description,
  company and copyright metadata.
- Elevation is now visible: the tray tooltip reads **TypeyTypey (Administrator)** and the Settings
  window carries a confirming line at the bottom. Neither appears when running normally.
- New `--admin` command line option restarts TypeyTypey elevated through UAC for that run, without
  changing the persisted **Run as administrator** setting.
- **Choosing an entry in the Clipboard History picker no longer types it immediately.** The entry
  becomes what the type hotkey will send, and an on-screen notification confirms the choice by
  length. Pick the text first, then the destination window, then press the hotkey — and press it
  again for a second field. Copying new text, deleting the entry or clearing the history returns the
  hotkey to the live clipboard.
- Deleting an entry from the picker now asks for confirmation. History is memory-only, so a
  mistaken Delete could not be undone.
- New `--admintask` command line option registers a Windows scheduled task that starts TypeyTypey
  with administrator rights at sign-in, without a UAC prompt on every start. `--admintask system`
  registers the boot-time SYSTEM variant, `--admintask off` removes either. Creating the task turns
  **Start with Windows** off so only one copy starts.

## 1.0.3 - 2026-07-25

- Keep the Clipboard History picker above the active application instead of owning it behind the settings window.
- Increase the Settings window to a practical default size.

## 1.0.2 - 2026-07-25

- Fixed the native `SendInput` ABI layout on 64-bit Windows, which could cause all simulated typing to fail with `ERROR_INVALID_PARAMETER`.
- Added a regression test for the Win32 `INPUT` structure size and actionable, clipboard-safe typing failure messages.
- Fixed Settings window height and width

## 1.0.1 - 2026-07-25

- Added an opt-in **Run as administrator** setting that restarts the application through Windows UAC.
- Preserved the single-instance guarantee across the elevation handoff and safely reverts the setting if elevation is cancelled.

## 1.0.0 - 2026-07-24

- First public release of the native Windows clipboard typing utility.
- Added Unicode `SendInput` typing, dual global hotkeys, configurable delays, and target-window restoration.
- Added in-memory searchable clipboard history, duplicate collapse, and monitoring controls.
- Added a single-instance tray app, command-line relay, Windows startup option, settings persistence, and self-contained publishing.
- Added unit tests and GitHub Actions build/publish artifact workflow.
