# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**[AGENTS.md](AGENTS.md) is the authoritative engineering document.** It carries the authority
hierarchy, the critical invariants, the validation strategy and the delivery process, and it wins
wherever this file is thinner. Read it before changing behaviour. This file is the short orientation.

## What this is

A Windows tray utility that types clipboard-derived text as simulated Unicode keystrokes through
Win32 `SendInput`, for targets that accept keyboard input but reject `Ctrl+V` — RDP, KVM/IPMI
consoles, credential dialogs, legacy applications. ~2,400 lines of C# across 22 files, .NET 8 /
WinForms, shipped as a self-contained single-file `win-x64` executable with no dependency outside
the BCL and xunit.

## Commands

Everything requires the **Windows** .NET 8 SDK — `net8.0-windows` with `UseWindowsForms` cannot be
built on Linux (AGENTS.md §4 records why, and the one partial workaround).

```powershell
dotnet test .\TypeyTypey.Tests\TypeyTypey.Tests.csproj                     # full suite
dotnet test .\TypeyTypey.Tests\TypeyTypey.Tests.csproj --filter FullyQualifiedName~PendingSelectionTests
dotnet test .\TypeyTypey.Tests\TypeyTypey.Tests.csproj --filter "FullyQualifiedName~VersionInfoTests.Format_ReducesToThreeParts"
.\publish.ps1                                                              # Release executable -> bin\TypeyTypey.exe
.\tools\diagnostics\probe-behaviour.ps1                                    # runtime probes; run after publishing
```

From WSL, invoke `dotnet.exe` with Windows-style absolute paths (`D:\...`); a `/mnt/d/...` path will
not work. There is no lint step beyond the compiler and `.editorconfig`.

On Linux — including a Claude Code on the web container, which `.claude/hooks/session-start.sh`
prepares on startup — only the platform-free subset can run:

```bash
dotnet test tools/linux-check/LinuxPolicyCheck.csproj
```

It compiles no WinForms code and builds no executable. Read `tools/linux-check/README.md` before
citing it as evidence.

**A change is not ready until the suite passes in Release, the executable publishes, and the changed
behaviour has been exercised by hand against that published executable on Windows.** Keyboard
injection, focus restoration, hotkey registration, the UAC handoff, tray interaction and anything
visual have no automated coverage and cannot acquire any — say explicitly whether they were verified
by running the executable or not verified at all. CI (`.github/workflows/publish.yml`,
`windows-latest`) is a backstop, not the development loop.

## Architecture

Folders group by responsibility; the namespace stays flat (`TypeyTypey`) and the `.csproj` needs no
change when a file moves. `App/` — entry point and lifetime. `Core/` — non-visual state and
services. `Ui/` — windows and presentation. `Windows/` — one Win32 concern per file.

The shape worth knowing before editing:

- **`TrayApplicationContext` owns everything that must outlive a window** — tray icon, hotkeys,
  clipboard monitoring, the single-instance IPC relay, typing orchestration and picker lifetime.
  Forms are pure UI surfaces. Through v1.0.3 these services lived on the Settings form, and closing
  it silently destroyed both global hotkeys, because a `RegisterHotKey` registration belongs to an
  HWND and WinForms recreates a form's handle whenever `ShowInTaskbar` changes. `HotkeyWindow` now
  holds them on a message-only handle created once. Do not move hotkeys, monitoring or IPC onto a
  form.
- **Every form sets `AutoScaleMode` inside a `SuspendLayout`/`ResumeLayout(true)` bracket.**
  Assigning it resets `AutoScaleDimensions` to the current device DPI, so an immediate scale pass
  computes a factor of 1.0 and silently does nothing. Sizes are written in logical (96 DPI) units
  and come from `WindowPlacement`.
- **Typing source is `PendingSelection`, not the clipboard directly.** Choosing an entry in the
  history picker arms it; the type hotkey sends the armed entry when there is one and the live
  clipboard otherwise. Copying new text, deleting the entry or clearing the history disarms it.
- **One instance, preserved across elevation.** `SingleInstanceManager` holds a mutex and a named
  pipe restricted to the current user and LocalSystem; a second launch relays a command and exits.
  `--admintask` is the one option that does not relay — it administers Task Scheduler and exits
  before the handshake.
- **The `.csproj` is the only version source.** The UI reads it through `VersionInfo.Display`, and a
  test fails if a literal version string is reintroduced.

## Constraints that outrank convenience

1. **Clipboard contents never leave memory.** Never logged, persisted, transmitted, or included in
   an error message, status string, notification or exception. History is memory-only and cleared on
   exit. `PendingSelection.Describe` and `InputTyper.DescribeFailure` are the models: report a
   length or a Windows error code, never the text.
2. **Keyboard simulation only.** Never add a `Ctrl+V` fallback — it is the reason the application
   exists.
3. **No network activity, no telemetry, no new runtime dependencies.** Prefer native P/Invoke over a
   package; each one is a cost against the single-file output.
4. **Scope ceiling** (CONTRIBUTING.md): not a password manager, clipboard replacement, macro
   recorder, launcher or plugin host. Requests crossing it are declined, not designed around.
5. **Do not commit `bin/`, `obj/` or built executables.**
6. **Do not push, open a pull request, trigger Actions, merge, tag or publish without being asked
   to.** Work stops in the local repository with its evidence.

Comment the non-obvious Win32 or WinForms constraint, not the code — match the sparse density of
`Windows/InputTyper.cs` and `Ui/SettingsForm`'s constructor. UI is built in code, not designer
files. Types are `internal`; the test project sees them through `InternalsVisibleTo`.

`Windows/InputTyper.cs` contains an apparently-dead `MOUSEINPUT` struct. **It is load-bearing** —
omitting the union member makes `Marshal.SizeOf<INPUT>()` report 32 bytes instead of the 40 Win32
requires on x64, and every `SendInput` call fails with `ERROR_INVALID_PARAMETER`.
`InputTyperTests.NativeInputSize_MatchesTheWin32InputAbi` guards it.

When adding a test, verify it can fail: introduce the defect it describes, watch it catch it, then
restore. Tests pin policy, never rendered appearance.
