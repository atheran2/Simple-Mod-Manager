using System;
using System.Text.Json.Serialization;

namespace VintageStoryModManager.Models;

/// <summary>
///     Represents an isolated game instance with its own mods, saves, and configurations.
/// </summary>
public sealed class GameInstance
{
    /// <summary>
    ///     Unique identifier for this instance.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    ///     Display name for this instance.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    ///     Full path to the instance folder (contains Mods, Saves, ModConfig, etc.).
    ///     Note: This is overridden at load time with the actual directory location
    ///     for portability - the stored value in instance.json may be outdated.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    ///     Optional override for the Vintage Story installation directory.
    ///     If null, uses the global game directory setting.
    /// </summary>
    public string? GameDirectory { get; set; }

    /// <summary>
    ///     Optional target Vintage Story version for this instance.
    /// </summary>
    public string? TargetVsVersion { get; set; }

    /// <summary>
    ///     When this instance was created.
    /// </summary>
    public DateTime Created { get; init; } = DateTime.UtcNow;

    /// <summary>
    ///     When this instance was last launched.
    /// </summary>
    public DateTime? LastPlayed { get; set; }

    /// <summary>
    ///     Optional path to a custom icon for this instance.
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    ///     Optional notes or description for this instance.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    ///     Total playtime in seconds (optional tracking).
    /// </summary>
    public long TotalPlaytimeSeconds { get; set; }

    /// <summary>
    ///     Gets the path to the Mods folder for this instance.
    /// </summary>
    [JsonIgnore]
    public string ModsPath => System.IO.Path.Combine(Path, "Mods");

    /// <summary>
    ///     Gets the path to the Saves folder for this instance.
    /// </summary>
    [JsonIgnore]
    public string SavesPath => System.IO.Path.Combine(Path, "Saves");

    /// <summary>
    ///     Gets the path to the ModConfig folder for this instance.
    /// </summary>
    [JsonIgnore]
    public string ModConfigPath => System.IO.Path.Combine(Path, "ModConfig");

    /// <summary>
    ///     Gets the path to the Logs folder for this instance.
    /// </summary>
    [JsonIgnore]
    public string LogsPath => System.IO.Path.Combine(Path, "Logs");

    /// <summary>
    ///     Gets the path to the Cache folder for this instance.
    /// </summary>
    [JsonIgnore]
    public string CachePath => System.IO.Path.Combine(Path, "Cache");

    /// <summary>
    ///     Gets the path to the instance.json metadata file.
    /// </summary>
    [JsonIgnore]
    public string MetadataFilePath => System.IO.Path.Combine(Path, "instance.json");

    public override string ToString() => Name;
}
