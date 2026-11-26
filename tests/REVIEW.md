# Swarm Cast Studio — Re-audit After Fixes

- Re-audit date: 2026-11-26
- Scope: current `src/SwarmCastStudio/` source, `src/SwarmCastStudio/publish/`, repo-root launcher, `tools/publish.ps1`, README, and current smoke/config-safety evidence.
- Method: source inspection; published `--smoke` execution; `tests/verify-config-safety.ps1` using the published executable; targeted three-save backup test using the published executable; read-only review of the real global config. No application source, config, test script, README, or live swarm config was edited. The only file written by this review is this `tests/REVIEW.md`; test runs wrote their expected artifacts under `tests/artifacts/` and the published smoke result.
- Result: **PASS — Ship: YES.**

## Verdict

**Ship: YES.**

The three High findings are resolved and verified. All seven previously reported Medium findings are resolved or have a documented, accepted Low residual. There are no unresolved Critical or High findings.

Top residual risks, all Low/accepted for v1:

1. `README.md:14` describes the backup suffix as `yyyyMMdd-HHmmss`; the implementation now adds `-fff` plus a collision suffix. Documentation-only drift; the actual backups are safer.
2. A successful file write followed by a failed OpenCode reload leaves the conservative dirty flag set, so the user can receive an extra “Discard unsaved changes?” prompt even though the file is already written. No data-loss path was found.
3. `SmokeRunner` still constructs `OpenCodeBridge` synchronously on the smoke path (`Services/SmokeRunner.cs:77`). Smoke mode has no window; the interactive UI path now constructs the bridge on the thread pool.
4. `publish/smoke-result.json` is a generated smoke artifact. It is intentionally present as the published-smoke acceptance evidence and is not a test binary or source file.

## Resolution Table

| ID | Prior severity | Status | Evidence and verification |
|---|---|---|---|
| H-1 | High | **RESOLVED** | `Services/SwarmConfig.cs:29-57` now calls `JsonNode.Parse(json, ..., new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })` once and rejects only a non-object root at `:47-50`. `tests/verify-config-safety.ps1` passed all checks: JSONC `--smoke-write` exit 0, result `ok=True`, `unknown_root_key=preserve-me`, `unknown_agent_key=42`, `swarms.demo.name=Demo`, coder model written. The saved `tests/artifacts/jsonc-config.json` contains no comments (intentional `JsonNode` round-trip) while all values and unknown keys are preserved. Malformed input is rejected. |
| H-2 | High | **RESOLVED** | `ViewModels/MainViewModel.cs:37-38` adds `_configUsable`/`_configLoadFailed`; `:279-283` exposes them; `:540-559` sets a persistent load failure; `ApplyFallbackConfig` at `:679-695` sets `_configService = null` and `SetConfigUsable(false)`; `SetConfigLoadFailure` at `:1908-1917` again nulls `_configService` and disables usability. `SaveCommand` requires `IsConfigUsable && _configService is not null` (`:90-92`), and `SaveAsync` independently rejects unusable/failed/null-service states (`:1575-1589`). All three `LoadConfigForTargetAsync` call sites (`:540-559`, `:803-853`, `:973-996`) catch and call the failure path; target/browse changes also clear `_configService` on error. The malformed-config safety test confirms exit 1 and a byte-identical file after the attempt. No path was found that can write a minimal config after a failed load. |
| H-3 | High | **RESOLVED** | `src/SwarmCastStudio/publish/SwarmCastStudio.exe` exists; `Run Swarm Cast Studio.cmd` exists at the repo root and points to it; `tools/publish.ps1` exists and validates the produced executable; README lines 44-50 document both the publish path and launcher. The published `--smoke` run exited 0 and `publish/smoke-result.json` is `ok=true` with 88 models, 28 agents, 26 roles, 21 live swarm agents. Published DLL hash matched the current Release DLL. |
| M-1 | Medium | **RESOLVED** | `IsEditingEnabled` is `IsIdle && IsConfigUsable` (`MainViewModel.cs:283`) and is raised when busy or usable state changes (`:269-273`, `:1883-1893`). It disables the agent list, the entire center editor (model library, fallbacks, temperature), and all three feature-gate checkboxes (`MainWindow.xaml:331`, `:420`, `:1069-1111`). Save captures `_editVersion` before writing and rechecks it after reload (`MainViewModel.cs:1601`, `:1646-1696`), so late edits are also represented as unsaved. |
| M-2 | Medium | **RESOLVED** | `IsDirty` is maintained at `MainViewModel.cs:1860-1881`; target changes prompt at `:785-789`; folder changes prompt at `:881-884`; `ConfirmClose`/`ConfirmDiscardUnsavedChanges` are at `:1919-1960`; `MainWindow.xaml.cs:39-44` wires `Window_Closing`. The core dirty tracking and prompt behavior is complete; the only residual is accepted Low L-14 (conservative dirty flag after a successful write plus a failed OpenCode reload, causing an extra prompt but no data loss). |
| M-3 | Medium | **RESOLVED** | `Services/SwarmConfigService.cs:58-70` now uses `yyyyMMdd-HHmmss-fff`, a collision suffix loop, and `File.Copy(..., overwrite: false)`. A targeted three-save test with the published executable produced two distinct backups for the second and third saves: `backup-collision.json.bak-20261126-193437-902` and `backup-collision.json.bak-20261126-193437-916`. The older `run-smoke.ps1` one-backup result is expected because its first save was a fresh file and therefore had nothing to back up. |
| M-4 | Medium | **RESOLVED** | `MainViewModel.cs:1080-1143` retries a suspect/empty catalog once, emits a `_catalogWarning` when models are empty or agents are only built-ins, sets `_liveModelCount = 0`, and calls `SynthesizeModelsFromConfig(replaceExisting: true)`; `SynthesizeModelsFromConfig` is at `:1249-1301`. The warning participates in `HasError` (`:338-343`), so the connection card/banner shows the incomplete catalog rather than silently displaying an empty picker. |
| M-5 | Medium | **RESOLVED** | The bridge is no longer constructed in the VM field initializer. `MainViewModel.cs:1018-1040` creates it on `Task.Run`; `:971` clears it for Retry; `:1042-1051` uses the re-created bridge; `RetryAsync` at `:959-1016` recreates it. Interactive startup no longer runs `where.exe` on the UI thread. Residual: smoke-mode construction is still synchronous, noted above. |
| M-6 | Medium | **RESOLVED** | `RetryCommand` is bound to the error banner (`MainWindow.xaml:241-249`). `RetryAsync` reloads the failed target config when `ConfigLoadFailed` (`MainViewModel.cs:973-996`), clears/recreates the bridge at `:971`, and refreshes with `recreateBridge: true` at `:999`. A repeated config failure leaves Save disabled and the error persistent. |
| M-7 | Medium | **RESOLVED** | `OpenCodeBridge.cs:248-249` now computes `IsFree = tiers.Count > 0 && tiers.All(...)`; missing, null, or empty cost data is no longer labeled FREE. |

## Explicit Acceptance Checks

### H-1 — JSONC parsing

- **One parse of original JSON:** Yes. `SwarmConfig.FromJson` passes the original `json` string to `JsonNode.Parse` with `CommentHandling.Skip` and `AllowTrailingCommas=true` (`Services/SwarmConfig.cs:38-45`).
- **Non-object root rejection only:** Yes. The root check is at `Services/SwarmConfig.cs:47-50`; malformed JSON is still wrapped as `InvalidDataException` at `:54-57`.
- **Config-safety test:** `tests/verify-config-safety.ps1` executed against `src\SwarmCastStudio\publish\SwarmCastStudio.exe` and passed all checks:

  ```text
  JSONC smoke-write exit 0             PASS
  JSONC smoke result ok                PASS
  network root key preserved           PASS
  unknown agent key preserved          PASS
  swarms block preserved               PASS
  coder model written                  PASS
  malformed config exits non-zero      PASS
  malformed config result not ok       PASS
  malformed config not overwritten     PASS

  CONFIG SAFETY: PASS
  ```

- **Comments intentionally dropped:** Confirmed. The saved `tests/artifacts/jsonc-config.json` contains no `//` comments; `$schema`, `unknown_root_key`, `agents.coder.unknown_agent_key`, `fallback_models`, and `swarms.demo` remain. This is the intended `JsonNode` value/key round-trip behavior.

### H-2 — Failed-load overwrite protection

- **Flags and state:** `_configUsable` and `_configLoadFailed` exist (`ViewModels/MainViewModel.cs:37-38`) and are exposed through `IsConfigUsable`/`ConfigLoadFailed` (`:279-283`).
- **Failed load nulls the service:** `ApplyFallbackConfig` sets `_configService = null` (`:682`) and `SetConfigUsable(false)` (`:693`); `SetConfigLoadFailure` sets `_configService = null` again (`:1912`) and disables use (`:1913`).
- **Save blocked:** `SaveCommand` requires usable state and non-null service (`:90-92`); `SaveAsync` repeats the guard and specifically checks `ConfigLoadFailed` (`:1582-1589`).
- **Persistent error:** `_configLoadError` is included in `HasError` and `ErrorMessage` (`:338-375`); `SetConfigLoadSuccess` is the normal clear path (`:1896-1906`), while `ClearAllErrors` is used only at initialization (`:2013-2021`). `RefreshAsync` does not clear it.
- **Retry:** `RetryAsync` reloads the config and recreates the bridge (`:959-1016`); a failure calls the same persistent failure path (`:986-996`).
- **Reachability audit:** Every `LoadConfigForTargetAsync` call is inside a catch that disables persistence; `ChangeTargetSafeAsync`, `BrowseFolderAsync`, `LoadTargetConfigWithBusyAsync`, `RetryAsync`, and `InitializeAsync` all route failures through `SetConfigLoadFailure`/`ApplyFallbackConfig`. There is no discovered path from a failed load to `SaveAsync` with `_configService != null`.
- **Malformed-file test:** The malformed config hash was unchanged after `--smoke-write` returned non-zero (`tests/verify-config-safety.ps1:38-53`).

### H-3 — Published deliverable

- `src/SwarmCastStudio/publish/SwarmCastStudio.exe` exists and is the current build.
- `Run Swarm Cast Studio.cmd` exists at the repository root and launches the published executable (`Run Swarm Cast Studio.cmd:1-10`).
- `tools/publish.ps1` exists, runs `dotnet publish`, and verifies the output executable (`tools/publish.ps1:1-18`).
- README documents the published path (`README.md:44-50`), the publish command (`:69-74`), and the framework-dependent .NET 9 Desktop Runtime requirement (`:76`).
- Published smoke executed via `Start-Process ... --smoke` returned exit code **0**; `src/SwarmCastStudio/publish/smoke-result.json` is `ok=true`, 88 models, 28 agents, 26 roles, 21 live swarm agents, global config path, `error=null`.
- `publish/SwarmCastStudio.dll` SHA-256 matched `bin/Release/net9.0-windows/SwarmCastStudio.dll` at audit time, confirming the publish output reflects the current source build.

### Medium-item re-check

| Item | Status | Evidence |
|---|---|---|
| Edits blocked during Save/Reload | **RESOLVED** | `IsEditingEnabled` bindings on the list, center editor, and gates; `_editVersion` re-check after reload (`MainViewModel.cs:283`, `:1601`, `:1646-1696`; `MainWindow.xaml:331`, `:420`, `:1069-1111`). |
| Dirty prompt on target/folder switch and close | **RESOLVED** | `ConfirmDiscardUnsavedChanges` at target (`:785`), browse (`:881`), and close (`:1919-1960`); `Window_Closing` at `MainWindow.xaml.cs:39-44`. |
| Backup collision | **RESOLVED** | Millisecond suffix + uniqueness loop + no-overwrite copy (`SwarmConfigService.cs:58-70`); targeted three-save test produced two distinct backups. |
| Empty catalog synthesizes config models and warns | **RESOLVED** | Warning + synthesize at `MainViewModel.cs:1114-1143`, `:1249-1301`; warning is part of `HasError` (`:338-343`). |
| Retry recreates bridge and reloads config | **RESOLVED** | `MainViewModel.cs:959-1016`, `:1018-1040`. |
| IsFree requires cost data | **RESOLVED** | `OpenCodeBridge.cs:248-249`. |

## Remaining Low-Risk Items (Accepted)

These are unchanged or newly observed Low items and are explicitly accepted for v1; none blocks the PASS.

| ID | Severity | Status | Evidence / rationale |
|---|---|---|---|
| L-1 | Low | ACCEPTED | `OpenCodeBridge.cs:225-229` still falls back from an empty `modelID` to `name` rather than trying `id`. Current live data supplies both; latent parser hardening only. |
| L-2 | Low | ACCEPTED | `OpenCodeBridge.cs:258-259` parses `status`/`enabled`, but the picker does not filter disabled/inactive models. No such models were returned by the current live catalog. |
| L-3 | Low | ACCEPTED | `where.exe` and `explorer.exe` are resolved through PATH/ShellExecute rather than absolute system paths (`OpenCodeBridge.cs:555-571`, `MainViewModel.cs:1835-1838`). The main CLI path uses no shell and `ArgumentList`; this is local hardening only. |
| L-4 | Low | ACCEPTED | `JsonText` still has an O(n²) brace scan and no input-size cap (`JsonText.cs:47-72`, `:74-131`), used for CLI output rather than config. Local trusted CLI boundary. |
| L-5 | Low | ACCEPTED | `App.OnStartup` catches a catastrophic smoke exception without writing a result (`App.xaml.cs:17-25`). Normal `SmokeRunner` failures write the result; only a pre-run failure is affected. |
| L-6 | Low | ACCEPTED | `publish/smoke-result.json` and `bin/.../smoke-result.json` are generated smoke artifacts. The published one is retained as required acceptance evidence; no test binaries or source are present in `publish/`. |
| L-7 | Low | ACCEPTED | Default 1360×840 and 1180×720 minimum can be larger than small/high-DPI screens (`MainWindow.xaml:8-12`; README notes DPI scaling at `:95`). Specified sizes and layout math are correct; environment fit is a UI limitation. |
| L-8 | Low | ACCEPTED | `SwarmConfigService.DefaultGlobalPath` falls back to the app base directory if no user profile can be resolved (`SwarmConfigService.cs:99-115`). This path is effectively unreachable on a normal Windows profile. |
| L-9 | Low | ACCEPTED | Settings writes remain non-atomic (`AppSettings.cs:101-115`); settings are convenience data and load falls back to defaults. |
| L-10 | Low | ACCEPTED | `SetAgent` does not trim its name while `GetAgentObject` does (`SwarmConfig.cs:161-165`, `:267`); current callers pass catalog names. |
| L-11 | Low | ACCEPTED | `SPEC.md:102` still says selecting a model applies immediately; current explicit-action behavior is correct and documented in README. |
| L-12 | Low | ACCEPTED | Live-status wording can say “session-selected model” when a live agent has no resolved model but has a configured model (`AgentRowViewModel.cs:175-177`). The inherited-model wording itself is correct. |
| L-13 | Low | ACCEPTED | README backup example (`README.md:14`) does not include the new `-fff`/collision suffix produced by `SwarmConfigService.cs:60-67`. Documentation-only drift; no source edits were allowed in this re-audit. |
| L-14 | Low | ACCEPTED | After a successful write followed by a reload failure, `_isDirty` remains true (conservative; `MainViewModel.cs:1648-1656`). This causes an extra discard prompt, not data loss. |
| L-15 | Low | ACCEPTED | `RetryAsync` only reloads config when `ConfigLoadFailed`; a project target with no folder has `ConfigLoadFailed=false` and `_configService=null`, so Browse is the intended prerequisite (`MainViewModel.cs:812-823`, `:973-996`). |
| L-16 | Low | ACCEPTED | `SmokeRunner` constructs `OpenCodeBridge` synchronously before its first await (`SmokeRunner.cs:77`); smoke mode is headless/no-window. The interactive UI uses `Task.Run` (`MainViewModel.cs:1032`). |

## Evidence Log

- `tests/verify-config-safety.ps1`: all nine checks PASS, `CONFIG SAFETY: PASS`; exit code 0.
- `tests/artifacts/jsonc-config.json`: saved JSON has no comments; `unknown_root_key`, `unknown_agent_key`, `swarms.demo.name`, and the coder model are preserved.
- `tests/artifacts/backup-collision.json.bak-20261126-193437-902` and `...-193437-916`: two distinct backups from consecutive saves, confirming M-3.
- `tests/artifacts/backup-collision.json`: round-trip `opencode-go/hungerg-free`.
- `src/SwarmCastStudio/publish/smoke-result.json`: `ok=true`, 88 models, 28 agents, 26 roles, 21 live swarm agents, `configPath=%USERPROFILE%\.config\opencode\opencode-swarm.json`, `error=null`.
- Published `--smoke` via `Start-Process`: exit code **0**.
- `tests/run-smoke.ps1`: `--smoke`, first `--smoke-write`, and second `--smoke-write` all exit 0 with `ok=true`; the single backup in that old test is expected because the first save created a fresh file.
- Global config `%USERPROFILE%\.config\opencode\opencode-swarm.json`: SHA-256 `C4317EA8E6C64E40AC092BF157C485C3BBB07D36BE65E6069336DCC01DEAF219`; mtime `2026-11-26 16:29:18`; unchanged. No app-generated `opencode-swarm.json.bak-*` exists in the live config directory.
- `publish/SwarmCastStudio.dll` SHA-256 matched `bin/Release/net9.0-windows/SwarmCastStudio.dll` (current build).
- No application source, README, test script, config, or live global config was edited during this re-audit.

## Limitations

- The UI fixes for M-1/M-2 were verified by source and binding inspection; no new interactive UI automation run was performed in this re-audit because the existing `UiProbe` writes its own report artifact and the request was limited to re-auditing the fixes. The previous QA report contains the UI search and model-apply evidence; the current source additionally includes the agent-filter selection preservation logic (`MainViewModel.cs:133-183`).
- Empty-catalog handling (M-4) was verified by source inspection; reproducing an empty live catalog is not possible without altering the external OpenCode service. The code now retries once, warns, and synthesizes saved model references.
- README backup-pattern drift (L-13) is a documentation-only residual; no source/docs edits were made in this re-audit.

## Final Verdict

**PASS — Ship: YES.**

H-1, H-2, and H-3 are resolved and independently evidenced. Every previously reported Medium finding is resolved; the remaining Low items are accepted with rationale and do not block shipment.
