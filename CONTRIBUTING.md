# Contributing to NvColorProfiles

Thanks for considering a contribution. This document covers everything you need to get started.

## Ways to contribute

- **Bug reports** — use the *Bug report* issue template
- **Feature requests** — use the *Feature request* issue template
- **Code changes** — see below

## Development setup

**Requirements:** the [.NET 10 SDK](https://dotnet.microsoft.com/download). Windows is required to *run* the app (it talks to NvAPI and the Windows gamma API), but the core library and the unit tests are plain `net10.0` and build/test on any OS.

```bash
git clone https://github.com/0skater0/NvColorProfiles.git
cd NvColorProfiles
dotnet build NvColorProfiles.slnx
dotnet test  NvColorProfiles.slnx
```

Run the tray app (on Windows):

```bash
dotnet run --project src/NvColorProfiles
```

`NvColorProfiles.exe --check` runs a non-destructive self-test (driver and display enumeration) and exits, which is handy for a smoke check.

### Portable build

```powershell
./build.ps1
```

Produces the self-contained single-file `artifacts/portable/NvColorProfiles.exe` (no .NET runtime needed on the target). The installer is built from that executable with [NSIS](https://nsis.sourceforge.io/) (`installer/nvcolorprofiles-setup.nsi`).

## Project structure

- `src/NvColorProfiles.Core` — headless, platform-agnostic logic: gamma math, profiles, rules, the schedule engine, config storage. No UI, no platform APIs. Most unit tests live here.
- `src/NvColorProfiles` — the Avalonia tray app and the Windows-only interop (NvAPI, gamma ramp, registry, foreground watcher, global hotkeys).

Keep the core free of UI and platform calls; Windows-specific code belongs in the app project.

## Code style

- **snake_case** for everything — types, methods, fields, namespaces.
- Comments in **English**, explaining *why* rather than *what*; keep them terse. No commented-out code, no decorative banners.
- User-facing strings are **localized**: add both German and English to the table in `src/NvColorProfiles/localization/i18n.cs` and reference the key via `{i18n:tr key}` in XAML or `i18n.t("key")` in code. Never hard-code a visible string.
- Pin new dependencies and commit the updated `packages.lock.json`.

## Pull requests

1. Fork the repo and create a branch from `main`.
2. Make your change plus a reasonable test where it makes sense.
3. Run `dotnet build` and `dotnet test` — both must pass.
4. Open the PR. Describe *what* changed and *why*, and reference any related issue with `Closes #N`.
5. Small, focused PRs are easier to review than large ones.

## Reporting bugs

See the [Reporting bugs](README.md#reporting-bugs) section of the README for the information that helps most.

## License

By contributing you agree that your work will be released under the [MIT License](LICENSE).
