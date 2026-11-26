using SwarmCastStudio.Models;

namespace SwarmCastStudio.Services;

/// <summary>
/// Read-only access to the live OpenCode model and agent catalog.
/// </summary>
public interface IOpenCodeBridge
{
    /// <summary>Verifies the CLI can reach the service and returns its version.</summary>
    Task<OpenCodeConnection> ConnectAsync(CancellationToken ct = default);

    /// <summary>Returns every model advertised by the running service.</summary>
    Task<IReadOnlyList<OpenCodeModel>> GetModelsAsync(CancellationToken ct = default);

    /// <summary>Returns every agent registered with the running service.</summary>
    Task<IReadOnlyList<LiveAgent>> GetAgentsAsync(CancellationToken ct = default);

    /// <summary>Asks the running service to reload its configuration.</summary>
    Task ReloadAsync(CancellationToken ct = default);
}

/// <summary>Identifies the CLI and service version used for a live connection.</summary>
public sealed record OpenCodeConnection(string CliPath, string Version);
