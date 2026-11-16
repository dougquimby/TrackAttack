using System.Windows;

namespace TrackAttack;

/// <summary>Applies the application's light or dark WPF resource palette.</summary>
public static class ThemeManager
{
    /// <summary>Switches the active application resource dictionary.</summary>
    /// <param name="darkMode">Whether to use the dark palette.</param>
    public static void Apply(bool darkMode)
    {
        var resources = Application.Current.Resources.MergedDictionaries;
        var theme = new ResourceDictionary
        {
            Source = new Uri(
                darkMode ? "/TrackAttack;component/Themes/Dark.xaml" : "/TrackAttack;component/Themes/Light.xaml",
                UriKind.Relative)
        };
        var current = resources.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true
            || dictionary.Source?.OriginalString.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true);
        if (current is null)
            resources.Add(theme);
        else
            resources[resources.IndexOf(current)] = theme;
    }
}
