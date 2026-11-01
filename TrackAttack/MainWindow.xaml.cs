using System.Windows;
using System.Windows.Controls;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Settings;
using TrackAttack.Views;

namespace TrackAttack;

public partial class MainWindow : Window
{
    private readonly IMusicSource _musicSource = new LocalFileMusicSource();
    private readonly IQuizHistoryRepository _history = new SQLiteQuizHistoryRepository();
    private readonly Stack<(UserControl View, string Title)> _viewHistory = new();
    private UserControl? _currentView;
    private QuizView? _activeQuiz;
    private bool _navigationCollapsed;

    public MainWindow()
    {
        InitializeComponent();
        DatabaseInitializer.Initialize();
        SetNavigationCollapsed(AppSettings.Load().NavigationCollapsed);
        ShowHome(pushHistory: false);
        UpdateLibraryStatus();
        Closed += (_, _) => _activeQuiz?.Dispose();
    }

    private void UpdateLibraryStatus()
    {
        var settings = AppSettings.Load();
        var directory = settings.MusicDirectory;
        LibraryStatus.Text = string.IsNullOrWhiteSpace(directory) ? "No music library selected" : directory;
        LibraryStatus.ToolTip = LibraryStatus.Text;
        foreach (var setup in _viewHistory.Select(item => item.View)
                     .Append(_currentView)
                     .OfType<QuizSetupView>()
                     .Distinct())
            _ = setup.RefreshSpotifyFeatureSwitchAsync(settings.EnableSpotifyIntegration);
    }

    private void Navigate(UserControl view, string title, bool pushHistory = true)
    {
        if (pushHistory && _currentView is not null && _currentView != view)
            _viewHistory.Push((_currentView, PageHeading.Text));
        _currentView = view;
        PageHost.Content = view;
        PageHeading.Text = title;
        BackButton.Visibility = _viewHistory.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowHome(bool pushHistory = true)
    {
        var home = new HomeView(_history);
        home.NewQuizRequested += OnNewQuizClickFromHome;
        home.MetadataRequested += OnMetadataClickFromHome;
        home.HistoryRequested += OnHistoryClickFromHome;
        home.SettingsRequested += OnSettingsClickFromHome;
        Navigate(home, "Home", pushHistory);
    }

    private void OnNewQuizClickFromHome() => OnNewQuizClick(this, new RoutedEventArgs());
    private void OnMetadataClickFromHome() => OnMetadataClick(this, new RoutedEventArgs());
    private void OnHistoryClickFromHome() => OnHistoryClick(this, new RoutedEventArgs());
    private void OnSettingsClickFromHome() => OnSettingsClick(this, new RoutedEventArgs());

    private void OnHomeClick(object sender, RoutedEventArgs e)
    {
        if (GuardActiveQuiz())
        {
            _viewHistory.Clear();
            ShowHome(pushHistory: false);
        }
    }

    private void OnNewQuizClick(object sender, RoutedEventArgs e)
    {
        if (!GuardActiveQuiz())
            return;
        var settings = AppSettings.Load();
        if (!settings.EnableSpotifyIntegration
            && (string.IsNullOrWhiteSpace(settings.MusicDirectory) || !Directory.Exists(settings.MusicDirectory)))
        {
            MessageBox.Show(this, "Choose a music folder in Settings before starting a quiz.",
                "Music folder required", MessageBoxButton.OK, MessageBoxImage.Information);
            ShowSettings();
            return;
        }

        var setup = new QuizSetupView(_musicSource, _history, settings.EnableSpotifyIntegration);
        setup.QuizRequested += OnQuizRequested;
        setup.SongsRequested += songs => Navigate(new SongsView(songs), "Songs matching filters");
        Navigate(setup, "New Quiz");
    }

    private void OnQuizRequested(QuizConfig config, IReadOnlyList<Song> songs, IReadOnlyList<Song> reserve)
    {
        IPlaybackService playback;
        if (string.Equals(config.SourceType, "Spotify", StringComparison.OrdinalIgnoreCase))
        {
            if (SpotifyService.Client is not { } spotifyClient)
            {
                MessageBox.Show(this, "Connect to Spotify in Settings before starting a Spotify quiz.",
                    "Spotify not connected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            playback = new SpotifyPlaybackService(spotifyClient, config.SpotifyPlaylistId);
        }
        else
        {
            playback = new NAudioPlaybackService();
        }

        _activeQuiz = new QuizView(config, songs, reserve, _history, playback);
        _activeQuiz.QuizFinished += session =>
        {
            _activeQuiz = null;
            var results = new ResultsView(session, _history);
            results.PlayAgainRequested += OnNewQuizClickFromHome;
            Navigate(results, "Quiz Results", pushHistory: false);
        };
        Navigate(_activeQuiz, config.QuizTitle);
    }

    private void OnMetadataClick(object sender, RoutedEventArgs e)
    {
        if (GuardActiveQuiz())
            Navigate(new MetadataView(), "Fix Library Metadata");
    }

    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        if (GuardActiveQuiz())
            Navigate(new HistoryView(_history), "Quiz History");
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (GuardActiveQuiz())
            ShowSettings();
    }

    private void ShowSettings()
    {
        var view = new SettingsView(_history);
        view.SettingsSaved += UpdateLibraryStatus;
        Navigate(view, "Settings");
    }

    private bool GuardActiveQuiz()
    {
        if (_activeQuiz is null)
            return true;
        if (MessageBox.Show(this, "A quiz is in progress. Abandon it and continue?",
                "Abandon Quiz", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return false;
        _activeQuiz.Abandon();
        _activeQuiz.Dispose();
        _activeQuiz = null;
        _currentView = null;
        PageHost.Content = null;
        return true;
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (!GuardActiveQuiz() || _viewHistory.Count == 0)
            return;
        var (view, title) = _viewHistory.Pop();
        _currentView = view;
        PageHost.Content = view;
        PageHeading.Text = title;
        BackButton.Visibility = _viewHistory.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateLibraryStatus();
    }

    private void OnToggleNavigationClick(object sender, RoutedEventArgs e)
    {
        SetNavigationCollapsed(!_navigationCollapsed);
        var settings = AppSettings.Load();
        settings.NavigationCollapsed = _navigationCollapsed;
        try
        {
            settings.Save();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Navigation changed, but the preference could not be saved:\n{exception.Message}",
                "Settings save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetNavigationCollapsed(bool collapsed)
    {
        _navigationCollapsed = collapsed;
        var navigationWidth = ((Grid)Content).ColumnDefinitions[0];
        navigationWidth.Width = _navigationCollapsed ? new GridLength(64) : new GridLength(230);
        ToggleNavigationButton.Content = _navigationCollapsed ? "»" : "«";
        ToggleNavigationButton.ToolTip = _navigationCollapsed ? "Expand navigation" : "Collapse navigation";

        NavigationBrand.Margin = _navigationCollapsed
            ? new Thickness(10, 24, 10, 24)
            : new Thickness(18, 24, 18, 24);
        NavigationBrand.HorizontalAlignment = _navigationCollapsed
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Left;
        NavigationLogoIcon.Margin = _navigationCollapsed
            ? new Thickness(0)
            : new Thickness(0, 0, 10, 0);
        NavigationWordmark.Visibility = _navigationCollapsed ? Visibility.Collapsed : Visibility.Visible;

        var navigationButtons = new (Button Button, TextBlock Label)[]
        {
            (HomeNavigationButton, HomeNavigationLabel),
            (NewQuizNavigationButton, NewQuizNavigationLabel),
            (MetadataNavigationButton, MetadataNavigationLabel),
            (HistoryNavigationButton, HistoryNavigationLabel),
            (SettingsNavigationButton, SettingsNavigationLabel)
        };
        foreach (var (button, label) in navigationButtons)
        {
            label.Visibility = _navigationCollapsed ? Visibility.Collapsed : Visibility.Visible;
            button.Margin = _navigationCollapsed
                ? new Thickness(8, 4, 8, 4)
                : new Thickness(12, 4, 12, 4);
            button.Padding = _navigationCollapsed
                ? new Thickness(4)
                : new Thickness(14, 8, 14, 8);
            button.HorizontalContentAlignment = _navigationCollapsed
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
            button.ToolTip = _navigationCollapsed ? label.Text : null;
        }
    }
}
