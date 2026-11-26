using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SwarmCastStudio.Models;

namespace SwarmCastStudio.Services;

/// <summary>
/// Headless verification entry point used by <c>--smoke</c> and <c>--smoke-write</c>.
/// Never throws; writes <c>smoke-result.json</c> even when the run fails.
/// </summary>
public static class SmokeRunner
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Runs a smoke pass and returns a process exit code (0 success, 1 failure).
    /// </summary>
    /// <param name="args">Raw process arguments.</param>
    /// <param name="outputDirectory">Directory that receives <c>smoke-result.json</c>.</param>
    public static async Task<int> RunAsync(string[] args, string outputDirectory)
    {
        args ??= Array.Empty<string>();

        var writeMode = TryGetWritePath(args, out var requestedWritePath, out var argumentError);

        var cliPath = (string?)null;
        var version = (string?)null;
        var models = 0;
        var agents = 0;
        var swarmAgents = AgentCatalog.All.Count;
        var liveSwarmAgents = 0;
        var configPath = SwarmConfigService.DefaultGlobalPath;
        var configExists = false;
        string? writePath = null;
        string? writtenModel = null;
        var ok = false;
        var error = argumentError;

        var resolvedOutputDirectory = ResolveOutputDirectory(outputDirectory);

        try
        {
            if (error is not null)
            {
                throw new InvalidOperationException(error);
            }

            if (writeMode)
            {
                if (string.IsNullOrWhiteSpace(requestedWritePath))
                {
                    throw new InvalidOperationException(
                        "Usage: SwarmCastStudio.exe --smoke-write <path-to-opencode-swarm.json>");
                }

                try
                {
                    writePath = Path.GetFullPath(requestedWritePath);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Invalid --smoke-write path '{requestedWritePath}': {ex.Message}", ex);
                }

                EnsureNotLiveGlobalConfig(writePath);
                configPath = writePath;
            }

            var bridge = new OpenCodeBridge();
            cliPath = bridge.CliPath;

            var connection = await bridge.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            cliPath = connection.CliPath;
            version = connection.Version;

            var modelList = await bridge.GetModelsAsync(CancellationToken.None).ConfigureAwait(false);
            var agentList = await bridge.GetAgentsAsync(CancellationToken.None).ConfigureAwait(false);

            // OpenCode can briefly return an empty or partial catalog just after a reload.
            // Retry exactly once after a short settle delay, then evaluate the second result.
            if (modelList.Count == 0 || agentList.Count <= 7)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500), CancellationToken.None).ConfigureAwait(false);

                connection = await bridge.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
                cliPath = connection.CliPath;
                version = connection.Version;
                modelList = await bridge.GetModelsAsync(CancellationToken.None).ConfigureAwait(false);
                agentList = await bridge.GetAgentsAsync(CancellationToken.None).ConfigureAwait(false);
            }

            models = modelList.Count;
            agents = agentList.Count;
            liveSwarmAgents = CountLiveSwarmAgents(agentList);
            swarmAgents = AgentCatalog.All.Count;

            if (models == 0)
            {
                throw new InvalidOperationException(
                    "OpenCode returned no models. Make sure OpenCode Desktop is running and signed in.");
            }

            if (agents == 0)
            {
                throw new InvalidOperationException(
                    "OpenCode returned no registered agents. Make sure the OpenCode Desktop service is ready.");
            }

            var configService = new SwarmConfigService(configPath);
            configExists = configService.Exists;
            var config = configService.Load();

            if (writeMode)
            {
                var chosen = modelList
                    .Where(model => model.SupportsTools)
                    .OrderByDescending(model =>
                        string.Equals(model.ProviderId, "opencode-go", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (chosen is null)
                {
                    throw new InvalidOperationException(
                        "No tool-capable model was returned by OpenCode, so agents.coder.model cannot be set.");
                }

                writtenModel = chosen.Ref;
                config.SetAgent(
                    "coder",
                    config.IsAgentEnabled("coder"),
                    chosen.Ref,
                    config.GetAgentFallbacks("coder"),
                    config.GetAgentTemperature("coder"));

                configService.Save(config);
                configExists = configService.Exists;

                var reloaded = configService.Load();
                var roundTripped = reloaded.GetAgentModel("coder");
                if (!string.Equals(roundTripped, writtenModel, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Round-trip verification failed: expected coder model '{writtenModel}', " +
                        $"but reloaded '{roundTripped ?? "(missing)"}' from '{configPath}'.");
                }
            }

            ok = true;
            error = null;
        }
        catch (OperationCanceledException)
        {
            error = "The smoke run was canceled before it completed.";
        }
        catch (Exception ex)
        {
            error = Describe(ex);
        }

        var result = BuildResult(
            ok,
            cliPath,
            version,
            models,
            agents,
            swarmAgents,
            liveSwarmAgents,
            configPath,
            configExists,
            writeMode,
            writePath,
            writtenModel,
            error);

        var json = result.ToJsonString(SerializerOptions);

        try
        {
            Directory.CreateDirectory(resolvedOutputDirectory);
            var resultPath = Path.Combine(resolvedOutputDirectory, "smoke-result.json");
            File.WriteAllText(resultPath, json + Environment.NewLine, Utf8NoBom);
        }
        catch (Exception ex)
        {
            ok = false;
            result["ok"] = false;
            result["error"] = CombineErrors(error, $"Could not write smoke-result.json: {ex.Message}");
            json = result.ToJsonString(SerializerOptions);
            TryWriteConsole(json);
            return 1;
        }

        TryWriteConsole(json);
        return ok ? 0 : 1;
    }

    private static JsonObject BuildResult(
        bool ok,
        string? cliPath,
        string? version,
        int models,
        int agents,
        int swarmAgents,
        int liveSwarmAgents,
        string configPath,
        bool configExists,
        bool writeMode,
        string? path,
        string? writtenModel,
        string? error)
    {
        var result = new JsonObject
        {
            ["ok"] = ok,
            ["cliPath"] = cliPath,
            ["version"] = version,
            ["models"] = models,
            ["agents"] = agents,
            ["swarmAgents"] = swarmAgents,
            ["liveSwarmAgents"] = liveSwarmAgents,
            ["configPath"] = configPath,
            ["configExists"] = configExists,
        };

        if (writeMode)
        {
            result["path"] = path;
        }

        if (!string.IsNullOrWhiteSpace(writtenModel))
        {
            result["writtenModel"] = writtenModel;
        }

        result["error"] = error;
        return result;
    }

    private static int CountLiveSwarmAgents(IReadOnlyList<LiveAgent> liveAgents)
    {
        var liveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var agent in liveAgents)
        {
            liveIds.Add(agent.Id);
        }

        var count = 0;
        foreach (var definition in AgentCatalog.All)
        {
            if (liveIds.Contains(definition.Name))
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryGetWritePath(string[] args, out string? path, out string? error)
    {
        path = null;
        error = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.StartsWith("--smoke-write=", StringComparison.OrdinalIgnoreCase))
            {
                path = argument["--smoke-write=".Length..];
                if (string.IsNullOrWhiteSpace(path))
                {
                    error = "The --smoke-write argument requires a file path.";
                    path = null;
                }

                continue;
            }

            if (!argument.Equals("--smoke-write", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Length ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = "The --smoke-write argument requires a file path: --smoke-write <path>";
                continue;
            }

            path = args[++index];
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "The --smoke-write argument requires a non-empty file path.";
                path = null;
            }
        }

        return path is not null;
    }

    private static string ResolveOutputDirectory(string outputDirectory)
    {
        try
        {
            var candidate = string.IsNullOrWhiteSpace(outputDirectory)
                ? AppContext.BaseDirectory
                : outputDirectory;
            var full = Path.GetFullPath(candidate);
            Directory.CreateDirectory(full);
            return full;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }

    private static void EnsureNotLiveGlobalConfig(string path)
    {
        var global = Path.GetFullPath(SwarmConfigService.DefaultGlobalPath);
        if (string.Equals(Path.GetFullPath(path), global, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to run --smoke-write against the live global swarm config. " +
                "Pass a temporary or project config path instead.");
        }
    }

    private static string Describe(Exception exception)
    {
        if (!string.IsNullOrWhiteSpace(exception.Message))
        {
            return exception.Message.Trim();
        }

        return exception.GetType().Name;
    }

    private static string CombineErrors(string? first, string second)
    {
        return string.IsNullOrWhiteSpace(first) ? second : first + " " + second;
    }

    private static void TryWriteConsole(string text)
    {
        try
        {
            Console.WriteLine(text);
        }
        catch
        {
            // A WinExe may not have a console attached; the result file is authoritative.
        }
    }
}
