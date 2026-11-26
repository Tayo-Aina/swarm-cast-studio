using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwarmCastStudio.Services;

/// <summary>
/// Defensive JSON reading shared by the CLI bridge and configuration loader.
/// Handles BOMs, ANSI colour codes, leading log noise, comments, and trailing commas.
/// </summary>
internal static class JsonText
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Regex AnsiEscapePattern = new(
        @"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07]*(?:\x07|\x1B\\))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Removes BOM characters and ANSI escape sequences from CLI output.</summary>
    public static string Sanitize(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var text = raw.Replace("\uFEFF", string.Empty, StringComparison.Ordinal);
        return AnsiEscapePattern.Replace(text, string.Empty);
    }

    /// <summary>
    /// Parses the first complete JSON value found in <paramref name="raw"/>.
    /// Throws <see cref="InvalidDataException"/> with a short output preview on failure.
    /// </summary>
    public static JsonDocument ParseDocument(string? raw, string context)
    {
        var text = Sanitize(raw);
        if (text.Length == 0)
        {
            throw new InvalidDataException($"{context}: the command returned no output.");
        }

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('{' or '['))
            {
                continue;
            }

            var candidate = ExtractBalancedValue(text, index);
            if (candidate is null)
            {
                continue;
            }

            try
            {
                return JsonDocument.Parse(candidate, DocumentOptions);
            }
            catch (JsonException)
            {
                // Keep scanning: the leading noise may itself contain braces.
            }
        }

        throw new InvalidDataException(
            $"{context}: could not find a valid JSON value in the command output. Output began with: {Preview(text)}");
    }

    private static string? ExtractBalancedValue(string text, int start)
    {
        var closers = new Stack<char>();
        var inString = false;
        var escaped = false;

        for (var index = start; index < text.Length; index++)
        {
            var ch = text[index];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    closers.Push('}');
                    break;
                case '[':
                    closers.Push(']');
                    break;
                case '}':
                case ']':
                    if (closers.Count == 0 || closers.Peek() != ch)
                    {
                        return null;
                    }

                    closers.Pop();
                    if (closers.Count == 0)
                    {
                        return text.Substring(start, index - start + 1);
                    }

                    break;
            }
        }

        return null;
    }

    private static string Preview(string text)
    {
        var flattened = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return flattened.Length <= 240 ? flattened : flattened[..240] + "…";
    }
}

/// <summary>Case-tolerant accessors for JSON objects returned by the OpenCode CLI.</summary>
internal static class JsonRead
{
    public static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        return false;
    }

    public static string? GetString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public static bool? GetBool(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
                return bool.TryParse(value.GetString(), out var parsed) ? parsed : null;
            case JsonValueKind.Number:
                return value.TryGetInt64(out var number) ? number != 0 : null;
            default:
                return null;
        }
    }

    public static long GetInt64(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt64(out var integer))
            {
                return integer;
            }

            if (value.TryGetDouble(out var number) &&
                double.IsFinite(number) &&
                number is >= long.MinValue and <= long.MaxValue)
            {
                return (long)number;
            }
        }
        else if (value.ValueKind == JsonValueKind.String &&
                 long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    public static decimal GetDecimal(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return 0m;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetDecimal(out var dec))
            {
                return dec;
            }

            if (value.TryGetDouble(out var number) && double.IsFinite(number))
            {
                try
                {
                    return (decimal)number;
                }
                catch (OverflowException)
                {
                    return 0m;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.String &&
                 decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0m;
    }
}
