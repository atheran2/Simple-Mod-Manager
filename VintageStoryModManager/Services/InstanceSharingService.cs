using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using VintageStoryModManager.Models;
using VintageStoryModManager.ViewModels;

namespace VintageStoryModManager.Services;

// JSON parsing options for modinfo.json - must allow trailing commas and comments
// since many mods have non-strict JSON formatting
internal static class ModInfoJsonOptions
{
    public static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Gets a string property from a JsonElement using case-insensitive property name matching.
    /// </summary>
    public static string? GetStringPropertyIgnoreCase(JsonElement element, string propertyName)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
            }
        }
        return null;
    }

    /// <summary>
    /// Converts a name to a modId format (lowercase letters and digits only).
    /// This matches the logic in ModDiscoveryService.ToModId.
    /// </summary>
    public static string ToModId(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "mod";

        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetter(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsDigit(ch))
            {
                if (builder.Length == 0) builder.Append('m');
                builder.Append(ch);
            }
        }

        return builder.Length == 0 ? "mod" : builder.ToString();
    }
}

/// <summary>
///     Progress information for instance import operations.
/// </summary>
public sealed class InstanceImportProgress
{
    public string CurrentOperation { get; set; } = string.Empty;
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public string? CurrentModName { get; set; }
}

/// <summary>
///     Preview information for an instance import.
/// </summary>
public sealed class InstanceImportPreview
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public string? TargetVsVersion { get; set; }
    public string? Uploader { get; set; }
    public string? ImageBase64 { get; set; }
    public List<ModImportInfo> Mods { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public int ConfigFileCount { get; set; }
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
///     Information about a mod to be imported.
/// </summary>
public sealed class ModImportInfo
{
    public string ModId { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? DisplayName { get; set; }
    public bool IsAvailable { get; set; }
    public string? UnavailableReason { get; set; }
    public string? DownloadUrl { get; set; }
    public string? FileName { get; set; }
}

/// <summary>
///     Service for exporting and importing game instances.
/// </summary>
public sealed class InstanceSharingService
{
    private const string InstanceFileExtension = ".vsinstance";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private readonly InstanceService _instanceService;
    private readonly IModApiService _modApiService;
    private readonly ModDatabaseService _modDatabaseService;
    private readonly ModUpdateService _modUpdateService;

    public InstanceSharingService(
        InstanceService instanceService,
        IModApiService modApiService,
        ModDatabaseService modDatabaseService,
        ModUpdateService modUpdateService)
    {
        _instanceService = instanceService;
        _modApiService = modApiService;
        _modDatabaseService = modDatabaseService;
        _modUpdateService = modUpdateService;
    }

    /// <summary>
    ///     File extension for instance exports.
    /// </summary>
    public static string FileExtension => InstanceFileExtension;

    #region Export

    /// <summary>
    ///     Builds a serializable representation of an instance.
    /// </summary>
    public SerializableInstance BuildSerializableInstance(
        GameInstance instance,
        IReadOnlyList<ModListItemViewModel>? mods,
        bool includeConfigs,
        bool includeCategories,
        string? description = null,
        string? imageBase64 = null)
    {
        var result = new SerializableInstance
        {
            FormatVersion = 1,
            Name = instance.Name,
            Description = description,
            Notes = instance.Notes,
            TargetVsVersion = instance.TargetVsVersion,
            ImageBase64 = imageBase64 ?? LoadInstanceImage(instance),
            Mods = new List<SerializableInstanceMod>()
        };

        // Add mods from the view model if available
        if (mods != null)
        {
            foreach (var mod in mods)
            {
                result.Mods.Add(new SerializableInstanceMod
                {
                    ModId = mod.ModId,
                    Version = mod.Version,
                    Name = mod.DisplayName,
                    IsActive = mod.IsActive
                });
            }
        }
        else
        {
            // Fallback: scan the mods folder directly
            result.Mods = ScanModsFolder(instance.ModsPath);
        }

        // Include mod configurations
        if (includeConfigs)
        {
            result.Configurations = LoadModConfigurations(instance.ModConfigPath);
        }

        // Include categories
        if (includeCategories && instance.Categories != null)
        {
            result.Categories = instance.Categories
                .Where(c => !c.IsDefault) // Don't export the default Uncategorized
                .Select(c => new SerializableCategory
                {
                    Id = c.Id,
                    Name = c.Name,
                    Order = c.Order
                })
                .ToList();

            result.ModCategoryAssignments = instance.ModCategoryAssignments != null
                ? new Dictionary<string, string>(instance.ModCategoryAssignments)
                : null;
        }

        return result;
    }

    /// <summary>
    ///     Exports an instance to a file.
    /// </summary>
    public async Task<string> ExportToFileAsync(
        GameInstance instance,
        IReadOnlyList<ModListItemViewModel>? mods,
        string filePath,
        bool includeConfigs,
        bool includeCategories,
        string? description = null,
        string? imageBase64 = null)
    {
        var serializable = BuildSerializableInstance(instance, mods, includeConfigs, includeCategories, description, imageBase64);
        var json = JsonSerializer.Serialize(serializable, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
        return filePath;
    }

    private static string? LoadInstanceImage(GameInstance instance)
    {
        if (string.IsNullOrWhiteSpace(instance.IconPath) || !File.Exists(instance.IconPath))
            return null;

        try
        {
            var bytes = File.ReadAllBytes(instance.IconPath);
            // Limit to ~500KB for reasonable file sizes
            if (bytes.Length > 500 * 1024)
                return null;

            return Convert.ToBase64String(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static List<SerializableInstanceMod> ScanModsFolder(string modsPath)
    {
        var result = new List<SerializableInstanceMod>();

        if (!Directory.Exists(modsPath))
            return result;

        // Scan .zip files
        foreach (var file in Directory.GetFiles(modsPath, "*.zip"))
        {
            var modInfo = TryReadModInfoFromZip(file);
            if (modInfo != null)
                result.Add(modInfo);
        }

        // Scan .cs files (source mods)
        foreach (var file in Directory.GetFiles(modsPath, "*.cs"))
        {
            var modInfo = TryReadModInfoFromSourceFile(file);
            if (modInfo != null)
                result.Add(modInfo);
        }

        // Scan .dll files
        foreach (var file in Directory.GetFiles(modsPath, "*.dll"))
        {
            var modInfo = TryReadModInfoFromDll(file);
            if (modInfo != null)
                result.Add(modInfo);
        }

        // Scan folder mods (directories with modinfo.json)
        foreach (var dir in Directory.GetDirectories(modsPath))
        {
            var modInfoPath = Path.Combine(dir, "modinfo.json");
            if (File.Exists(modInfoPath))
            {
                var modInfo = TryReadModInfoFromFolder(dir, modInfoPath);
                if (modInfo != null)
                    result.Add(modInfo);
            }
        }

        return result;
    }

    private static SerializableInstanceMod? TryReadModInfoFromSourceFile(string csPath)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(csPath);
            var isActive = !fileName.StartsWith("_") &&
                          !csPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

            return new SerializableInstanceMod
            {
                ModId = fileName,
                Version = null,
                Name = fileName,
                IsActive = isActive
            };
        }
        catch
        {
            return null;
        }
    }

    private static SerializableInstanceMod? TryReadModInfoFromDll(string dllPath)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(dllPath);
            var isActive = !fileName.StartsWith("_") &&
                          !dllPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

            return new SerializableInstanceMod
            {
                ModId = fileName,
                Version = null,
                Name = fileName,
                IsActive = isActive
            };
        }
        catch
        {
            return null;
        }
    }

    private static SerializableInstanceMod? TryReadModInfoFromFolder(string folderPath, string modInfoPath)
    {
        try
        {
            var json = File.ReadAllText(modInfoPath);
            using var doc = JsonDocument.Parse(json, ModInfoJsonOptions.DocumentOptions);
            var root = doc.RootElement;

            // Use case-insensitive property lookup
            var modId = ModInfoJsonOptions.GetStringPropertyIgnoreCase(root, "modid");
            var version = ModInfoJsonOptions.GetStringPropertyIgnoreCase(root, "version");
            var name = ModInfoJsonOptions.GetStringPropertyIgnoreCase(root, "name");

            if (string.IsNullOrWhiteSpace(modId))
                modId = Path.GetFileName(folderPath);

            var dirName = Path.GetFileName(folderPath);
            var isActive = !dirName.StartsWith("_");

            return new SerializableInstanceMod
            {
                ModId = modId,
                Version = version,
                Name = name ?? modId,
                IsActive = isActive
            };
        }
        catch
        {
            return null;
        }
    }

    private static SerializableInstanceMod? TryReadModInfoFromZip(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            // Look for modinfo.json - could be at root or in a subfolder
            var modInfoEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Equals("modinfo.json", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith("/modinfo.json", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith("\\modinfo.json", StringComparison.OrdinalIgnoreCase));

            if (modInfoEntry == null)
            {
                // Fallback: create entry from filename if no modinfo.json
                var fallbackName = Path.GetFileNameWithoutExtension(zipPath);
                var fallbackActive = !fallbackName.StartsWith("_") &&
                              !zipPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                return new SerializableInstanceMod
                {
                    ModId = fallbackName,
                    Version = null,
                    Name = fallbackName,
                    IsActive = fallbackActive
                };
            }

            using var stream = modInfoEntry.Open();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            using var doc = JsonDocument.Parse(json, ModInfoJsonOptions.DocumentOptions);
            var root = doc.RootElement;

            // Use case-insensitive property lookup
            var modId = ModInfoJsonOptions.GetStringPropertyIgnoreCase(root, "modid");
            var version = ModInfoJsonOptions.GetStringPropertyIgnoreCase(root, "version");
            var name = ModInfoJsonOptions.GetStringPropertyIgnoreCase(root, "name");

            // Check if mod is disabled (filename starts with underscore or has .disabled extension)
            var fileName = Path.GetFileName(zipPath);
            var isActive = !fileName.StartsWith("_") &&
                          !fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

            // If modId is empty, use filename as fallback
            if (string.IsNullOrWhiteSpace(modId))
                modId = Path.GetFileNameWithoutExtension(zipPath);

            return new SerializableInstanceMod
            {
                ModId = modId,
                Version = version,
                Name = name ?? modId,
                IsActive = isActive
            };
        }
        catch
        {
            // Fallback: create entry from filename on any error
            var errorFallbackName = Path.GetFileNameWithoutExtension(zipPath);
            var errorFallbackActive = !errorFallbackName.StartsWith("_") &&
                          !zipPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            return new SerializableInstanceMod
            {
                ModId = errorFallbackName,
                Version = null,
                Name = errorFallbackName,
                IsActive = errorFallbackActive
            };
        }
    }

    private static List<SerializableModConfiguration> LoadModConfigurations(string configPath)
    {
        var result = new List<SerializableModConfiguration>();

        if (!Directory.Exists(configPath))
            return result;

        foreach (var file in Directory.GetFiles(configPath, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var relativePath = Path.GetRelativePath(configPath, file);
                var content = File.ReadAllText(file);

                result.Add(new SerializableModConfiguration
                {
                    FileName = Path.GetFileName(file),
                    RelativePath = relativePath,
                    Content = content
                });
            }
            catch
            {
                // Skip files we can't read
            }
        }

        return result;
    }

    #endregion

    #region Import

    /// <summary>
    ///     Loads a serializable instance from a file.
    /// </summary>
    public async Task<SerializableInstance?> LoadFromFileAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<SerializableInstance>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Creates a preview of what will be imported.
    /// </summary>
    public async Task<InstanceImportPreview> PreviewImportAsync(
        SerializableInstance source,
        CancellationToken ct = default)
    {
        var preview = new InstanceImportPreview
        {
            Name = source.Name,
            Description = source.Description,
            Notes = source.Notes,
            TargetVsVersion = source.TargetVsVersion,
            Uploader = source.Uploader,
            ImageBase64 = source.ImageBase64,
            ConfigFileCount = source.Configurations?.Count ?? 0,
            Categories = source.Categories?.Select(c => c.Name ?? "Unnamed").ToList() ?? new List<string>()
        };

        // Check mod availability
        if (source.Mods != null)
        {
            foreach (var mod in source.Mods)
            {
                ct.ThrowIfCancellationRequested();

                var modInfo = new ModImportInfo
                {
                    ModId = mod.ModId ?? string.Empty,
                    Version = mod.Version,
                    DisplayName = mod.Name ?? mod.ModId ?? "Unknown"
                };

                // Try to find the mod in the database using ModDatabaseService (same as modlist install)
                try
                {
                    var info = await _modDatabaseService
                        .TryLoadDatabaseInfoAsync(mod.ModId ?? string.Empty, mod.Version, null, false, ct)
                        .ConfigureAwait(false);

                    // If not found by modId and we have a name, try searching by name as fallback
                    if ((info == null || info.Releases?.Count == 0) && !string.IsNullOrWhiteSpace(mod.Name))
                    {
                        // Extract first word/token from mod name for broader search (e.g., "Cairns" from "Cairns 1.2 - Recoverable Stones")
                        var searchTerm = mod.Name;
                        var firstSpace = mod.Name.IndexOf(' ');
                        if (firstSpace > 2)
                            searchTerm = mod.Name.Substring(0, firstSpace);

                        var searchResults = await _modDatabaseService
                            .SearchModsAsync(searchTerm, 10, ct)
                            .ConfigureAwait(false);

                        // Look for a match - try exact name, contains, or alternate IDs
                        var matchingResult = searchResults.FirstOrDefault(r =>
                            string.Equals(r.Name, mod.Name, StringComparison.OrdinalIgnoreCase) ||
                            r.Name?.Contains(mod.Name, StringComparison.OrdinalIgnoreCase) == true ||
                            mod.Name.Contains(r.Name ?? "", StringComparison.OrdinalIgnoreCase) ||
                            r.AlternateIds?.Any(altId =>
                                mod.Name.Replace(" ", "").Replace("-", "").Replace(".", "")
                                    .Contains(altId, StringComparison.OrdinalIgnoreCase)) == true);

                        if (matchingResult != null && !string.IsNullOrWhiteSpace(matchingResult.ModId))
                        {
                            // Found a match by name, now load the full info
                            info = await _modDatabaseService
                                .TryLoadDatabaseInfoAsync(matchingResult.ModId, mod.Version, null, false, ct)
                                .ConfigureAwait(false);
                        }
                    }

                    if (info != null && info.Releases?.Count > 0)
                    {
                        // Find the matching version or latest
                        ModReleaseInfo? release = null;
                        if (!string.IsNullOrWhiteSpace(mod.Version))
                        {
                            var normalizedVersion = VersionStringUtility.Normalize(mod.Version);
                            release = info.Releases.FirstOrDefault(r =>
                                string.Equals(r.Version?.Trim(), mod.Version, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(r.NormalizedVersion, normalizedVersion, StringComparison.OrdinalIgnoreCase));
                        }
                        release ??= info.Releases.FirstOrDefault();

                        if (release != null && release.DownloadUri != null)
                        {
                            modInfo.IsAvailable = true;
                            modInfo.DownloadUrl = release.DownloadUri.ToString();
                            modInfo.FileName = release.FileName;
                        }
                        else
                        {
                            modInfo.IsAvailable = false;
                            modInfo.UnavailableReason = $"Version {mod.Version} not found";
                            preview.Warnings.Add($"Mod '{modInfo.DisplayName}' version {mod.Version} not found in database");
                        }
                    }
                    else
                    {
                        modInfo.IsAvailable = false;
                        modInfo.UnavailableReason = "Not found in mod database";
                        preview.Warnings.Add($"Mod '{modInfo.DisplayName}' not found in mod database");
                    }
                }
                catch (Exception ex)
                {
                    modInfo.IsAvailable = false;
                    modInfo.UnavailableReason = $"Error: {ex.Message}";
                    preview.Warnings.Add($"Error checking mod '{modInfo.DisplayName}': {ex.Message}");
                }

                preview.Mods.Add(modInfo);
            }
        }

        return preview;
    }

    /// <summary>
    ///     Imports an instance from a serializable representation.
    /// </summary>
    public async Task<GameInstance> ImportInstanceAsync(
        SerializableInstance source,
        string instanceName,
        IProgress<InstanceImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        // Create new instance
        var instance = _instanceService.CreateInstance(instanceName);
        instance.Notes = source.Notes;
        instance.TargetVsVersion = source.TargetVsVersion;

        // Save instance image if provided
        if (!string.IsNullOrWhiteSpace(source.ImageBase64))
        {
            try
            {
                var imageBytes = Convert.FromBase64String(source.ImageBase64);
                var imagePath = Path.Combine(instance.Path, "instance-icon.png");
                await File.WriteAllBytesAsync(imagePath, imageBytes, ct);
                instance.IconPath = imagePath;
            }
            catch
            {
                // Ignore image errors
            }
        }

        var modCount = source.Mods?.Count ?? 0;
        var totalSteps = modCount + 2; // mods + configs + categories
        var currentStep = 0;

        // Download mods
        if (source.Mods != null)
        {
            var downloadedCount = 0;
            var failedCount = 0;

            foreach (var mod in source.Mods)
            {
                ct.ThrowIfCancellationRequested();

                progress?.Report(new InstanceImportProgress
                {
                    CurrentOperation = "Downloading mods",
                    CurrentStep = ++currentStep,
                    TotalSteps = totalSteps,
                    CurrentModName = mod.Name ?? mod.ModId
                });

                var success = await TryDownloadModAsync(mod, instance.ModsPath, ct);
                if (success)
                    downloadedCount++;
                else
                    failedCount++;

                // Small delay to avoid rate limiting
                await Task.Delay(50, ct);
            }

            System.Diagnostics.Debug.WriteLine($"[InstanceSharing] Download complete: {downloadedCount} succeeded, {failedCount} failed out of {source.Mods.Count} total");
            if (failedCount > 0)
            {
                StatusLogService.AppendStatus($"Downloaded {downloadedCount} mods, {failedCount} failed", true);
            }
        }

        // Write configurations
        progress?.Report(new InstanceImportProgress
        {
            CurrentOperation = "Writing configurations",
            CurrentStep = ++currentStep,
            TotalSteps = totalSteps
        });

        if (source.Configurations != null)
        {
            foreach (var config in source.Configurations)
            {
                try
                {
                    var targetPath = Path.Combine(
                        instance.ModConfigPath,
                        config.RelativePath ?? config.FileName ?? "config.json");

                    var directory = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    await File.WriteAllTextAsync(targetPath, config.Content ?? "{}", ct);
                }
                catch
                {
                    // Skip configs that fail
                }
            }
        }

        // Set up categories
        progress?.Report(new InstanceImportProgress
        {
            CurrentOperation = "Setting up categories",
            CurrentStep = ++currentStep,
            TotalSteps = totalSteps
        });

        if (source.Categories != null && source.Categories.Count > 0)
        {
            instance.Categories = source.Categories.Select(c => new ModCategory(
                c.Id ?? Guid.NewGuid().ToString("N"),
                c.Name ?? "Unnamed",
                c.Order
            )).ToList();

            // Always ensure Uncategorized exists
            if (!instance.Categories.Any(c => c.IsDefault))
                instance.Categories.Add(ModCategory.CreateDefault());
        }

        if (source.ModCategoryAssignments != null)
        {
            instance.ModCategoryAssignments = new Dictionary<string, string>(source.ModCategoryAssignments);
        }

        // Save instance metadata
        _instanceService.SaveInstance(instance);

        return instance;
    }

    private async Task<bool> TryDownloadModAsync(SerializableInstanceMod mod, string modsPath, CancellationToken ct)
    {
        try
        {
            var modId = mod.ModId?.Trim() ?? string.Empty;
            var modDisplayName = mod.Name ?? modId ?? "Unknown";

            if (string.IsNullOrWhiteSpace(modId))
            {
                StatusLogService.AppendStatus($"Mod '{modDisplayName}' has no ID, skipping", true);
                return false;
            }

            // Use the same approach as modlist preset install - use ModDatabaseService
            var info = await _modDatabaseService
                .TryLoadDatabaseInfoAsync(modId, mod.Version, null, false, ct)
                .ConfigureAwait(false);

            if (info == null)
            {
                StatusLogService.AppendStatus($"Mod '{modDisplayName}' not found in database, skipping", true);
                return false;
            }

            // Find the best release
            var releases = info.Releases ?? Array.Empty<ModReleaseInfo>();
            if (releases.Count == 0)
            {
                StatusLogService.AppendStatus($"No releases found for '{modDisplayName}', skipping", true);
                return false;
            }

            // Try to match version, otherwise use latest
            ModReleaseInfo? release = null;
            if (!string.IsNullOrWhiteSpace(mod.Version))
            {
                var normalizedVersion = VersionStringUtility.Normalize(mod.Version);
                release = releases.FirstOrDefault(r =>
                    string.Equals(r.Version?.Trim(), mod.Version, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.NormalizedVersion, normalizedVersion, StringComparison.OrdinalIgnoreCase));
            }

            // Fall back to latest release
            release ??= releases.FirstOrDefault();

            if (release == null || release.DownloadUri == null)
            {
                StatusLogService.AppendStatus($"No suitable release for '{modDisplayName}', skipping", true);
                return false;
            }

            // Build the target path
            var fileName = release.FileName ?? $"{modId}.zip";
            if (!mod.IsActive)
                fileName = "_" + fileName;

            var targetPath = Path.Combine(modsPath, fileName);

            // Use ModUpdateService for downloading (handles caching, retries, etc.)
            var descriptor = new ModUpdateDescriptor(
                modId,
                modDisplayName,
                release.DownloadUri,
                targetPath,
                false,
                release.FileName,
                release.Version,
                null);

            var result = await _modUpdateService
                .UpdateAsync(descriptor, false, null, ct)
                .ConfigureAwait(false);

            if (!result.Success)
            {
                var errorMsg = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Download failed" : result.ErrorMessage;
                StatusLogService.AppendStatus($"Failed to download '{modDisplayName}': {errorMsg}", true);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            StatusLogService.AppendStatus($"Error downloading '{mod.Name ?? mod.ModId}': {ex.Message}", true);
            return false;
        }
    }

    #endregion

    #region File Dialog Helpers

    /// <summary>
    ///     Gets the file filter for open/save dialogs.
    /// </summary>
    public static string FileFilter => $"Instance Files (*{InstanceFileExtension})|*{InstanceFileExtension}|All Files (*.*)|*.*";

    /// <summary>
    ///     Checks if a file path has the correct extension for an instance file.
    /// </summary>
    public static bool IsInstanceFile(string filePath)
    {
        return filePath.EndsWith(InstanceFileExtension, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
