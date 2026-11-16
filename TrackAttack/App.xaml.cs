using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using TrackAttack.Settings;

namespace TrackAttack
{
    /// <summary>
    /// Initializes application resources and handles settings-load failures.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>Creates the application and registers settings error handling.</summary>
        public App()
        {
            DispatcherUnhandledException += (_, e) =>
            {
                if (e.Exception is not SettingsLoadException)
                    return;

                MessageBox.Show(e.Exception.Message, "Settings load failed", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
        }

        static App()
        {
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(
                typeof(FrameworkElement), new FrameworkPropertyMetadata(200));
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(
                typeof(FrameworkContentElement), new FrameworkPropertyMetadata(200));
        }

        /// <inheritdoc />
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                ThemeManager.Apply(AppSettings.Load().DarkModeEnabled);
            }
            catch (SettingsLoadException exception)
            {
                MessageBox.Show(exception.Message, "Settings load failed", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
                return;
            }
            base.OnStartup(e);
        }
    }

}
