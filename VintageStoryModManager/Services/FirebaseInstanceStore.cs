using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimpleVsManager.Cloud;
using VintageStoryModManager.Models;
using static SimpleVsManager.Cloud.FirebaseAnonymousAuthenticator;

namespace VintageStoryModManager.Services;

/// <summary>
///     Cloud instance registry entry for browsing.
/// </summary>
public sealed class CloudInstanceRegistryEntry
{
    public required string RegistryId { get; init; }
    public required string? OwnerId { get; init; }
    public string? ContentJson { get; init; }
    public DateTimeOffset? DateAdded { get; init; }
    public bool IsContentComplete { get; init; }
    public bool IsPublic { get; init; }
}

/// <summary>
///     Summary entry for instance browsing list.
/// </summary>
public sealed class CloudInstanceListEntry
{
    public required string RegistryId { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Notes { get; init; }
    public string? TargetVsVersion { get; init; }
    public string? Uploader { get; init; }
    public int ModCount { get; init; }
    public int CategoryCount { get; init; }
    public DateTimeOffset? DateAdded { get; init; }
    public bool IsPublic { get; init; }
    public string? ContentJson { get; init; }
    public string? ImageBase64 { get; init; }
}

/// <summary>
///     Firebase RTDB store for shared instances.
///     Reuses the same database and paths as modlists with "inst_" prefix to avoid conflicts.
/// </summary>
public sealed class FirebaseInstanceStore : IDisposable
{
    private static readonly HttpClient HttpClient = new();

    // Uses separate Firebase project for instances
    private static readonly string DefaultDbUrl = DevConfig.FirebaseInstanceDefaultDbUrl;
    private static readonly string DefaultApiKey = DevConfig.FirebaseInstanceApiKey;

    // 3 slots for instances
    private static readonly string[] KnownSlots = { "slot1", "slot2", "slot3" };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _dbUrl;
    private readonly SemaphoreSlim _ownershipClaimLock = new(1, 1);
    private string? _ownershipClaimedForUid;
    private string? _playerName;
    private string? _playerUid;
    private string? _sanitizedPlayerUid;
    private bool _disposed;

    public FirebaseInstanceStore()
        : this(DefaultDbUrl, new FirebaseAnonymousAuthenticator(DefaultApiKey))
    {
    }

    public FirebaseInstanceStore(string dbUrl, FirebaseAnonymousAuthenticator authenticator)
    {
        _dbUrl = (dbUrl ?? throw new ArgumentNullException(nameof(dbUrl))).TrimEnd('/');
        Authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
    }

    internal FirebaseAnonymousAuthenticator Authenticator { get; }

    public static IReadOnlyList<string> SlotKeys => KnownSlots;

    public string? CurrentUserId => string.IsNullOrWhiteSpace(_playerUid) ? null : _playerUid;

    public void SetPlayerIdentity(string? playerUid, string? playerName)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FirebaseInstanceStore));
        _playerUid = Normalize(playerUid);
        _playerName = Normalize(playerName);
        _sanitizedPlayerUid = string.IsNullOrWhiteSpace(_playerUid)
            ? null
            : SanitizePlayerUidForFirebase(_playerUid);

        if (!string.Equals(_ownershipClaimedForUid, _sanitizedPlayerUid, StringComparison.Ordinal))
            _ownershipClaimedForUid = null;
    }

    /// <summary>
    ///     Save an instance to a slot with visibility option.
    /// </summary>
    public async Task SaveAsync(string slotKey, string instanceJson, bool isPublic, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FirebaseInstanceStore));
        InternetAccessManager.ThrowIfInternetAccessDisabled();
        ValidateSlotKey(slotKey);
        var identity = GetIdentityComponents();
        await EnsureOwnershipAsync(identity, ct).ConfigureAwait(false);

        using var document = JsonDocument.Parse(instanceJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The instance JSON must be an object.");

        var normalizedContent = ReplaceUploader(document.RootElement, identity);

        // Read existing slot to get registryId if present
        var existing = await TryReadSlotNodeAsync(identity.SanitizedUid, slotKey, ct).ConfigureAwait(false);
        var registryId = existing != null && !string.IsNullOrWhiteSpace(existing.RegistryId)
            ? existing.RegistryId!
            : GenerateEntryId();

        var dateAddedIso = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        var summaryJson = BuildSummaryJson(normalizedContent, dateAddedIso, isPublic);
        var userSlotJson = BuildSlotNodeJson(normalizedContent, registryId, dateAddedIso, isPublic);

        var saveResult = await SendWithAuthRetryAsync(session =>
        {
            var rootUrl = BuildAuthenticatedUrl(session.IdToken, null);

            var registryNodeJson =
                $"{{\"content\":{normalizedContent},\"dateAdded\":{JsonSerializer.Serialize(dateAddedIso)},\"isPublic\":{(isPublic ? "true" : "false")}}}";
            var registryOwnerJson = JsonSerializer.Serialize(session.UserId);

            var patchJson =
                $"{{" +
                $"\"/users/{identity.SanitizedUid}/{slotKey}\":{userSlotJson}," +
                $"\"/registryOwners/{registryId}\":{registryOwnerJson}," +
                $"\"/registry/{registryId}\":{registryNodeJson}," +
                $"\"/registrySummaries/{registryId}\":{summaryJson}" +
                $"}}";

            var req = new HttpRequestMessage(new HttpMethod("PATCH"), rootUrl)
            {
                Content = new StringContent(patchJson, Encoding.UTF8, "application/json")
            };
            return HttpClient.SendAsync(req, ct);
        }, ct).ConfigureAwait(false);

        using (saveResult.Response)
        {
            if (!saveResult.Response.IsSuccessStatusCode)
            {
                var errorBody = await saveResult.Response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException($"Save instance failed: {saveResult.Response.StatusCode} - {errorBody}");
            }
        }
    }

    /// <summary>
    ///     Load instance JSON from a slot.
    /// </summary>
    public async Task<string?> LoadAsync(string slotKey, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FirebaseInstanceStore));
        InternetAccessManager.ThrowIfInternetAccessDisabled();
        ValidateSlotKey(slotKey);
        var identity = GetIdentityComponents();
        await EnsureOwnershipAsync(identity, ct).ConfigureAwait(false);

        var sendResult = await SendWithAuthRetryAsync(session =>
        {
            var userUrl = BuildAuthenticatedUrl(session.IdToken, null, "users", identity.SanitizedUid, slotKey);
            return HttpClient.GetAsync(userUrl, ct);
        }, ct).ConfigureAwait(false);

        using var response = sendResult.Response;

        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        await EnsureOk(response, "Load instance").ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("content", out var content))
                return content.GetRawText();
        }
        catch
        {
            // Ignore parse errors
        }

        return null;
    }

    /// <summary>
    ///     Delete an instance from a slot.
    /// </summary>
    public async Task DeleteAsync(string slotKey, CancellationToken ct = default)
    {
        InternetAccessManager.ThrowIfInternetAccessDisabled();
        ValidateSlotKey(slotKey);
        var identity = GetIdentityComponents();
        await EnsureOwnershipAsync(identity, ct).ConfigureAwait(false);

        var node = await TryReadSlotNodeAsync(identity.SanitizedUid, slotKey, ct).ConfigureAwait(false);
        var registryId = node?.RegistryId;

        var result = await SendWithAuthRetryAsync(session =>
        {
            var rootUrl = BuildAuthenticatedUrl(session.IdToken, null);

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"/users/{identity.SanitizedUid}/{slotKey}\":null");

            if (!string.IsNullOrWhiteSpace(registryId))
            {
                sb.Append($",\"/registry/{registryId}\":null");
                sb.Append($",\"/registryOwners/{registryId}\":null");
                sb.Append($",\"/registrySummaries/{registryId}\":null");
            }

            sb.Append('}');

            var req = new HttpRequestMessage(new HttpMethod("PATCH"), rootUrl)
            {
                Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/json")
            };
            return HttpClient.SendAsync(req, ct);
        }, ct).ConfigureAwait(false);

        using (result.Response)
        {
            if (!result.Response.IsSuccessStatusCode && result.Response.StatusCode != HttpStatusCode.NotFound)
                await EnsureOk(result.Response, "Delete instance").ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     List used slots for the current user.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default)
    {
        InternetAccessManager.ThrowIfInternetAccessDisabled();
        var identity = GetIdentityComponents();
        await EnsureOwnershipAsync(identity, ct).ConfigureAwait(false);

        var sendResult = await SendWithAuthRetryAsync(session =>
        {
            var url = BuildAuthenticatedUrl(session.IdToken, "shallow=true", "users", identity.SanitizedUid);
            return HttpClient.GetAsync(url, ct);
        }, ct).ConfigureAwait(false);

        using var response = sendResult.Response;

        if (response.StatusCode == HttpStatusCode.NotFound)
            return Array.Empty<string>();

        await EnsureOk(response, "List instance slots").ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return Array.Empty<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateObject()
                .Select(p => p.Name)
                .Where(n => KnownSlots.Contains(n, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    ///     Get the first free slot, or null if all slots are used.
    /// </summary>
    public async Task<string?> GetFirstFreeSlotAsync(CancellationToken ct = default)
    {
        var usedSlots = await ListSlotsAsync(ct).ConfigureAwait(false);
        return KnownSlots.FirstOrDefault(s => !usedSlots.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Get public instance registry entries for browsing.
    /// </summary>
    public async Task<IReadOnlyList<CloudInstanceListEntry>> GetRegistryEntriesAsync(bool publicOnly, CancellationToken ct = default)
    {
        InternetAccessManager.ThrowIfInternetAccessDisabled();

        var sendResult = await SendWithAuthRetryAsync(session =>
        {
            var url = BuildAuthenticatedUrl(session.IdToken, null, "registrySummaries");
            return HttpClient.GetAsync(url, ct);
        }, ct).ConfigureAwait(false);

        using var response = sendResult.Response;

        if (response.StatusCode == HttpStatusCode.NotFound)
            return Array.Empty<CloudInstanceListEntry>();

        await EnsureOk(response, "Get instance registry").ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return Array.Empty<CloudInstanceListEntry>();

        var entries = new List<CloudInstanceListEntry>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var entry = ParseSummaryEntry(prop.Name, prop.Value);
                if (entry != null && (!publicOnly || entry.IsPublic))
                    entries.Add(entry);
            }
        }
        catch
        {
            // Ignore parse errors
        }

        return entries.OrderByDescending(e => e.DateAdded).ToList();
    }

    /// <summary>
    ///     Get a specific instance from the registry by ID.
    /// </summary>
    public async Task<CloudInstanceRegistryEntry?> GetRegistryEntryAsync(string registryId, CancellationToken ct = default)
    {
        InternetAccessManager.ThrowIfInternetAccessDisabled();

        if (string.IsNullOrWhiteSpace(registryId))
            throw new ArgumentException("Registry ID cannot be null or whitespace.", nameof(registryId));

        var sendResult = await SendWithAuthRetryAsync(session =>
        {
            var url = BuildAuthenticatedUrl(session.IdToken, null, "registry", registryId);
            return HttpClient.GetAsync(url, ct);
        }, ct).ConfigureAwait(false);

        using var response = sendResult.Response;

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureOk(response, "Get instance entry").ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var contentJson = root.TryGetProperty("content", out var content)
                ? content.GetRawText()
                : null;

            var dateAdded = root.TryGetProperty("dateAdded", out var dateStr)
                ? DateTimeOffset.TryParse(dateStr.GetString(), out var dt) ? dt : (DateTimeOffset?)null
                : null;

            var isPublic = root.TryGetProperty("isPublic", out var isPub) && isPub.GetBoolean();

            return new CloudInstanceRegistryEntry
            {
                RegistryId = registryId,
                OwnerId = null,
                ContentJson = contentJson,
                DateAdded = dateAdded,
                IsContentComplete = true,
                IsPublic = isPublic
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Get all slots owned by the current user with their content.
    /// </summary>
    public async Task<IReadOnlyList<UserInstanceSlot>> GetUserSlotsAsync(CancellationToken ct = default)
    {
        InternetAccessManager.ThrowIfInternetAccessDisabled();
        var identity = GetIdentityComponents();
        await EnsureOwnershipAsync(identity, ct).ConfigureAwait(false);

        var sendResult = await SendWithAuthRetryAsync(session =>
        {
            var url = BuildAuthenticatedUrl(session.IdToken, null, "users", identity.SanitizedUid);
            return HttpClient.GetAsync(url, ct);
        }, ct).ConfigureAwait(false);

        using var response = sendResult.Response;

        if (response.StatusCode == HttpStatusCode.NotFound)
            return Array.Empty<UserInstanceSlot>();

        await EnsureOk(response, "Get user instance slots").ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return Array.Empty<UserInstanceSlot>();

        var slots = new List<UserInstanceSlot>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var slotKey in KnownSlots)
            {
                if (!doc.RootElement.TryGetProperty(slotKey, out var slotElement))
                    continue;

                var registryId = slotElement.TryGetProperty("registryId", out var regId) ? regId.GetString() : null;
                var isPublic = slotElement.TryGetProperty("isPublic", out var isPub) && isPub.GetBoolean();
                var contentJson = slotElement.TryGetProperty("content", out var content) ? content.GetRawText() : null;

                string? name = null;
                string? description = null;
                string? targetVsVersion = null;
                var modCount = 0;

                if (contentJson != null)
                {
                    try
                    {
                        using var contentDoc = JsonDocument.Parse(contentJson);
                        var root = contentDoc.RootElement;
                        name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                        description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
                        targetVsVersion = root.TryGetProperty("targetVsVersion", out var v) ? v.GetString() : null;
                        modCount = root.TryGetProperty("mods", out var mods) && mods.ValueKind == JsonValueKind.Array
                            ? mods.GetArrayLength()
                            : 0;
                    }
                    catch
                    {
                        // Ignore parse errors
                    }
                }

                slots.Add(new UserInstanceSlot(slotKey, registryId, name, description, targetVsVersion, modCount, isPublic, contentJson));
            }
        }
        catch
        {
            // Ignore parse errors
        }

        return slots;
    }

    /// <summary>
    ///     Update the visibility of an existing instance.
    /// </summary>
    public async Task UpdateVisibilityAsync(string slotKey, bool isPublic, CancellationToken ct = default)
    {
        InternetAccessManager.ThrowIfInternetAccessDisabled();
        ValidateSlotKey(slotKey);
        var identity = GetIdentityComponents();
        await EnsureOwnershipAsync(identity, ct).ConfigureAwait(false);

        // Read the existing slot to get registryId and content
        var node = await TryReadSlotNodeAsync(identity.SanitizedUid, slotKey, ct).ConfigureAwait(false);
        if (node == null || string.IsNullOrWhiteSpace(node.RegistryId))
            throw new InvalidOperationException("Slot not found or has no registry entry.");

        var registryId = node.RegistryId!;
        var contentJson = node.Content.GetRawText();

        // Rebuild the summary with the new visibility
        var dateAddedIso = node.DateAdded ?? DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        var summaryJson = BuildSummaryJson(contentJson, dateAddedIso, isPublic);
        var userSlotJson = BuildSlotNodeJson(contentJson, registryId, dateAddedIso, isPublic);

        var result = await SendWithAuthRetryAsync(session =>
        {
            var rootUrl = BuildAuthenticatedUrl(session.IdToken, null);

            var registryNodeJson =
                $"{{\"content\":{contentJson},\"dateAdded\":{JsonSerializer.Serialize(dateAddedIso)},\"isPublic\":{(isPublic ? "true" : "false")}}}";

            var patchJson =
                $"{{" +
                $"\"/users/{identity.SanitizedUid}/{slotKey}\":{userSlotJson}," +
                $"\"/registry/{registryId}\":{registryNodeJson}," +
                $"\"/registrySummaries/{registryId}\":{summaryJson}" +
                $"}}";

            var req = new HttpRequestMessage(new HttpMethod("PATCH"), rootUrl)
            {
                Content = new StringContent(patchJson, Encoding.UTF8, "application/json")
            };
            return HttpClient.SendAsync(req, ct);
        }, ct).ConfigureAwait(false);

        using (result.Response)
        {
            await EnsureOk(result.Response, "Update instance visibility").ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ownershipClaimLock.Dispose();
        Authenticator.Dispose();
    }

    #region Private Helpers

    private static string? Normalize(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string SanitizePlayerUidForFirebase(string uid)
    {
        var sb = new StringBuilder(uid.Length);
        foreach (var c in uid)
        {
            if (c is '.' or '#' or '$' or '[' or ']' or '/')
                sb.Append('_');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static void ValidateSlotKey(string slotKey)
    {
        if (!KnownSlots.Contains(slotKey, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid slot key: {slotKey}");
    }

    private static string GenerateEntryId() => Guid.NewGuid().ToString("n");

    private PlayerIdentity GetIdentityComponents()
    {
        if (string.IsNullOrWhiteSpace(_playerUid) || string.IsNullOrWhiteSpace(_sanitizedPlayerUid))
            throw new InvalidOperationException("Player identity not set. Call SetPlayerIdentity first.");

        return new PlayerIdentity(_playerUid, _sanitizedPlayerUid, _playerName ?? "Unknown");
    }

    private async Task EnsureOwnershipAsync(PlayerIdentity identity, CancellationToken ct)
    {
        if (_ownershipClaimedForUid == identity.SanitizedUid)
            return;

        await _ownershipClaimLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ownershipClaimedForUid == identity.SanitizedUid)
                return;

            // First check if ownership already claimed
            var readResult = await SendWithAuthRetryAsync(session =>
            {
                var url = BuildAuthenticatedUrl(session.IdToken, null, "owners", identity.SanitizedUid);
                return HttpClient.GetAsync(url, ct);
            }, ct).ConfigureAwait(false);

            using (readResult.Response)
            {
                if (readResult.Response.IsSuccessStatusCode)
                {
                    var body = await readResult.Response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(body) && body != "null")
                    {
                        // Ownership exists - check if it's ours
                        var ownerId = body.Trim().Trim('"');
                        if (string.Equals(ownerId, readResult.Session.UserId, StringComparison.Ordinal))
                        {
                            _ownershipClaimedForUid = identity.SanitizedUid;
                            return;
                        }
                    }
                }
            }

            // Claim ownership
            var claimResult = await SendWithAuthRetryAsync(session =>
            {
                var url = BuildAuthenticatedUrl(session.IdToken, null, "owners", identity.SanitizedUid);
                var req = new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(session.UserId), Encoding.UTF8, "application/json")
                };
                return HttpClient.SendAsync(req, ct);
            }, ct).ConfigureAwait(false);

            using (claimResult.Response)
            {
                await EnsureOk(claimResult.Response, "Claim ownership").ConfigureAwait(false);
            }

            _ownershipClaimedForUid = identity.SanitizedUid;
        }
        finally
        {
            _ownershipClaimLock.Release();
        }
    }

    private async Task<InstanceSlotNode?> TryReadSlotNodeAsync(string uid, string slotKey, CancellationToken ct)
    {
        var sendResult = await SendWithAuthRetryAsync(session =>
        {
            var url = BuildAuthenticatedUrl(session.IdToken, null, "users", uid, slotKey);
            return HttpClient.GetAsync(url, ct);
        }, ct).ConfigureAwait(false);

        using var response = sendResult.Response;

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureOk(response, "Read instance slot").ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return null;

        try
        {
            return JsonSerializer.Deserialize<InstanceSlotNode>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildSlotNodeJson(string contentJson, string registryId, string dateAdded, bool isPublic)
    {
        return $"{{\"registryId\":{JsonSerializer.Serialize(registryId)},\"content\":{contentJson},\"dateAdded\":{JsonSerializer.Serialize(dateAdded)},\"isPublic\":{(isPublic ? "true" : "false")}}}";
    }

    private static string BuildSummaryJson(string contentJson, string dateAdded, bool isPublic)
    {
        using var doc = JsonDocument.Parse(contentJson);
        var root = doc.RootElement;

        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        var description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
        var vsVersion = root.TryGetProperty("targetVsVersion", out var v) ? v.GetString() : null;
        var uploader = root.TryGetProperty("uploader", out var u) ? u.GetString() : null;
        var imageBase64 = root.TryGetProperty("imageBase64", out var img) ? img.GetString() : null;
        var modCount = root.TryGetProperty("mods", out var mods) && mods.ValueKind == JsonValueKind.Array
            ? mods.GetArrayLength()
            : 0;
        var categoryCount = root.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array
            ? cats.GetArrayLength()
            : 0;

        var summary = new
        {
            name,
            description,
            targetVsVersion = vsVersion,
            uploader,
            modCount,
            categoryCount,
            isPublic,
            isInstance = true, // Distinguishes instances from modlists in the shared registry
            imageBase64,
            dateAdded
        };

        return JsonSerializer.Serialize(summary, JsonOpts);
    }

    private static string ReplaceUploader(JsonElement content, PlayerIdentity identity)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content.GetRawText(), JsonOpts)
            ?? new Dictionary<string, JsonElement>();

        dict["uploader"] = JsonSerializer.SerializeToElement(identity.Name, JsonOpts);
        dict["uploaderId"] = JsonSerializer.SerializeToElement(identity.SanitizedUid, JsonOpts);

        return JsonSerializer.Serialize(dict, JsonOpts);
    }

    private static CloudInstanceListEntry? ParseSummaryEntry(string registryId, JsonElement element)
    {
        try
        {
            var name = element.TryGetProperty("name", out var n) ? n.GetString() : null;
            var description = element.TryGetProperty("description", out var d) ? d.GetString() : null;
            var vsVersion = element.TryGetProperty("targetVsVersion", out var v) ? v.GetString() : null;
            var uploader = element.TryGetProperty("uploader", out var u) ? u.GetString() : null;
            var modCount = element.TryGetProperty("modCount", out var m) ? m.GetInt32() : 0;
            var categoryCount = element.TryGetProperty("categoryCount", out var c) ? c.GetInt32() : 0;
            var isPublic = element.TryGetProperty("isPublic", out var p) && p.GetBoolean();
            var imageBase64 = element.TryGetProperty("imageBase64", out var img) ? img.GetString() : null;
            var dateAdded = element.TryGetProperty("dateAdded", out var dt)
                ? DateTimeOffset.TryParse(dt.GetString(), out var parsed) ? parsed : (DateTimeOffset?)null
                : null;

            return new CloudInstanceListEntry
            {
                RegistryId = registryId,
                Name = name,
                Description = description,
                TargetVsVersion = vsVersion,
                Uploader = uploader,
                ModCount = modCount,
                CategoryCount = categoryCount,
                DateAdded = dateAdded,
                IsPublic = isPublic,
                ImageBase64 = imageBase64
            };
        }
        catch
        {
            return null;
        }
    }

    private string BuildAuthenticatedUrl(string idToken, string? queryParams, params string[] pathSegments)
    {
        var path = string.Join("/", pathSegments.Where(s => !string.IsNullOrEmpty(s)));
        var url = $"{_dbUrl}/{path}.json?auth={Uri.EscapeDataString(idToken)}";
        if (!string.IsNullOrWhiteSpace(queryParams))
            url += $"&{queryParams}";
        return url;
    }

    private async Task<SendResult> SendWithAuthRetryAsync(
        Func<FirebaseAuthSession, Task<HttpResponseMessage>> sendFunc,
        CancellationToken ct)
    {
        var hasRetried = false;

        while (true)
        {
            InternetAccessManager.ThrowIfInternetAccessDisabled();
            var session = await Authenticator.GetSessionAsync(ct).ConfigureAwait(false);
            var response = await sendFunc(session).ConfigureAwait(false);

            if (IsAuthError(response.StatusCode) && !hasRetried)
            {
                hasRetried = true;
                response.Dispose();
                await Authenticator.MarkTokenAsExpiredAsync(ct).ConfigureAwait(false);
                continue;
            }

            return new SendResult(session, response);
        }
    }

    private static bool IsAuthError(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    }

    private static async Task EnsureOk(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new HttpRequestException($"{operation} failed: {response.StatusCode} - {body}");
    }

    #endregion

    #region Internal Types

    private readonly record struct PlayerIdentity(string OriginalUid, string SanitizedUid, string Name);

    private readonly record struct SendResult(FirebaseAuthSession Session, HttpResponseMessage Response);

    private sealed class InstanceSlotNode
    {
        [JsonPropertyName("registryId")]
        public string? RegistryId { get; set; }

        [JsonPropertyName("content")]
        public JsonElement Content { get; set; }

        [JsonPropertyName("dateAdded")]
        public string? DateAdded { get; set; }

        [JsonPropertyName("isPublic")]
        public bool IsPublic { get; set; }
    }

    #endregion
}

/// <summary>
///     Represents a user's own instance slot for management operations.
/// </summary>
public sealed record UserInstanceSlot(
    string SlotKey,
    string? RegistryId,
    string? Name,
    string? Description,
    string? TargetVsVersion,
    int ModCount,
    bool IsPublic,
    string? ContentJson)
{
    public string SlotLabel => SlotKey switch
    {
        "slot1" => "Slot 1",
        "slot2" => "Slot 2",
        "slot3" => "Slot 3",
        _ => SlotKey
    };
}
