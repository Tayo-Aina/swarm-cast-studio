using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SwarmCastStudio.Models;

namespace SwarmCastStudio.Services;

/// <summary>
/// Talks to the running OpenCode Desktop service through its bundled CLI.
/// The CLI is the authentication boundary; no direct HTTP calls are made.
/// </summary>
public sealed class OpenCodeBridge : IOpenCodeBridge
{
    /// <summary>Maximum time a single CLI command may run.</summary>
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(45);

    private const string ModelEndpoint = "api get /api/model";
    private const string AgentEndpoint = "api get /api/agent";
    private const string InfoEndpoint = "api get /api/info";

    /// <summary>Creates a bridge, optionally forcing a specific CLI executable.</summary>
    public OpenCodeBridge(string? cliPathOverride = null)
    {
        if (string.IsNullOrWhiteSpace(cliPathOverride))
        {
            CliPath = DiscoverCli();
        }
        else
        {
            var expanded = Environment.ExpandEnvironmentVariables(cliPathOverride.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(expanded))
            {
                CliPath = null;
            }
            else
            {
                try
                {
                    CliPath = Path.GetFullPath(expanded);
                }
                catch
                {
                    CliPath = expanded;
                }
            }
        }
    }

    /// <summary>The resolved OpenCode CLI path, or <see langword="null"/> when none was found.</summary>
    public string? CliPath { get; }

    /// <summary>
    /// Finds the CLI using the documented order: environment override, OpenCode Desktop,
    /// PATH, then the standalone install location. Returns <see langword="null"/> on failure.
    /// </summary>
    public static string? DiscoverCli()
    {
        try
        {
            var overridden = Environment.GetEnvironmentVariable("SWARMCAST_OPENCODE_CLI");
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                var candidate = Environment.ExpandEnvironmentVariables(overridden.Trim().Trim('"'));
                if (IsWindowsExecutable(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                var desktopCli = Path.Combine(
                    localAppData,
                    "Programs",
                    "ai.opencode.desktop",
                    "resources",
                    "opencode-cli.exe");

                if (IsWindowsExecutable(desktopCli))
                {
                    return Path.GetFullPath(desktopCli);
                }
            }

            var onPath = FindOnPath();
            if (!string.IsNullOrWhiteSpace(onPath))
            {
                return onPath;
            }

            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                var standalone = Path.Combine(localAppData, "Programs", "opencode", "opencode.exe");
                if (IsWindowsExecutable(standalone))
                {
                    return Path.GetFullPath(standalone);
                }
            }
        }
        catch
        {
            // Discovery is best effort; ConnectAsync reports the actionable error.
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<OpenCodeConnection> ConnectAsync(CancellationToken ct = default)
    {
        var cli = RequireCli();
        var output = await ExecuteAsync(new[] { "api", "get", "/api/info" }, ct).ConfigureAwait(false);

        try
        {
            using var document = JsonText.ParseDocument(output, InfoEndpoint);
            var root = document.RootElement;

            string? version = null;
            if (root.ValueKind == JsonValueKind.Object)
            {
                version = JsonRead.GetString(root, "version");
                if (string.IsNullOrWhiteSpace(version) &&
                    JsonRead.TryGetProperty(root, out var data, "data") &&
                    data.ValueKind == JsonValueKind.Object)
                {
                    version = JsonRead.GetString(data, "version");
                }
            }

            return new OpenCodeConnection(cli, string.IsNullOrWhiteSpace(version) ? "unknown" : version.Trim());
        }
        catch (InvalidDataException ex)
        {
            throw new OpenCodeUnavailableException(
                $"OpenCode CLI returned an unexpected response from '{InfoEndpoint}'. " +
                "Make sure OpenCode Desktop is running and the bundled CLI is not corrupted. " + ex.Message,
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpenCodeModel>> GetModelsAsync(CancellationToken ct = default)
    {
        var output = await ExecuteAsync(new[] { "api", "get", "/api/model" }, ct).ConfigureAwait(false);

        try
        {
            using var document = JsonText.ParseDocument(output, ModelEndpoint);
            var items = GetDataItems(document.RootElement, ModelEndpoint);
            var models = new List<OpenCodeModel>(items.Count);

            foreach (var item in items)
            {
                var model = MapModel(item);
                if (model is not null)
                {
                    models.Add(model);
                }
            }

            return models;
        }
        catch (InvalidDataException ex)
        {
            throw new OpenCodeUnavailableException(
                $"OpenCode CLI returned an unexpected model catalog from '{ModelEndpoint}'. " +
                "Retry after OpenCode Desktop has fully started. " + ex.Message,
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LiveAgent>> GetAgentsAsync(CancellationToken ct = default)
    {
        var output = await ExecuteAsync(new[] { "api", "get", "/api/agent" }, ct).ConfigureAwait(false);

        try
        {
            using var document = JsonText.ParseDocument(output, AgentEndpoint);
            var items = GetDataItems(document.RootElement, AgentEndpoint);
            var agents = new List<LiveAgent>(items.Count);

            foreach (var item in items)
            {
                var agent = MapAgent(item);
                if (agent is not null)
                {
                    agents.Add(agent);
                }
            }

            return agents;
        }
        catch (InvalidDataException ex)
        {
            throw new OpenCodeUnavailableException(
                $"OpenCode CLI returned an unexpected agent catalog from '{AgentEndpoint}'. " +
                "Retry after OpenCode Desktop has fully started. " + ex.Message,
                ex);
        }
    }

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await ExecuteAsync(new[] { "reload" }, ct).ConfigureAwait(false);
    }

    private static OpenCodeModel? MapModel(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var providerId = JsonRead.GetString(item, "providerID", "providerId", "provider");
        if (string.IsNullOrWhiteSpace(providerId) &&
            JsonRead.TryGetProperty(item, out var settings, "settings"))
        {
            providerId = JsonRead.GetString(settings, "provider", "providerID", "providerId");
        }

        var modelId = JsonRead.GetString(item, "modelID", "modelId", "id");
        if (string.IsNullOrWhiteSpace(modelId))
        {
            modelId = JsonRead.GetString(item, "name");
        }

        providerId = string.IsNullOrWhiteSpace(providerId) ? "unknown" : providerId.Trim();
        modelId = string.IsNullOrWhiteSpace(modelId) ? "unknown" : modelId.Trim();

        var displayName = JsonRead.GetString(item, "name");
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = modelId;
        }

        long contextLimit = 0;
        long outputLimit = 0;
        if (JsonRead.TryGetProperty(item, out var limit, "limit") && limit.ValueKind == JsonValueKind.Object)
        {
            contextLimit = JsonRead.GetInt64(limit, "context");
            outputLimit = JsonRead.GetInt64(limit, "output");
        }

        var tiers = ReadCostTiers(item);
        var isFree = tiers.Count > 0 && tiers.All(tier => tier.Input == 0m && tier.Output == 0m);

        var supportsTools = true;
        if (JsonRead.TryGetProperty(item, out var capabilities, "capabilities") &&
            capabilities.ValueKind == JsonValueKind.Object)
        {
            supportsTools = JsonRead.GetBool(capabilities, "tools") ?? true;
        }

        var status = JsonRead.GetString(item, "status");
        var enabled = JsonRead.GetBool(item, "enabled") ?? true;

        return new OpenCodeModel
        {
            ProviderId = providerId,
            ModelId = modelId,
            DisplayName = displayName.Trim(),
            ContextLimit = contextLimit,
            OutputLimit = outputLimit,
            InputPricePerMillion = tiers.Count > 0 ? tiers[0].Input : 0m,
            OutputPricePerMillion = tiers.Count > 0 ? tiers[0].Output : 0m,
            SupportsTools = supportsTools,
            IsFree = isFree,
            Status = string.IsNullOrWhiteSpace(status) ? "active" : status.Trim(),
            Enabled = enabled,
        };
    }

    private static List<(decimal Input, decimal Output)> ReadCostTiers(JsonElement item)
    {
        var tiers = new List<(decimal Input, decimal Output)>();
        if (!JsonRead.TryGetProperty(item, out var cost, "cost"))
        {
            return tiers;
        }

        if (cost.ValueKind == JsonValueKind.Array)
        {
            foreach (var tier in cost.EnumerateArray())
            {
                if (tier.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                tiers.Add((
                    JsonRead.GetDecimal(tier, "input"),
                    JsonRead.GetDecimal(tier, "output")));
            }
        }
        else if (cost.ValueKind == JsonValueKind.Object)
        {
            tiers.Add((
                JsonRead.GetDecimal(cost, "input"),
                JsonRead.GetDecimal(cost, "output")));
        }

        return tiers;
    }

    private static LiveAgent? MapAgent(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = JsonRead.GetString(item, "id", "name");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        string? providerId = null;
        string? modelId = null;

        if (JsonRead.TryGetProperty(item, out var model, "model"))
        {
            if (model.ValueKind == JsonValueKind.Object)
            {
                providerId = JsonRead.GetString(model, "providerID", "providerId", "provider");
                modelId = JsonRead.GetString(model, "id", "modelID", "modelId");
            }
            else if (model.ValueKind == JsonValueKind.String)
            {
                var reference = model.GetString();
                if (!string.IsNullOrWhiteSpace(reference))
                {
                    var slash = reference.IndexOf('/');
                    if (slash > 0 && slash < reference.Length - 1)
                    {
                        providerId = reference[..slash];
                        modelId = reference[(slash + 1)..];
                    }
                    else
                    {
                        modelId = reference;
                    }
                }
            }
        }

        providerId ??= JsonRead.GetString(item, "providerID", "providerId");
        modelId ??= JsonRead.GetString(item, "modelID", "modelId");

        return new LiveAgent
        {
            Id = id.Trim(),
            ProviderId = string.IsNullOrWhiteSpace(providerId) ? null : providerId.Trim(),
            ModelId = string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim(),
            Mode = TrimToNull(JsonRead.GetString(item, "mode")),
            Description = TrimToNull(JsonRead.GetString(item, "description")),
        };
    }

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<JsonElement> GetDataItems(JsonElement root, string context)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToArray();
        }

        if (root.ValueKind == JsonValueKind.Object &&
            JsonRead.TryGetProperty(root, out var data, "data"))
        {
            if (data.ValueKind == JsonValueKind.Array)
            {
                return data.EnumerateArray().ToArray();
            }

            if (data.ValueKind == JsonValueKind.Object)
            {
                return new[] { data };
            }
        }

        throw new InvalidDataException(
            $"{context}: expected a JSON array or an object containing a 'data' array.");
    }

    private string RequireCli()
    {
        if (string.IsNullOrWhiteSpace(CliPath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var desktopCli = string.IsNullOrWhiteSpace(localAppData)
                ? "%LOCALAPPDATA%\\Programs\\ai.opencode.desktop\\resources\\opencode-cli.exe"
                : Path.Combine(localAppData, "Programs", "ai.opencode.desktop", "resources", "opencode-cli.exe");
            var standalone = string.IsNullOrWhiteSpace(localAppData)
                ? "%LOCALAPPDATA%\\Programs\\opencode\\opencode.exe"
                : Path.Combine(localAppData, "Programs", "opencode", "opencode.exe");

            throw new OpenCodeUnavailableException(
                "OpenCode CLI was not found. Set the SWARMCAST_OPENCODE_CLI environment variable to " +
                "opencode-cli.exe, start OpenCode Desktop, or add 'opencode' to PATH. " +
                $"Looked for '{desktopCli}', 'opencode' on PATH, and '{standalone}'.");
        }

        if (!File.Exists(CliPath))
        {
            throw new OpenCodeUnavailableException(
                $"OpenCode CLI was not found at '{CliPath}'. Check SWARMCAST_OPENCODE_CLI or reinstall OpenCode Desktop.");
        }

        return CliPath;
    }

    private async Task<string> ExecuteAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var cli = RequireCli();

        var startInfo = new ProcessStartInfo
        {
            FileName = cli,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            WorkingDirectory = AppContext.BaseDirectory,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new OpenCodeUnavailableException(
                    $"OpenCode CLI '{cli}' did not start. Try restarting OpenCode Desktop.");
            }
        }
        catch (OpenCodeUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OpenCodeUnavailableException(
                $"Could not start OpenCode CLI '{cli}': {ex.Message}. " +
                "If this path is not executable, set SWARMCAST_OPENCODE_CLI to opencode-cli.exe.",
                ex);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ProcessTimeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await WaitForExitQuietlyAsync(process).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                throw;
            }

            throw new OpenCodeUnavailableException(
                $"OpenCode CLI timed out after {ProcessTimeout.TotalSeconds:0} seconds while running " +
                $"'{string.Join(" ", arguments)}'. Make sure OpenCode Desktop is running and responsive.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var detail = Summarize(stderr, stdout);
            throw new OpenCodeUnavailableException(
                $"OpenCode CLI failed while running '{string.Join(" ", arguments)}' " +
                $"(exit code {process.ExitCode}). {detail} " +
                "Check that OpenCode Desktop is running, then retry.");
        }

        return stdout;
    }

    private static async Task WaitForExitQuietlyAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(3))
                .ConfigureAwait(false);
        }
        catch
        {
            // The process was killed; best effort only.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process may have exited between the check and the kill.
        }
    }

    private static string Summarize(string stdout, string stderr)
    {
        var text = JsonText.Sanitize(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim();
        if (text.Length == 0)
        {
            return "(the CLI produced no diagnostic output)";
        }

        return text.Length <= 1200 ? text : text[..1200] + "…";
    }

    private static bool IsWindowsExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return string.IsNullOrEmpty(extension) ||
               extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".com", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindOnPath()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            startInfo.ArgumentList.Add("opencode");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(10_000))
            {
                TryKill(process);
                return null;
            }

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Trim().Trim('"');
                if (IsWindowsExecutable(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }
        catch
        {
            // PATH discovery is optional.
        }

        return null;
    }
}
