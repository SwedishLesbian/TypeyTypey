# Changelog

All notable changes to TypeyTypey are documented here.

## 1.0.1 - 2026-07-25

- Added an opt-in **Run as administrator** setting that restarts the application through Windows UAC.
- Preserved the single-instance guarantee across the elevation handoff and safely reverts the setting if elevation is cancelled.

## 1.0.0 - 2026-07-24

- First public release of the native Windows clipboard typing utility.
- Added Unicode `SendInput` typing, dual global hotkeys, configurable delays, and target-window restoration.
- Added in-memory searchable clipboard history, duplicate collapse, and monitoring controls.
- Added a single-instance tray app, command-line relay, Windows startup option, settings persistence, and self-contained publishing.
- Added unit tests and GitHub Actions build/publish artifact workflow.
