# Swarm Cast Studio — Code Contract

Both coders must code against these exact public signatures. Implementations may add
internal helpers, but names, properties, and method signatures below must match.

Namespace root: `SwarmCastStudio`.

## SwarmCastStudio.Models

```csharp
namespace SwarmCastStudio.Models;

public sealed class OpenCodeModel
{
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public required string DisplayName { get; init; }
    public long ContextLimit { get; init; }
    public long OutputLimit { get; init; }
    public decimal InputPricePerMillion { get; init; }
    public decimal OutputPricePerMillion { get; init; }
    public bool SupportsTools { get; init; } = true;
    public bool IsFree { get; init; }
    public string Status { get; init; } = "active";
    public bool Enabled { get; init; } = true;

    public string Ref => $"{ProviderId}/{ModelId}";
    public string ContextLabel => ContextLimit >= 1_000_000
        ? $"{ContextLimit / 1_000_000.0:0.#}M ctx"
        : $"{ContextLimit / 1000.0:0}K ctx";
    public string PriceLabel => IsFree ? "FREE" : $"${InputPricePerMillion:0.##}/${OutputPricePerMillion:0.##}";
}

public sealed class LiveAgent
{
    public required string Id { get; init; }
    public string? ProviderId { get; init; }
    public string? ModelId { get; init; }
    public string? Mode { get; init; }
    public string? Description { get; init; }

    public string? ModelRef => string.IsNullOrWhiteSpace(ProviderId) || string.IsNullOrWhiteSpace(ModelId)
        ? null
        : $"{ProviderId}/{ModelId}";
}

public enum AgentGate { None, UiReview, DesignDocs, CouncilGeneral }

public sealed record AgentDefinition(
    string Name,
    string DisplayName,
    string Category,
    string Description,
    bool Core,
    AgentGate Gate);
```

## SwarmCastStudio.Services

```csharp
namespace SwarmCastStudio.Services;

public sealed record OpenCodeConnection(string CliPath, string Version);

public sealed class OpenCodeUnavailableException : Exception
{
    public OpenCodeUnavailableException(string message) : base(message) { }
    public OpenCodeUnavailableException(string message, Exception inner) : base(message, inner) { }
}

public interface IOpenCodeBridge
{
    Task<OpenCodeConnection> ConnectAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OpenCodeModel>> GetModelsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LiveAgent>> GetAgentsAsync(CancellationToken ct = default);
    Task ReloadAsync(CancellationToken ct = default);
}

public sealed class OpenCodeBridge : IOpenCodeBridge
{
    public OpenCodeBridge(string? cliPathOverride = null);
    public string? CliPath { get; }

    public static string? DiscoverCli();
    public Task<OpenCodeConnection> ConnectAsync(CancellationToken ct = default);
    public Task<IReadOnlyList<OpenCodeModel>> GetModelsAsync(CancellationToken ct = default);
    public Task<IReadOnlyList<LiveAgent>> GetAgentsAsync(CancellationToken ct = default);
    public Task ReloadAsync(CancellationToken ct = default);
}

public static class AgentCatalog
{
    public static IReadOnlyList<AgentDefinition> All { get; }
    public static AgentDefinition? Find(string name);
    public static IReadOnlyList<string> CoreRequiredNames { get; } // architect, coder, reviewer, test_engineer
}

public sealed class SwarmConfigService
{
    public SwarmConfigService(string path);
    public string Path { get; }
    public bool Exists { get; }

    public SwarmConfig Load();          // missing file -> SwarmConfig.CreateNew()
    public void Save(SwarmConfig config); // backup + temp + atomic replace
    public static string DefaultGlobalPath { get; }
    public static string ProjectPathFor(string projectDirectory);
}

public sealed class SwarmConfig
{
    public SwarmCastStudio.Models.JsonObjectAlias Root { get; } // see note

    public static SwarmConfig FromJson(string json);
    public static SwarmConfig CreateNew();
    public string ToJson();

    public bool IsAgentEnabled(string name);
    public string? GetAgentModel(string name);
    public IReadOnlyList<string> GetAgentFallbacks(string name);
    public double? GetAgentTemperature(string name);
    public void SetAgent(string name, bool enabled, string? model,
        IReadOnlyList<string> fallbacks, double? temperature);

    public bool GetFeature(string dottedPath);          // e.g. "ui_review.enabled"
    public void SetFeature(string dottedPath, bool value);
}

public sealed class AppSettings
{
    public string Target { get; set; } = "global"; // "global" | "project"
    public string? ProjectDirectory { get; set; }

    public static string SettingsPath { get; }
    public static AppSettings Load();
    public void Save();
}

public static class SmokeRunner
{
    public static Task<int> RunAsync(string[] args, string outputDirectory);
}
```

**JsonObject note:** use `System.Text.Json.Nodes.JsonObject`. In the contract above,
`JsonObjectAlias` means `System.Text.Json.Nodes.JsonObject`; do not create a custom type.
The `Root` property may be `public JsonObject Root { get; }`.

Implementation rules:

- `SwarmConfig.FromJson`: `JsonNode.Parse` with comments skipped and trailing commas allowed;
  root must be an object or throw `InvalidDataException`.
- `CreateNew`: object containing `$schema` =
  `https://unpkg.com/opencode-swarm@7.205.10/opencode-swarm.schema.json`,
  empty `agents` object, and `max_iterations: 5`.
- `SetAgent`: merge, never replace the agent object; remove keys for empty values.
- `GetFeature`/`SetFeature`: create intermediate objects on set; missing returns false.
- `Save`: ensure directory; timestamped `.bak-yyyyMMdd-HHmmss`; temp file then
  `File.Move(temp, Path, overwrite: true)`; UTF-8 no BOM; indented 2 spaces; newline at EOF.
- `AgentCatalog`: exactly the 26 roles in SPEC.md with short, useful descriptions.
  `Core = true` only for architect, explorer, coder, reviewer, test_engineer, critic, sme, docs.
- `OpenCodeBridge`: process runner must be shared, no shell, window hidden, 45s timeout,
  kill on timeout, capture stdout/stderr, tolerate BOM/leading noise.
- `SmokeRunner`: write `smoke-result.json` into `outputDirectory`; never throw out.

## SwarmCastStudio (App / UI)

B owns these. Use `MainViewModel` and `AgentRowViewModel`, `RelayCommand`.
Public shape is internal to the UI and only needs to compile together.

- `App.xaml` / `App.xaml.cs`: if any arg starts with `--smoke`, invoke
  `SmokeRunner.RunAsync(e.Args, AppContext.BaseDirectory)`, then `Shutdown(exitCode)`.
  Otherwise show `MainWindow`.
- `MainWindow.xaml` / `.xaml.cs`: dark 3-column UI per SPEC.md; DataContext = MainViewModel;
  `Loaded` triggers `InitializeAsync`.
- `MainViewModel`: orchestrates bridge, config service, rows, save/reload, errors, target
  switching, feature gates, model filtering. Must never throw to the dispatcher.
- `AgentRowViewModel`: definition, enabled, model, fallbacks, temperature, gate satisfaction,
  live status/resolved model. Implements `INotifyPropertyChanged`.
- `RelayCommand`: `ICommand`, supports `Action`/`Action<object?>` and `Func<Task>` with
  `CanExecute`; raises `CanExecuteChanged`.
- Converters: bool→visibility (and inverse), null→visibility, string empty→visibility.

## Layout

```
src/SwarmCastStudio/
  SwarmCastStudio.csproj      (A)
  SPEC.md                     (integrator, read-only)
  CONTRACT.md                 (integrator, read-only)
  Models/                     (A)
  Services/                   (A)
  App.xaml / App.xaml.cs      (B)
  MainWindow.xaml / .cs       (B)
  Themes/Dark.xaml            (B)
  ViewModels/                 (B)
  Converters/                 (B)
```
