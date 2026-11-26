using SwarmCastStudio.Models;

namespace SwarmCastStudio.Services;

/// <summary>
/// The fixed roster of opencode-swarm agents. The app edits configuration for these
/// roles; it never invents new agent roles.
/// </summary>
public static class AgentCatalog
{
    private static readonly AgentDefinition[] Definitions =
    {
        // Core pipeline.
        new("architect", "Architect", "Core pipeline",
            "Plans architecture, decomposes work, and makes structural decisions.", true, AgentGate.None),
        new("explorer", "Explorer", "Core pipeline",
            "Maps existing code, files, and dependencies before changes are planned.", true, AgentGate.None),
        new("coder", "Coder", "Core pipeline",
            "Implements features and fixes using the approved plan.", true, AgentGate.None),
        new("reviewer", "Reviewer", "Core pipeline",
            "Reviews changes for correctness, maintainability, and regressions.", true, AgentGate.None),
        new("test_engineer", "Test Engineer", "Core pipeline",
            "Writes and runs tests, reproduces bugs, and verifies fixes.", true, AgentGate.None),
        new("critic", "Critic", "Core pipeline",
            "Challenges plans and designs before implementation begins.", true, AgentGate.None),
        new("sme", "Subject Matter Expert", "Core pipeline",
            "Supplies specialist domain knowledge for technical decisions.", true, AgentGate.None),
        new("docs", "Docs Writer", "Core pipeline",
            "Writes and updates user and developer documentation.", true, AgentGate.None),

        // Critics and oversight.
        new("critic_finding_validator", "Finding Validator", "Critics & oversight",
            "Validates review findings and rejects unsupported claims.", false, AgentGate.None),
        new("critic_oversight", "Oversight Critic", "Critics & oversight",
            "Audits workflow compliance and process quality.", false, AgentGate.None),
        new("critic_sounding_board", "Sounding Board", "Critics & oversight",
            "Stress-tests proposals and surfaces hidden risks before commitment.", false, AgentGate.None),
        new("critic_drift_verifier", "Drift Verifier", "Critics & oversight",
            "Checks that implementation stays aligned with the approved plan.", false, AgentGate.None),
        new("critic_hallucination_verifier", "Hallucination Verifier", "Critics & oversight",
            "Verifies factual claims and flags invented or unsupported details.", false, AgentGate.None),
        new("critic_architecture_supervisor", "Architecture Supervisor", "Critics & oversight",
            "Guards architectural integrity across changes and phases.", false, AgentGate.None),

        // Support and learning.
        new("researcher", "Researcher", "Support & learning",
            "Gathers external evidence and summarizes relevant sources.", false, AgentGate.None),
        new("skill_improver", "Skill Improver", "Support & learning",
            "Improves reusable skills from run feedback and lessons learned.", false, AgentGate.None),
        new("spec_writer", "Spec Writer", "Support & learning",
            "Turns requirements into precise, testable specifications.", false, AgentGate.None),
        new("curator_init", "Init Curator", "Support & learning",
            "Initializes durable project memory and context at startup.", false, AgentGate.None),
        new("curator_phase", "Phase Curator", "Support & learning",
            "Captures phase learnings and updates working memory.", false, AgentGate.None),
        new("curator_postmortem", "Postmortem Curator", "Support & learning",
            "Runs postmortems and records root causes for future work.", false, AgentGate.None),
        new("curator_consolidation", "Consolidation Curator", "Support & learning",
            "Consolidates memory and removes redundant or stale knowledge.", false, AgentGate.None),

        // Conditional roles.
        new("designer", "UI Designer", "Conditional",
            "Designs UI flows, layouts, and visual direction.", false, AgentGate.UiReview),
        new("docs_design", "Design Docs Writer", "Conditional",
            "Produces design documents and architecture write-ups.", false, AgentGate.DesignDocs),
        new("council_generalist", "Council Generalist", "Conditional",
            "Represents broad product and engineering judgment in councils.", false, AgentGate.CouncilGeneral),
        new("council_skeptic", "Council Skeptic", "Conditional",
            "Challenges council consensus and looks for failure modes.", false, AgentGate.CouncilGeneral),
        new("council_domain_expert", "Council Domain Expert", "Conditional",
            "Brings specialist domain context to council decisions.", false, AgentGate.CouncilGeneral),
    };

    private static readonly IReadOnlyList<string> RequiredCoreNames =
        Array.AsReadOnly(new[] { "architect", "coder", "reviewer", "test_engineer" });

    /// <summary>All 26 swarm roles, in display order.</summary>
    public static IReadOnlyList<AgentDefinition> All { get; } = Array.AsReadOnly(Definitions);

    /// <summary>Finds a role by its configuration key, ignoring case.</summary>
    public static AgentDefinition? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (var definition in Definitions)
        {
            if (string.Equals(definition.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return definition;
            }
        }

        return null;
    }

    /// <summary>Core roles that should not normally be disabled.</summary>
    public static IReadOnlyList<string> CoreRequiredNames { get; } = RequiredCoreNames;
}
