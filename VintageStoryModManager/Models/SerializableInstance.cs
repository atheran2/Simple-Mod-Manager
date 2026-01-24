namespace VintageStoryModManager.Models;

/// <summary>
///     Serializable representation of a game instance for sharing/export.
///     Contains instance metadata, mods, configurations, and categories.
/// </summary>
public sealed class SerializableInstance
{
    /// <summary>
    ///     Format version for future compatibility.
    /// </summary>
    public int FormatVersion { get; set; } = 1;

    /// <summary>
    ///     Instance name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    ///     User-provided description for sharing (shown to others when browsing).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Private notes (also included in export).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    ///     Target Vintage Story version for this instance.
    /// </summary>
    public string? TargetVsVersion { get; set; }

    /// <summary>
    ///     Instance icon/image as base64 string (optional, max ~500KB recommended).
    /// </summary>
    public string? ImageBase64 { get; set; }

    /// <summary>
    ///     Uploader's player name (set when uploading to cloud).
    /// </summary>
    public string? Uploader { get; set; }

    /// <summary>
    ///     Uploader's player UID (set when uploading to cloud).
    /// </summary>
    public string? UploaderId { get; set; }

    /// <summary>
    ///     List of mods in this instance.
    /// </summary>
    public List<SerializableInstanceMod>? Mods { get; set; }

    /// <summary>
    ///     Mod configurations (contents of ModConfig folder).
    ///     Reuses SerializableModConfiguration from SerializableTypes.cs.
    /// </summary>
    public List<SerializableModConfiguration>? Configurations { get; set; }

    /// <summary>
    ///     Category definitions for this instance.
    /// </summary>
    public List<SerializableCategory>? Categories { get; set; }

    /// <summary>
    ///     Maps mod IDs to category IDs.
    /// </summary>
    public Dictionary<string, string>? ModCategoryAssignments { get; set; }
}

/// <summary>
///     Serializable representation of a mod in an instance.
/// </summary>
public sealed class SerializableInstanceMod
{
    /// <summary>
    ///     Mod ID as listed in modinfo.json.
    /// </summary>
    public string? ModId { get; set; }

    /// <summary>
    ///     Version of the mod.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    ///     Display name of the mod (for preview purposes, not used during import).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    ///     Whether this mod is active (enabled) in the instance.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
///     Serializable representation of a mod category.
/// </summary>
public sealed class SerializableCategory
{
    /// <summary>
    ///     Unique identifier for the category.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    ///     Display name of the category.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    ///     Sort order for the category.
    /// </summary>
    public int Order { get; set; }
}
