using System.Windows;
using System.Windows.Controls;

namespace JinxyClicker;

/// <summary>
/// The tab bar at the top of the Mod page, Lyre-style: one row of segment
/// buttons that swap which group of cards is shown.
/// </summary>
public partial class MainWindow
{
    private void ModTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tab }) return;

        // The Performance tab's RadioButton is checked in XAML, so its Checked
        // fires during parse before the panels exist — ignore until they do.
        if (ModTabPerformance == null) return;

        ModTabPerformance.Visibility = tab == "Performance" ? Visibility.Visible : Visibility.Collapsed;
        ModTabFonts.Visibility = tab == "Fonts" ? Visibility.Visible : Visibility.Collapsed;
        ModTabScreen.Visibility = tab == "Screen" ? Visibility.Visible : Visibility.Collapsed;
        ModTabMouse.Visibility = tab == "Mouse" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OptTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tab }) return;
        if (OptTabBoost == null) return;

        OptTabBoost.Visibility = tab == "Boost" ? Visibility.Visible : Visibility.Collapsed;
        OptTabCleaner.Visibility = tab == "Cleaner" ? Visibility.Visible : Visibility.Collapsed;
        OptTabNetwork.Visibility = tab == "Network" ? Visibility.Visible : Visibility.Collapsed;
    }
}
