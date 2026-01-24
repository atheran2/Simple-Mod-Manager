using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using VintageStoryModManager.Models;
using WpfMessageBox = VintageStoryModManager.Services.ModManagerMessageBox;

namespace VintageStoryModManager.Views.Dialogs;

public partial class ManageCloudInstancesDialog : Window
{
    private readonly Func<CloudInstanceManagementEntry, Task<bool>> _deleteCallback;
    private readonly ObservableCollection<CloudInstanceManagementEntry> _entries;
    private readonly Func<Task<IReadOnlyList<CloudInstanceManagementEntry>>> _refreshCallback;
    private readonly Func<CloudInstanceManagementEntry, bool, Task<bool>> _toggleVisibilityCallback;
    private bool _isBusy;

    public ManageCloudInstancesDialog(
        Window owner,
        IEnumerable<CloudInstanceManagementEntry> entries,
        Func<Task<IReadOnlyList<CloudInstanceManagementEntry>>> refreshCallback,
        Func<CloudInstanceManagementEntry, bool, Task<bool>> toggleVisibilityCallback,
        Func<CloudInstanceManagementEntry, Task<bool>> deleteCallback)
    {
        InitializeComponent();

        Owner = owner;
        _refreshCallback = refreshCallback ?? throw new ArgumentNullException(nameof(refreshCallback));
        _toggleVisibilityCallback = toggleVisibilityCallback ?? throw new ArgumentNullException(nameof(toggleVisibilityCallback));
        _deleteCallback = deleteCallback ?? throw new ArgumentNullException(nameof(deleteCallback));

        _entries = new ObservableCollection<CloudInstanceManagementEntry>(
            entries ?? Enumerable.Empty<CloudInstanceManagementEntry>());
        InstancesListView.ItemsSource = _entries;

        if (_entries.Count > 0) InstancesListView.SelectedIndex = 0;

        UpdateButtonStates();
        UpdateSelectedDetails();
    }

    private CloudInstanceManagementEntry? SelectedEntry =>
        InstancesListView.SelectedItem as CloudInstanceManagementEntry;

    public bool HasSelection => SelectedEntry is not null;

    private async void ToggleVisibilityButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry is not CloudInstanceManagementEntry entry) return;

        var newVisibility = !entry.IsPublic;
        var visibilityText = newVisibility ? "public (visible in browse list)" : "unlisted (link sharing only)";

        var confirmation = WpfMessageBox.Show(
            $"Change \"{entry.EffectiveName}\" to {visibilityText}?",
            "Simple VS Manager",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes) return;

        await RunOperationAsync(() => _toggleVisibilityCallback(entry, newVisibility));
    }

    private async void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry is not CloudInstanceManagementEntry entry) return;

        var displayName = string.IsNullOrWhiteSpace(entry.Name)
            ? entry.SlotLabel
            : $"{entry.SlotLabel} (\"{entry.Name}\")";

        var confirmation = WpfMessageBox.Show(
            $"Are you sure you want to delete {displayName}?\n\nThis will remove it from the cloud and free up the slot. This action cannot be undone.",
            "Simple VS Manager",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes) return;

        await RunOperationAsync(() => _deleteCallback(entry));
    }

    private async Task RunOperationAsync(Func<Task<bool>> operation)
    {
        if (operation is null || _isBusy) return;

        SetIsBusy(true);
        try
        {
            var success = await operation();
            if (success) await RefreshEntriesAsync();
        }
        finally
        {
            SetIsBusy(false);
        }
    }

    private async Task RefreshEntriesAsync()
    {
        IReadOnlyList<CloudInstanceManagementEntry>? entries;
        try
        {
            entries = await _refreshCallback();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"Failed to refresh cloud instances:\n{ex.Message}",
                "Simple VS Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var selectedSlotKey = SelectedEntry?.SlotKey;

        _entries.Clear();
        if (entries is not null)
            foreach (var entry in entries)
                if (entry is not null)
                    _entries.Add(entry);

        if (_entries.Count == 0)
        {
            WpfMessageBox.Show(
                "You do not have any cloud instances saved.",
                "Simple VS Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DialogResult = true;
            return;
        }

        var selectedIndex = -1;
        if (!string.IsNullOrWhiteSpace(selectedSlotKey))
            for (var i = 0; i < _entries.Count; i++)
                if (string.Equals(_entries[i].SlotKey, selectedSlotKey, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }

        if (selectedIndex >= 0)
            InstancesListView.SelectedIndex = selectedIndex;
        else if (_entries.Count > 0) InstancesListView.SelectedIndex = 0;

        UpdateButtonStates();
        UpdateSelectedDetails();
    }

    private void SetIsBusy(bool isBusy)
    {
        _isBusy = isBusy;
        InstancesListView.IsEnabled = !isBusy;
        if (isBusy)
        {
            ToggleVisibilityButton.IsEnabled = false;
            DeleteButton.IsEnabled = false;
        }
        else
        {
            UpdateButtonStates();
        }
    }

    private void UpdateButtonStates()
    {
        if (_isBusy) return;

        var hasSelection = SelectedEntry is not null;
        ToggleVisibilityButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private void UpdateSelectedDetails()
    {
        var entry = SelectedEntry;

        if (SelectedInstanceNameText is not null)
        {
            SelectedInstanceNameText.Text = entry?.EffectiveName ?? string.Empty;
        }

        if (SelectedInstanceDescriptionText is not null)
        {
            if (entry is not null)
            {
                var details = new List<string>();
                if (!string.IsNullOrWhiteSpace(entry.TargetVsVersion))
                    details.Add($"VS {entry.TargetVsVersion}");
                details.Add(entry.ModsSummary);
                details.Add(entry.VisibilityDisplay);

                var detailsLine = string.Join(" | ", details);
                var description = !string.IsNullOrWhiteSpace(entry.Description)
                    ? $"{detailsLine}\n{entry.Description}"
                    : detailsLine;

                SelectedInstanceDescriptionText.Text = description;
            }
            else
            {
                SelectedInstanceDescriptionText.Text = string.Empty;
            }
        }
    }

    private void InstancesListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtonStates();
        UpdateSelectedDetails();
    }
}
