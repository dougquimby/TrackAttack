using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using TrackAttack.Settings;

namespace TrackAttack
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        static App()
        {
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(
                typeof(FrameworkElement), new FrameworkPropertyMetadata(200));
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(
                typeof(FrameworkContentElement), new FrameworkPropertyMetadata(200));
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            ThemeManager.Apply(AppSettings.Load().DarkModeEnabled);
            base.OnStartup(e);
        }
    }

}
