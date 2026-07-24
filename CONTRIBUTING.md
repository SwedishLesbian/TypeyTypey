# Contributing to TypeyTypey

Thanks for helping improve TypeyTypey.

## Scope

Keep changes aligned with the project’s single purpose: type clipboard-derived text reliably into Windows applications that accept keyboard input. Features that turn it into a password manager, persistent clipboard replacement, macro system, launcher, plugin host, or cloud application are out of scope.

## Development

- Target .NET 8 and WinForms on Windows.
- Preserve the privacy boundary: never log, persist, report, or transmit clipboard contents.
- Prefer native Windows APIs and minimal dependencies.
- Add or update tests for deterministic behavior, then run `dotnet test .\TypeyTypey.Tests\TypeyTypey.Tests.csproj`.
- Manually verify keyboard injection changes in ordinary and protected desktop contexts where practical.

## Pull requests

Use focused pull requests with a clear description, validation results, and any known limitations. Do not commit generated `bin/`, `obj/`, or release executables.
