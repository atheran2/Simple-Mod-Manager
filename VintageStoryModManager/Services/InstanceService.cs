using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using VintageStoryModManager.Models;

namespace VintageStoryModManager.Services;

/// <summary>
///     Manages game instances - isolated VS environments with their own mods, saves, and configurations.
/// </summary>
public sealed class InstanceService
{
    private const string InstancesDirectoryName = "Instances";
    private const string InstanceMetadataFileName = "instance.json";
    private const string InstancesConfigFileName = "instances-config.json";
    private const string BaseModsDirectoryName = "_BaseMods";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private readonly Dictionary<string, GameInstance> _instances = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _configFilePath;

    private string _instancesRootPath;
    private string? _activeInstanceId;
    private bool _copyBaseModsOnCreate = true;

    public InstanceService()
    {
        _instancesRootPath = GetDefaultInstancesRootPath();
        _configFilePath = GetConfigFilePath();
        LoadConfiguration();
        DiscoverInstances();
    }

    /// <summary>
    ///     Event raised when the active instance changes.
    /// </summary>
    public event EventHandler<GameInstance?>? ActiveInstanceChanged;

    /// <summary>
    ///     Event raised when the instances list changes (add/remove/rename).
    /// </summary>
    public event EventHandler? InstancesChanged;

    /// <summary>
    ///     Gets or sets the root path where instances are stored.
    /// </summary>
    public string InstancesRootPath
    {
        get => _instancesRootPath;
        set
        {
            if (string.Equals(_instancesRootPath, value, StringComparison.OrdinalIgnoreCase))
                return;

            _instancesRootPath = value;
            SaveConfiguration();
            DiscoverInstances();
        }
    }

    /// <summary>
    ///     Gets the path to the base mods directory.
    /// </summary>
    public string BaseModsPath => Path.Combine(_instancesRootPath, BaseModsDirectoryName);

    /// <summary>
    ///     Gets or sets whether to copy base mods when creating new instances.
    /// </summary>
    public bool CopyBaseModsOnCreate
    {
        get => _copyBaseModsOnCreate;
        set
        {
            if (_copyBaseModsOnCreate == value)
                return;
            _copyBaseModsOnCreate = value;
            SaveConfiguration();
        }
    }

    /// <summary>
    ///     Gets the currently active instance, or null if none is selected.
    /// </summary>
    public GameInstance? ActiveInstance =>
        _activeInstanceId != null && _instances.TryGetValue(_activeInstanceId, out var instance)
            ? instance
            : null;

    /// <summary>
    ///     Gets all discovered instances.
    /// </summary>
    public IReadOnlyList<GameInstance> GetAllInstances()
    {
        return _instances.Values
            .OrderByDescending(i => i.LastPlayed ?? DateTime.MinValue)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    ///     Gets an instance by its ID.
    /// </summary>
    public GameInstance? GetInstance(string id)
    {
        return _instances.TryGetValue(id, out var instance) ? instance : null;
    }

    /// <summary>
    ///     Creates a new instance with the given name.
    /// </summary>
    /// <param name="name">Display name for the instance.</param>
    /// <param name="sourceDataDirectory">Optional source data directory to copy player session from.</param>
    /// <param name="targetVsVersion">Optional target VS version.</param>
    /// <param name="gameDirectory">Optional game directory override.</param>
    public GameInstance CreateInstance(string name, string? sourceDataDirectory = null, string? targetVsVersion = null, string? gameDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Instance name cannot be empty.", nameof(name));

        var sanitizedName = SanitizeFolderName(name);
        var instancePath = GetUniqueInstancePath(sanitizedName);
        var id = Guid.NewGuid().ToString("N");

        var instance = new GameInstance
        {
            Id = id,
            Name = name.Trim(),
            Path = instancePath,
            TargetVsVersion = targetVsVersion,
            GameDirectory = gameDirectory,
            Created = DateTime.UtcNow
        };

        // Create the folder structure
        CreateInstanceFolderStructure(instance);

        // Copy player session data from source if provided
        if (!string.IsNullOrWhiteSpace(sourceDataDirectory))
        {
            CopyPlayerSessionToInstance(sourceDataDirectory, instance.Path);
        }

        // Copy base mods if enabled and the base mods directory exists
        if (_copyBaseModsOnCreate)
        {
            CopyBaseModsToInstance(instance);
        }

        // Save instance metadata
        SaveInstanceMetadata(instance);

        // Add to our collection
        _instances[id] = instance;

        // If this is the first instance, make it active
        if (_activeInstanceId == null)
            SetActiveInstance(id);

        SaveConfiguration();
        InstancesChanged?.Invoke(this, EventArgs.Empty);

        return instance;
    }

    /// <summary>
    ///     Copies player session data (login credentials) from a source data directory to an instance.
    /// </summary>
    private static readonly string[] PlayerSessionFields =
    {
        "sessionkey",
        "sessionsignature",
        "useremail",
        "playeruid",
        "playername",
        "mptoken"
    };

    private void CopyPlayerSessionToInstance(string sourceDataDirectory, string instancePath)
    {
        try
        {
            var sourceSettingsPath = Path.Combine(sourceDataDirectory, "clientsettings.json");
            if (!File.Exists(sourceSettingsPath))
                return;

            var sourceJson = File.ReadAllText(sourceSettingsPath);
            using var sourceDoc = JsonDocument.Parse(sourceJson);

            if (!sourceDoc.RootElement.TryGetProperty("stringSettings", out var sourceStringSettings))
                return;

            // Build the target clientsettings.json with player session data
            var targetSettingsPath = Path.Combine(instancePath, "clientsettings.json");

            // Create a new clientsettings with just the session data
            var targetRoot = new Dictionary<string, object>
            {
                ["stringSettings"] = new Dictionary<string, object?>(),
                ["stringListSettings"] = new Dictionary<string, object>
                {
                    ["modPaths"] = new[] { "Mods", Path.Combine(instancePath, "Mods") }
                },
                ["intSettings"] = new Dictionary<string, object>(),
                ["boolSettings"] = new Dictionary<string, object>()
            };

            var targetStringSettings = (Dictionary<string, object?>)targetRoot["stringSettings"];

            // Copy player session fields from source
            foreach (var field in PlayerSessionFields)
            {
                if (sourceStringSettings.TryGetProperty(field, out var value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                        targetStringSettings[field] = value.GetString();
                    else if (value.ValueKind == JsonValueKind.Null)
                        targetStringSettings[field] = null;
                }
            }

            var json = JsonSerializer.Serialize(targetRoot, JsonOptions);
            File.WriteAllText(targetSettingsPath, json);
        }
        catch
        {
            // Ignore errors - instance will just require login
        }
    }

    /// <summary>
    ///     Copies all mods from the base mods directory to a new instance.
    /// </summary>
    private void CopyBaseModsToInstance(GameInstance instance)
    {
        if (!Directory.Exists(BaseModsPath))
            return;

        try
        {
            var modFiles = Directory.GetFiles(BaseModsPath, "*.zip", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(BaseModsPath, "*.cs", SearchOption.TopDirectoryOnly));

            foreach (var modFile in modFiles)
            {
                var fileName = Path.GetFileName(modFile);
                var destPath = Path.Combine(instance.ModsPath, fileName);
                File.Copy(modFile, destPath, overwrite: false);
            }
        }
        catch
        {
            // Ignore errors - base mods are optional
        }
    }

    /// <summary>
    ///     Gets the list of mods in the base mods directory.
    /// </summary>
    public IReadOnlyList<string> GetBaseModFiles()
    {
        if (!Directory.Exists(BaseModsPath))
            return Array.Empty<string>();

        try
        {
            return Directory.GetFiles(BaseModsPath, "*.zip", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(BaseModsPath, "*.cs", SearchOption.TopDirectoryOnly))
                .Select(Path.GetFileName)
                .Where(f => f != null)
                .Cast<string>()
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    ///     Ensures the base mods directory exists.
    /// </summary>
    public void EnsureBaseModsDirectoryExists()
    {
        if (!Directory.Exists(BaseModsPath))
            Directory.CreateDirectory(BaseModsPath);
    }

    /// <summary>
    ///     Opens the base mods directory in the file explorer.
    /// </summary>
    public void OpenBaseModsFolder()
    {
        EnsureBaseModsDirectoryExists();

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = BaseModsPath,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors opening folder
        }
    }

    /// <summary>
    ///     Adds a mod file to the base mods directory.
    /// </summary>
    public bool AddModToBaseMods(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
            return false;

        try
        {
            EnsureBaseModsDirectoryExists();

            var fileName = Path.GetFileName(sourceFilePath);
            var destPath = Path.Combine(BaseModsPath, fileName);
            File.Copy(sourceFilePath, destPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Removes a mod file from the base mods directory.
    /// </summary>
    public bool RemoveModFromBaseMods(string fileName)
    {
        try
        {
            var filePath = Path.Combine(BaseModsPath, fileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Sets the active instance by ID.
    /// </summary>
    public bool SetActiveInstance(string? id)
    {
        if (id == null)
        {
            if (_activeInstanceId != null)
            {
                _activeInstanceId = null;
                SaveConfiguration();
                ActiveInstanceChanged?.Invoke(this, null);
            }
            return true;
        }

        if (!_instances.ContainsKey(id))
            return false;

        if (_activeInstanceId == id)
            return true;

        _activeInstanceId = id;
        SaveConfiguration();
        ActiveInstanceChanged?.Invoke(this, ActiveInstance);
        return true;
    }

    /// <summary>
    ///     Renames an instance.
    /// </summary>
    public bool RenameInstance(string id, string newName)
    {
        if (!_instances.TryGetValue(id, out var instance))
            return false;

        if (string.IsNullOrWhiteSpace(newName))
            return false;

        instance.Name = newName.Trim();
        SaveInstanceMetadata(instance);
        InstancesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    ///     Deletes an instance.
    /// </summary>
    public bool DeleteInstance(string id, bool deleteFiles = false)
    {
        if (!_instances.TryGetValue(id, out var instance))
            return false;

        if (deleteFiles && Directory.Exists(instance.Path))
        {
            try
            {
                Directory.Delete(instance.Path, recursive: true);
            }
            catch (Exception)
            {
                // If we can't delete files, still remove from our tracking
            }
        }

        _instances.Remove(id);

        if (_activeInstanceId == id)
        {
            // Select another instance if available
            var remaining = _instances.Values.FirstOrDefault();
            _activeInstanceId = remaining?.Id;
            ActiveInstanceChanged?.Invoke(this, ActiveInstance);
        }

        SaveConfiguration();
        InstancesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    ///     Duplicates an existing instance.
    /// </summary>
    /// <param name="sourceId">The ID of the instance to duplicate.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <returns>The new duplicated instance, or null if the source was not found.</returns>
    public GameInstance? DuplicateInstance(string sourceId, IProgress<string>? progress = null)
    {
        if (!_instances.TryGetValue(sourceId, out var source))
            return null;

        if (!Directory.Exists(source.Path))
            return null;

        // Generate new name with (Copy) suffix
        var newName = $"{source.Name} (Copy)";
        var sanitizedName = SanitizeFolderName(newName);
        var newPath = GetUniqueInstancePath(sanitizedName);
        var newId = Guid.NewGuid().ToString("N");

        progress?.Report("Creating instance folder...");

        // Copy the entire folder
        CopyDirectory(source.Path, newPath, progress);

        // Create the new instance object
        var newInstance = new GameInstance
        {
            Id = newId,
            Name = newName,
            Path = newPath,
            GameDirectory = source.GameDirectory,
            TargetVsVersion = source.TargetVsVersion,
            IconPath = source.IconPath,
            Notes = source.Notes,
            Created = DateTime.UtcNow,
            LastPlayed = null,
            TotalPlaytimeSeconds = 0,
            // Copy categories and assignments
            Categories = source.Categories?.Select(c => c.Clone()).ToList(),
            ModCategoryAssignments = source.ModCategoryAssignments != null
                ? new Dictionary<string, string>(source.ModCategoryAssignments)
                : null
        };

        // Save instance metadata (overwrites the copied one with new ID)
        SaveInstanceMetadata(newInstance);

        // Add to our collection
        _instances[newId] = newInstance;

        SaveConfiguration();
        // Note: Don't fire InstancesChanged here - caller is responsible for refreshing UI
        // since this method may run on a background thread

        return newInstance;
    }

    private static void CopyDirectory(string sourcePath, string destinationPath, IProgress<string>? progress = null)
    {
        Directory.CreateDirectory(destinationPath);

        // Copy files
        foreach (var file in Directory.GetFiles(sourcePath))
        {
            var fileName = Path.GetFileName(file);
            progress?.Report($"Copying {fileName}...");
            var destFile = Path.Combine(destinationPath, fileName);
            File.Copy(file, destFile, overwrite: true);
        }

        // Copy subdirectories recursively
        foreach (var dir in Directory.GetDirectories(sourcePath))
        {
            var dirName = Path.GetFileName(dir);
            var destDir = Path.Combine(destinationPath, dirName);
            CopyDirectory(dir, destDir, progress);
        }
    }

    /// <summary>
    ///     Updates the last played time for the active instance.
    /// </summary>
    public void UpdateLastPlayed()
    {
        if (ActiveInstance == null)
            return;

        ActiveInstance.LastPlayed = DateTime.UtcNow;
        SaveInstanceMetadata(ActiveInstance);
    }

    /// <summary>
    ///     Saves the instance metadata to disk.
    /// </summary>
    public void SaveInstance(GameInstance instance)
    {
        SaveInstanceMetadata(instance);
        InstancesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Gets the Mods path for the active instance, or null if no instance is active.
    /// </summary>
    public string? GetActiveInstanceModsPath() => ActiveInstance?.ModsPath;

    /// <summary>
    ///     Gets the data path (instance root) for the active instance.
    /// </summary>
    public string? GetActiveInstanceDataPath() => ActiveInstance?.Path;

    /// <summary>
    ///     Re-scans the instances root folder for instances.
    /// </summary>
    public void RefreshInstances()
    {
        DiscoverInstances();
        InstancesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Opens the instance folder in the file explorer.
    /// </summary>
    public void OpenInstanceFolder(string id)
    {
        if (!_instances.TryGetValue(id, out var instance))
            return;

        if (!Directory.Exists(instance.Path))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = instance.Path,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors opening folder
        }
    }

    private static string GetDefaultInstancesRootPath()
    {
        return Path.Combine(AppContext.BaseDirectory, InstancesDirectoryName);
    }

    private static string GetConfigFilePath()
    {
        // Store config in app directory
        return Path.Combine(AppContext.BaseDirectory, InstancesConfigFileName);
    }

    /// <summary>
    ///     Converts a path to be relative to the app base directory if it's under the app folder.
    ///     Returns the original path if it's outside the app folder.
    /// </summary>
    private static string ToPortablePath(string absolutePath)
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (normalizedPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
        {
            var relativePart = normalizedPath.Substring(baseDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.IsNullOrEmpty(relativePart) ? "." : relativePart;
        }

        // Path is outside app folder, keep absolute
        return absolutePath;
    }

    /// <summary>
    ///     Resolves a potentially relative path to an absolute path based on app base directory.
    /// </summary>
    private static string ToAbsolutePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private void LoadConfiguration()
    {
        if (!File.Exists(_configFilePath))
            return;

        try
        {
            var json = File.ReadAllText(_configFilePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("instancesRootPath", out var pathProp))
            {
                var path = pathProp.GetString();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var resolvedPath = ToAbsolutePath(path);

                    // If the resolved path doesn't exist but the default does,
                    // the app was likely moved - use the default instead
                    if (!Directory.Exists(resolvedPath))
                    {
                        var defaultPath = GetDefaultInstancesRootPath();
                        if (Directory.Exists(defaultPath))
                        {
                            _instancesRootPath = defaultPath;
                            // Save the corrected path
                            SaveConfiguration();
                        }
                        else
                        {
                            // Neither exists, use default (will be created when needed)
                            _instancesRootPath = defaultPath;
                        }
                    }
                    else
                    {
                        _instancesRootPath = resolvedPath;
                    }
                }
            }

            if (root.TryGetProperty("activeInstanceId", out var activeProp))
            {
                _activeInstanceId = activeProp.GetString();
            }

            if (root.TryGetProperty("copyBaseModsOnCreate", out var baseModsProp))
            {
                _copyBaseModsOnCreate = baseModsProp.GetBoolean();
            }
        }
        catch
        {
            // Use defaults on error
        }
    }

    private void SaveConfiguration()
    {
        try
        {
            // Store path as relative if it's under the app folder for portability
            var portablePath = ToPortablePath(_instancesRootPath);

            var config = new Dictionary<string, object?>
            {
                ["instancesRootPath"] = portablePath,
                ["activeInstanceId"] = _activeInstanceId,
                ["copyBaseModsOnCreate"] = _copyBaseModsOnCreate
            };

            var json = JsonSerializer.Serialize(config, JsonOptions);

            var directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_configFilePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    private void DiscoverInstances()
    {
        _instances.Clear();

        if (!Directory.Exists(_instancesRootPath))
            return;

        foreach (var instanceDir in Directory.GetDirectories(_instancesRootPath))
        {
            // Skip the _BaseMods directory
            if (Path.GetFileName(instanceDir).Equals(BaseModsDirectoryName, StringComparison.OrdinalIgnoreCase))
                continue;

            var metadataPath = Path.Combine(instanceDir, InstanceMetadataFileName);
            if (!File.Exists(metadataPath))
                continue;

            try
            {
                var json = File.ReadAllText(metadataPath);
                var instance = JsonSerializer.Deserialize<GameInstance>(json, JsonOptions);
                if (instance != null)
                {
                    // Override the stored path with the actual directory location
                    // This makes instances portable - the path is determined by where
                    // the instance.json was found, not by what's stored inside it
                    instance.Path = instanceDir;
                    _instances[instance.Id] = instance;
                }
            }
            catch
            {
                // Skip invalid instances
            }
        }

        // Validate active instance still exists
        if (_activeInstanceId != null && !_instances.ContainsKey(_activeInstanceId))
        {
            _activeInstanceId = _instances.Values.FirstOrDefault()?.Id;
            SaveConfiguration();
        }
    }

    private void CreateInstanceFolderStructure(GameInstance instance)
    {
        // Create main instance folder
        Directory.CreateDirectory(instance.Path);

        // Create standard subfolders
        Directory.CreateDirectory(instance.ModsPath);
        Directory.CreateDirectory(instance.SavesPath);
        Directory.CreateDirectory(instance.ModConfigPath);
        Directory.CreateDirectory(instance.LogsPath);
        Directory.CreateDirectory(instance.CachePath);
    }

    private void SaveInstanceMetadata(GameInstance instance)
    {
        try
        {
            var json = JsonSerializer.Serialize(instance, JsonOptions);
            File.WriteAllText(instance.MetadataFilePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    private string GetUniqueInstancePath(string baseName)
    {
        var basePath = Path.Combine(_instancesRootPath, baseName);

        if (!Directory.Exists(basePath))
            return basePath;

        // Find unique name with suffix
        for (var i = 2; i < 1000; i++)
        {
            var path = Path.Combine(_instancesRootPath, $"{baseName} ({i})");
            if (!Directory.Exists(path))
                return path;
        }

        // Fallback to GUID-based name
        return Path.Combine(_instancesRootPath, $"{baseName}_{Guid.NewGuid():N}");
    }

    private static string SanitizeFolderName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Trim()
            .Where(c => !invalidChars.Contains(c))
            .ToArray());

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "Instance";

        return sanitized;
    }
}
