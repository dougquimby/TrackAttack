using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DrawingRectangle = System.Drawing.Rectangle;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Settings;
using TrackAttack.Views;

namespace TrackAttack;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush ActiveNavigationBrush = new(Color.FromRgb(23, 105, 170));
    private static readonly SolidColorBrush InactiveNavigationBrush = new(Color.FromRgb(37, 50, 72));
    private readonly IMusicSource _musicSource = new LocalFileMusicSource();
    private readonly IQuizHistoryRepository _history = new SQLiteQuizHistoryRepository();
    private readonly Stack<(UserControl View, string Title)> _viewHistory = new();
    private UserControl? _currentView;
    private QuizView? _activeQuiz;
    private bool _navigationCollapsed;
    private bool _wasMaximized;
    private bool _replayingHistoryQuiz;

    public MainWindow()
    {
        InitializeComponent();
        DatabaseInitializer.Initialize();
        RestoreWindowPlacement();
        SetNavigationCollapsed(AppSettings.Load().NavigationCollapsed);
        ShowHome(pushHistory: false);
        RefreshQuizSetupSources();
        StateChanged += OnWindowStateChanged;
        Closing += (_, _) => SaveWindowPlacement();
        Closed += (_, _) => _activeQuiz?.Dispose();
    }

    private void RestoreWindowPlacement()
    {
        var settings = AppSettings.Load();
        var bounds = settings.WindowBounds;
        _wasMaximized = settings.WindowMaximized;
        if (settings.WindowRestored
            && bounds.Width >= MinWidth
            && bounds.Height >= MinHeight
            && IsWindowBoundsVisible(bounds))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }

        if (settings.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
            _wasMaximized = WindowState == WindowState.Maximized;
    }

    private static bool IsWindowBoundsVisible(DrawingRectangle bounds)
    {
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
        var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;
        return bounds.Left < screenRight
            && bounds.Right > screenLeft
            && bounds.Top < screenBottom
            && bounds.Bottom > screenTop;
    }

    private void SaveWindowPlacement()
    {
        try
        {
            var settings = AppSettings.Load();
            settings.WindowMaximized = WindowState == WindowState.Minimized
                ? _wasMaximized
                : WindowState == WindowState.Maximized;

            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                settings.WindowBounds = new DrawingRectangle(
                    (int)Math.Round(bounds.Left),
                    (int)Math.Round(bounds.Top),
                    (int)Math.Round(bounds.Width),
                    (int)Math.Round(bounds.Height));
                settings.WindowRestored = true;
            }

            settings.Save();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Window placement could not be saved:\n{exception.Message}",
                "Settings save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshQuizSetupSources()
    {
        var settings = AppSettings.Load();
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
        if (view is HomeView home)
            home.RefreshProfiles();
        BackButton.Visibility = _viewHistory.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateNavigationSelection();
    }

    private void UpdateNavigationSelection()
    {
        SetNavigationButtonActive(HomeNavigationButton, _currentView is HomeView);
        SetNavigationButtonActive(NewQuizNavigationButton,
            _currentView is QuizSetupView or SongsView or QuizView or ResultsView);
        SetNavigationButtonActive(MetadataNavigationButton, _currentView is MetadataView);
        SetNavigationButtonActive(HistoryNavigationButton, _currentView is HistoryView);
        SetNavigationButtonActive(SettingsNavigationButton, _currentView is SettingsView);
    }

    private static void SetNavigationButtonActive(Button button, bool isActive)
    {
        button.Background = isActive ? ActiveNavigationBrush : InactiveNavigationBrush;
        button.BorderThickness = isActive ? new Thickness(1) : new Thickness(0);
    }

    private void ShowHome(bool pushHistory = true)
    {
        var home = new HomeView(_history);
        home.NewQuizRequested += OnNewQuizClickFromHome;
        home.MetadataRequested += OnMetadataClickFromHome;
        home.HistoryRequested += OnHistoryClickFromHome;
        home.SettingsRequested += OnSettingsClickFromHome;
        home.ProfileChanged += RefreshQuizSetupProfileSettings;
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

        _activeQuiz = new QuizView(config, songs, reserve, _history, playback, AppSettings.Load().ActiveProfileName);
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
        {
            var historyView = new HistoryView(_history);
            historyView.ReplayRequested += OnReplayQuizRequested;
            Navigate(historyView, "Quiz History");
        }
    }

    private async void OnReplayQuizRequested(QuizSession session)
    {
        if (_replayingHistoryQuiz || !GuardActiveQuiz())
            return;

        _replayingHistoryQuiz = true;
        try
        {
            var config = session.Config.Clone();
            var historySongs = session.Results
                .Select(result => result.Song)
                .ToArray();
            if (historySongs.Length == 0)
            {
                MessageBox.Show(this, "This quiz has no saved tracks to replay.",
                    "Replay unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IReadOnlyList<Song> availableSongs;
            if (string.Equals(config.SourceType, "Spotify", StringComparison.OrdinalIgnoreCase))
            {
                if (SpotifyService.Client is null && !await SpotifyService.TryReconnectAsync())
                {
                    MessageBox.Show(this, "Connect to Spotify in Settings before replaying this quiz.",
                        "Spotify connection required", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var playlistId = string.IsNullOrWhiteSpace(config.SpotifyPlaylistId)
                    ? config.SourceDirectory
                    : config.SpotifyPlaylistId;
                availableSongs = await new SpotifyMusicSource(SpotifyService.Client!)
                    .GetPlaylistSongsAsync(playlistId);
            }
            else
            {
                availableSongs = await Task.Run(
                    () => _musicSource.ScanDirectory(config.SourceDirectory).ToArray());
            }

            var resolvedSongs = new List<Song>(historySongs.Length);
            var unresolvedSongs = new List<Song>();
            foreach (var historySong in historySongs)
            {
                var sourceSong = !string.IsNullOrWhiteSpace(historySong.SourcePath)
                    ? availableSongs.FirstOrDefault(song => string.Equals(
                        song.SourcePath, historySong.SourcePath, StringComparison.OrdinalIgnoreCase))
                    : availableSongs.FirstOrDefault(song =>
                        string.Equals(song.Title, historySong.Title, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(song.Artist, historySong.Artist, StringComparison.OrdinalIgnoreCase));

                if (sourceSong is null)
                {
                    unresolvedSongs.Add(historySong);
                    continue;
                }

                resolvedSongs.Add(CreateReplaySong(historySong, sourceSong));
            }

            if (unresolvedSongs.Count > 0)
            {
                var missingTitles = string.Join(Environment.NewLine,
                    unresolvedSongs.Take(5).Select(song => $"• {song.Artist} — {song.Title}"));
                var remainingCount = unresolvedSongs.Count - Math.Min(unresolvedSongs.Count, 5);
                var remaining = remainingCount > 0 ? $"{Environment.NewLine}…and {remainingCount} more." : string.Empty;
                MessageBox.Show(this,
                    $"Could not find {unresolvedSongs.Count} track(s) from this quiz in its original source. Replay was not started.{Environment.NewLine}{missingTitles}{remaining}",
                    "Tracks unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedPaths = resolvedSongs
                .Select(song => song.SourcePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var reserve = GetReplayReserve(config, availableSongs, selectedPaths);
            config.SongCount = resolvedSongs.Count;
            config.MulliganCount = reserve.Length;
            if (reserve.Length < session.Config.MulliganCount)
            {
                MessageBox.Show(this,
                    $"Only {reserve.Length} eligible reserve track(s) are currently available; the replay will use that many mulligan(s).",
                    "Replay reserve changed", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            OnQuizRequested(config, resolvedSongs, reserve);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not replay this quiz:{Environment.NewLine}{exception.Message}",
                "Replay failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _replayingHistoryQuiz = false;
        }
    }

    private static Song CreateReplaySong(Song historySong, Song sourceSong)
        => new()
        {
            Title = historySong.Title,
            Artist = historySong.Artist,
            Album = historySong.Album,
            Genre = historySong.Genre,
            Year = historySong.Year,
            SourcePath = sourceSong.SourcePath,
            MusicBrainzReleaseId = sourceSong.MusicBrainzReleaseId,
            AlbumCoverArtUrl = sourceSong.AlbumCoverArtUrl,
            PreviewUrl = sourceSong.PreviewUrl,
            Duration = sourceSong.Duration,
            SourceType = sourceSong.SourceType
        };

    private static Song[] GetReplayReserve(
        QuizConfig config, IReadOnlyList<Song> availableSongs, HashSet<string> selectedPaths)
    {
        if (config.MulliganCount <= 0)
            return [];

        var candidates = availableSongs
            .Where(song => !selectedPaths.Contains(song.SourcePath))
            .Where(song => MatchesReplayFilters(song, config))
            .ToArray();
        Random.Shared.Shuffle(candidates);
        return candidates.Take(config.MulliganCount).ToArray();
    }

    private static bool MatchesReplayFilters(Song song, QuizConfig config)
    {
        if (config.MinSongDuration is { } minimum && song.Duration < minimum)
            return false;
        if (config.MaxSongDuration is { } maximum && song.Duration > maximum)
            return false;

        if (!string.IsNullOrWhiteSpace(config.FilterGenre))
        {
            var genres = config.FilterGenre.Split(
                [',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!genres.Any(genre => string.Equals(genre, LocalFileMusicSource.UnknownMetadataOption,
                    StringComparison.OrdinalIgnoreCase)
                    ? string.IsNullOrWhiteSpace(song.Genre)
                    : string.Equals(genre, song.Genre, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(config.FilterDecade))
        {
            var decades = config.FilterDecade.Split(
                [',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var includesUnknown = decades.Any(decade => string.Equals(
                decade, LocalFileMusicSource.UnknownMetadataOption, StringComparison.OrdinalIgnoreCase));
            var years = decades
                .Where(decade => !string.Equals(decade, LocalFileMusicSource.UnknownMetadataOption,
                    StringComparison.OrdinalIgnoreCase))
                .Select(decade => int.TryParse(decade.TrimEnd('s'), out var year) ? year : (int?)null)
                .Where(year => year.HasValue)
                .Select(year => year!.Value)
                .ToArray();
            if (!(includesUnknown && song.Year <= 0)
                && !years.Any(year => song.Year >= year && song.Year < year + 10))
                return false;
        }

        return true;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (GuardActiveQuiz())
            ShowSettings();
    }

    private void ShowSettings()
    {
        var view = new SettingsView(_history);
        view.SettingsSaved += RefreshQuizSetupSources;
        view.ProfileChanged += RefreshQuizSetupProfileSettings;
        Navigate(view, "Settings");
    }

    private void RefreshQuizSetupProfileSettings()
    {
        foreach (var setup in _viewHistory.Select(item => item.View)
                     .Append(_currentView)
                     .OfType<QuizSetupView>()
                     .Distinct())
            _ = setup.RefreshProfileSettingsAsync();
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
        UpdateNavigationSelection();
        RefreshQuizSetupSources();
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
