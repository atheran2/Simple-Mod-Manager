using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VintageStoryModManager.Services;

namespace VintageStoryModManager.ViewModels;

/// <summary>
///     ViewModel for displaying cloud instance entries in the browser.
/// </summary>
public sealed class CloudInstanceListEntryViewModel
{
    public CloudInstanceListEntryViewModel(CloudInstanceListEntry entry)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        RegistryId = entry.RegistryId;
        Name = string.IsNullOrWhiteSpace(entry.Name) ? null : entry.Name.Trim();
        Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description.Trim();
        TargetVsVersion = string.IsNullOrWhiteSpace(entry.TargetVsVersion) ? null : entry.TargetVsVersion.Trim();
        Uploader = string.IsNullOrWhiteSpace(entry.Uploader) ? "Unknown" : entry.Uploader.Trim();
        ModCount = entry.ModCount;
        CategoryCount = entry.CategoryCount;
        DateAdded = entry.DateAdded;
        IsPublic = entry.IsPublic;
        ContentJson = entry.ContentJson;
        ImageBase64 = entry.ImageBase64;

        // Parse image if available
        if (!string.IsNullOrWhiteSpace(entry.ImageBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(entry.ImageBase64);
                using var ms = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                Image = bitmap;
            }
            catch
            {
                // Ignore image parse errors
            }
        }
    }

    public string RegistryId { get; }
    public string? Name { get; }
    public string? Description { get; }
    public string? TargetVsVersion { get; }
    public string Uploader { get; }
    public int ModCount { get; }
    public int CategoryCount { get; }
    public DateTimeOffset? DateAdded { get; }
    public bool IsPublic { get; }
    public string? ContentJson { get; }
    public string? ImageBase64 { get; }
    public ImageSource? Image { get; }

    // Display properties
    public string DisplayName => Name ?? "Unnamed Instance";
    public string ModsSummary => ModCount == 0 ? "No mods" : ModCount == 1 ? "1 mod" : $"{ModCount} mods";
    public string CategoriesSummary => CategoryCount == 0 ? "No categories" : CategoryCount == 1 ? "1 category" : $"{CategoryCount} categories";
    public string DateAddedDisplay => DateAdded?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "-";
    public string VsVersionDisplay => string.IsNullOrWhiteSpace(TargetVsVersion) ? "-" : TargetVsVersion!;
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "No description" : Description!;
    public bool HasImage => Image != null;
}
