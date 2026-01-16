using CommunityToolkit.Mvvm.ComponentModel;

namespace VintageStoryModManager.ViewModels;

/// <summary>
///     Represents a category header row in the flat grouped mod list.
///     This is a "fake" row that acts as a collapsible section header.
/// </summary>
public sealed class CategoryHeaderViewModel : ObservableObject
{
    private bool _isExpanded = true;
    private int _modCount;

    public CategoryHeaderViewModel(string categoryId, string categoryName, int sortOrder)
    {
        CategoryId = categoryId;
        CategoryName = categoryName;
        SortOrder = sortOrder;
    }

    /// <summary>
    ///     The unique identifier for this category.
    /// </summary>
    public string CategoryId { get; }

    /// <summary>
    ///     The display name of this category.
    /// </summary>
    public string CategoryName { get; }

    /// <summary>
    ///     The sort order of this category (lower values appear first).
    /// </summary>
    public int SortOrder { get; }

    /// <summary>
    ///     Whether this category is expanded (showing mods) or collapsed.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>
    ///     The number of mods in this category (updated dynamically).
    /// </summary>
    public int ModCount
    {
        get => _modCount;
        set => SetProperty(ref _modCount, value);
    }

    /// <summary>
    ///     Sort key used to ensure category headers sort before their mods.
    ///     Format: "SortOrder|0|CategoryName" where 0 indicates header position.
    /// </summary>
    public string CategoryGroupKey => $"{SortOrder:D10}|0|{CategoryName}";
}
