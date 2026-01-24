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
            ShareButton.Content = "Uploading...";

            // Set player identity
            _cloudStore.SetPlayerIdentity(_playerUid, _playerName);

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
