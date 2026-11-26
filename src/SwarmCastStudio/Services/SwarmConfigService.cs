using System.Globalization;
using System.Text;

namespace SwarmCastStudio.Services;

/// <summary>
/// Loads and saves opencode-swarm configuration files with timestamped backups
/// and an atomic temp-file replacement.
/// </summary>
public sealed class SwarmConfigService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Creates a service for a specific configuration file path.</summary>
    public SwarmConfigService(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Configuration path cannot be empty.", nameof(path));
        }

        Path = path;
    }

    /// <summary>The configuration file path.</summary>
    public string Path { get; }

    /// <summary>Whether the configuration file currently exists.</summary>
    public bool Exists => File.Exists(Path);

    /// <summary>Loads the file, or returns a new default document when it does not exist.</summary>
    public SwarmConfig Load()
    {
        if (!Exists)
        {
            return SwarmConfig.CreateNew();
        }

        var json = File.ReadAllText(Path, Encoding.UTF8);
        return SwarmConfig.FromJson(json);
    }

    /// <summary>
    /// Backs up an existing file, writes a UTF-8 (no BOM) temp file in the same directory,
    /// then atomically replaces the target.
    /// </summary>
    public void Save(SwarmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var target = System.IO.Path.GetFullPath(Path);
        var directory = System.IO.Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(target))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            var backup = target + ".bak-" + timestamp;
            var suffix = 1;
            while (File.Exists(backup))
            {
                backup = target + ".bak-" + timestamp + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            File.Copy(target, backup, overwrite: false);
        }

        var temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, config.ToJson(), Utf8NoBom);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // A stale temp file is harmless; do not mask the original failure.
                }
            }
        }
    }

    /// <summary>The default global configuration path under the user profile.</summary>
    public static string DefaultGlobalPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            }

            if (string.IsNullOrWhiteSpace(home))
            {
                home = AppContext.BaseDirectory;
            }

            return System.IO.Path.Combine(home, ".config", "opencode", "opencode-swarm.json");
        }
    }

    /// <summary>Returns <c>&lt;project&gt;\.opencode\opencode-swarm.json</c>.</summary>
    public static string ProjectPathFor(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new ArgumentException("Project directory cannot be empty.", nameof(projectDirectory));
        }

        return System.IO.Path.Combine(projectDirectory.Trim(), ".opencode", "opencode-swarm.json");
    }
}
