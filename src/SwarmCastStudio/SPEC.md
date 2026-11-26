# Swarm Cast Studio — Build Specification

A Windows desktop app (C# / WPF / .NET 9) to view and edit the **opencode-swarm** agent cast:
which agents are enabled, which OpenCode model each uses, fallbacks, temperature, and the
feature gates that register optional agents. It must read the **live** model and agent
catalog from the running OpenCode Desktop app so the picker only shows models that actually exist.

## Environment facts (verified)

- Windows 10/11, .NET SDK **9.0.306**, WindowsDesktop runtime 9.0.10 installed.
- WPF template available. **No NuGet packages allowed** — built-in WPF only.
- OpenCode Desktop is running as a background service.
- Bundled CLI: `%LOCALAPPDATA%\Programs\ai.opencode.desktop\resources\opencode-cli.exe`
  (version 2.4.2). CLI supports:
  - `opencode-cli.exe api get /api/model` → `{ "location": ..., "data": [model...] }`
  - `opencode-cli.exe api get /api/agent` → `{ "location": ..., "data": [agent...] }`
  - `opencode-cli.exe api get /api/info` → `{ "version": "2.4.2", "pid": ..., "urls": [...] }`
  - `opencode-cli.exe reload` → reloads config into the running service.
- `/api/model` item shape (verified): `id`, `modelID`, `providerID`, `name`, `enabled`,
  `status`, `capabilities: { tools: bool, input: [], output: [] }`,
  `limit: { context: number, output: number, input?: number }`,
  `cost: [ { input: number, output: number, cache: { read, output } } ]`, `variants: []`.
  88 models currently.
- `/api/agent` item shape: `id`, `name`, `mode`, `hidden`, `description?`,
  `model?: { id: string, providerID: string }`.
- Swarm config files:
  - Global: `%USERPROFILE%\.config\opencode\opencode-swarm.json`
  - Project: `<project>\.opencode\opencode-swarm.json` (overrides global)
- Agent roster is fixed by the plugin. This app edits config; it does not invent agents.

## Deliverable

`src/SwarmCastStudio/` containing:
- `SwarmCastStudio.csproj` — net9.0-windows, UseWPF, WinExe, nullable, ImplicitUsings.
- Core (Coder A owns): `Models/`, `Services/`, `CONTRACT.md` implementation.
- UI (Coder B owns): `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`,
  `Themes/Dark.xaml`, `ViewModels/`, `Converters/`.
- Headless smoke modes for CI/verification (see below).
- After integration: publish folder + a launcher at repo root.

File ownership is strict. Do not edit files owned by the other coder. Do not run `dotnet build`
while the other coder is still working; the integrator builds after both finish.

## Exact contract

See `src/SwarmCastStudio/CONTRACT.md`. All types live in namespace `SwarmCastStudio`
(sub-namespaces `SwarmCastStudio.Models`, `SwarmCastStudio.Services`, `SwarmCastStudio.ViewModels`).

## Product requirements

### 1. Live OpenCode integration
- Discover the CLI in order: env `SWARMCAST_OPENCODE_CLI`; desktop path above;
  `where opencode`; `%LOCALAPPDATA%\Programs\opencode\opencode.exe`.
- Run child processes without a window, redirect stdout/stderr, UTF-8, 45s timeout,
  kill on timeout, no shell execution.
- Parse defensively (BOM/ANSI tolerant, locate first `{`/`[`).
- On failure: do not crash. Show an error banner with the exact message and a Retry button;
  synthesize model entries from model refs already present in the config so editing still works.
- Connection status shows OpenCode version, model count, registered agent count.

### 2. Config editing
- Target selector: **Global (all projects)** or **Project override** (folder picker).
- Global default path above. Project path = `<folder>\.opencode\opencode-swarm.json`.
- When Project is selected and the file is missing, seed the editor from the global file;
  saving creates the project file with the full edited cast.
- If a file exists, load it. Preserve unknown keys (root and per-agent) via `JsonNode`.
- On save: create parent dirs; back up existing file to `<name>.bak-yyyyMMdd-HHmmss`;
  write to a temp file in the same directory and atomically replace.
- `agents.<name>` fields written only when meaningful:
  - `disabled: true` when unchecked; remove `disabled` when checked.
  - `model` string when set.
  - `fallback_models` array when non-empty, otherwise remove.
  - `temperature` number (2 decimals) when set.
- Feature gates under `ui_review.enabled`, `design_docs.enabled`, `council.general.enabled`.
- Never touch `.swarm/` or `swarms` unless editing project target config.

### 3. Agent roster (26 swarm roles)
Core pipeline: architect, explorer, coder, reviewer, test_engineer, critic, sme, docs.
Critics/oversight: critic_finding_validator, critic_oversight, critic_sounding_board,
critic_drift_verifier, critic_hallucination_verifier, critic_architecture_supervisor.
Support: researcher, skill_improver, spec_writer, curator_init, curator_phase,
curator_postmortem, curator_consolidation.
Conditional: designer (gate `ui_review`), docs_design (gate `design_docs`),
council_generalist / council_skeptic / council_domain_expert (gate `council.general`).

Categories for grouping: `Core pipeline`, `Critics & oversight`, `Support & learning`,
`Conditional`.

Conditional agents cannot be enabled until their gate is on; show "Feature off" and disable
their checkbox. Toggling the gate refreshes the row.

### 4. UI (dark, intuitive, 3 columns)
- Header: app name, live status pill (green = connected, red = error), Refresh button,
  Save & Reload primary button.
- Left column (≈320 px): agent search box; grouped list of agents. Each row: checkbox,
  display name, short model ref, `LIVE` dot when the running OpenCode has that agent.
  Core rows marked subtly. Gated rows show `FEATURE OFF` when locked.
- Center column: selected agent editor.
  - Description, enable toggle, live resolved model (if any).
  - Model search box + grouped model list (group by provider: `opencode-go`, `opencode`, …).
    Each item: display name, `provider/model`, context length, `FREE` badge, `NO TOOLS` badge.
    Selecting an item sets the agent's primary model immediately.
  - Current model chip always visible even if filtered out.
  - Fallbacks: list with remove buttons, "Add selected model" button.
  - Temperature slider 0.0–2.0, value shown.
- Right column (≈320 px):
  - Connection card: CLI path, version, model count, agent count, Refresh.
  - Config target card: Global/Project radio or combo, project folder + Browse, config path.
  - Feature gates card: three checkboxes with one-line explanations.
  - Raw JSON preview button (read-only modal or expander).
- Footer: status text, last action, `Open config folder` link.
- Saving: if a core agent (architect, coder, reviewer, test_engineer) is disabled, ask for
  confirmation with the risk spelled out. After writing, call `reload`, refresh live agents,
  and show success/failure.
- Keyboard: Ctrl+S save; F5 refresh. Window min size 1180×720, default 1360×840.
- No external themes/packages. Rounded cards, subtle borders, accent `#7C5CFF`.
- All long-running work async; UI never freezes. Disable Save while busy.

### 5. Headless smoke modes (for verification)
In `Services/SmokeRunner.cs`:
- `--smoke`: connect; get models; get agents; load global config; write
  `smoke-result.json` in `AppContext.BaseDirectory` with:
  `{ ok, cliPath, version, models, agents, swarmAgents, configPath, configExists, error }`.
  Exit 0 on success, 1 on failure.
- `--smoke-write <path>`: same, plus create/load config at `<path>`, set `agents.coder.model`
  to a tool-capable model, save, reload the file, verify the value; include
  `path` and `writtenModel` in the result. Exit 0/1.
- Always write the JSON file, even on failure. Also print the JSON to stdout.
- Must not create a window in these modes.

## Build & verification

Build:
```powershell
dotnet build src/SwarmCastStudio/SwarmCastStudio.csproj -c Release
```
Smoke:
```powershell
src\SwarmCastStudio\bin\Release\net9.0-windows\SwarmCastStudio.exe --smoke
```
Publish (integrator):
```powershell
dotnet publish src/SwarmCastStudio/SwarmCastStudio.csproj -c Release -o src/SwarmCastStudio/publish
```

## Non-goals
- No multiple-swarms editor in v1.
- No prompt-file editing in v1.
- No custom/unknown agent creation.
- No direct HTTP calls to the service; use the CLI as the auth boundary.
