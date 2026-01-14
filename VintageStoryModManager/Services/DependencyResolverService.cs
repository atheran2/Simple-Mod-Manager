using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VintageStoryModManager.Models;
using VintageStoryModManager.ViewModels;

namespace VintageStoryModManager.Services;

/// <summary>
/// Service for resolving mod dependencies recursively.
/// </summary>
public class DependencyResolverService
{
    private readonly ModDatabaseService _modDatabaseService;

    public DependencyResolverService(ModDatabaseService modDatabaseService)
    {
        _modDatabaseService = modDatabaseService;
    }

    /// <summary>
    /// Represents a dependency that needs to be installed.
    /// </summary>
    public class DependencyToInstall
    {
        public required string ModId { get; init; }
        public required string DisplayName { get; init; }
        public string? RequiredVersion { get; init; }
        public string? AvailableVersion { get; init; }
        public ModDatabaseInfo? DatabaseInfo { get; init; }
        public ModReleaseInfo? Release { get; init; }
        public bool IsAlreadyInstalled { get; init; }
        public bool NeedsUpdate { get; init; }
    }

    /// <summary>
    /// Result of dependency resolution.
    /// </summary>
    public class DependencyResolutionResult
    {
        public List<DependencyToInstall> DependenciesToInstall { get; } = new();
        public List<string> UnresolvableDependencies { get; } = new();
        public List<string> AlreadySatisfied { get; } = new();
        public bool HasUnresolvable => UnresolvableDependencies.Count > 0;
        public bool HasDependenciesToInstall => DependenciesToInstall.Count > 0;
    }

    /// <summary>
    /// Extracts dependencies from a mod zip file's modinfo.json.
    /// </summary>
    public static List<ModDependencyInfo> ExtractDependenciesFromZip(string zipPath)
    {
        var dependencies = new List<ModDependencyInfo>();

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            // Look for modinfo.json at root or in a subfolder
            var modinfoEntry = archive.Entries.FirstOrDefault(e =>
                e.Name.Equals("modinfo.json", StringComparison.OrdinalIgnoreCase));

            if (modinfoEntry == null)
                return dependencies;

            using var stream = modinfoEntry.Open();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("dependencies", out var depsElement) &&
                depsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in depsElement.EnumerateObject())
                {
                    var modId = prop.Name;
                    var version = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : "";

                    dependencies.Add(new ModDependencyInfo(modId, version));
                }
            }
        }
        catch
        {
            // Failed to extract dependencies, return empty list
        }

        return dependencies;
    }

    /// <summary>
    /// Resolves all missing dependencies recursively for a list of dependencies.
    /// </summary>
    public async Task<DependencyResolutionResult> ResolveAllDependenciesAsync(
        IReadOnlyList<ModDependencyInfo> dependencies,
        Func<string, ModListItemViewModel?> findInstalledMod,
        string? installedGameVersion,
        bool requireExactVersionMatch,
        IProgress<string>? progress = null)
    {
        var result = new DependencyResolutionResult();
        var processedModIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingDependencies = new Queue<ModDependencyInfo>(dependencies);

        while (pendingDependencies.Count > 0)
        {
            var dependency = pendingDependencies.Dequeue();

            // Skip game/core dependencies
            if (dependency.IsGameOrCoreDependency)
                continue;

            // Skip already processed
            if (!processedModIds.Add(dependency.ModId))
                continue;

            progress?.Report($"Checking dependency: {dependency.ModId}");

            // Check if already installed
            var installedMod = findInstalledMod(dependency.ModId);

            if (installedMod != null)
            {
                // Check if version is satisfied
                var versionSatisfied = string.IsNullOrWhiteSpace(dependency.Version) ||
                    VersionStringUtility.SatisfiesMinimumVersion(dependency.Version, installedMod.Version);

                if (versionSatisfied)
                {
                    result.AlreadySatisfied.Add(dependency.ModId);
                    continue;
                }
            }

            // Need to install or update - look up in database
            var dbInfo = await _modDatabaseService.TryLoadDatabaseInfoAsync(
                dependency.ModId,
                installedMod?.Version,
                installedGameVersion,
                requireExactVersionMatch).ConfigureAwait(false);

            if (dbInfo == null)
            {
                result.UnresolvableDependencies.Add($"{dependency.ModId} (not found in mod database)");
                continue;
            }

            // Find a compatible release
            var release = SelectBestRelease(dbInfo, dependency.Version, installedGameVersion, requireExactVersionMatch);

            if (release == null)
            {
                result.UnresolvableDependencies.Add($"{dependency.ModId} (no compatible release found)");
                continue;
            }

            result.DependenciesToInstall.Add(new DependencyToInstall
            {
                ModId = dependency.ModId,
                DisplayName = dependency.ModId, // ModDatabaseInfo doesn't have Name, use ModId
                RequiredVersion = dependency.Version,
                AvailableVersion = release.Version,
                DatabaseInfo = dbInfo,
                Release = release,
                IsAlreadyInstalled = installedMod != null,
                NeedsUpdate = installedMod != null
            });

            // If the release has a download URL, we could potentially extract its dependencies too
            // But that would require downloading first - for now, we'll rely on the database
            // to have accurate dependency info (which it doesn't provide directly)
            //
            // For recursive resolution, after installing each dependency, we'd need to
            // extract its modinfo.json and queue its dependencies. This is handled
            // in the actual install loop.
        }

        return result;
    }

    /// <summary>
    /// Resolves dependencies for a single mod after it's been downloaded but before refresh.
    /// Call this after downloading a mod's zip file to check what else needs to be installed.
    /// </summary>
    public async Task<DependencyResolutionResult> ResolveFromDownloadedModAsync(
        string downloadedZipPath,
        Func<string, ModListItemViewModel?> findInstalledMod,
        string? installedGameVersion,
        bool requireExactVersionMatch,
        IProgress<string>? progress = null)
    {
        var dependencies = ExtractDependenciesFromZip(downloadedZipPath);

        if (dependencies.Count == 0)
            return new DependencyResolutionResult();

        // Filter out game/core dependencies
        var modDependencies = dependencies.Where(d => !d.IsGameOrCoreDependency).ToList();

        if (modDependencies.Count == 0)
            return new DependencyResolutionResult();

        return await ResolveAllDependenciesAsync(
            modDependencies,
            findInstalledMod,
            installedGameVersion,
            requireExactVersionMatch,
            progress).ConfigureAwait(false);
    }

    /// <summary>
    /// Selects the best release for a dependency.
    /// </summary>
    private static ModReleaseInfo? SelectBestRelease(
        ModDatabaseInfo dbInfo,
        string? requiredVersion,
        string? gameVersion,
        bool requireExactVersionMatch)
    {
        if (dbInfo.Releases == null || dbInfo.Releases.Count == 0)
            return null;

        var candidates = dbInfo.Releases
            .Where(r => r.DownloadUri != null)
            .ToList();

        if (candidates.Count == 0)
            return null;

        // If we have a game version, prefer releases compatible with it
        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            var compatible = candidates
                .Where(r => IsReleaseCompatible(r, gameVersion, requireExactVersionMatch))
                .ToList();

            if (compatible.Count > 0)
                candidates = compatible;
        }

        // If we need a minimum version, filter
        if (!string.IsNullOrWhiteSpace(requiredVersion))
        {
            var versionSatisfying = candidates
                .Where(r => VersionStringUtility.SatisfiesMinimumVersion(requiredVersion, r.Version))
                .ToList();

            if (versionSatisfying.Count > 0)
                candidates = versionSatisfying;
        }

        // Return the most recent release
        return candidates
            .OrderByDescending(r => r.CreatedUtc ?? DateTime.MinValue)
            .FirstOrDefault();
    }

    private static bool IsReleaseCompatible(ModReleaseInfo release, string gameVersion, bool requireExact)
    {
        if (release.GameVersionTags == null || release.GameVersionTags.Count == 0)
            return true; // No tags = assume compatible

        var normalizedGame = NormalizeGameVersion(gameVersion);

        foreach (var tag in release.GameVersionTags)
        {
            var normalizedTag = NormalizeGameVersion(tag);

            if (requireExact)
            {
                if (string.Equals(normalizedTag, normalizedGame, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                // Check major.minor compatibility
                if (AreVersionsCompatible(normalizedTag, normalizedGame))
                    return true;
            }
        }

        return false;
    }

    private static string NormalizeGameVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "";

        // Remove 'v' prefix if present
        var normalized = version.TrimStart('v', 'V');

        // Handle pre-release tags
        var dashIndex = normalized.IndexOf('-');
        if (dashIndex > 0)
            normalized = normalized.Substring(0, dashIndex);

        return normalized;
    }

    private static bool AreVersionsCompatible(string releaseVersion, string gameVersion)
    {
        var releaseParts = releaseVersion.Split('.');
        var gameParts = gameVersion.Split('.');

        // Check major version
        if (releaseParts.Length > 0 && gameParts.Length > 0)
        {
            if (!string.Equals(releaseParts[0], gameParts[0], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Check minor version
        if (releaseParts.Length > 1 && gameParts.Length > 1)
        {
            if (!string.Equals(releaseParts[1], gameParts[1], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
