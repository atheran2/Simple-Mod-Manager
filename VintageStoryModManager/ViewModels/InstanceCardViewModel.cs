using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using VintageStoryModManager.Models;

namespace VintageStoryModManager.ViewModels;

/// <summary>
/// ViewModel for an instance card in the Instance Browser.
/// </summary>
public partial class InstanceCardViewModel : ObservableObject
{
    private readonly GameInstance _instance;

    public InstanceCardViewModel(GameInstance instance)
    {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
        RefreshModCount();
        LoadIcon();
    }

    /// <summary>
    /// The underlying instance model.
    /// </summary>
    public GameInstance Instance => _instance;

    /// <summary>
    /// Instance unique identifier.
    /// </summary>
    public string Id => _instance.Id;

    /// <summary>
    /// Instance display name.
    /// </summary>
    public string Name => _instance.Name;

    /// <summary>
    /// Full path to the instance folder.
    /// </summary>
    public string Path => _instance.Path;

    /// <summary>
    /// Target VS version, or "Any" if not set.
    /// </summary>
    public string GameVersion => string.IsNullOrWhiteSpace(_instance.TargetVsVersion)
        ? "Any"
        : _instance.TargetVsVersion;

    /// <summary>
    /// Total playtime formatted as hours.
    /// </summary>
    public string PlaytimeDisplay
    {
        get
        {
            var hours = _instance.TotalPlaytimeSeconds / 3600.0;
            return hours < 0.1 ? "< 0.1 hrs" : $"{hours:F1} hrs";
        }
    }

    /// <summary>
    /// Last played date formatted, or "Never" if not played.
    /// </summary>
    public string LastPlayedDisplay
    {
        get
        {
            if (_instance.LastPlayed == null)
                return "Never";

            var local = _instance.LastPlayed.Value.ToLocalTime();
            var daysAgo = (DateTime.Now - local).TotalDays;

            if (daysAgo < 1)
                return "Today";
            if (daysAgo < 2)
                return "Yesterday";
            if (daysAgo < 7)
                return $"{(int)daysAgo} days ago";

            return local.ToString("MMM d, yyyy");
        }
    }

    /// <summary>
    /// Notes/description for the instance.
    /// </summary>
    public string? Notes => _instance.Notes;

    /// <summary>
    /// Whether the instance has notes.
    /// </summary>
    public bool HasNotes => !string.IsNullOrWhiteSpace(_instance.Notes);

    /// <summary>
    /// Created date formatted.
    /// </summary>
    public string CreatedDisplay => _instance.Created.ToLocalTime().ToString("MMM d, yyyy");

    /// <summary>
    /// Custom game directory override, if set.
    /// </summary>
    public string? GameDirectory => _instance.GameDirectory;

    /// <summary>
    /// Whether a custom game directory is set.
    /// </summary>
    public bool HasCustomGameDirectory => !string.IsNullOrWhiteSpace(_instance.GameDirectory);

    [ObservableProperty]
    private int _modCount;

    [ObservableProperty]
    private ImageSource? _iconImage;

    [ObservableProperty]
    private bool _hasCustomIcon;

    /// <summary>
    /// Display text for mod count.
    /// </summary>
    public string ModCountDisplay => ModCount == 1 ? "1 mod" : $"{ModCount} mods";

    /// <summary>
    /// First letter of instance name for default icon.
    /// </summary>
    public string NameInitial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[0].ToString().ToUpperInvariant();

    /// <summary>
    /// Refreshes the mod count from the instance's Mods folder.
    /// </summary>
    public void RefreshModCount()
    {
        try
        {
            if (Directory.Exists(_instance.ModsPath))
            {
                ModCount = Directory.EnumerateFiles(_instance.ModsPath, "*.zip", SearchOption.TopDirectoryOnly).Count()
                         + Directory.EnumerateDirectories(_instance.ModsPath).Count();
            }
            else
            {
                ModCount = 0;
            }
        }
        catch
        {
            ModCount = 0;
        }

        OnPropertyChanged(nameof(ModCountDisplay));
    }

    /// <summary>
    /// Loads the instance icon from the icon path if set.
    /// </summary>
    private void LoadIcon()
    {
        if (string.IsNullOrWhiteSpace(_instance.IconPath))
        {
            HasCustomIcon = false;
            IconImage = null;
            return;
        }

        var iconPath = _instance.IconPath;

        // If relative path, resolve from instance folder
        if (!System.IO.Path.IsPathRooted(iconPath))
        {
            iconPath = System.IO.Path.Combine(_instance.Path, iconPath);
        }

        if (!File.Exists(iconPath))
        {
            HasCustomIcon = false;
            IconImage = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 200; // Reasonable size for card
            bitmap.EndInit();
            bitmap.Freeze();

            IconImage = bitmap;
            HasCustomIcon = true;
        }
        catch
        {
            HasCustomIcon = false;
            IconImage = null;
        }
    }

    /// <summary>
    /// Refreshes all dynamic data (mod count, icon, etc.)
    /// </summary>
    public void Refresh()
    {
        RefreshModCount();
        LoadIcon();

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(GameVersion));
        OnPropertyChanged(nameof(PlaytimeDisplay));
        OnPropertyChanged(nameof(LastPlayedDisplay));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(HasNotes));
    }
}
