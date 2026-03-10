using System.IO;
using System.Windows;
using System.Windows.Controls;
using VintageStoryModManager.Services;
using WinForms = System.Windows.Forms;

namespace VintageStoryModManager.Views.Dialogs;

public partial class CreateInstanceDialog : Window
{
    private readonly string? _defaultGameDirectory;

    public CreateInstanceDialog(Window owner, string? defaultGameDirectory)
    {
        InitializeComponent();
        Owner = owner;
        _defaultGameDirectory = defaultGameDirectory;
    }

    public string InstanceName => NameTextBox.Text.Trim();

    public string? GameDirectory => string.IsNullOrWhiteSpace(GameDirectoryTextBox.Text)
        ? null
        : GameDirectoryTextBox.Text.Trim();

    public string? TargetVsVersion => string.IsNullOrWhiteSpace(VersionTextBox.Text)
        ? null
        : VersionTextBox.Text.Trim();

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        NameTextBox.Focus();
        NameTextBox.SelectAll();

        // Pre-populate with global game directory if available
        if (!string.IsNullOrWhiteSpace(_defaultGameDirectory))
        {
            GameDirectoryTextBox.Text = _defaultGameDirectory;
            RefreshDetectedVersion(_defaultGameDirectory);
        }

        UpdateCreateButtonState();
    }

    private void NameTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCreateButtonState();
    }

    private void UpdateCreateButtonState()
    {
        if (CreateButton is null) return;
        CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text);
    }

    private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select Vintage Story installation folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (!string.IsNullOrWhiteSpace(GameDirectoryTextBox.Text)
            && Directory.Exists(GameDirectoryTextBox.Text))
        {
            dialog.InitialDirectory = GameDirectoryTextBox.Text;
        }

        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;

        var selected = dialog.SelectedPath;
        GameDirectoryTextBox.Text = selected;
        RefreshDetectedVersion(selected);
    }

    private void RefreshDetectedVersion(string? directory)
    {
        var version = VintageStoryVersionLocator.GetInstalledVersion(directory);
        VersionTextBox.Text = version ?? string.Empty;
    }

    private void CreateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InstanceName)) return;
        DialogResult = true;
    }
}
