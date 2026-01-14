using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using VintageStoryModManager.Models;
using VintageStoryModManager.Services;
using WinForms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;

namespace VintageStoryModManager.Views.Dialogs;

public partial class InstanceEditDialog : Window
{
    private readonly GameInstance _instance;
    private readonly string _originalName;

    public InstanceEditDialog(Window owner, GameInstance instance, string? gameDirectory)
    {
        InitializeComponent();
        Owner = owner;

        _instance = instance;
        _originalName = instance.Name;

        // Populate fields with current values
        NameTextBox.Text = instance.Name;
        IconPathTextBox.Text = instance.IconPath ?? string.Empty;
        NotesTextBox.Text = instance.Notes ?? string.Empty;

        // Auto-detect and display game version from global game directory
        var gameVersion = VintageStoryVersionLocator.GetInstalledVersion(gameDirectory);
        GameVersionText.Text = gameVersion ?? "Not detected";
    }

    /// <summary>
    /// Gets the updated instance name.
    /// </summary>
    public string InstanceName => NameTextBox.Text.Trim();

    /// <summary>
    /// Gets the updated icon path (null if empty).
    /// </summary>
    public string? IconPath => string.IsNullOrWhiteSpace(IconPathTextBox.Text)
        ? null
        : IconPathTextBox.Text.Trim();

    /// <summary>
    /// Gets the updated notes (null if empty).
    /// </summary>
    public string? Notes => string.IsNullOrWhiteSpace(NotesTextBox.Text)
        ? null
        : NotesTextBox.Text.Trim();

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateSaveButtonState();
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    private void NameTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSaveButtonState();
    }

    private void UpdateSaveButtonState()
    {
        if (SaveButton is null) return;

        SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text);
    }

    private void BrowseIconButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = "Select instance icon",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(IconPathTextBox.Text) && File.Exists(IconPathTextBox.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(IconPathTextBox.Text);
        }

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            IconPathTextBox.Text = dialog.FileName;
        }
    }

    private void ClearIconButton_OnClick(object sender, RoutedEventArgs e)
    {
        IconPathTextBox.Text = string.Empty;
    }

    private void OpenFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_instance.Path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _instance.Path,
                UseShellExecute = true
            });
        }
        else
        {
            WpfMessageBox.Show(
                $"Instance folder does not exist:\n{_instance.Path}",
                "Folder Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InstanceName))
        {
            WpfMessageBox.Show(
                "Instance name cannot be empty.",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
