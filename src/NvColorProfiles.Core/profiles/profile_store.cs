using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace nv_color_profiles.core.profiles;

/// <summary>
/// Loads and saves the <see cref="app_config"/> as JSON. Missing config yields a fresh default;
/// a corrupt file is backed up and replaced with the default rather than crashing. Saves are
/// atomic (write to a temp file, then replace).
/// </summary>
public sealed class profile_store
{
    private static readonly JsonSerializerOptions json_options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string path;
    private readonly ILogger<profile_store> log;

    public profile_store(string path, ILogger<profile_store> log)
    {
        this.path = path;
        this.log = log;
    }

    public app_config load()
    {
        if (!File.Exists(path))
        {
            log.LogInformation("No config at {path}; using defaults", path);
            return app_config.create_default();
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<app_config>(json, json_options);
            if (config is null)
            {
                throw new JsonException("config deserialized to null");
            }
            return config.sanitized().with_default_ensured();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Config at {path} is unreadable; backing it up and using defaults", path);
            try_backup_corrupt();
            return app_config.create_default();
        }
    }

    public void save(app_config config)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, json_options);
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // don't leave a stray temp file behind on a failed write/move
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
                // best-effort cleanup
            }
            throw;
        }
    }

    /// <summary>Serializes a config for export — same JSON shape as the on-disk file.</summary>
    public static string to_json(app_config config) => JsonSerializer.Serialize(config, json_options);

    /// <summary>Parses an exported config; returns null when the JSON is missing or invalid.</summary>
    public static app_config? from_json(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<app_config>(json, json_options)?.sanitized().with_default_ensured();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void try_backup_corrupt()
    {
        try
        {
            var backup = path + ".corrupt";
            File.Copy(path, backup, overwrite: true);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not back up corrupt config");
        }
    }
}
