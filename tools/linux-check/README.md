# Linux policy check

A partial test run for machines that cannot build TypeyTypey.

`net8.0-windows` with `UseWindowsForms` needs the `Microsoft.NET.Sdk.WindowsDesktop` targets, which
ship only with the official Windows SDK; the distribution-packaged Linux SDK omits them entirely and
fails at import with `MSB4019` before compiling anything. So the real project, the real test project
and every form are unbuildable outside Windows.

This project links the three sources that touch no Windows UI type — `Core/PendingSelection.cs`,
`App/AppCommand.cs`, `Windows/ScheduledTaskManager.cs` — together with
`TypeyTypey.Tests/SelectionAndTaskTests.cs`, and stubs `AppSettings`, `StartupManager` and
`PrivilegeManager`. Sources are linked rather than copied, so they cannot drift.

```bash
dotnet test tools/linux-check/LinuxPolicyCheck.csproj
```

## What a green run proves

Command-line parsing for `--admintask`, the scheduled-task XML for each mode, and the picker
selection rules, all against the real source.

## What it does not prove

Everything else. It compiles no WinForms code, so a break in `TrayApplicationContext`,
`HistoryPicker`, `SettingsForm`, `Program` or any `Windows/` P/Invoke will pass unnoticed here. It
runs none of `ClipboardHistoryTests` or `PolicyTests`. It produces no executable.

**A green run here is not a validated change.** See AGENTS.md §4: the full suite on Windows, a
Release publish, and manual verification against that published executable remain the bar, and CI on
`windows-latest` is the fallback when no Windows machine is at hand.
