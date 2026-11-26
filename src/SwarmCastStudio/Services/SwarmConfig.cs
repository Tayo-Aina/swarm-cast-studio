using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SwarmCastStudio.Services;

/// <summary>
/// Editable representation of an opencode-swarm configuration file.
/// Unknown root and per-agent keys are preserved.
/// </summary>
public sealed class SwarmConfig
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private SwarmConfig(JsonObject root)
    {
        Root = root;
    }

    /// <summary>The complete configuration document.</summary>
    public JsonObject Root { get; }

    /// <summary>Parses configuration JSON, rejecting a non-object root.</summary>
    public static SwarmConfig FromJson(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        try
        {
            var node = JsonNode.Parse(
                json,
                new JsonNodeOptions { PropertyNameCaseInsensitive = false },
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });

            if (node is not JsonObject root)
            {
                throw new InvalidDataException("The opencode-swarm configuration root must be a JSON object.");
            }

            return new SwarmConfig(root);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The opencode-swarm configuration is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Creates the default document used when no configuration file exists.</summary>
    public static SwarmConfig CreateNew()
    {
        var root = new JsonObject
        {
            ["$schema"] = "https://unpkg.com/opencode-swarm@7.205.10/opencode-swarm.schema.json",
            ["agents"] = new JsonObject(),
            ["max_iterations"] = 5,
        };

        return new SwarmConfig(root);
    }

    /// <summary>Serializes the configuration with two-space indentation and a trailing newline.</summary>
    public string ToJson()
    {
        var json = Root.ToJsonString(SerializerOptions);
        return json.EndsWith('\n') ? json : json + Environment.NewLine;
    }

    /// <summary>Returns whether an agent is enabled. Unknown agents default to enabled.</summary>
    public bool IsAgentEnabled(string name)
    {
        var agent = GetAgentObject(name);
        if (agent is null)
        {
            return true;
        }

        if (agent.TryGetPropertyValue("disabled", out var disabled))
        {
            return ReadBool(disabled) != true;
        }

        return true;
    }

    /// <summary>Returns the primary model reference for an agent, or <see langword="null"/>.</summary>
    public string? GetAgentModel(string name)
    {
        var agent = GetAgentObject(name);
        if (agent is null || !agent.TryGetPropertyValue("model", out var model))
        {
            return null;
        }

        return ReadString(model);
    }

    /// <summary>Returns the configured fallback model references for an agent.</summary>
    public IReadOnlyList<string> GetAgentFallbacks(string name)
    {
        var agent = GetAgentObject(name);
        if (agent is null ||
            !agent.TryGetPropertyValue("fallback_models", out var fallbacks) ||
            fallbacks is not JsonArray array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(array.Count);
        foreach (var fallback in array)
        {
            var text = ReadString(fallback);
            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(text);
            }
        }

        return result;
    }

    /// <summary>Returns an agent temperature, or <see langword="null"/> when unset.</summary>
    public double? GetAgentTemperature(string name)
    {
        var agent = GetAgentObject(name);
        if (agent is null || !agent.TryGetPropertyValue("temperature", out var temperature))
        {
            return null;
        }

        return ReadDouble(temperature);
    }

    /// <summary>
    /// Merges agent settings, preserving any unknown keys already present on the agent object.
    /// Empty model, fallback, and temperature values remove the corresponding keys.
    /// </summary>
    public void SetAgent(
        string name,
        bool enabled,
        string? model,
        IReadOnlyList<string> fallbacks,
        double? temperature)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Agent name cannot be empty.", nameof(name));
        }

        var agents = EnsureObject(Root, "agents");
        if (agents[name] is not JsonObject agent)
        {
            agent = new JsonObject();
            agents[name] = agent;
        }

        if (enabled)
        {
            agent.Remove("disabled");
        }
        else
        {
            agent["disabled"] = true;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            agent.Remove("model");
        }
        else
        {
            agent["model"] = model.Trim();
        }

        var normalizedFallbacks = new List<string>();
        if (fallbacks is not null)
        {
            foreach (var fallback in fallbacks)
            {
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    normalizedFallbacks.Add(fallback.Trim());
                }
            }
        }

        if (normalizedFallbacks.Count == 0)
        {
            agent.Remove("fallback_models");
        }
        else
        {
            var array = new JsonArray();
            foreach (var fallback in normalizedFallbacks)
            {
                array.Add(fallback);
            }

            agent["fallback_models"] = array;
        }

        if (temperature is { } value && double.IsFinite(value))
        {
            agent["temperature"] = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            agent.Remove("temperature");
        }
    }

    /// <summary>Reads a boolean feature gate such as <c>ui_review.enabled</c>.</summary>
    public bool GetFeature(string dottedPath)
    {
        var node = FindNode(dottedPath);
        return ReadBool(node) ?? false;
    }

    /// <summary>Writes a boolean feature gate, creating intermediate objects as needed.</summary>
    public void SetFeature(string dottedPath, bool value)
    {
        if (string.IsNullOrWhiteSpace(dottedPath))
        {
            throw new ArgumentException("Feature path cannot be empty.", nameof(dottedPath));
        }

        var segments = dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException("Feature path cannot be empty.", nameof(dottedPath));
        }

        var current = Root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            var segment = segments[index];
            if (current[segment] is not JsonObject child)
            {
                child = new JsonObject();
                current[segment] = child;
            }

            current = child;
        }

        current[segments[^1]] = value;
    }

    private JsonObject? GetAgentObject(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Root["agents"] is JsonObject agents && agents[name.Trim()] is JsonObject agent
            ? agent
            : null;
    }

    private JsonNode? FindNode(string dottedPath)
    {
        if (string.IsNullOrWhiteSpace(dottedPath))
        {
            return null;
        }

        var segments = dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        JsonNode? current = Root;
        foreach (var segment in segments)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
            {
                return null;
            }
        }

        return current;
    }

    private static JsonObject EnsureObject(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[propertyName] = created;
        return created;
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        return null;
    }

    private static bool? ReadBool(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        if (value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed))
        {
            return parsed;
        }

        if (value.TryGetValue<long>(out var number))
        {
            return number != 0;
        }

        return null;
    }

    private static double? ReadDouble(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var number) && double.IsFinite(number))
        {
            return number;
        }

        if (value.TryGetValue<decimal>(out var decimalNumber))
        {
            return (double)decimalNumber;
        }

        if (value.TryGetValue<long>(out var integer))
        {
            return integer;
        }

        if (value.TryGetValue<int>(out var smallInteger))
        {
            return smallInteger;
        }

        if (value.TryGetValue<string>(out var text) &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            double.IsFinite(parsed))
        {
            return parsed;
        }

        return null;
    }
}
