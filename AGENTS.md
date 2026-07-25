# TypeyTypey — Engineering Operating Environment

Adopted 2026-07-25 at v1.0.3. This file defines **how** engineering decisions are made in this
repository. It is the agent-facing entry point; it cross-references existing authoritative
documents rather than restating them.

Sections marked **[CANDIDATE]** are derived from the existing codebase and documentation but have
**not** been confirmed by the maintainer. Treat them as the working assumption and flag them when
relevant; do not represent them as approved rules.

---

## 1. What this project is

A single-purpose Windows tray utility that types clipboard-derived text as simulated Unicode
keystrokes via Win32 `SendInput`, for targets that accept keyboard input but reject `Ctrl+V`
(RDP, KVM/IPMI consoles, credential dialogs, legacy applications).

~2,000 lines of C# across 19 files. .NET 8 / WinForms, published as a self-contained single-file
`win-x64` executable. No dependency outside the BCL and xunit.

See [README.md](README.md) for user-facing behavior and CLI surface.

### Layout

Source is grouped by responsibility. The namespace stays flat (`TypeyTypey`); the folders are for
navigation, not for layering, and the `.csproj` needs no change when files move between them.

| Folder | Holds |
|---|---|
| `App/` | Entry point and application lifetime — `Program`, `TrayApplicationContext`, `AppCommand` |
| `Core/` | Non-visual state and services — settings, theme enum, history, version, single-instance |
| `Ui/` | Windows and presentation — `SettingsForm`, `HistoryPicker`, `ThemeManager`, `WindowPlacement` |
| `Windows/` | Win32 P/Invoke, one concern per file — hotkeys, input injection, clipboard, focus, privilege, startup |

`Windows/` files use Win32 field names (`dx`, `wVk`, `dwFlags`) that intentionally violate .NET
naming; `.editorconfig` suppresses IDE1006 for that folder rather than renaming the ABI.

## 2. Authority hierarchy

Highest first. Lower authority may refine higher authority; it may never silently contradict it.

1. **Privacy and scope boundaries** — [CONTRIBUTING.md](CONTRIBUTING.md) (scope ceiling, privacy
   boundary) and [SECURITY.md](SECURITY.md) (security model). These are the project's stated
   promises to its users and may not be relaxed by an implementation decision.
2. **Documented behavior** — [README.md](README.md) and [CHANGELOG.md](CHANGELOG.md). A change to
   documented behavior is a change to the record, not just to the code.
3. **This file** — engineering process and validation strategy.
4. **Source code and existing conventions** — see §6.

When authority conflicts and the conflict cannot be resolved objectively, escalate to the
maintainer rather than choosing an interpretation.

## 3. Critical invariants **[CANDIDATE]**

These are the guarantees the project's own documentation asserts. Preserve them unless the
maintainer explicitly authorizes a change.

1. **Clipboard contents never leave memory.** Never logged, persisted to disk, transmitted, or
   included in an error message, status string, or exception. History is memory-only and cleared
   on exit. Enforced by hand today — see the deliberately clipboard-safe messages in
   `InputTyper.DescribeFailure` and the catch-all in `TrayApplicationContext.StartTypingAsync`.
2. **Keyboard simulation only — never clipboard paste.** The reason the application exists.
   Do not introduce a `Ctrl+V` fallback path.
3. **Exactly one running instance,** preserved across the UAC elevation handoff
   (`App/Program.cs`, `Core/SingleInstanceManager.cs`).
4. **No network activity and no telemetry,** ever.
5. **Scope ceiling.** Not a password manager, clipboard replacement, macro recorder, launcher, or
   plugin host. See CONTRIBUTING.md — feature requests crossing this line are declined, not
   designed around.

Any change touching an invariant must identify it before implementation and state what evidence
shows it still holds.

## 4. Validation strategy

**Local-first. Validate locally; do not consume GitHub Actions minutes during implementation.**

### Environment

The development shell is RHEL 10.1 under WSL2; the .NET 8 SDK is installed on the **Windows** host
and driven from WSL via interop. The Linux side cannot build this project at all —
`net8.0-windows` with `UseWindowsForms` requires the `Microsoft.WindowsDesktop.App.Ref` targeting
pack, which ships for Windows only. Do not install `dotnet-sdk` under WSL expecting it to work.

Verified working 2026-07-25: SDK `8.0.423` at `C:\Program Files\dotnet\sdk`, invoked as
`dotnet.exe` from WSL with **Windows-style absolute paths** (`D:\TheEdge\...`). Passing a Linux
path (`/mnt/d/...`) to `dotnet.exe` will not work; translate with `wslpath -w` if needed.

```bash
dotnet.exe restore 'D:\TheEdge\KingslayerTM\TypeyTypey\TypeyTypey.Tests\TypeyTypey.Tests.csproj'
dotnet.exe test    'D:\TheEdge\KingslayerTM\TypeyTypey\TypeyTypey.Tests\TypeyTypey.Tests.csproj' --configuration Release
dotnet.exe publish 'D:\TheEdge\KingslayerTM\TypeyTypey\TypeyTypey.csproj' --configuration Release \
    --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

Or `.\publish.ps1` from PowerShell on the Windows side. Expected output:
`bin\TypeyTypey.exe` (~72 MB, self-contained). A post-publish target copies it there from the
canonical `bin\Release\net8.0-windows\win-x64\publish\` directory, which is left in place so the
CI artifact and release jobs are unaffected.

### Required validation before work is offered as ready

1. Full automated suite passes locally in Release.
2. Release executable builds locally.
3. **Manual Windows validation against that exact published executable** — not against a debug
   build, and not inferred from the tests.
4. Failures corrected locally and 1–3 repeated until clean.

### Obligations that follow

- **Runtime behavior — keyboard injection, focus restoration, hotkey registration, UAC handoff,
  tray interaction, and anything visual — has no automated coverage and cannot acquire any.**
  It is verified by running the published executable on Windows, or it is not verified. State
  which, explicitly, whenever a change touches it.
- Never describe an unrun test as passing, or infer a result from the shape of a change.
- Where a change *can* be pinned by a deterministic test, add one. See §5.

### CI

GitHub Actions, [`.github/workflows/publish.yml`](.github/workflows/publish.yml), on
`windows-latest`: restore → `dotnet test` → self-contained publish → upload artifact.
Runs on pull requests, pushes to `main`, `v*` tags, and manual dispatch. It is a backstop and a
release mechanism, **not** the primary development feedback loop.

### Remote operations require explicit maintainer instruction

Do not push a branch, open a pull request, trigger Actions, merge to `main`, create a tag, or
publish a release without being told to. Work stops in the local repository with its evidence.

## 5. Testing

`TypeyTypey.Tests/` — xunit. `ClipboardHistoryTests.cs` covers history, CLI parsing and the INPUT
ABI; `PolicyTests.cs` covers settings, theme, hotkey bindings, version and placement policy. The
main project exposes internals via
`InternalsVisibleTo("TypeyTypey.Tests")` in `Properties/AssemblyInfo.cs`, so internal types are
directly testable without widening their visibility.

```powershell
dotnet test .\TypeyTypey.Tests\TypeyTypey.Tests.csproj   # requires the Windows .NET 8 SDK
```

Covered: `ClipboardHistory` ordering/dedup/trim/clear, `CommandLine` parsing, the Win32 `INPUT` ABI
size, `AppSettings.Normalize` clamps, theme persistence and settings-file compatibility,
`HotkeyBinding` validity and equality, version formatting, and `WindowPlacement` sizing and
monitor-clamping policy. 68 tests as of v1.0.4.

**`InputTyperTests.NativeInputSize_MatchesTheWin32InputAbi` is a load-bearing regression test.**
It exists because v1.0.2 fixed a bug where omitting the unused `MOUSEINPUT` union member made
`Marshal.SizeOf<INPUT>()` report 32 bytes on x64 instead of Win32's required 40, causing every
`SendInput` call to fail with `ERROR_INVALID_PARAMETER`. The apparently-dead `MOUSEINPUT` struct
in `Windows/InputTyper.cs` **is** the fix. Do not remove it.

Tests must not claim to prove rendered layout or theme appearance. `WindowPlacementTests` pins the
sizing *policy*; whether the result looks right at a given scaling factor is manual validation.

**Verify a new test can fail.** Introduce a deliberate defect in the behaviour under test, watch the
test catch it, then restore. A green test that has never been observed failing is evidence of
nothing. This was done for the v1.0.4 additions.

## 6. Conventions

- **.NET 8, WinForms, Windows-only.** `ImplicitUsings` and `Nullable` are enabled; keep both clean.
- **Prefer native Win32 P/Invoke over new dependencies.** Every added package is a cost against the
  self-contained single-file output and the project's minimal-dependency stance.
- **One thin class per OS concern,** flat `TypeyTypey` namespace, no DI, no layering. This is
  deliberate at this size. Do not introduce architecture the file count does not justify.
- **Types are `internal`** unless a test or the entry point requires otherwise.
- **Comment the non-obvious Win32 constraint, not the code.** The existing comments in
  `Windows/InputTyper.cs`, `Windows/HotkeyWindow.cs` and `Ui/SettingsForm`'s constructor are the
  model: they explain a
  platform behavior the code alone cannot convey. Match that density — sparse, and only where earned.
- **UI is constructed in code,** not designer files. `SettingsForm.BuildLayout` is the pattern.

### Two window rules that are not negotiable

Both were learned from shipped defects and are easy to reintroduce.

1. **Application services never live on a visible window.** `TrayApplicationContext` owns lifetime,
   the tray icon, clipboard monitoring, IPC and typing; `HotkeyWindow` owns the hotkey
   registrations on a handle that is created once and never recreated. A `RegisterHotKey`
   registration belongs to an HWND and dies silently with it, and WinForms recreates a form's handle
   whenever `ShowInTaskbar` changes. Through v1.0.3 closing Settings destroyed both hotkeys.
   Do not move hotkeys, monitoring or IPC onto a form.
2. **Every form sets `AutoScaleMode` inside a `SuspendLayout`/`ResumeLayout(true)` bracket.**
   Assigning `AutoScaleMode` resets `AutoScaleDimensions` to the current device DPI, so a scale pass
   that runs immediately computes a factor of 1.0 and does nothing. Only the deferred pass applies
   the real factor. Sizes are written in logical (96 DPI) units and come from `WindowPlacement`.
   Verified by measurement: without the bracket a 615x700 logical client stayed 615x700 device
   pixels at 144 DPI; with it, 922x1050.

## 7. Delivery

Established practice, from the repository's history: work on an `agent/<topic>` branch, open a pull
request against `main`, merge after CI passes. Releases are cut by tagging `v*`, which triggers the
GitHub Release job using `RELEASE_NOTES.md` as the body.

A release updates `TypeyTypey.csproj` (`Version`, `AssemblyVersion`, `FileVersion`),
`CHANGELOG.md` and `RELEASE_NOTES.md`. The project file is the **only** version source: the UI reads
it through `VersionInfo.Display`, and `VersionInfoTests` fails if a literal is reintroduced.

Do not commit `bin/`, `obj/`, or built executables.

## 8. Open gaps

| Gap | Impact | Exit condition |
|---|---|---|
| **Invariants in §3 unconfirmed** | Recorded from documentation, not maintainer approval. | Maintainer confirms, and `[CANDIDATE]` is removed. |
| **Theme appearance unverified by automation** | Dark-mode contrast, focus indicators, selection colours and high-contrast behaviour can only be judged visually. `ListBox` selection uses the system highlight rather than a themed colour. | Maintainer confirms appearance per the v1.0.4 manual checklist; revisit owner-drawn selection only if the system highlight reads poorly on dark. |
| **Non-elevated pipe DACL not separately measured** | The v1.0.4 DACL was verified from an elevated instance. `WindowsIdentity.GetCurrent().User` returns the same SID either way, so the explicit `PipeSecurity` is elevation-independent by construction; only the *historical* v1.0.3 default claim is unmeasured for the non-elevated case. | Re-run `pipe-acl3.ps1` from a non-elevated shell if the question resurfaces. |
| **Over-the-shoulder elevation may break the CLI relay — UNVERIFIED** | If a standard user elevates by entering a *different* admin account's credentials, the elevated instance owns the pipe under that admin's SID and the original user's `TypeyTypey.exe --history` cannot write to it. Reasoned, not reproduced. Appears **pre-existing**: the prior Windows default also granted Everyone read-only, so a cross-user write would have failed identically. Not a v1.0.4 regression. | Reproduce with a second local admin account, then either document the limitation in README or widen the DACL deliberately. Do not widen it without evidence. |

### Closed in v1.0.4

- Version duplication — now sourced from assembly metadata and covered by tests.
- Named-pipe ACL exposure — measured, then restricted to owner + LocalSystem. See `SECURITY.md`.
- Bare `catch { }` around startup registration — now typed, reported, and reverted consistently.

## 9. Pending manual validation — v1.0.4

**These block tagging v1.0.4.** Everything below is visual or requires a UAC prompt, so it cannot be
automated and was not verified by the agent that implemented the change. Tick items off here as they
are confirmed; delete this section once the release is tagged.

The development machine used for v1.0.4 ran at 144 DPI (150%) with UAC disabled and a single admin
account, which is why these specific gaps exist.

The implementation is complete and committed on the `agent/runtime-ui-theme` branch, unpushed.
`git log main..HEAD` carries the reasoning for each change; that history is the authoritative record
of what was done and why, and is not restated here.

### UAC and elevation — needs UAC enabled and a reboot

With UAC off, an admin account has no split token, every process runs elevated, and no consent
prompt can be raised. `EnableLUA` requires a **reboot** to take effect.

- [ ] Non-elevated launch: tray tooltip reads `TypeyTypey`, no elevation line in Settings.
- [ ] `TypeyTypey.exe --admin` raises UAC; accepting yields tooltip `TypeyTypey (Administrator)` and the elevation line at the bottom of Settings.
- [ ] `--admin` again while elevated: balloon says it is already elevated, nothing restarts, still one process.
- [ ] Declining the UAC prompt leaves TypeyTypey running unelevated with a cancellation message, and does not exit.
- [ ] The **Run as administrator** checkbox still performs the same restart, and cancelling reverts the setting.

### Display scaling

Only 150% was measurable locally. Settings client is 615x700 logical.

- [ ] 100%, 125% and 200%: all sections fit, Save and Type buttons reachable, status text visible, Theme control uncrowded.
- [ ] Shrink below the minimum: the layout scrolls rather than clipping.
- [ ] Saved position honoured on reopen; move to a second monitor, close, disconnect it, reopen — window lands somewhere visible.

### Theme

- [ ] Light and Dark applied to Settings and the picker: search box, history list, buttons, labels, check boxes, combo boxes, numeric controls, group-box labels and borders, status text.
- [ ] Selected history entry and search selection are clearly visible in Dark. **`ListBox` selection uses the system highlight rather than a themed colour — most likely to disappoint.**
- [ ] Keyboard focus indicators visible; disabled controls distinguishable; no white-on-white or dark-on-dark.
- [ ] System default follows a live Windows light/dark switch with both windows reopened.
- [ ] Preference survives restart. High contrast remains readable.
- [ ] Tray context menu and About dialog readable in Dark.

### Typing and existing behaviour

- [ ] ASCII, punctuation, Unicode, and a long generated password into Notepad.
- [ ] An RDP password prompt and an elevated target with administrator mode on.
- [ ] Tray double-click and every tray menu item.
- [ ] Picker keys: arrows, Enter, Escape, Delete, double-click; only one picker at a time.
- [ ] History is empty after exit and restart.

### Diagnostic probes

Version controlled at [`tools/diagnostics/`](tools/diagnostics/), which has its own README covering
each probe and the measurement traps. Run after `.\publish.ps1`; each exits non-zero on failure.

```powershell
.\tools\diagnostics\probe-behaviour.ps1    # v1.0.4 runtime regression suite
.\tools\diagnostics\probe-elevation.ps1    # elevation notice matches elevation state
.\tools\diagnostics\probe-pipe-acl.ps1     # command pipe DACL is restricted
dotnet run --project .\tools\diagnostics\ScalingDiag\ScalingDiag.csproj
```

Probes must set `PER_MONITOR_AWARE_V2` before any `GetWindowRect` call. PowerShell is DPI-unaware by
default and Windows silently virtualises the results, which produced measurements off by exactly the
scale factor during the v1.0.4 investigation.

## 10. Escalate rather than decide

Stop and ask the maintainer when a change would:

- weaken or reinterpret any invariant in §3;
- widen the scope ceiling in CONTRIBUTING.md;
- alter the security model in SECURITY.md, the elevation flow, or the single-instance guarantee;
- add a runtime dependency; or
- change documented CLI, hotkey, or settings behavior.
