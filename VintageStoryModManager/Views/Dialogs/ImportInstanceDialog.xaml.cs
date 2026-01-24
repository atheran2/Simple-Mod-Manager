using System.Windows;
using VintageStoryModManager.Models;
using VintageStoryModManager.Services;
using MessageBox = System.Windows.MessageBox;

namespace VintageStoryModManager.Views.Dialogs;

/// <summary>
///     View model for mod items in the import list.
/// </summary>
public sealed class ImportModItemViewModel
{
    public string ModId { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Version { get; init; }
    public bool IsAvailable { get; init; }
    public string? UnavailableReason { get; init; }
    public string StatusIcon => IsAvailable ? "\u2714" : "\u26A0"; // check mark or warning
}

public partial class ImportInstanceDialog : Window
{
    private readonly SerializableInstance _sourceInstance;
    private readonly InstanceSharingService _sharingService;
    private InstanceImportPreview? _preview;

    public ImportInstanceDialog(
        Window owner,
        SerializableInstance sourceInstance,
        InstanceSharingService sharingService)
    {
        InitializeComponent();
        Owner = owner;

        _sourceInstance = sourceInstance;
        _sharingService = sharingService;

        // Set default name
        InstanceNameTextBox.Text = sourceInstance.Name ?? "Imported Instance";

        // Show source info
        SourceInfoText.Text = !string.IsNullOrWhiteSpace(sourceInstance.Uploader)
            ? $"From: {sourceInstance.Uploader}"
            : "From: Local file";

        VsVersionText.Text = !string.IsNullOrWhiteSpace(sourceInstance.TargetVsVersion)
            ? $"VS Version: {sourceInstance.TargetVsVersion}"
            : "VS Version: Not specified";

        if (!string.IsNullOrWhiteSpace(sourceInstance.Description))
        {
            DescriptionText.Text = sourceInstance.Description;
            DescriptionText.Visibility = Visibility.Visible;
        }

        // Load preview asynchronously
        Loaded += async (_, _) => await LoadPreviewAsync();
    }

    /// <summary>
    ///     Gets the created instance after successful import.
    /// </summary>
    public GameInstance? ImportedInstance { get; private set; }

    private async Task LoadPreviewAsync()
    {
        try
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = "Checking mod availability...";
            ImportButton.IsEnabled = false;

            _preview = await _sharingService.PreviewImportAsync(_sourceInstance);

            // Update UI
            ModsHeaderRun.Text = $"Mods ({_preview.Mods.Count})";

            var modItems = _preview.Mods.Select(m => new ImportModItemViewModel
            {
                ModId = m.ModId,
                DisplayName = m.DisplayName,
                Version = m.Version,
                IsAvailable = m.IsAvailable,
                UnavailableReason = m.UnavailableReason
            }).ToList();

            ModsListView.ItemsSource = modItems;

            // Categories & configs info
            CategoriesInfoText.Text = _preview.Categories.Count > 0
                ? $"Categories: {string.Join(", ", _preview.Categories)}"
                : "Categories: None";

            ConfigsInfoText.Text = $"Configuration files: {_preview.ConfigFileCount}";

            // Warnings
            if (_preview.Warnings.Count > 0)
            {
                WarningsPanel.Visibility = Visibility.Visible;
                WarningsText.Text = string.Join("\n", _preview.Warnings.Take(5));
                if (_preview.Warnings.Count > 5)
                    WarningsText.Text += $"\n... and {_preview.Warnings.Count - 5} more";
            }

            ImportButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to load preview:\n{ex.Message}",
                "Preview Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        var instanceName = InstanceNameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            MessageBox.Show(
                "Please enter a name for the instance.",
                "Import Instance",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            ImportButton.IsEnabled = false;
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = "Importing instance...";

            var progress = new Progress<InstanceImportProgress>(p =>
            {
                Dispatcher.Invoke(() =>
                {
                    ImportProgressBar.Value = (double)p.CurrentStep / p.TotalSteps * 100;
                    ImportProgressText.Text = !string.IsNullOrWhiteSpace(p.CurrentModName)
                        ? $"{p.CurrentOperation}: {p.CurrentModName}"
                        : p.CurrentOperation;
                });
            });

            ImportedInstance = await _sharingService.ImportInstanceAsync(
                _sourceInstance,
                instanceName,
                progress);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to import instance:\n{ex.Message}",
                "Import Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            ImportButton.IsEnabled = true;
        }
    }
}
