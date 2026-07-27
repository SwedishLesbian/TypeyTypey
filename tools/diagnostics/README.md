# Diagnostic probes

Runtime behaviour in this application — hotkey delivery, window sizing, elevation, IPC — cannot be
covered by unit tests. These probes measure it against the **published executable** instead, and
exist because two defects shipped in v1.0.3 that no automated test could have caught.

Run them from PowerShell on Windows. They locate the repository relative to their own path, so a
clone works without editing. Build first:

```powershell
.\publish.ps1          # produces bin\TypeyTypey.exe
.\tools\diagnostics\probe-behaviour.ps1
```

None of these read clipboard contents, history entries, or typed text. They report window topology,
handles, sizes, DPI, and security descriptors only.

## The measurement trap — read before writing another probe

**PowerShell is DPI-unaware by default, and Windows silently virtualises `GetWindowRect` results for
DPI-unaware callers.** During the v1.0.4 investigation this produced measurements off by exactly the
scale factor being investigated — a 640x420 window reported as 427x280 on a 150% display. The
conclusion drawn from those numbers was wrong in an interesting direction: it looked like windows
were mysteriously *shrinking* rather than simply never scaling up.

Every probe here calls `SetProcessDpiAwarenessContext(-4)` (`PER_MONITOR_AWARE_V2`) **before any
window query**. Do not remove it, and do not add a probe without it.

Two lesser traps, both also hit during v1.0.4:

- Use `CharSet = CharSet.Unicode` on `GetWindowText`/`GetClassName`. Declaring the `W` entry point
  without it marshals the result as ANSI and truncates every string to its first character.
- `Get-Acl` and `GetNamedSecurityInfoW` both fail with error 87 on a named pipe path. Open a handle
  with `READ_CONTROL` and query it as `SE_KERNEL_OBJECT` instead.

## Probes

### `probe-behaviour.ps1`

The v1.0.4 regression suite. Verifies, against the published executable:

- cold start opens **no** window (quiet tray start);
- the history hotkey opens the picker with Settings **never** opened;
- the hotkey still works after Settings has been opened **and closed** — this is the v1.0.3 defect,
  where closing Settings toggled `ShowInTaskbar`, recreated the form's handle, and silently
  destroyed both `RegisterHotKey` registrations;
- `--history` works over the IPC pipe;
- Settings measures 615x700 **logical** (922x1050 device pixels at 144 DPI) — v1.0.3 applied no
  scale factor and produced 510x610 device pixels;
- the process survives closing every window.

### `probe-elevation.ps1`

Enumerates the Settings window's child controls and confirms the elevation notice renders. Reports
whether the launching context is elevated, since TypeyTypey inherits it.

Note that with UAC disabled an administrator account has no split token, every process runs
elevated, and the non-elevated branch is unreachable. Enabling `EnableLUA` requires a reboot.

### `probe-pipe-acl.ps1`

Prints the command pipe's security descriptor as SDDL and expands each ACE.

Expected from v1.0.4 — full control for LocalSystem and the owning user, nothing else:

```
D:(A;;0x1f019f;;;SY)(A;;0x1f019f;;;<user SID>)
```

The Windows default this replaced, measured on v1.0.3, was:

```
D:(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;BA)(A;;FR;;;WD)(A;;FR;;;AN)
```

— read access for Everyone and ANONYMOUS LOGON. That did not permit another user to send commands,
because the server is inbound and sending requires write access, but it did let any local process
occupy the single pipe instance. See `SECURITY.md`.

### `ScalingDiag/`

A standalone WinForms harness for isolating framework behaviour away from the application. Excluded
from `TypeyTypey.csproj` compilation. Run with:

```powershell
dotnet run --project .\tools\diagnostics\ScalingDiag\ScalingDiag.csproj
```

Two experiments:

1. **Handle lifetime** — proves `Hide()` preserves a form's handle while `ShowInTaskbar = false`
   destroys and recreates it, taking any hotkey registration with it.
2. **Autoscaling** — compares construction patterns to show that assigning `AutoScaleMode` resets
   `AutoScaleDimensions` to the current device DPI, so a scale pass running immediately computes a
   factor of 1.0 and does nothing. Only a pass deferred to `ResumeLayout(true)` applies the real
   factor.

Both underpin rules in `AGENTS.md` §6. Re-run them before arguing that either rule is unnecessary.
