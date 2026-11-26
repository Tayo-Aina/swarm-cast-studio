# Swarm Cast Studio

A Windows desktop editor for the **opencode-swarm** agent cast. Pick which agents are enabled, assign each one a model, set fallbacks and temperature, and toggle the feature gates that register optional roles — all against the live OpenCode model catalog.

Dark WPF UI, no external packages, no API keys, no direct HTTP. It talks to the running OpenCode Desktop app through its bundled CLI, so the model picker only shows models that actually exist on this machine.

## What it edits

| File | Scope |
|---|---|
| `%USERPROFILE%\.config\opencode\opencode-swarm.json` | Global cast for every project |
| `<project>\.opencode\opencode-swarm.json` | Per-project override |

Unknown keys are preserved. Saves are atomic and create a timestamped backup (`opencode-swarm.json.bak-yyyyMMdd-HHmmss-fff`) before replacing the file.

## Features

- **26 swarm roles**, grouped into Core pipeline, Critics & oversight, Support & learning, and Conditional.
- **Live model library** grouped by provider (`opencode-go` first, then `opencode`, then others), with context size, FREE and NO TOOLS badges.
- **Per-agent editing**: enabled/disabled, primary model, fallback chain, temperature (0–2).
- **Explicit model apply**: filtering the model list never changes the cast. Select a row, then click **Set as primary** or double-click it.
- **Feature gates**: UI review (designer), design docs (docs_design), General Council (three council roles). Gated roles stay locked until their gate is on.
- **Live status**: OpenCode version, model count, registered agent count, and a LIVE marker on each agent the running service reports.
- **Save & Reload**: writes the selected config, runs `opencode reload`, refreshes the live catalog, and reports the result.
- **Raw JSON preview** of exactly what will be written.
- **Headless smoke modes** for verification.

## Requirements

- Windows 10/11
- OpenCode Desktop running and signed in
- `opencode-swarm` installed in OpenCode

The **self-contained** build (`publish-selfcontained`) needs no .NET runtime. The smaller framework-dependent build (`publish`) requires the .NET 9 Desktop Runtime.

The app discovers the CLI in this order:

1. `SWARMCAST_OPENCODE_CLI` environment variable
2. `%LOCALAPPDATA%\Programs\ai.opencode.desktop\resources\opencode-cli.exe`
3. `opencode` on `PATH`
4. `%LOCALAPPDATA%\Programs\opencode\opencode.exe`

## Run

From the self-contained build:

```powershell
src\SwarmCastStudio\publish-selfcontained\SwarmCastStudio.exe
```

The smaller framework-dependent build is at `src\SwarmCastStudio\publish\SwarmCastStudio.exe`.

Or double-click **Run Swarm Cast Studio.cmd** in the repository root (it prefers the self-contained build) or the **Swarm Cast Studio** desktop shortcut.

From source:

```powershell
dotnet run --project src\SwarmCastStudio\SwarmCastStudio.csproj
```

## Using it

1. Pick **Global** or **Project override** on the right.
2. Choose an agent on the left.
3. Search for a model, highlight it, then **Set as primary** (or double-click).
4. Add/remove fallbacks and adjust temperature.
5. Toggle feature gates to unlock conditional roles.
6. **Save & Reload** (Ctrl+S). The app writes the file, backs up the old one, reloads OpenCode, and refreshes the LIVE roster.

If an agent has no explicit model but OpenCode resolves one, the detail panel shows the resolved or inherited model instead of implying the agent has none.

## Build and publish

```powershell
dotnet build src\SwarmCastStudio\SwarmCastStudio.csproj -c Release
dotnet publish src\SwarmCastStudio\SwarmCastStudio.csproj -c Release -o src\SwarmCastStudio\publish
```

Self-contained single-file build (no .NET runtime required):

```powershell
dotnet publish src\SwarmCastStudio\SwarmCastStudio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o src\SwarmCastStudio\publish-selfcontained
```

Desktop shortcut (prefers the self-contained build when present):

```powershell
powershell -ExecutionPolicy Bypass -File tools\create-desktop-shortcut.ps1
```

The framework-dependent publish output needs the .NET 9 Desktop Runtime; the self-contained one does not.

## Verification

```powershell
# Live catalog + config check
src\SwarmCastStudio\bin\Release\net9.0-windows\SwarmCastStudio.exe --smoke

# Config write round-trip (never the real global config)
src\SwarmCastStudio\bin\Release\net9.0-windows\SwarmCastStudio.exe --smoke-write tests\artifacts\smoke.json
```

Each run writes `smoke-result.json` next to the executable and exits 0 on success.

## Troubleshooting

- **"No models" / service not ready** — OpenCode Desktop may still be starting or reloading. Wait a moment and press **Refresh**. The first run after a reload can briefly see a partial catalog.
- **CLI not found** — set `SWARMCAST_OPENCODE_CLI` to the full path of `opencode-cli.exe`.
- **Project override wins unexpectedly** — check `<project>\.opencode\opencode-swarm.json`; it overrides global values for that project.
- **Window appears larger than 1360×840** — the app uses DIPs; at 125% Windows scaling the physical window is 1700×1050. That is expected.

## Scope

v1 edits the default swarm cast only. It does not create custom agent roles, edit prompts, or manage multiple named `swarms` blocks; those remain in the JSON config.
