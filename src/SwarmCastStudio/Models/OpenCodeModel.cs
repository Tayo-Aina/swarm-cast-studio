using System.Globalization;

namespace SwarmCastStudio.Models;

/// <summary>
/// A model advertised by the live OpenCode service, normalized for the model picker.
/// </summary>
public sealed class OpenCodeModel
{
    /// <summary>Provider identifier, for example <c>opencode-go</c>.</summary>
    public required string ProviderId { get; init; }

    /// <summary>Model identifier within the provider.</summary>
    public required string ModelId { get; init; }

    /// <summary>Human-readable model name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Maximum context window in tokens.</summary>
    public long ContextLimit { get; init; }

    /// <summary>Maximum output tokens.</summary>
    public long OutputLimit { get; init; }

    /// <summary>Input price from the first advertised cost tier, per million tokens.</summary>
    public decimal InputPricePerMillion { get; init; }

    /// <summary>Output price from the first advertised cost tier, per million tokens.</summary>
    public decimal OutputPricePerMillion { get; init; }

    /// <summary>Whether the model advertises tool calling support.</summary>
    public bool SupportsTools { get; init; } = true;

    /// <summary>Whether every advertised cost tier is zero.</summary>
    public bool IsFree { get; init; }

    /// <summary>Service-reported status, for example <c>active</c>.</summary>
    public string Status { get; init; } = "active";

    /// <summary>Whether the model is enabled in the live service.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The <c>provider/model</c> reference used in swarm configuration.</summary>
    public string Ref => $"{ProviderId}/{ModelId}";

    /// <summary>Compact context-window label for the UI.</summary>
    public string ContextLabel => ContextLimit >= 1_000_000
        ? string.Format(CultureInfo.InvariantCulture, "{0:0.#}M ctx", ContextLimit / 1_000_000.0)
        : string.Format(CultureInfo.InvariantCulture, "{0:0}K ctx", ContextLimit / 1000.0);

    /// <summary>Compact price label for the UI.</summary>
    public string PriceLabel => IsFree
        ? "FREE"
        : string.Format(
            CultureInfo.InvariantCulture,
            "${0:0.##}/${1:0.##}",
            InputPricePerMillion,
            OutputPricePerMillion);
}
