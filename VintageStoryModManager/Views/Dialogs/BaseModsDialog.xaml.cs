using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using VintageStoryModManager.Services;
using WinForms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;

namespace VintageStoryModManager.Views.Dialogs;

public partial class BaseModsDialog : Window
{
    private readonly InstanceService _instanceService;
    private readonly ObservableCollection<string> _modFiles = new();
    private string? _selectedVersion = null;

    public BaseModsDialog(Window owner, InstanceService instanceService)
    {
        InitializeComponent();
        Owner = owner;
        _instanceService = instanceService;

        ModsListBox.ItemsSource = _modFiles;
        ModsListBox.SelectionChanged += ModsListBox_SelectionChanged;
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        CopyBaseModsCheckBox.IsChecked = _instanceService.CopyBaseModsOnCreate;
        RefreshVersionList();
    }

    private void RefreshVersionList()
    {
        var versions = _instanceService.GetExistingBaseModVersions().ToList();
        VersionComboBox.Items.Clear();
        VersionComboBox.Items.Add("(unversioned)");
        foreach (var v in versions)
            VersionComboBox.Items.Add(v);

        // Select first real version if any, else unversioned
        VersionComboBox.SelectedIndex = versions.Count > 0 ? 1 : 0;
    }

    private void RefreshModsList()
    {
        _modFiles.Clear();

        foreach (var file in _instanceService.GetBaseModFiles(_selectedVersion))
        {
            _modFiles.Add(file);
        }

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = _modFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ModsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RemoveModButton.IsEnabled = ModsListBox.SelectedItems.Count > 0;
    }

    private void CopyBaseModsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _instanceService.CopyBaseModsOnCreate = CopyBaseModsCheckBox.IsChecked == true;
    }

    private void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Use Text (not SelectedItem) so typed values on the editable ComboBox are captured
        var text = VersionComboBox.Text?.Trim();
        _selectedVersion = string.IsNullOrWhiteSpace(text) || text == "(unversioned)" ? null : text;
        RefreshModsList();
    }

    private void NewVersionButton_OnClick(object sender, RoutedEventArgs e)
    {
        var typed = VersionComboBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(typed) || typed == "(unversioned)") return;

        _instanceService.EnsureVersionedBaseModsDirectoryExists(typed);
        RefreshVersionList();

        // Select the newly created version
        var idx = VersionComboBox.Items.IndexOf(typed);
        if (idx >= 0) VersionComboBox.SelectedIndex = idx;
    }

    private void AddModButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = "Select mod file(s) to add to base mods",
            Filter = "Mod files (*.zip;*.cs)|*.zip;*.cs|Zip files (*.zip)|*.zip|C# files (*.cs)|*.cs|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
            return;

        var addedCount = 0;
        var failedCount = 0;

        foreach (var filePath in dialog.FileNames)
        {
            if (_instanceService.AddModToBaseMods(filePath, _selectedVersion))
                addedCount++;
            else
                failedCount++;
        }

        RefreshModsList();

        if (failedCount > 0)
        {
            WpfMessageBox.Show(
                $"Added {addedCount} mod(s). Failed to add {failedCount} mod(s).",
                "Base Mods",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RemoveModButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedItems = ModsListBox.SelectedItems.Cast<string>().ToList();
        if (selectedItems.Count == 0)
            return;

        var message = selectedItems.Count == 1
            ? $"Remove '{selectedItems[0]}' from base mods?"
            : $"Remove {selectedItems.Count} mods from base mods?";

        var result = WpfMessageBox.Show(
            message + "\n\nThis will delete the files from the base mods folder.",
            "Remove Base Mods",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        var removedCount = 0;
        foreach (var fileName in selectedItems)
        {
            if (_instanceService.RemoveModFromBaseMods(fileName, _selectedVersion))
                removedCount++;
        }

        RefreshModsList();
    }

    private void OpenFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        _instanceService.OpenBaseModsFolder(_selectedVersion);
    }
}
