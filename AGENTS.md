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

~3,200 lines of C# across 24 files. .NET 8 / WinForms, published as a self-contained single-file
`win-x64` executable. No dependency outside the BCL and xunit.

See [README.md](README.md) for user-facing behavior and CLI surface.

### Layout

Source is grouped by responsibility. The namespace stays flat (`TypeyTypey`); the folders are for
navigation, not for layering, and the `.csproj` needs no change when files move between them.

| Folder | Holds |
|---|---|
| `App/` | Entry point and application lifetime — `Program`, `TrayApplicationContext`, `AppCommand` |
| `Core/` | Non-visual state and services — settings, theme enum, history, version, single-instance, typing plan |
| `Ui/` | Windows and presentation — `SettingsForm`, `HistoryPicker`, `InfoDialog`, `ThemeManager`, `WindowPlacement`, `UiKit` |
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

Measured again 2026-07-27 on a Linux-only agent container, which narrows the reason: the
distribution's `dotnet-sdk-8.0` package (Ubuntu 8.0.129) omits
`Sdks/Microsoft.NET.Sdk.WindowsDesktop` entirely, so any `UseWindowsForms` project fails at import
with `MSB4019` before compilation starts. `-p:EnableWindowsTargeting=true` does not help — that
switch unblocks the *official* SDK, which the container could not download.

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

Where no Windows SDK is available, [`tools/linux-check/`](tools/linux-check/README.md) runs the part
of the suite that has no Windows dependency:

```bash
dotnet test tools/linux-check/LinuxPolicyCheck.csproj
```

It links the platform-free sources and their tests — `SelectionAndTaskTests.cs` and
`TypingModeTests.cs` — and stubs the rest. Read its
README before quoting a green run: it compiles no WinForms code and produces no executable, so CI on
`windows-latest` remains the only evidence for the full suite and the binary. A Claude Code on the
web session sets this up automatically through `.claude/hooks/session-start.sh`.

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
ABI; `PolicyTests.cs` covers settings, theme, hotkey bindings, version and placement policy;
`SelectionAndTaskTests.cs` covers `--admintask` parsing, the scheduled-task XML and the picker
selection rules; `TypingModeTests.cs` covers typing-mode planning, key-event ordering and the
override-hotkey ids against a fake keyboard layout. That third file deliberately touches no WinForms type, so it is the part of the
suite a non-Windows machine can still run — see §4. The
main project exposes internals via
`InternalsVisibleTo("TypeyTypey.Tests")` in `Properties/AssemblyInfo.cs`, so internal types are
directly testable without widening their visibility.

```powershell
dotnet test .\TypeyTypey.Tests\TypeyTypey.Tests.csproj   # requires the Windows .NET 8 SDK
```

Covered: `ClipboardHistory` ordering/dedup/trim/clear, `CommandLine` parsing, the Win32 `INPUT` ABI
size, `AppSettings.Normalize` clamps, theme persistence and settings-file compatibility,
`HotkeyBinding` validity and equality, version formatting, and `WindowPlacement` sizing and
monitor-clamping policy, `--admintask` parsing, scheduled-task XML and picker selection rules.
v1.0.5 adds the modifier-release decision rule, three-way hotkey validation, working-area fitting,
`--help` parsing and the product metadata About reads, and typing-mode planning, key-event
ordering, mode persistence and override-hotkey validation. 177 tests as of v1.0.5, from the Release
run on windows-latest rather than counted by hand.

`HelpCommandTests.EveryDocumentedOption_IsAcceptedByTheParser` is worth keeping. It walks
`CommandLine.Options` — the list the Help window renders — and asserts each entry actually parses,
so an option cannot be documented and simultaneously rejected on the command line.

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

### Three rules that are not negotiable

All three were learned from shipped defects and are easy to reintroduce.

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
3. **Never treat a keyboard-state read as authoritative.** `GetAsyncKeyState` returns `0` both for
   "the key is up" and for "UIPI will not let you read the foreground thread's input", and nothing
   in the return value separates them. A non-elevated process reading state while a
   higher-integrity window has focus therefore sees an idle keyboard no matter what is held. This
   is what corrupted and truncated typing through v1.0.4: the wait for the hotkey to be released
   was satisfied instantly and the characters went out as control codes. Guards over input state
   must not depend on being allowed to read it — `InputTyper.ReleaseModifiers` releases the
   hotkey's modifiers outright instead. The same trap applies to `GetKeyState` and to any future
   check of what the user is pressing.

   This is the general form of a rule the codebase already follows elsewhere:
   `TrayApplicationContext.ReadClipboard` deliberately distinguishes "no text" from "could not
   read", because collapsing them decides the wrong thing.

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

## 9. Manual validation record — v1.0.4

Acceptance testing was performed by the maintainer on 2026-07-27 against the published executable,
and recorded in issues #6 and #7. **Confirmed:** theme system (light, dark, system, live Windows
switch, high contrast); elevation, including that a chosen administrator mode persists across runs
and that the UAC prompt still has to be accepted; tray double-click and the right-click menu;
settings preserved across display scaling; the tray icon surviving the closing of every window;
picker keyboard navigation.

Two defects were raised from that pass and are fixed in v1.0.4: selecting a history entry started
the typing timer instead of arming the hotkey (#9, #7), and deleting an entry took effect with no
confirmation (#7).

### Still unverified on Windows

Everything added after that acceptance pass — the picker selection rules, the delete confirmation
and `--admintask` — has unit coverage for its decision rules and none for its runtime behaviour. It
was implemented on a Linux container that cannot build this project (§4), so no agent has seen any
of it run.

The remaining verification lives in the issue tracker rather than here, so there is one list rather
than two: **#11** (delete confirmation) and **#12** (`--admintask`). #12 is the one that matters
most — it is the only change that shells out to another process, raises a UAC prompt and writes to
Task Scheduler, and a schema-ordering defect that would have made every invocation fail was found by
review rather than by running it.

The picker selection rules (#10) are closed without runtime verification, by the maintainer's
decision. Treat them as unproven at runtime, not as confirmed.

## 9a. Manual validation record — v1.0.5

Verified by running the published executable on Windows, 2026-07-27:

- `tools/diagnostics/probe-behaviour.ps1` — all ten checks, including the two new ones covering
  `--help` opening its own window without disturbing the running instance.
- Settings measured at 615x760 logical (922x1140 device at 144 DPI).
- Help measured at 660x780 logical, clamped by `WindowPlacement.FitToWorkingArea`.
- Settings and Help inspected by screenshot in Dark theme. Two defects were found that way and
  fixed: `InfoDialog` themed itself before its content existed, leaving each card filled with the
  default window colour and showing a bright ring through the card padding; and the Help term column
  was a fixed 148 px, which clipped `--clear-history` and rendered the three `--admintask` variants
  as identical stubs. The column is now measured from the widest term.
- `probe-pipe-acl.ps1` and `probe-elevation.ps1` still pass.

### Still unverified on Windows

**The issue #13 fix itself has not been observed working, and cannot be from this environment.** The
agent shell runs elevated, so `GetAsyncKeyState` is readable and the defect does not reproduce. What
was verified is that the application builds, runs, registers three hotkeys and types; what was not
is the non-elevated path the fix exists for. That needs a non-elevated TypeyTypey typing into a
window it does not outrank, with the hotkey held down at the moment of the press.

Also unverified at runtime: the Stop typing hotkey actually cancelling a run in progress, the
five-second modifier timeout message, Light and high-contrast rendering of the new cards, and About.

## 9b. Typing Mode — decisions and limits (v1.0.5)

### Why the default is Unicode Input, not Automatic

`TypingMode.Unicode` is persisted as `0`, so a settings file written before v1.0.5 — which has no
such property — deserializes to the behaviour that build had. That is deliberate and is the whole
migration: no version check, no upgrade flag, no way for the two to disagree. New installations get
Unicode Input as well. Distinguishing "upgraded" from "new" would need a marker written by a build
that never wrote one, and the reward would be defaulting users into a mode whose runtime behaviour no
one has yet observed. `TypingModeSettingsTests` pins both the numeric value and the legacy-file case.

### Automatic mixes physical and Unicode within one run

Per character, decided in full before any event is injected. The alternative — all-physical or
all-Unicode for the whole string — was rejected because one `™` in a paragraph would push the rest
onto the path a browser console cannot read, which is the case the mode exists for. Mixing is safe
here because each character is emitted as its own complete `SendInput` batch with its own modifiers
pressed and released, so no state carries across the boundary between a physical step and a Unicode
one.

### Keyboard-layout semantics

- **Which layout:** the one belonging to the thread that owns the foreground window, read through
  `GetKeyboardLayout` after `GetWindowThreadProcessId`. Windows tracks layout per thread, so this is
  the layout that decides what a key produces at the destination.
- **When:** immediately before typing begins — after the initial delay, after focus restoration, and
  after the hotkey's modifiers are released. Not when the hotkey was pressed. The documented workflow
  is that the user presses the hotkey and *then* clicks into the destination, so a layout captured at
  hotkey time would describe the wrong window.
- **Focus changing during the delay:** handled by the above. The plan is built against wherever focus
  ended up.
- **Local vs remote layout:** unresolvable from this machine, and not claimed. A remote console set
  to a different layout will interpret the same keys differently. Physical Keypresses promises
  correct local key presses, not correct remote characters. Stated in README and RELEASE_NOTES.

### What is rejected rather than guessed

`VkKeyScanEx` shift states beyond Shift/Ctrl/Alt (Hankaku and the OEM-reserved bits), surrogates,
control characters other than tab and newline, and anything the layout has no key for. In Physical
Keypresses those refuse the whole string with a position and code point; in Automatic they fall back
to Unicode individually.

### Modifier release

`InputTyper` records what it pressed *before* the batch that presses it and clears the record only
once the batch carrying the key-ups is fully accepted, so a partial `SendInput` leaves the modifier
recorded as held. `TypePlanAsync` releases in a `finally`, and `ExitApplication` and `Dispose`
release again in case the process ends before that unwinds.

### Runtime validation status

**Confirmed by the maintainer, 2026-07-30, against the published executable:** Physical and
Automatic both type correctly into an iDRAC password field. That is the defect in #16 closed at its
primary use case, and the reason v1.0.5 was released.

Three findings came out of that pass and are documented in the Help window rather than fixed,
because each is expected behaviour rather than a defect:

- Unicode mode produced lowercase text in iDRAC — the console never receives modifier state.
- Physical mode triggered Chrome tab-group behaviour: a modifier-dependent character is
  indistinguishable from the shortcut using the same keys, and the target decides which it is.
- An elevated Windows Terminal blocks an unelevated TypeyTypey from typing into *any* Windows
  Terminal window, including apparently unelevated tabs. `conhost.exe` was unaffected. Recorded as a
  Windows Terminal integrity interaction, not a TypeyTypey defect.

**Still unobserved.** Tray submenu rendering in either theme, the Settings card, the Help window,
override hotkeys, and scheduled-task creation on the affected system. The theming fix and the
scheduler compatibility path have unit coverage for their decision rules and none for their
appearance or for Task Scheduler itself; no agent has seen either run.

- [ ] Typing Mode submenu readable in light mode, and in dark mode, including the tick beside the
      active mode and any disabled item.
- [ ] Help window shows the five operational sections and is readable in both themes.
- [ ] Unicode, Physical and Automatic each type `AaZz019!@#_-+=[]{};:'",.<>/?\|`~` correctly into
      Notepad.
- [ ] `TypeyTypey ™ café ✓ 😀`: Unicode types it whole; Physical refuses it naming position 12
      (U+2122) and types nothing; Automatic types it whole.
- [ ] Startup clipboard: text copied before launch types with the hotkey but is absent from history.
- [ ] History is empty after a restart, and after an elevation restart.
- [ ] Settings persist across a restart, and the tray tick matches the Settings dropdown.
- [ ] Unelevated TypeyTypey cannot type into an elevated window.
- [ ] `--admintask` creates the task on the system that previously rejected the XML.
- [ ] Cancellation mid-run and during a shifted character leaves no modifier down.

## 10. Escalate rather than decide

Stop and ask the maintainer when a change would:

- weaken or reinterpret any invariant in §3;
- widen the scope ceiling in CONTRIBUTING.md;
- alter the security model in SECURITY.md, the elevation flow, or the single-instance guarantee;
- add a runtime dependency; or
- change documented CLI, hotkey, or settings behavior.
