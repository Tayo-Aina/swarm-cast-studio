# Swarm Cast Studio -- Independent QA Report

> **Latest result (published build): PASS.** See "Re-run against published build" at the end of this file. The earlier `bin\Release` findings are retained as historical evidence; the agent-filter defect was fixed and verified against `publish\SwarmCastStudio.exe`.

- **Date:** 2026-11-26, 18:24-18:42 local
- **App under test:** `%REPO%\src\SwarmCastStudio\bin\Release\net9.0-windows\SwarmCastStudio.exe`
- **Session:** Windows 10/11, DPI **120 (125%)**, OpenCode Desktop CLI **2.4.2**
- **Test code:** `tests\UiProbe\` (UIAutomationClient probe), `tests\run-smoke.ps1`, `tests\run-smoke-stability.ps1`, `tests\verify-smoke.ps1`, `tests\check-rawjson-template.ps1`
- **Artifacts:** `tests\artifacts\`
- **Constraint honored:** no application source was edited; no save was issued to the real global swarm config.

## Verdict

| Area | Result |
|---|---|
| Release build (0 warnings / 0 errors) | **PASS** |
| Headless `--smoke` | **PASS** (one transient first invocation; 5/5 subsequent runs passed) |
| Headless `--smoke-write` (first + second save) | **PASS** |
| UI: ModelSearchBox value / text / rect height | **PASS** |
| UI: model filtering leaves Architect and PRIMARY untouched | **PASS** |
| UI: AgentSearchBox text / critic rows | **PASS** |
| UI: agent filtering preserves selected agent / editor | **FAIL** |
| UI: select-only does not apply model | **PASS** (after a documented recovery) |
| UI: Set as primary updates chip + unsaved state | **PASS** (after a documented recovery) |
| DPI / window size / header / right rail bounds | **PASS** |
| Raw JSON preview template padding | **PASS** |
| No source or real-config writes | **PASS** |

**Acceptance gates exactly as listed: PASS.** The build is 0/0; both smoke modes return the required live counts; the typed model text is 22px tall; model filtering leaves Architect/PRIMARY untouched with no unsaved state; select-only is inert; `Set as primary` updates the chip and marks unsaved; and no source or real config was modified.

**Overall QA verdict: FAIL.** The requested UI sequence itself is blocked by a separate, reproducible defect: `AgentSearch` filtering clears the ListBox selection (`agentListSelectedItems=0`), nulls `SelectedAgent`, and collapses the center editor and PRIMARY chip. The post-agent-filter model selection / `Set as primary` steps could only be exercised after clearing the search and reselecting Architect. This defect was not fixed.

## 1. Release build

Command:

```powershell
dotnet build src\SwarmCastStudio\SwarmCastStudio.csproj -c Release --nologo
```

Result:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:00.81
```

**PASS.**

## 2. Headless smoke

Harness: `powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-smoke.ps1` -- starts the WinExe via `System.Diagnostics.Process` so exit codes are captured, then copies `smoke-result.json` into `tests\artifacts`.

Exact app commands:

```powershell
src\SwarmCastStudio\bin\Release\net9.0-windows\SwarmCastStudio.exe --smoke
src\SwarmCastStudio\bin\Release\net9.0-windows\SwarmCastStudio.exe --smoke-write "%REPO%\tests\artifacts\smoke-config.json"
```

### 2.1 `--smoke`

| Assertion | Result | Evidence |
|---|---|---|
| exit code 0 | PASS | `exit=0` |
| `ok=true` | PASS | `ok=true` |
| `models > 50` | PASS | `models=88` |
| `agents > 20` | PASS | `agents=28` |
| `swarmAgents=26` | PASS | `swarmAgents=26` |
| `liveSwarmAgents=21` | PASS | `liveSwarmAgents=21` |
| `configExists=true` | PASS | `configExists=true`, `configPath=%USERPROFILE%\.config\opencode\opencode-swarm.json` |
| `error=null` | PASS | `error=null` |

Raw result (`tests\artifacts\smoke-result.json`):

```json
{
  "ok": true,
  "cliPath": "C:\\Users\\Tayod\\AppData\\Local\\Programs\\ai.opencode.desktop\\resources\\opencode-cli.exe",
  "version": "2.4.2",
  "models": 88,
  "agents": 28,
  "swarmAgents": 26,
  "liveSwarmAgents": 21,
  "configPath": "C:\\Users\\Tayod\\.config\\opencode\\opencode-swarm.json",
  "configExists": true,
  "error": null
}
```

Stability (`tests\run-smoke-stability.ps1`, `tests\artifacts\smoke-stability.json`): **5/5** consecutive `--smoke` runs returned exit 0 with `88 / 28 / 26 / 21 / configExists=true`.

**Transient observation:** the very first invocation at 18:26:13 returned exit 1 with `models=0`, `agents=7`, `configExists=false`, `error="OpenCode returned no models. Make sure OpenCode Desktop is running and signed in."` The immediate rerun and all subsequent runs passed. No code/config changed. This looks like an external OpenCode Desktop startup/restart window and was not reproducible; recorded as an observation, not masked.

### 2.2 `--smoke-write` (temp file, never the real global config)

| Assertion | Result | Evidence |
|---|---|---|
| first run exit 0 / ok=true | PASS | `exit=0`, `ok=true` |
| models>50, agents>20, swarmAgents=26, liveSwarmAgents=21 | PASS | `88 / 28 / 26 / 21` |
| `configExists=true` | PASS | true |
| `writtenModel` set | PASS | `opencode-go/hungerg-free` |
| temp file exists | PASS | `tests\artifacts\smoke-config.json` |
| re-read `agents.coder.model == writtenModel` | PASS | `opencode-go/hungerg-free` == `opencode-go/hungerg-free` |
| second save exit 0 / ok=true | PASS | `exit=0`, `ok=true` |
| second save round-trip | PASS | `opencode-go/hungerg-free` == `opencode-go/hungerg-free` |
| second save created `.bak-*` | PASS | `smoke-config.json.bak-20261126-184117` |
| wrote temp, not global | PASS | `path=...\tests\artifacts\smoke-config.json`; global path untouched |

Raw result (`tests\artifacts\smoke-write-result-1.json`):

```json
{
  "ok": true,
  "models": 88,
  "agents": 28,
  "swarmAgents": 26,
  "liveSwarmAgents": 21,
  "configPath": "C:\\Users\\Tayod\\Documents\\Default Project\\tests\\artifacts\\smoke-config.json",
  "configExists": true,
  "path": "C:\\Users\\Tayod\\Documents\\Default Project\\tests\\artifacts\\smoke-config.json",
  "writtenModel": "opencode-go/hungerg-free",
  "error": null
}
```

Verification summary: `powershell -NoProfile -ExecutionPolicy Bypass -File tests\verify-smoke.ps1` -> 26/26 PASS (`tests\artifacts\smoke-verification.json`).

Real global config was not written: hash stayed `c4317ea8e6c64e40ac092bf157c485c3bbb07d36be65e6069336dcc01deaf219`, mtime stayed `2026-11-26 16:29:18` local, `agents.architect.model` remained `opencode-go/deepseek-v4.1-flash`, and `agents.coder.model` remained `opencode-go/kimi-k2.7-code` (not the temp-written `hungerg-free`).

**PASS.**

## 3. UI automation

Harness:

```powershell
dotnet tests\UiProbe\bin\Release\net9.0-windows\UiProbe.dll
```

The probe launched the app, waited for the window and the two search boxes, drove the UI via UIAutomationClient patterns only, and killed the process without invoking Save. Two full attempts ran; both completed and produced the **same two failures** (`tests\artifacts\ui-report.json`, attempts 1 and 2).

### 3.1 Search box / model filtering

| Check | Result | Evidence |
|---|---|---|
| ModelSearchBox `ValuePattern` value | PASS | `glm` |
| ModelSearchBox `TextPattern` text | PASS | `glm` |
| typed text bounding-rect height > 10px | PASS | `GetBoundingRectangles()` returned `Rect[]`: `[(612,379,27x22)]`, height **22px** |
| Architect row model unchanged by filter | PASS | `opencode-go/deepseek-v4.1-flash` |
| PRIMARY chip unchanged by filter | PASS | `opencode-go/deepseek-v4.1-flash` |
| footer does not show "Unsaved changes" | PASS | footer: `Connected to OpenCode 2.4.2 -- 88 models, 28 agents / Live catalog refreshed at 18:39 / Open config folder` |

Note on the array structure: in this runtime the UIA text-range call returns a `System.Windows.Rect[]` (one Rect per line), not a flat `double[]`. Reading the rect's `.Height` gives 22px; this is the correct >10px assertion and confirms the former 3px bug is gone.

### 3.2 Agent search

| Check | Result | Evidence |
|---|---|---|
| AgentSearchBox `ValuePattern` value | PASS | `critic` |
| AgentSearchBox `TextPattern` text | PASS | `critic` |
| typed text bounding-rect height > 10px | PASS | `Rect[]`: `[(192,206,33x22)]`, height **22px** |
| visible rows are critic-related | PASS | `[Critic, Finding Validator, Oversight Critic, Sounding Board, Drift Verifier, Hallucination Verifier, Architecture Supervisor]` |

### 3.3 Agent filter defect -- FAIL

| Check | Result | Evidence |
|---|---|---|
| selected agent retained while filtering | **FAIL** | `centerArchitect=False`; **`agentListSelectedItems=0`**; center placeholder `Select an agent` is visible; PRIMARY chip is gone |
| PRIMARY chip retained while filtering | **FAIL** | `PRIMARY chip=''` |
| no "Unsaved changes" while filtering agents | PASS | footer has no "Unsaved changes" (the filter does not dirty config) |

What happens: typing `critic` filters the selected **Architect** row out of the left ListBox. WPF clears the ListBox selection, the TwoWay binding pushes `SelectedAgent=null`, and the center editor (`Visibility` bound to `HasSelectedAgent`) collapses. The model library, PRIMARY chip, and `Set as primary` button disappear. This is consistent across both probe attempts and is not a UIA artifact: `SelectionPattern.GetSelection().Length` is 0 and the app's own `Select an agent` placeholder is rendered.

### 3.4 Recovery + select / Set as primary

To still exercise the remaining functional checks, the probe performed a **documented recovery** (not an app fix): clear AgentSearch, wait for the Architect row, `SelectionItemPattern.Select()` Architect, then re-apply the `glm` model filter.

| Check | Result | Evidence |
|---|---|---|
| selecting a model alone does not change PRIMARY | PASS | chip stayed `opencode-go/deepseek-v4.1-flash` |
| selecting a model alone does not set unsaved | PASS | footer unchanged, no "Unsaved changes" |
| `Set as primary` changes PRIMARY chip | PASS | chip became `opencode-go/glm-5.3` |
| footer shows "Unsaved changes" | PASS | `Primary model set to opencode-go/glm-5.3. / Unsaved changes` |
| killed without saving | PASS | process killed; no Save invoke; source/config hashes unchanged |

**PASS** for the select-vs-primary behavior itself; **the requested sequence as written is blocked by the 3.3 defect.**

## 4. DPI / layout

| Check | Result | Evidence |
|---|---|---|
| window physical size at 125% | PASS | `DPI=120 (125%)`, window `(110,15,1700x1050)`, expected `1700x1050` |
| header `Save & Reload` on-screen | PASS | button rect `(1441,37,163x42)` inside window; `IsOffscreen=false` |
| right rail cards within window | PASS | right rail pane `(1359,116,430x874)`; headings `Connection(1380,137,386x35)`, `Config target(1380,390,386x23)`, `Feature gates(1380,626,386x23)`, `Raw JSON(1380,893,386x35)`; all inside window |
| nothing beyond right edge | PASS | scanned 510 visible UIA elements; max right edge <= `1810` (window right) |

## 5. Raw JSON template regression

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\check-rawjson-template.ps1
```

| Check | Result | Evidence |
|---|---|---|
| `CodeTextBox` style padding | PASS | `Padding=12` |
| template Border count | PASS | exactly 1 Border in the ControlTemplate |
| no double Padding on template Border | PASS | no `Padding` attribute on the template Border |
| Raw JSON preview TextBox | PASS | `Style="{StaticResource CodeTextBox}" Height="190"`, no explicit `Padding` |
| preview parent Border | PASS | no `Padding` attribute |

Evidence JSON: `tests\artifacts\rawjson-template-check.json` (allPassed=true).

The base TextBox template pattern (Border -> ScrollViewer, no Border.Padding) honors TextBox.Padding -- the search box text was measured 45px left / 12px top inside a box with `Padding="34,8,10,8"` at 125% -- so `CodeTextBox`'s `Padding=12` remains usable and is not doubled.

## 6. No source / config modifications

| Check | Result | Evidence |
|---|---|---|
| application source unchanged | PASS | source tree hash `f579971a231d491d72cc27def2ec0a2b57a42a9f7ea4ef05942f7b2d28621335` before and after UI run |
| no source edited during QA | PASS | latest source `LastWriteTime` is `2026-11-26 18:21:15` (`Themes\Dark.xaml`), before QA began |
| real global config unchanged | PASS | SHA-256 `c4317ea8e6c64e40ac092bf157c485c3bbb07d36be65e6069336dcc01deaf219`, mtime `2026-11-26 16:29:18`, unchanged before/after UI probe and after final smoke run |
| no save issued to global config | PASS | no Save button invoked; `--smoke-write` used `tests\artifacts\smoke-config.json` only |

## 7. Reproduction of the failing UI defect

1. Launch `SwarmCastStudio.exe`; Architect is selected by default and PRIMARY shows `opencode-go/deepseek-v4.1-flash`.
2. Type `critic` into `AgentSearchBox`.
3. Observe: the agent list filters correctly to the seven critic-related rows, but the ListBox has **0 selected items**. The center editor collapses to the `Select an agent` placeholder; the model library, PRIMARY chip, and `Set as primary` button are gone.
4. Clearing `AgentSearch` does **not** restore the selection; the operator must click an agent row again.

Expected for the requested flow: the currently selected agent should remain selected/editable while filtering, or the filter should select the first matching row. Fixing this is out of scope for this QA task.

## Evidence file index

| File | Contents |
|---|---|
| `tests\artifacts\smoke-summary.json` | exit codes + parsed fields for both smoke modes |
| `tests\artifacts\smoke-result.json` | `--smoke` result |
| `tests\artifacts\smoke-write-result-1.json` / `-2.json` | first/second `--smoke-write` results |
| `tests\artifacts\smoke-config.json` + `.bak-20261126-184117` | temp config + backup proof |
| `tests\artifacts\smoke-verification.json` | 26/26 smoke assertions |
| `tests\artifacts\smoke-stability.json`, `stability-1..5.json` | 5/5 stability evidence |
| `tests\artifacts\ui-report.json` | full UIA result, both attempts, hashes |
| `tests\artifacts\ui-tree.txt` | captured UIA control tree at 125% |
| `tests\artifacts\rawjson-template-check.json` | static XAML padding checks |

---

# Re-run against published build (2026-11-26 19:30-19:40)

This section supersedes the earlier `bin\Release` verdict for acceptance. The published binary was tested, not the old `bin` output.

## Binary under test

| Item | Value |
|---|---|
| Published exe | `%REPO%\src\SwarmCastStudio\publish\SwarmCastStudio.exe` |
| SHA-256 | `2393745e31d0ea89d400ab613285c312474c75d97792b76d540c5805fbbf83eb` |
| Size / last write | `156160` bytes / `2026-11-26 19:28:39` local |
| Latest app source mtime | `2026-11-26 19:19:14` (`MainViewModel.cs`), before QA began |
| OpenCode CLI | `2.4.2`, 88 models, 28 live agents |
| Session DPI | 120 (125%); window `1700x1050` |

## Verdict

**PASS.** Published smoke, JSONC/malformed config safety, UI behavior, layout, and immutability checks all pass. No app source or real swarm config was modified.

| Area | Result |
|---|---|
| Published `--smoke` | **PASS** |
| Published `--smoke` stability (5/5) | **PASS** |
| JSONC comments / trailing commas / unknown keys | **PASS** |
| Malformed config rejected and not overwritten | **PASS** |
| Agent filter keeps editor populated | **PASS** (Critic heading + PRIMARY chip, no placeholder) |
| Clear agent filter restores Architect | **PASS** |
| ModelSearchBox `glm` value / text / height | **PASS** (22px) |
| Model filtering leaves Architect/PRIMARY untouched | **PASS** |
| Select-only does not apply model or mark unsaved | **PASS** |
| Set as primary updates chip + unsaved state | **PASS** |
| DPI / layout / right-rail bounds | **PASS** |
| Raw JSON preview padding regression | **PASS** |
| No source / config / exe writes | **PASS** |

## 8. Published `--smoke`

Command (exact binary):

```powershell
src\SwarmCastStudio\publish\SwarmCastStudio.exe --smoke
```

Harnesses:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-published-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-published-smoke-only.ps1 -Count 5
powershell -NoProfile -ExecutionPolicy Bypass -File tests\verify-published-smoke.ps1
```

| Assertion | Result | Evidence |
|---|---|---|
| exit code 0 | PASS | `exit=0` (all 5 stability runs) |
| `ok=true` | PASS | `ok=true` |
| `models > 50` | PASS | `models=88` |
| `agents > 20` | PASS | `agents=28` |
| `swarmAgents=26` | PASS | `swarmAgents=26` |
| `liveSwarmAgents=21` | PASS | `liveSwarmAgents=21` |
| `configExists=true` | PASS | `configExists=true`; path `%USERPROFILE%\.config\opencode\opencode-swarm.json` |
| `error=null` | PASS | `error=null` |

`src\SwarmCastStudio\publish\smoke-result.json` was re-written by a final `--smoke` run and currently contains the successful result above (the later config-safety malformed-file run had briefly overwritten it; it was restored with a final `--smoke`).

Published stability (`tests\artifacts\published\smoke-stability.json`): 5/5 runs with `exit=0`, `88 / 28 / 26 / 21`, `configExists=true`, `error=null`.

### Published `--smoke-write` extras

| Assertion | Result | Evidence |
|---|---|---|
| first temp save exit 0 / ok=true | PASS | `exit=0`, `ok=true` |
| first save counts | PASS | `88 / 28 / 26 / 21` |
| `writtenModel` set / round-trip | PASS | `opencode-go/hungerg-free` in both file and result |
| second save exit 0 / round-trip | PASS | `opencode-go/hungerg-free` |
| collision-proof backup name | PASS | `smoke-config.json.bak-20261126-193209-904` (millisecond field) |
| real global config untouched | PASS | SHA-256 `c4317ea8e6c64e40ac092bf157c485c3bbb07d36be65e6069336dcc01deaf219`; architect still `opencode-go/deepseek-v4.1-flash`; coder still `opencode-go/kimi-k2.7-code` |

## 9. Config safety against published exe

Command (exact):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\verify-config-safety.ps1 -Exe "src\SwarmCastStudio\publish\SwarmCastStudio.exe"
```

Result: **CONFIG SAFETY: PASS**, wrapper exit 0, failCount 0 (`tests\artifacts\published\config-safety\config-safety-summary.json`).

| Check | Result | Evidence |
|---|---|---|
| JSONC smoke-write exits 0 | PASS | `exit=0` |
| JSONC smoke result ok | PASS | `ok=True`; `error=null` |
| unknown root key preserved | PASS | `unknown_root_key=preserve-me` |
| unknown agent key preserved | PASS | `agents.coder.unknown_agent_key=42` |
| swarms block preserved | PASS | `swarms.demo.name=Demo` |
| coder model written | PASS | `opencode-go/hungerg-free` |
| malformed config exits non-zero | PASS | `exit=1` |
| malformed result not ok | PASS | `ok=False` |
| malformed file not overwritten | PASS | SHA-256 `4456C9BF5FC2A969733589B4529B94525D0D6A0C5CDB2C5BBA25FE4230F9BB4E` before/after; 23-byte file intact |

The JSONC fixture uses `//` comments, trailing commas, an unknown root key, an unknown per-agent key, and a `swarms` block. After `--smoke-write`, all of those survived (`tests\artifacts\published\config-safety\jsonc-config.json`). A same-second backup was also produced: `jsonc-config.json.bak-20261126-193616-430`.

## 10. UI automation against published exe

Command:

```powershell
dotnet tests\UiProbe\bin\Release\net9.0-windows\UiProbe.dll "src\SwarmCastStudio\publish\SwarmCastStudio.exe" "tests\artifacts\published"
```

Result: **PASS**, attempt 1, 25.66s, no retry (`tests\artifacts\published\ui-report.json`).

A second independent probe invocation (`tests\artifacts\published\ui-run2\ui-report.json`) passed on **attempt 2** after an environment flake on attempt 1: the app window was effectively off-screen/minimized (UIA child rects around `-30730,-31625`, footer empty), so the right-rail layout check and the Set-as-primary wait timed out even though the functional agent/model-filter checks passed. The probe's automatic retry (attempt 2) passed every check. This is recorded as a window-state/UIA environment flake, not an app defect; the primary run above passed on attempt 1 and the retry passed all functional checks.

### 10.1 Agent filter first

| Check | Result | Evidence |
|---|---|---|
| AgentSearchBox value / text | PASS | ValuePattern `critic`; TextPattern `critic` |
| AgentSearchBox text height > 10px | PASS | `Rect[]` `[(192,206,33x22)]`, height 22px |
| visible rows are critic-related | PASS | `[Critic, Finding Validator, Oversight Critic, Sounding Board, Drift Verifier, Hallucination Verifier, Architecture Supervisor]` |
| center editor populated | PASS | `centerCritic=True`; detail heading `Critic`; PRIMARY chip `opencode-go/glm-5.3`; `agentListSelectedItems=1` |
| no "Select an agent" collapse | PASS | `centerPlaceholderVisible=False` |
| no unsaved state from filtering | PASS | footer has no "Unsaved changes" |
| clearing filter restores Architect | PASS | center heading `Architect`; PRIMARY `opencode-go/deepseek-v4.1-flash`; no unsaved |

This verifies the agent-filter recovery fix: filtering no longer collapses the editor; it selects the first matching role (Critic). Clearing the filter restores the pre-filter Architect selection.

### 10.2 Model filter (Architect restored)

| Check | Result | Evidence |
|---|---|---|
| ModelSearchBox value / text | PASS | ValuePattern `glm`; TextPattern `glm` |
| ModelSearchBox text height > 10px | PASS | `Rect[]` `[(612,379,27x22)]`, height 22px |
| Architect row unchanged | PASS | `opencode-go/deepseek-v4.1-flash` |
| PRIMARY chip unchanged | PASS | `opencode-go/deepseek-v4.1-flash` |
| Architect editor still shown | PASS | center detail heading `Architect` visible |
| no "Unsaved changes" | PASS | footer: `Connected to OpenCode 2.4.2 - 88 models, 28 agents / Live catalog refreshed at 19:34 / Open config folder` |

Because the new agent-filter behavior selects Critic, the probe cleared AgentSearch to restore Architect before this check. That restoration is asserted above; the model-filter non-mutation assertion is then made against Architect as requested.

### 10.3 Select-only vs Set as primary

| Check | Result | Evidence |
|---|---|---|
| `SelectionItemPattern.Select()` only | PASS | PRIMARY stayed `opencode-go/deepseek-v4.1-flash`; no "Unsaved changes" |
| `Set as primary` invoke | PASS | PRIMARY became `opencode-go/glm-5.3` |
| dirty footer | PASS | `Primary model set to opencode-go/glm-5.3. / Unsaved changes` |
| kill without saving | PASS | process killed; no Save invoke; global config and source hashes unchanged |

### 10.4 Layout

| Check | Result | Evidence |
|---|---|---|
| window size at 125% | PASS | DPI 120; window `(110,15,1700x1050)`, expected `1700x1050` |
| header Save & Reload visible | PASS | button rect `(1441,37,163x42)` inside window; `IsOffscreen=false` |
| right rail within window | PASS | right rail pane `(1359,116,430x874)`; headings `Connection(1380,137,386x35)`, `Config target(1380,390,386x23)`, `Feature gates(1380,626,386x23)`, `Raw JSON(1380,893,386x35)` |
| nothing beyond right edge | PASS | 510 visible UIA elements scanned; max right edge <= 1810 |

## 11. Raw JSON template regression (re-run)

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\check-rawjson-template.ps1
```

All five checks PASS: `CodeTextBox` `Padding=12`; one ControlTemplate Border; no Padding on that Border; Raw JSON TextBox `Height=190` with no explicit Padding; parent Border no Padding.

## 12. Published-build safety

| Check | Result | Evidence |
|---|---|---|
| source unchanged | PASS | source tree hash `3e6201798ae3c166111df930948fa852231f33350880ee7ec536d6639aa3b36d` before/after |
| published exe unchanged | PASS | SHA-256 `2393745e31d0ea89d400ab613285c312474c75d97792b76d540c5805fbbf83eb` before/after |
| real global config unchanged | PASS | SHA-256 `c4317ea8e6c64e40ac092bf157c485c3bbb07d36be65e6069336dcc01deaf219`; mtime unchanged |
| no save to real config | PASS | `--smoke-write` used `tests\artifacts\published\smoke-config.json`; UI was killed without Save |

## 13. New findings

- No blocking findings in the published build. The previous agent-filter collapse defect is fixed: filtering selects the first matching role and keeps the editor/PRIMARY chip populated; clearing the filter restores the previous Architect selection.
- The published `smoke-result.json` is order-sensitive because every `--smoke`/`--smoke-write` invocation overwrites it. It was restored with a final successful `--smoke` run after the config-safety malformed-file test.
- No app source edits were made during this re-run.

## Published-build evidence index

| File | Contents |
|---|---|
| `tests\artifacts\published\smoke-result.json` | final published `--smoke` result |
| `tests\artifacts\published\smoke-summary.json` | published smoke + smoke-write exit codes and fields |
| `tests\artifacts\published\smoke-write-result-1.json` / `-2.json` | published temp write round-trips |
| `tests\artifacts\published\smoke-config.json` + `.bak-20261126-193209-904` | temp config + collision-proof backup |
| `tests\artifacts\published\smoke-stability.json`, `smoke-run-1..5.json` | 5/5 published smoke stability |
| `tests\artifacts\published\verify-smoke.json` | 21/21 published smoke assertions |
| `tests\artifacts\published\ui-report.json` | full UIA result, exe hash, source/config hashes |
| `tests\artifacts\published\ui-run2\ui-report.json` | second UI run: attempt 1 environment flake, attempt 2 full pass |
| `tests\artifacts\published\config-safety\config-safety-summary.json` | config-safety wrapper exit 0, failCount 0 |
| `tests\artifacts\published\config-safety\config-safety-output.txt` | 9/9 config-safety checks |
| `tests\artifacts\published\config-safety\jsonc-config.json` | saved JSONC with unknown keys/swarms preserved |
| `tests\artifacts\published\config-safety\broken-config.json` | malformed file, unchanged |

