namespace SwarmCastStudio.Models;

/// <summary>
/// Static description of one of the fixed opencode-swarm roles.
/// </summary>
/// <param name="Name">Configuration key used under <c>agents</c>.</param>
/// <param name="DisplayName">Human-readable name for the UI.</param>
/// <param name="Category">Grouping category from the swarm specification.</param>
/// <param name="Description">Short description of the role.</param>
/// <param name="Core">Whether this role belongs to the core pipeline.</param>
/// <param name="Gate">Feature gate required for conditional roles.</param>
public sealed record AgentDefinition(
    string Name,
    string DisplayName,
    string Category,
    string Description,
    bool Core,
    AgentGate Gate);
