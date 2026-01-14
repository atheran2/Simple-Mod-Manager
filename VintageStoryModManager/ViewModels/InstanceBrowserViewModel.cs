using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VintageStoryModManager.Models;
using VintageStoryModManager.Services;

namespace VintageStoryModManager.ViewModels;

/// <summary>
/// ViewModel for the Instance Browser view.
/// </summary>
public partial class InstanceBrowserViewModel : ObservableObject
{
    private readonly InstanceService _instanceService;

    public InstanceBrowserViewModel(InstanceService instanceService)
    {
        _instanceService = instanceService ?? throw new ArgumentNullException(nameof(instanceService));

        _instanceService.InstancesChanged += OnInstancesChanged;
        _instanceService.ActiveInstanceChanged += OnActiveInstanceChanged;

        RefreshInstances();
    }

    /// <summary>
    /// Collection of instance cards to display.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<InstanceCardViewModel> _instances = [];

    /// <summary>
    /// The currently selected/active instance card.
    /// </summary>
    [ObservableProperty]
    private InstanceCardViewModel? _selectedInstance;

    /// <summary>
    /// Whether there are any instances.
    /// </summary>
    public bool HasInstances => Instances.Count > 0;

    /// <summary>
    /// Whether the instance list is empty.
    /// </summary>
    public bool IsEmpty => Instances.Count == 0;

    /// <summary>
    /// Event raised when user requests to launch an instance.
    /// </summary>
    public event EventHandler<GameInstance>? LaunchInstanceRequested;

    /// <summary>
    /// Event raised when user requests to view/edit instance details.
    /// </summary>
    public event EventHandler<InstanceCardViewModel>? EditInstanceRequested;

    /// <summary>
    /// Event raised when user requests to create a new instance.
    /// </summary>
    public event EventHandler? CreateInstanceRequested;

    /// <summary>
    /// Event raised when user requests to delete an instance.
    /// </summary>
    public event EventHandler<InstanceCardViewModel>? DeleteInstanceRequested;

    /// <summary>
    /// Event raised when user requests to duplicate an instance.
    /// </summary>
    public event EventHandler<InstanceCardViewModel>? DuplicateInstanceRequested;

    /// <summary>
    /// Event raised when user requests to open instance folder.
    /// </summary>
    public event EventHandler<InstanceCardViewModel>? OpenFolderRequested;

    /// <summary>
    /// Refreshes the instances list from the service.
    /// </summary>
    public void RefreshInstances()
    {
        var allInstances = _instanceService.GetAllInstances();
        var activeId = _instanceService.ActiveInstance?.Id;

        Instances.Clear();

        foreach (var instance in allInstances)
        {
            var card = new InstanceCardViewModel(instance);
            Instances.Add(card);

            if (instance.Id == activeId)
            {
                SelectedInstance = card;
            }
        }

        OnPropertyChanged(nameof(HasInstances));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Refreshes a specific instance card's data.
    /// </summary>
    public void RefreshInstance(string instanceId)
    {
        var card = Instances.FirstOrDefault(c => c.Id == instanceId);
        card?.Refresh();
    }

    /// <summary>
    /// Command to launch an instance (click on image area).
    /// </summary>
    [RelayCommand]
    private void LaunchInstance(InstanceCardViewModel? card)
    {
        if (card == null) return;
        LaunchInstanceRequested?.Invoke(this, card.Instance);
    }

    /// <summary>
    /// Command to edit instance details (click on details area).
    /// </summary>
    [RelayCommand]
    private void EditInstance(InstanceCardViewModel? card)
    {
        if (card == null) return;
        EditInstanceRequested?.Invoke(this, card);
    }

    /// <summary>
    /// Command to create a new instance.
    /// </summary>
    [RelayCommand]
    private void CreateInstance()
    {
        CreateInstanceRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Command to delete an instance.
    /// </summary>
    [RelayCommand]
    private void DeleteInstance(InstanceCardViewModel? card)
    {
        if (card == null) return;
        DeleteInstanceRequested?.Invoke(this, card);
    }

    /// <summary>
    /// Command to duplicate an instance.
    /// </summary>
    [RelayCommand]
    private void DuplicateInstance(InstanceCardViewModel? card)
    {
        if (card == null) return;
        DuplicateInstanceRequested?.Invoke(this, card);
    }

    /// <summary>
    /// Command to open instance folder.
    /// </summary>
    [RelayCommand]
    private void OpenFolder(InstanceCardViewModel? card)
    {
        if (card == null) return;
        OpenFolderRequested?.Invoke(this, card);
    }

    /// <summary>
    /// Selects an instance by ID.
    /// </summary>
    public void SelectInstance(string? instanceId)
    {
        if (instanceId == null)
        {
            SelectedInstance = null;
            return;
        }

        SelectedInstance = Instances.FirstOrDefault(c => c.Id == instanceId);
    }

    private void OnInstancesChanged(object? sender, EventArgs e)
    {
        RefreshInstances();
    }

    private void OnActiveInstanceChanged(object? sender, GameInstance? instance)
    {
        SelectInstance(instance?.Id);
    }

    /// <summary>
    /// Cleanup when disposing.
    /// </summary>
    public void Cleanup()
    {
        _instanceService.InstancesChanged -= OnInstancesChanged;
        _instanceService.ActiveInstanceChanged -= OnActiveInstanceChanged;
    }
}
