namespace SwarmCastStudio.Models;

/// <summary>
/// An agent currently registered with the running OpenCode service.
/// </summary>
public sealed class LiveAgent
{
    /// <summary>Agent identifier as registered by OpenCode.</summary>
    public required string Id { get; init; }

    /// <summary>Resolved model provider, when the agent has a model.</summary>
    public string? ProviderId { get; init; }

    /// <summary>Resolved model identifier, when the agent has a model.</summary>
    public string? ModelId { get; init; }

    /// <summary>Agent mode, for example <c>primary</c> or <c>subagent</c>.</summary>
    public string? Mode { get; init; }

    /// <summary>Agent description supplied by the service.</summary>
    public string? Description { get; init; }

    /// <summary>The resolved <c>provider/model</c> reference, if complete.</summary>
    public string? ModelRef => string.IsNullOrWhiteSpace(ProviderId) || string.IsNullOrWhiteSpace(ModelId)
        ? null
        : $"{ProviderId}/{ModelId}";
}
