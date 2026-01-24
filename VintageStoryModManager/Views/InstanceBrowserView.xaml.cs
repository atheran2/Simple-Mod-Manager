using System.Windows;
using System.Windows.Controls;
using VintageStoryModManager.ViewModels;
using VintageStoryModManager.Views.Controls;

namespace VintageStoryModManager.Views;

/// <summary>
/// Interaction logic for InstanceBrowserView.xaml
/// </summary>
public partial class InstanceBrowserView : System.Windows.Controls.UserControl
{
    public InstanceBrowserView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the ViewModel from DataContext.
    /// </summary>
    public InstanceBrowserViewModel? ViewModel => DataContext as InstanceBrowserViewModel;

    private void InstanceCard_LaunchClicked(object sender, RoutedEventArgs e)
    {
        if (sender is InstanceCard card && card.ViewModel != null)
        {
            ViewModel?.LaunchInstanceCommand.Execute(card.ViewModel);
        }
    }

    private void InstanceCard_DetailsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is InstanceCard card && card.ViewModel != null)
        {
            ViewModel?.EditInstanceCommand.Execute(card.ViewModel);
        }
    }

    private void LaunchMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardFromContextMenu(sender) is { } card)
        {
            ViewModel?.LaunchInstanceCommand.Execute(card.ViewModel);
        }
    }

    private void EditMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardFromContextMenu(sender) is { } card)
        {
            ViewModel?.EditInstanceCommand.Execute(card.ViewModel);
        }
    }

    private void OpenFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardFromContextMenu(sender) is { } card)
        {
            ViewModel?.OpenFolderCommand.Execute(card.ViewModel);
        }
    }

    private void DuplicateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardFromContextMenu(sender) is { } card)
        {
            ViewModel?.DuplicateInstanceCommand.Execute(card.ViewModel);
        }
    }

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardFromContextMenu(sender) is { } card)
        {
            ViewModel?.DeleteInstanceCommand.Execute(card.ViewModel);
        }
    }

    private void ShareInstanceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardFromContextMenu(sender) is { } card)
        {
            ViewModel?.ShareInstanceCommand.Execute(card.ViewModel);
        }
    }

    private void ImportInstanceButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ImportInstanceCommand.Execute(null);
    }

    private static InstanceCard? GetCardFromContextMenu(object sender)
    {
        if (sender is MenuItem menuItem &&
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is InstanceCard card)
        {
            return card;
        }

        return null;
    }
}
