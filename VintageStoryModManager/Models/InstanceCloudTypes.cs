namespace VintageStoryModManager.Models;

/// <summary>
///     Represents a cloud instance owned by the current user for management operations.
/// </summary>
public sealed class CloudInstanceManagementEntry
{
    public CloudInstanceManagementEntry(
        string slotKey,
        string slotLabel,
        string? name,
        string? description,
        string? targetVsVersion,
        int modCount,
        bool isPublic,
        string? cachedContent)
    {
        SlotKey = slotKey ?? throw new ArgumentNullException(nameof(slotKey));
        SlotLabel = slotLabel ?? throw new ArgumentNullException(nameof(slotLabel));
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        TargetVsVersion = string.IsNullOrWhiteSpace(targetVsVersion) ? null : targetVsVersion.Trim();
        ModCount = modCount;
        IsPublic = isPublic;
        CachedContent = cachedContent;
    }

    public string SlotKey { get; }
    public string SlotLabel { get; }
    public string? Name { get; }
    public string? Description { get; }
    public string? TargetVsVersion { get; }
    public int ModCount { get; }
    public bool IsPublic { get; }
    public string? CachedContent { get; }

    public string EffectiveName => Name ?? "Unnamed Instance";
    public string ModsSummary => ModCount == 0 ? "No mods" : ModCount == 1 ? "1 mod" : $"{ModCount} mods";
    public string VisibilityDisplay => IsPublic ? "Public" : "Unlisted";

    public override string ToString()
    {
        return !string.IsNullOrWhiteSpace(Name) ? Name : SlotLabel;
    }
}
