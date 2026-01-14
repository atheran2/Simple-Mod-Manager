using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VintageStoryModManager.ViewModels;

namespace VintageStoryModManager.Views.Controls;

/// <summary>
/// Interaction logic for InstanceCard.xaml
/// </summary>
public partial class InstanceCard : System.Windows.Controls.UserControl
{
    public InstanceCard()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Event raised when the image area is clicked (launch game).
    /// </summary>
    public event RoutedEventHandler? LaunchClicked;

    /// <summary>
    /// Event raised when the details area is clicked (edit instance).
    /// </summary>
    public event RoutedEventHandler? DetailsClicked;

    private void ImageArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        LaunchClicked?.Invoke(this, new RoutedEventArgs());
    }

    private void DetailsArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        DetailsClicked?.Invoke(this, new RoutedEventArgs());
    }

    /// <summary>
    /// Gets the InstanceCardViewModel from DataContext.
    /// </summary>
    public InstanceCardViewModel? ViewModel => DataContext as InstanceCardViewModel;
}
