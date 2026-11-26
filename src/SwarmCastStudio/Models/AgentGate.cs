namespace SwarmCastStudio.Models;

/// <summary>
/// Feature gate that controls whether a conditional swarm agent is registered.
/// </summary>
public enum AgentGate
{
    /// <summary>No gate; the agent is always registered.</summary>
    None,

    /// <summary>Requires <c>ui_review.enabled</c>.</summary>
    UiReview,

    /// <summary>Requires <c>design_docs.enabled</c>.</summary>
    DesignDocs,

    /// <summary>Requires <c>council.general.enabled</c>.</summary>
    CouncilGeneral,
}
