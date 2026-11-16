using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using DrawingRectangle = System.Drawing.Rectangle;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Settings;
using TrackAttack.Views;

namespace TrackAttack;

/// <summary>Hosts application navigation, views, and quiz composition.</summary>
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
    private bool _loadingChallenge;

    /// <summary>Creates the main window and initializes history and navigation.</summary>
    public MainWindow()
    {
        InitializeComponent();
        DatabaseInitializer.Initialize();
        RestoreWindowPlacement();
        SetNavigationCollapsed(AppSettings.Load().NavigationCollapsed);
        RefreshNavigationProfile();
        ShowHome(pushHistory: false);
        RefreshQuizSetupSources();
        StateChanged += OnWindowStateChanged;
        Closing += (_, e) =>
        {
            if (!CanLeaveSettings())
            {
                e.Cancel = true;
                return;
            }
            SaveWindowPlacement();
        };
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
        SetNavigationButtonActive(HistoryNavigationButton, _currentView is HistoryView);
        SetNavigationButtonActive(StatisticsNavigationButton, _currentView is StatisticsView);
        SetNavigationButtonActive(SocialNavigationButton, _currentView is SocialView);
        SetNavigationButtonActive(SettingsNavigationButton, _currentView is SettingsView or MetadataView);
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
        home.MetadataRequested += OnMetadataRequested;
        home.HistoryRequested += OnHistoryClickFromHome;
        home.SettingsRequested += OnSettingsClickFromHome;
        home.ProfileChanged += OnHomeProfileChanged;
        Navigate(home, "Home", pushHistory);
    }

    private void OnNewQuizClickFromHome() => OnNewQuizClick(this, new RoutedEventArgs());
    private void OnHistoryClickFromHome() => OnHistoryClick(this, new RoutedEventArgs());
    private void OnSettingsClickFromHome() => OnSettingsClick(this, new RoutedEventArgs());
    private void OnHomeProfileChanged()
    {
        RefreshQuizSetupProfileSettings();
        RefreshNavigationProfile();
    }

    private void OnHomeClick(object sender, RoutedEventArgs e)
    {
        if (GuardNavigation())
        {
            _viewHistory.Clear();
            ShowHome(pushHistory: false);
        }
    }

    private void OnNewQuizClick(object sender, RoutedEventArgs e)
    {
        if (!GuardNavigation())
            return;
        var settings = AppSettings.Load();
        var setup = new QuizSetupView(_musicSource, _history, settings.EnableSpotifyIntegration);
        setup.QuizRequested += OnQuizRequested;
        setup.ChallengeRequested += OnCreateChallengeRequested;
        setup.SongsRequested += songs => Navigate(new SongsView(songs), "Songs matching filters");
        Navigate(setup, "New Quiz");
    }

    private void OnQuizRequested(QuizConfig config, IReadOnlyList<Song> songs, IReadOnlyList<Song> reserve)
        => StartQuiz(config, songs, reserve);

    private void StartQuiz(QuizConfig config, IReadOnlyList<Song> songs, IReadOnlyList<Song> reserve,
        IReadOnlyList<TimeSpan>? clipStarts = null)
    {
        if (!CanLeaveSettings())
            return;

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

        _activeQuiz = new QuizView(config, songs, reserve, _history, playback, AppSettings.Load().ActiveProfileName, clipStarts);
        _activeQuiz.QuizFinished += session =>
        {
            _activeQuiz = null;
            var results = new ResultsView(session, _history);
            results.PlayAgainRequested += OnNewQuizClickFromHome;
            results.HistoryRequested += OnHistoryClickFromHome;
            Navigate(results, "Quiz Results", pushHistory: false);
        };
        Navigate(_activeQuiz, config.QuizTitle);
    }

    private void OnMetadataRequested()
    {
        if (GuardNavigation())
            Navigate(new MetadataView(), "Fix Library Metadata");
    }

    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        if (GuardNavigation())
        {
            var historyView = new HistoryView(_history);
            historyView.ReplayRequested += OnReplayQuizRequested;
            historyView.ChallengeRequested += OnHistoryChallengeRequested;
            Navigate(historyView, "Quiz History");
        }
    }

    private void OnSocialClick(object sender, RoutedEventArgs e)
    {
        if (!GuardNavigation())
            return;
        var social = new SocialView();
        social.OpenChallengeRequested += OnOpenChallengeRequested;
        social.CreateChallengeRequested += OnNewQuizClickFromHome;
        social.HistoryRequested += OnHistoryClickFromHome;
        Navigate(social, "Social");
    }

    private void OnCreateChallengeRequested(QuizConfig config, IReadOnlyList<Song> songs, IReadOnlyList<Song> reserve)
    {
        try
        {
            var challenge = QuizChallengeService.Create(config, songs);
            if (SaveChallenge(challenge))
                StartQuiz(config, songs, reserve, challenge.Tracks.Select(track => track.ClipStart).ToArray());
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not create the challenge:\n{exception.Message}",
                "Challenge creation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool SaveChallenge(QuizChallenge challenge)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save challenge",
            Filter = "TrackAttack challenge (*.tachallenge)|*.tachallenge|All files (*.*)|*.*",
            DefaultExt = ".tachallenge",
            FileName = "TrackAttack Challenge.tachallenge"
        };
        if (dialog.ShowDialog(this) != true)
            return false;
        QuizChallengeService.Save(dialog.FileName, challenge);
        MessageBox.Show(this, "Challenge saved. The file does not include audio.",
            "Challenge saved", MessageBoxButton.OK, MessageBoxImage.Information);
        return true;
    }

    private async void OnHistoryChallengeRequested(QuizSession session)
    {
        if (_loadingChallenge || !GuardNavigation())
            return;
        if (MessageBox.Show(this,
                "History does not save the original clip positions. New positions will be chosen, so the original result is not directly comparable. The final tracks after any mulligans are used.\n\nContinue?",
                "Create challenge from history", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
            return;
        _loadingChallenge = true;
        var originalView = _currentView;
        try
        {
            var songs = session.Results.Select(result => result.Song).ToArray();
            var local = (LocalFileMusicSource)_musicSource;
            var historyRoots = string.IsNullOrWhiteSpace(session.Config.LocalPlaylistPath)
                ? MusicFolderSelection.Roots(session.Config) : [];
            var roots = historyRoots.Concat(AppSettings.Load().GetMusicDirectories())
                .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var scan = await Task.Run(() => local.ScanLibrary(roots, null));
            var originals = await Task.Run(() => local.ScanFiles(songs.Select(song => song.SourcePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))));
            if (_currentView != originalView)
                return;
            ShowScanWarnings(scan.Issues.Concat(originals.Issues).ToArray());
            var available = scan.Songs.Concat(originals.Songs)
                .DistinctBy(song => song.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray();
            var slots = ReplayTrackResolver.Resolve(songs, available).Slots;
            if (ShowUnavailableTracks(songs, slots))
                return;
            SaveChallenge(QuizChallengeService.Create(session.Config, slots.OfType<Song>().ToArray()));
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not create the challenge:\n{exception.Message}",
                "Challenge creation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _loadingChallenge = false; }
    }

    private async void OnOpenChallengeRequested()
    {
        if (_loadingChallenge || !GuardNavigation())
            return;
        var dialog = new OpenFileDialog
        {
            Title = "Open challenge",
            Filter = "TrackAttack challenge (*.tachallenge)|*.tachallenge|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        _loadingChallenge = true;
        var originalView = _currentView;
        try
        {
            var challenge = QuizChallengeService.Load(dialog.FileName);
            var settings = AppSettings.Load();
            var roots = settings.GetMusicDirectories().Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
            if (roots.Length == 0)
            {
                var folder = new OpenFolderDialog { Title = "Select the folder containing your music" };
                if (folder.ShowDialog(this) != true)
                    return;
                roots = [folder.FolderName];
            }
            var scan = await Task.Run(() => ((LocalFileMusicSource)_musicSource).ScanLibrary(roots, null));
            if (_currentView != originalView)
                return;
            ShowScanWarnings(scan.Issues);
            var slots = QuizChallengeService.Resolve(challenge, scan.Songs);
            var requested = challenge.Tracks.Select(track => new Song { Title = track.Title, Artist = track.Artist }).ToArray();
            if (ShowUnavailableTracks(requested, slots))
                return;
            var selected = slots.OfType<Song>().ToArray();
            var config = challenge.Config.Clone();
            config.SourceDirectory = roots[0];
            config.LocalMusicDirectories = [.. roots];
            var recent = config.ExcludeRecentQuizSongs
                ? await _history.GetRecentQuizSongsAsync(config.RecentQuizzesToExclude, config.SourceType,
                    config.SourceDirectory, settings.ActiveProfileName)
                : [];
            if (_currentView != originalView)
                return;
            var reserve = GetLocalReplayCandidates(config, scan.Songs, selected, recent, selected)
                .Take(config.MulliganCount).ToArray();
            if (reserve.Length < config.MulliganCount)
            {
                MessageBox.Show(this, $"This challenge needs {config.MulliganCount} eligible reserve tracks, but only {reserve.Length} were found. Add more matching tracks before playing.",
                    "Challenge reserve unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            StartQuiz(config, selected, reserve, challenge.Tracks.Select(track => track.ClipStart).ToArray());
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not open the challenge:\n{exception.Message}",
                "Challenge open failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _loadingChallenge = false; }
    }

    private void ShowScanWarnings(IReadOnlyList<LibraryScanIssue> issues)
    {
        if (issues.Count > 0)
            MessageBox.Show(this, LibraryScanIssueSummary.Format(issues),
                "Library scan warnings", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private bool ShowUnavailableTracks(IReadOnlyList<Song> requested, IReadOnlyList<Song?> slots)
    {
        var missing = requested.Where((_, index) => slots[index] is null).ToArray();
        if (missing.Length == 0)
            return false;
        var list = string.Join(Environment.NewLine, missing.Take(10).Select(song => $"• {song.Artist} — {song.Title}"));
        var remaining = missing.Length > 10 ? $"\n…and {missing.Length - 10} more." : string.Empty;
        MessageBox.Show(this, $"{missing.Length} track(s) are missing, ambiguous, or have an incompatible duration. All tracks are required.\n{list}{remaining}",
            "Challenge tracks unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        return true;
    }

    private void OnStatisticsClick(object sender, RoutedEventArgs e)
    {
        if (GuardNavigation())
            Navigate(new StatisticsView(_history), "Statistics");
    }

    private async void OnReplayQuizRequested(QuizSession session)
    {
        if (_replayingHistoryQuiz || !GuardNavigation())
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
                if (_musicSource is not LocalFileMusicSource localSource)
                    throw new InvalidOperationException("The selected source cannot scan local music folders.");
                var playlistPath = config.LocalPlaylistPath;
                var roots = (string.IsNullOrWhiteSpace(playlistPath) ? MusicFolderSelection.Roots(config) : [])
                    .Concat(AppSettings.Load().GetMusicDirectories())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var scan = await Task.Run(() => localSource.ScanLibrary(roots, config.LocalFolderRules));
                var available = scan.Songs.ToList();
                var issues = scan.Issues.ToList();
                HashSet<string>? playlistPaths = null;
                if (!string.IsNullOrWhiteSpace(playlistPath))
                {
                    var playlist = await Task.Run(() => localSource.ScanPlaylist(playlistPath));
                    var originals = await Task.Run(() => localSource.ScanFiles(historySongs
                        .Select(song => song.SourcePath).Where(path => !string.IsNullOrWhiteSpace(path))));
                    playlistPaths = playlist.Songs.Select(song => song.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    available.InsertRange(0, originals.Songs.Concat(playlist.Songs));
                    issues.AddRange(playlist.Issues);
                    issues.AddRange(originals.Issues);
                }
                if (issues.Count > 0)
                    MessageBox.Show(this, LibraryScanIssueSummary.Format(issues.DistinctBy(issue => issue.Path,
                            StringComparer.OrdinalIgnoreCase).ToArray()),
                        "Library scan warnings", MessageBoxButton.OK, MessageBoxImage.Warning);
                IReadOnlyList<Song> recent = config.ExcludeRecentQuizSongs
                    ? await _history.GetRecentQuizSongsAsync(Math.Max(1, config.RecentQuizzesToExclude),
                        config.SourceType, config.SourceDirectory, AppSettings.Load().ActiveProfileName)
                    : [];
                StartLocalReplay(session, config, historySongs,
                    available.DistinctBy(song => song.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray(), recent, playlistPaths);
                return;
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

    private void StartLocalReplay(
        QuizSession session, QuizConfig config, IReadOnlyList<Song> historySongs,
        IReadOnlyList<Song> availableSongs, IReadOnlyList<Song> recentSongs, IReadOnlySet<string>? playlistPaths = null)
    {
        var resolution = ReplayTrackResolver.Resolve(historySongs, availableSongs);
        var slots = resolution.Slots.ToArray();
        var missing = historySongs.Where((_, index) => slots[index] is null).ToArray();
        var candidates = GetLocalReplayCandidates(
            config, availableSongs, slots.OfType<Song>().ToArray(), recentSongs, historySongs, playlistPaths);

        if (missing.Length > 0)
        {
            var availableCount = slots.Length - missing.Length;
            if (availableCount == 0 && candidates.Length < missing.Length)
            {
                MessageBox.Show(this,
                    $"None of the original tracks are available, and only {candidates.Length} eligible replacement track(s) were found. {missing.Length} are needed.",
                    "Replay unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new ReplayMissingTracksWindow(missing, availableCount, candidates.Length) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            if (dialog.Choice == MissingTrackChoice.Replace)
            {
                var replacementIndex = 0;
                for (var index = 0; index < slots.Length; index++)
                    if (slots[index] is null)
                        slots[index] = candidates[replacementIndex++];
            }
        }

        var resolvedSongs = slots.OfType<Song>().ToArray();
        var currentByPath = availableSongs.ToDictionary(song => song.SourcePath, StringComparer.OrdinalIgnoreCase);
        var changedMetadata = resolvedSongs.Count(song =>
            currentByPath.TryGetValue(song.SourcePath, out var current)
            && (!string.Equals(song.Title, current.Title, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(song.Artist, current.Artist, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(song.Album, current.Album, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(song.Genre, current.Genre, StringComparison.OrdinalIgnoreCase)
                || song.Year != current.Year));
        if (resolution.RelocatedCount > 0 || changedMetadata > 0)
        {
            MessageBox.Show(this,
                $"{resolution.RelocatedCount} original track(s) were found at new locations; {changedMetadata} have changed metadata. Original quiz metadata is retained for these tracks.",
                "Replay tracks updated", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        var reserve = GetLocalReplayCandidates(config, availableSongs, resolvedSongs, recentSongs, historySongs, playlistPaths)
            .Take(config.MulliganCount).ToArray();
        config.SongCount = resolvedSongs.Length;
        config.MulliganCount = reserve.Length;
        if (reserve.Length < session.Config.MulliganCount)
        {
            MessageBox.Show(this,
                $"Only {reserve.Length} eligible reserve track(s) are currently available; the replay will use that many mulligan(s).",
                "Replay reserve changed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        OnQuizRequested(config, resolvedSongs, reserve);
    }

    private static Song[] GetLocalReplayCandidates(
        QuizConfig config, IReadOnlyList<Song> availableSongs, IReadOnlyList<Song> selectedSongs,
        IReadOnlyList<Song> recentSongs, IReadOnlyList<Song> originalSongs, IReadOnlySet<string>? playlistPaths = null)
    {
        var selectedArtists = selectedSongs.Select(song => song.Artist.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roots = MusicFolderSelection.Roots(config).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        var recentPaths = recentSongs.Where(song => !string.IsNullOrWhiteSpace(song.SourcePath))
            .Select(song => song.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recentIdentities = recentSongs.Select(SongIdentity.Key).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = availableSongs
            .Where(song => playlistPaths is not null
                ? playlistPaths.Contains(song.SourcePath)
                : Path.GetDirectoryName(song.SourcePath) is { } directory
                    && roots.Any(root => MusicFolderSelection.IsSameOrDescendant(directory, root))
                    && MusicFolderSelection.IsIncluded(directory, config.LocalFolderRules))
            .Where(song => MatchesReplayFilters(song, config))
            .Where(song => !config.UniqueArtistsOnly || !selectedArtists.Contains(song.Artist.Trim()))
            .Where(song => !recentPaths.Contains(song.SourcePath)
                && (SongIdentity.Key(song) is not { } key || !recentIdentities.Contains(key)))
            .ToArray();
        Random.Shared.Shuffle(candidates);
        var distinctCandidates = SongIdentity.Distinct(candidates, selectedSongs.Concat(originalSongs));
        return config.UniqueArtistsOnly
            ? distinctCandidates.DistinctBy(song => song.Artist.Trim(), StringComparer.OrdinalIgnoreCase).ToArray()
            : distinctCandidates.ToArray();
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
            if (!YearFilter.FromString(config.FilterDecade).Matches(song.Year))
                return false;
        }

        return true;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_currentView is not SettingsView && GuardNavigation())
            ShowSettings();
    }

    private void ShowSettings()
        => Navigate(CreateSettingsView(), "Settings");

    private SettingsView CreateSettingsView()
    {
        var view = new SettingsView(_history);
        view.SettingsSaved += RefreshQuizSetupSources;
        view.SettingsSaved += RefreshQuizSetupProfileSettings;
        view.SettingsSaved += RefreshNavigationProfile;
        view.ProfileChanged += OnHomeProfileChanged;
        view.MetadataRequested += OnMetadataRequested;
        return view;
    }

    private void RefreshNavigationProfile()
    {
        var settings = AppSettings.Load();
        var profile = settings.Profiles.FirstOrDefault(item =>
            string.Equals(item.Name, settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase));
        NavigationProfileName.Text = profile?.Name ?? settings.ActiveProfileName;
        NavigationProfileInitials.Text = ProfileIconFiles.GetInitials(profile?.Name);
        NavigationProfileInitials.Visibility = Visibility.Visible;
        NavigationProfileImage.Source = null;
        NavigationProfileImage.Visibility = Visibility.Collapsed;
        NavigationProfileName.ToolTip = null;

        var path = ProfileIconFiles.GetExistingPath(profile?.IconFileName);
        if (path is null)
            return;

        try
        {
            NavigationProfileImage.Source = ProfileIconFiles.LoadBitmapSource(path);
            NavigationProfileImage.Visibility = Visibility.Visible;
            NavigationProfileInitials.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or System.IO.FileFormatException)
        {
            NavigationProfileName.ToolTip = $"The saved profile image could not be loaded: {exception.Message}";
        }
    }

    private void RefreshQuizSetupProfileSettings()
    {
        foreach (var setup in _viewHistory.Select(item => item.View)
                     .Append(_currentView)
                     .OfType<QuizSetupView>()
                     .Distinct())
            _ = setup.RefreshProfileSettingsAsync();
    }

    private bool CanLeaveSettings()
    {
        if (_currentView is not SettingsView settings || !settings.HasUnsavedChanges())
            return true;

        var choice = MessageBox.Show(this, "Save your changes to Settings before leaving?",
            "Unsaved settings", MessageBoxButton.YesNoCancel, MessageBoxImage.Question,
            MessageBoxResult.Cancel);
        if (choice == MessageBoxResult.Cancel)
            return false;
        if (choice == MessageBoxResult.Yes)
            return settings.TrySaveSettings();

        _currentView = CreateSettingsView();
        return true;
    }

    private bool GuardNavigation()
    {
        if (!CanLeaveSettings())
            return false;
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
        if (_viewHistory.Count == 0 || !GuardNavigation())
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
        NavigationProfileName.Visibility = _navigationCollapsed ? Visibility.Collapsed : Visibility.Visible;
        NavigationProfileFooter.Margin = _navigationCollapsed
            ? new Thickness(8, 12, 8, 12)
            : new Thickness(12, 12, 12, 12);
        NavigationProfileFooter.Padding = _navigationCollapsed
            ? new Thickness(3)
            : new Thickness(8);

        var navigationButtons = new (Button Button, TextBlock Label)[]
        {
            (HomeNavigationButton, HomeNavigationLabel),
            (NewQuizNavigationButton, NewQuizNavigationLabel),
            (SocialNavigationButton, SocialNavigationLabel),
            (StatisticsNavigationButton, StatisticsNavigationLabel),
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
