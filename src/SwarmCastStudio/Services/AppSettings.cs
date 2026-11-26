using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SwarmCastStudio.Services;

/// <summary>
/// Small, user-local preferences. Loading and saving are best effort; neither throws.
/// </summary>
public sealed class AppSettings
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Target kind: <c>global</c> or <c>project</c>.</summary>
    public string Target { get; set; } = "global";

    /// <summary>Selected project directory when <see cref="Target"/> is <c>project</c>.</summary>
    public string? ProjectDirectory { get; set; }

    /// <summary>The settings file under <c>%LOCALAPPDATA%\SwarmCastStudio\settings.json</c>.</summary>
    public static string SettingsPath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(localAppData))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                localAppData = Path.Combine(home, "AppData", "Local");
            }

            return Path.Combine(localAppData, "SwarmCastStudio", "settings.json");
        }
    }

    /// <summary>Loads settings, returning defaults for missing, unreadable, or malformed files.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppSettings();
            }

            using var document = JsonText.ParseDocument(json, "settings");
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new AppSettings();
            }

            var target = JsonRead.GetString(root, "target");
            var projectDirectory = JsonRead.GetString(root, "projectDirectory", "project_directory");
            var settings = new AppSettings
            {
                Target = string.Equals(target?.Trim(), "project", StringComparison.OrdinalIgnoreCase)
                    ? "project"
                    : "global",
                ProjectDirectory = string.IsNullOrWhiteSpace(projectDirectory) ? null : projectDirectory.Trim(),
            };

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>Saves settings; failures are ignored so the UI can keep running.</summary>
    public void Save()
    {
        try
        {
            var path = SettingsPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var root = new JsonObject
            {
                ["target"] = string.Equals(Target, "project", StringComparison.OrdinalIgnoreCase)
                    ? "project"
                    : "global",
                ["projectDirectory"] = ProjectDirectory,
            };

            var json = root.ToJsonString(SerializerOptions);
            if (!json.EndsWith('\n'))
            {
                json += Environment.NewLine;
            }

            File.WriteAllText(path, json, Utf8NoBom);
        }
        catch
        {
            // Settings are a convenience; never fail the app because they could not be written.
        }
    }
}
