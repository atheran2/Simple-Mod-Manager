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
        RefreshModsList();
    }

    private void RefreshModsList()
    {
        _modFiles.Clear();

        foreach (var file in _instanceService.GetBaseModFiles())
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
            if (_instanceService.AddModToBaseMods(filePath))
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
            if (_instanceService.RemoveModFromBaseMods(fileName))
                removedCount++;
        }

        RefreshModsList();
    }

    private void OpenFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        _instanceService.OpenBaseModsFolder();
    }
}
