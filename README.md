# Swarm Cast Studio

A Windows desktop editor for the **opencode-swarm** agent cast: choose which agents are enabled, assign each one a live OpenCode model, set fallbacks and temperature, and toggle the feature gates that register optional roles.

Built with C# / WPF / .NET 9. No third-party packages. It talks to the running OpenCode Desktop app through its bundled CLI, so the model picker only shows models that actually exist on this machine.

## Download

[Latest release](https://github.com/Tayo-Aina/swarm-cast-studio/releases/latest) — `SwarmCastStudio-win-x64.exe`, self-contained single file (no .NET runtime required). A SHA-256 checksum is attached.

## Highlights

- All **26 swarm roles**, grouped by category, with live registration markers
- Model library grouped `opencode-go` → `opencode` → others, with context size and FREE / NO TOOLS badges
- Enable/disable, primary model, fallback chain, temperature
- Feature gates for designer, design docs, and the General Council roles
- Global or per-project config targets, atomic saves, timestamped backups
- **Save & Reload** writes the config, reloads OpenCode, and re-verifies the live roster
- Explicit model application: filtering the list never changes the cast
- JSONC configs supported; malformed configs can never be overwritten

## Run

The portable self-contained build needs no .NET runtime:

```powershell
src\SwarmCastStudio\publish-selfcontained\SwarmCastStudio.exe
```

Or double-click **Run Swarm Cast Studio.cmd** in the repository root. The framework-dependent build at `src\SwarmCastStudio\publish\SwarmCastStudio.exe` requires the .NET 9 Desktop Runtime.

## Build

```powershell
dotnet build src\SwarmCastStudio\SwarmCastStudio.csproj -c Release

# Portable single-file build
powershell -ExecutionPolicy Bypass -File tools\publish-selfcontained.ps1

# Small framework-dependent build
powershell -ExecutionPolicy Bypass -File tools\publish.ps1
```

## Layout

```
src/SwarmCastStudio/   WPF app, services, view models, theme
tools/                 build, publish, shortcut, QA helpers
tests/                 QA and review reports, regression scripts
```

Full app documentation: [src/SwarmCastStudio/README.md](src/SwarmCastStudio/README.md)

## Verification

- `tests/QA-REPORT.md` — headless and UI automation results against the published build
- `tests/REVIEW.md` — independent code and safety audit
- `tests/verify-config-safety.ps1` — JSONC and malformed-config regression test

## Requirements

- Windows 10/11 (x64)
- OpenCode Desktop running and signed in
- opencode-swarm installed in OpenCode

The app discovers the desktop CLI automatically, in this order:

1. `SWARMCAST_OPENCODE_CLI` environment variable
2. `%LOCALAPPDATA%\Programs\ai.opencode.desktop\resources\opencode-cli.exe`
3. `opencode` on `PATH`
4. `%LOCALAPPDATA%\Programs\opencode\opencode.exe`

If OpenCode is installed elsewhere, set `SWARMCAST_OPENCODE_CLI` to the full path of `opencode-cli.exe`. The self-contained exe only removes the .NET runtime dependency; OpenCode Desktop is still required.
