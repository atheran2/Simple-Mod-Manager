using System.IO;
using System.Text.Json;
using System.Windows;
using VintageStoryModManager.Models;
using VintageStoryModManager.Services;
using VintageStoryModManager.ViewModels;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace VintageStoryModManager.Views.Dialogs;

public partial class ShareInstanceDialog : Window
{
    private readonly GameInstance _instance;
    private readonly IReadOnlyList<ModListItemViewModel>? _mods;
    private readonly InstanceSharingService _sharingService;
    private readonly FirebaseInstanceStore? _cloudStore;
    private readonly string? _playerUid;
    private readonly string? _playerName;

    public ShareInstanceDialog(
        Window owner,
        GameInstance instance,
        IReadOnlyList<ModListItemViewModel>? mods,
        InstanceSharingService sharingService,
        FirebaseInstanceStore? cloudStore = null,
        string? playerUid = null,
        string? playerName = null)
    {
        InitializeComponent();
        Owner = owner;

        _instance = instance;
        _mods = mods;
        _sharingService = sharingService;
        _cloudStore = cloudStore;
        _playerUid = playerUid;
        _playerName = playerName;

        // Display instance info
        InstanceNameText.Text = instance.Name;

        // Count mods - either from passed list or by scanning the folder
        var modCount = mods?.Count ?? CountModsInFolder(instance.ModsPath);
        ModCountText.Text = $"{modCount} mods";

        VsVersionText.Text = !string.IsNullOrWhiteSpace(instance.TargetVsVersion)
            ? $"VS Version: {instance.TargetVsVersion}"
            : "VS Version: Not specified";

        // Pre-fill description with notes if available
        if (!string.IsNullOrWhiteSpace(instance.Notes))
            DescriptionTextBox.Text = instance.Notes;

        // Enable cloud upload if we have a player identity
        var hasCloudAccess = cloudStore != null && !string.IsNullOrWhiteSpace(playerUid);
        UploadToCloudRadio.IsEnabled = hasCloudAccess;
        if (!hasCloudAccess)
            UploadToCloudRadio.ToolTip = "Cloud sharing requires a valid Vintage Story player identity.";
        else
            UploadToCloudRadio.ToolTip = null;
    }

    /// <summary>
    ///     Gets whether the export was successful.
    /// </summary>
    public bool ExportSucceeded { get; private set; }

    /// <summary>
    ///     Gets the path where the instance was exported (for file export).
    /// </summary>
    public string? ExportedFilePath { get; private set; }

    /// <summary>
    ///     Gets the cloud registry ID if uploaded to cloud.
    /// </summary>
    public string? CloudRegistryId { get; private set; }

    private async void ShareButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SaveToFileRadio.IsChecked == true)
        {
            await ExportToFileAsync();
        }
        else if (UploadToCloudRadio.IsChecked == true)
        {
            await UploadToCloudAsync();
        }
    }

    private async Task ExportToFileAsync()
    {
        var saveDialog = new SaveFileDialog
        {
            Title = "Save Instance",
            Filter = InstanceSharingService.FileFilter,
            FileName = $"{SanitizeFileName(_instance.Name)}.vsinstance",
            DefaultExt = ".vsinstance"
        };

        if (saveDialog.ShowDialog() != true)
            return;

        try
        {
            ShareButton.IsEnabled = false;
            ShareButton.Content = "Exporting...";

            await _sharingService.ExportToFileAsync(
                _instance,
                _mods,
                saveDialog.FileName,
                IncludeConfigsCheckBox.IsChecked == true,
                IncludeCategoriesCheckBox.IsChecked == true,
                DescriptionTextBox.Text);

            ExportSucceeded = true;
            ExportedFilePath = saveDialog.FileName;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to export instance:\n{ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ShareButton.IsEnabled = true;
            ShareButton.Content = "Share";
        }
    }

    private async Task UploadToCloudAsync()
    {
        if (_cloudStore == null || string.IsNullOrWhiteSpace(_playerUid))
        {
            MessageBox.Show(
                "Cloud sharing requires a valid Vintage Story player identity.",
                "Share Instance",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            ShareButton.IsEnabled = false;
            ShareButton.Content = "Checking...";

            // Set player identity
            _cloudStore.SetPlayerIdentity(_playerUid, _playerName);

            // Build the serializable instance
            var serializable = _sharingService.BuildSerializableInstance(
                _instance,
                _mods,
                IncludeConfigsCheckBox.IsChecked == true,
                IncludeCategoriesCheckBox.IsChecked == true,
                DescriptionTextBox.Text);

            // Serialize to JSON (use camelCase to match FirebaseInstanceStore expectations)
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var instanceJson = JsonSerializer.Serialize(serializable, jsonOptions);

            // Check if an identical instance already exists
            var existingSlots = await _cloudStore.GetUserSlotsAsync();
            var existingSlot = FindMatchingSlot(existingSlots, instanceJson);

            if (existingSlot != null)
            {
                // Instance already exists with same content
                var result = MessageBox.Show(
                    $"An identical instance already exists in {existingSlot.SlotLabel}.\n\n" +
                    "Do you want to upload anyway (will use a new slot)?",
                    "Instance Already Exists",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            ShareButton.Content = "Uploading...";

            // Check for free slot
            var freeSlot = await _cloudStore.GetFirstFreeSlotAsync();
            if (freeSlot == null)
            {
                MessageBox.Show(
                    "All instance slots are in use (max 3).\n\nDelete an existing shared instance to free up a slot.",
                    "Share Instance",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Upload
            var isPublic = PublicVisibilityRadio.IsChecked == true;
            await _cloudStore.SaveAsync(freeSlot, instanceJson, isPublic);

            ExportSucceeded = true;
            DialogResult = true;

            var visibilityText = isPublic ? "public (visible in browse list)" : "unlisted (link sharing only)";
            MessageBox.Show(
                $"Instance uploaded successfully as {visibilityText}!\n\nOthers can find it in the shared instances browser or import by ID.",
                "Share Instance",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to upload instance:\n{ex.Message}",
                "Upload Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ShareButton.IsEnabled = true;
            ShareButton.Content = "Share";
        }
    }

    private static UserInstanceSlot? FindMatchingSlot(IReadOnlyList<UserInstanceSlot> slots, string newInstanceJson)
    {
        // Parse the new instance to compare key fields
        try
        {
            using var newDoc = JsonDocument.Parse(newInstanceJson);
            var newRoot = newDoc.RootElement;

            var newName = newRoot.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
            var newMods = newRoot.TryGetProperty("mods", out var modsProp) ? modsProp.GetArrayLength() : 0;

            // Build a simple hash of mod IDs and versions for comparison
            var newModsHash = BuildModsHash(newRoot);

            foreach (var slot in slots)
            {
                if (string.IsNullOrWhiteSpace(slot.ContentJson))
                    continue;

                try
                {
                    using var existingDoc = JsonDocument.Parse(slot.ContentJson);
                    var existingRoot = existingDoc.RootElement;

                    var existingName = existingRoot.TryGetProperty("name", out var existingNameProp)
                        ? existingNameProp.GetString()
                        : null;

                    // Check if names match
                    if (!string.Equals(newName, existingName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Check if mods match
                    var existingModsHash = BuildModsHash(existingRoot);
                    if (newModsHash == existingModsHash)
                        return slot;
                }
                catch
                {
                    // Skip slots with invalid JSON
                }
            }
        }
        catch
        {
            // If we can't parse the new instance, don't block upload
        }

        return null;
    }

    private static string BuildModsHash(JsonElement root)
    {
        if (!root.TryGetProperty("mods", out var modsArray))
            return string.Empty;

        var modEntries = new List<string>();
        foreach (var mod in modsArray.EnumerateArray())
        {
            var modId = mod.TryGetProperty("modId", out var idProp) ? idProp.GetString() ?? "" : "";
            var version = mod.TryGetProperty("version", out var vProp) ? vProp.GetString() ?? "" : "";
            var isActive = mod.TryGetProperty("isActive", out var aProp) && aProp.GetBoolean();
            modEntries.Add($"{modId}|{version}|{isActive}");
        }

        modEntries.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(";", modEntries);
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
    }

    private static int CountModsInFolder(string? modsPath)
    {
        if (string.IsNullOrWhiteSpace(modsPath) || !Directory.Exists(modsPath))
            return 0;

        try
        {
            var zipCount = Directory.GetFiles(modsPath, "*.zip", SearchOption.TopDirectoryOnly).Length;
            var csCount = Directory.GetFiles(modsPath, "*.cs", SearchOption.TopDirectoryOnly).Length;
            var dllCount = Directory.GetFiles(modsPath, "*.dll", SearchOption.TopDirectoryOnly).Length;
            var folderCount = Directory.GetDirectories(modsPath)
                .Count(d => File.Exists(Path.Combine(d, "modinfo.json")));
            return zipCount + csCount + dllCount + folderCount;
        }
        catch
        {
            return 0;
        }
    }
}
