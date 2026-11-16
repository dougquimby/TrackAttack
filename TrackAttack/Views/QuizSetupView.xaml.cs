using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Settings;
using SpotifyAPI.Web;

namespace TrackAttack.Views;

/// <summary>Collects quiz configuration, library filters, and saved setup selections.</summary>
public partial class QuizSetupView : UserControl
{
    private static readonly HintType[] HintTypes = Enum.GetValues<HintType>();
    private readonly IMusicSource _musicSource;
    private readonly IQuizHistoryRepository _history;
    private bool _spotifyEnabled;
    private readonly ObservableCollection<FilterChoice> _genres = [];
    private readonly ObservableCollection<YearFilterNode> _decades = [];
    private readonly HashSet<string> _recentQuizSongPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _recentQuizSongIdentities = new(StringComparer.OrdinalIgnoreCase);
    private List<Song> _scannedSongs = [];
    private List<string> _musicDirectories = [];
    private List<MusicFolderNode> _folderNodes = [];
    private List<MusicFolderRule> _unavailableFolderRules = [];
    private bool _folderSelectionDirty;
    private bool _busy;
    private TaskCompletionSource? _scanCompletion;
    private bool _initializing;
    private int _matchingSongCount;
    private bool _recentHistoryLoaded = true;
    private long _recentRequestVersion;
    private bool _showSongCounts = true;
    private bool _loadingSpotifyPlaylists;
    private SpotifyMusicSource? _spotifySource;
    private SpotifyClient? _spotifyPlaylistClient;
    private FullPlaylist? _selectedSpotifyPlaylist;
    private List<SpotifyPlaylistChoice> _spotifyPlaylists = [];
    private bool _initialSetupLoaded;
    private string _setupProfileName = string.Empty;

    private bool IsSpotifySource => _spotifyEnabled && SourceCombo.SelectedIndex == 1;
    private bool IsSelectedSpotifyPlaylistDenied =>
        IsSpotifySource && SpotifyPlaylistCombo.SelectedItem is SpotifyPlaylistChoice { IsAccessDenied: true };

    /// <summary>Occurs with the configuration, initial songs, and mulligan reserve when a quiz is requested.</summary>
    public event Action<QuizConfig, IReadOnlyList<Song>, IReadOnlyList<Song>>? QuizRequested;
    /// <summary>Occurs with the current matching songs when the song list is requested.</summary>
    public event Action<IReadOnlyList<Song>>? SongsRequested;

    /// <summary>Creates the quiz setup view.</summary>
    /// <param name="musicSource">The source used to scan the library.</param>
    /// <param name="history">The repository used for recent-song exclusions.</param>
    /// <param name="spotifyEnabled">Whether the optional source selector is enabled.</param>
    public QuizSetupView(IMusicSource musicSource, IQuizHistoryRepository history, bool spotifyEnabled = false)
    {
        _musicSource = musicSource;
        _history = history;
        _spotifyEnabled = spotifyEnabled;
        _initializing = true;
        InitializeComponent();
        UpdateClipStartInput();
        SourceSelectorPanel.Visibility = spotifyEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (spotifyEnabled && !Directory.Exists(AppSettings.Load().MusicDirectory))
            SourceCombo.SelectedIndex = 1;
        UpdateSourcePanels();
        GenreList.ItemsSource = _genres;
        DecadeList.ItemsSource = _decades;
        QuizModeCombo.SelectedIndex = 2;
        PlaybackCombo.SelectedIndex = 0;
        TimerModeCombo.SelectedIndex = 0;
        GridColumnsCombo.SelectedIndex = 0;
        foreach (var type in HintTypes)
            HintTypesPanel.Children.Add(new CheckBox { Content = HintLabel(type), IsChecked = true, Margin = new Thickness(4) });
        UpdateSourcePanels();

        var settings = AppSettings.Load();
        _setupProfileName = settings.ActiveProfileName;
        _musicDirectories = settings.GetMusicDirectories();
        SecondsText.Text = settings.DefaultSecondsPerSong.ToString();
        ClipDurationText.Text = settings.DefaultClipDurationSeconds.ToString();
        FuzzyArtistCheck.IsChecked = settings.FuzzyArtistMatching;
        FuzzyTitleCheck.IsChecked = settings.FuzzySongTitleMatching;
        IgnorePunctuationCheck.IsChecked = settings.IgnorePunctuation;
        SkipCorrectCheck.IsChecked = settings.SkipOnCorrectGuess;
        ExcludeRecentCheck.IsChecked = settings.ExcludeRecentQuizSongs;
        RecentQuizCountText.Text = settings.RecentQuizzesToExclude.ToString();
        UpdateRecentCountInput();
        _showSongCounts = settings.ShowSongCountsInLists;
        PresetCombo.ItemsSource = settings.LocalQuizPresets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        _initializing = false;
        SetDefaultQuizPointsForMode();
        UpdateMatchCount();

        Loaded += async (_, _) =>
        {
            if (!_initialSetupLoaded)
            {
                _initialSetupLoaded = true;
                while (_scanCompletion is { } scan)
                    await scan.Task;
                if (!IsLoaded)
                {
                    _initialSetupLoaded = false;
                    return;
                }
                if (AppSettings.Load().GetLastQuizSetup() is { } lastSetup)
                {
                    await RestoreSetupAsync(lastSetup, restoringLastSetup: true);
                    return;
                }
            }
            if (IsSpotifySource)
                await LoadSpotifyPlaylistsAsync();
            else if (_musicDirectories.Count > 0)
                await ScanAsync();
        };
    }

    private void UpdateSourcePanels()
    {
        var spotify = IsSpotifySource;
        LocalSourcePanel.Visibility = spotify ? Visibility.Collapsed : Visibility.Visible;
        SpotifySourcePanel.Visibility = spotify ? Visibility.Visible : Visibility.Collapsed;
        GenreFilterPanel.IsEnabled = !spotify;
        SpotifyGenreNotice.Visibility = spotify ? Visibility.Visible : Visibility.Collapsed;
        SpotifyPreviewNotice.Visibility = spotify ? Visibility.Visible : Visibility.Collapsed;
        for (var index = 0; index < HintTypesPanel.Children.Count; index++)
        {
            if (HintTypes[index] != HintType.Genre)
                continue;
            var hint = (CheckBox)HintTypesPanel.Children[index];
            hint.IsEnabled = !spotify;
            hint.ToolTip = spotify ? "Spotify playlists do not provide genre metadata." : null;
            ToolTipService.SetShowOnDisabled(hint, spotify);
        }
        UpdateStartQuizAvailability();
    }

    private void OnRandomClipCheckChanged(object sender, RoutedEventArgs e)
        => UpdateClipStartInput();

    private void OnQuizTimeInputChanged(object sender, TextChangedEventArgs e) => UpdateQuizTime();
    private void OnQuizTimerModeChanged(object sender, SelectionChangedEventArgs e) => UpdateQuizTime();

    private void UpdateQuizTime()
    {
        if (_initializing)
            return;

        var totalQuiz = TimerModeCombo.SelectedIndex == 1;
        TimerModeDescription.Text = totalQuiz
            ? "One shared countdown runs for the whole quiz. Passing does not reset it; the quiz ends when time runs out."
            : "Each song has its own countdown. Passing and returning keep its remaining time. At zero, an unfinished song is missed and cannot be revisited; a mulligan gives the replacement a full timer.";

        if (!TryInt(SongCountText.Text, 20, 1, 1000, out var requestedCount)
            || !TryInt(SecondsText.Text, 30, 5, 600, out var seconds))
        {
            QuizTimeText.Text = "Enter a valid song count and seconds per song to calculate quiz time.";
            return;
        }

        var count = _matchingSongCount > 0 ? Math.Min(requestedCount, _matchingSongCount) : requestedCount;
        var totalSeconds = count * seconds;
        var time = totalSeconds >= 3600
            ? $"{totalSeconds / 3600}:{totalSeconds / 60 % 60:00}:{totalSeconds % 60:00}"
            : $"{totalSeconds / 60}:{totalSeconds % 60:00}";
        var label = totalQuiz ? "Total quiz time" : "Starting time allocation";
        var availability = _matchingSongCount == 0 ? " No matching tracks are available yet."
            : count < requestedCount ? " Based on the available matching tracks." : string.Empty;
        var mulligans = totalQuiz ? string.Empty : " Mulligan replacements receive additional time.";
        QuizTimeText.Text = $"{label}: {time} ({count:N0} songs × {seconds} sec).{availability}{mulligans}";
    }

    private void OnTreePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not TreeView tree || e.Delta == 0)
            return;

        var inner = FindScrollViewer(tree);
        if (inner is null || (e.Delta > 0 && inner.VerticalOffset > 0)
                          || (e.Delta < 0 && inner.VerticalOffset < inner.ScrollableHeight))
            return;

        for (var parent = VisualTreeHelper.GetParent(tree); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not ScrollViewer outer)
                continue;

            e.Handled = true;
            outer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = Mouse.MouseWheelEvent
            });
            return;
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is ScrollViewer scrollViewer)
                return scrollViewer;
            if (FindScrollViewer(child) is { } nested)
                return nested;
        }
        return null;
    }

    private void OnQuizModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;
        SetDefaultQuizPointsForMode();
    }

    private QuizMode GetSelectedQuizMode()
        => QuizModeCombo.SelectedIndex switch
        {
            1 => QuizMode.GuessArtist,
            2 => QuizMode.GuessBoth,
            _ => QuizMode.GuessSongTitle
        };

    private void SetDefaultQuizPointsForMode()
    {
        switch (GetSelectedQuizMode())
        {
            case QuizMode.GuessArtist:
                ArtistPointsText.Text = "2";
                break;
            case QuizMode.GuessBoth:
                SongTitlePointsText.Text = "1.5";
                ArtistPointsText.Text = "0.5";
                break;
            default:
                SongTitlePointsText.Text = "2";
                break;
        }

        SongTitlePointsPanel.Visibility = GetSelectedQuizMode() == QuizMode.GuessArtist
            ? Visibility.Collapsed
            : Visibility.Visible;
        ArtistPointsPanel.Visibility = GetSelectedQuizMode() == QuizMode.GuessSongTitle
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private bool TryGetQuizPointValues(out QuizMode mode, out double? songTitlePoints, out double? artistPoints)
    {
        mode = GetSelectedQuizMode();
        songTitlePoints = null;
        artistPoints = null;

        if (mode != QuizMode.GuessArtist)
        {
            if (!TryPointValue(SongTitlePointsText.Text, out var titleValue))
            {
                QuizRulesTab.IsSelected = true;
                MessageBox.Show("Song title points must be zero or a positive finite number.",
                    "Invalid point value", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            songTitlePoints = titleValue;
        }

        if (mode != QuizMode.GuessSongTitle)
        {
            if (!TryPointValue(ArtistPointsText.Text, out var artistValue))
            {
                QuizRulesTab.IsSelected = true;
                MessageBox.Show("Artist points must be zero or a positive finite number.",
                    "Invalid point value", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            artistPoints = artistValue;
        }

        return true;
    }

    private void UpdateClipStartInput()
        => ClipStartPanel.IsEnabled = RandomClipCheck.IsChecked != true;

    /// <summary>Reloads profile settings and refreshes the library or recent-song filters as needed.</summary>
    /// <returns>A task that completes after any required refresh.</returns>
    public async Task RefreshProfileSettingsAsync()
    {
        while (_scanCompletion is { } scan)
            await scan.Task;

        var settings = AppSettings.Load();
        var profileChanged = !string.Equals(
            _setupProfileName, settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase);
        if (profileChanged)
        {
            _setupProfileName = settings.ActiveProfileName;
            _initialSetupLoaded = false;
        }
        var previousFolders = _musicDirectories;
        var countsChanged = _showSongCounts != settings.ShowSongCountsInLists;
        _showSongCounts = settings.ShowSongCountsInLists;
        _initializing = true;
        _musicDirectories = settings.GetMusicDirectories();
        if (profileChanged)
        {
            SecondsText.Text = settings.DefaultSecondsPerSong.ToString();
            ClipDurationText.Text = settings.DefaultClipDurationSeconds.ToString();
            FuzzyArtistCheck.IsChecked = settings.FuzzyArtistMatching;
            FuzzyTitleCheck.IsChecked = settings.FuzzySongTitleMatching;
            IgnorePunctuationCheck.IsChecked = settings.IgnorePunctuation;
            SkipCorrectCheck.IsChecked = settings.SkipOnCorrectGuess;
            ExcludeRecentCheck.IsChecked = settings.ExcludeRecentQuizSongs;
            RecentQuizCountText.Text = settings.RecentQuizzesToExclude.ToString();
            UpdateRecentCountInput();
        }
        _initializing = false;

        if (countsChanged)
            PopulateFilters();

        if (!IsSpotifySource && !previousFolders.SequenceEqual(_musicDirectories, StringComparer.OrdinalIgnoreCase))
        {
            _folderNodes = [];
            _unavailableFolderRules = [];
            MusicFolderTree.ItemsSource = null;
            _folderSelectionDirty = true;
            if (_musicDirectories.Count > 0)
                await ScanAsync([]);
            else
            {
                _scannedSongs.Clear();
                PopulateFilters();
                _folderSelectionDirty = false;
                ScanStatus.Text = "Add library folders in Settings.";
                UpdateMatchCount();
            }
        }
        else if (ExcludeRecentCheck.IsChecked == true)
        {
            await RefreshRecentSongsAsync();
            UpdateMatchCount();
        }
        else
        {
            _recentRequestVersion++;
            _recentQuizSongPaths.Clear();
            _recentQuizSongIdentities.Clear();
            _recentHistoryLoaded = true;
            UpdateMatchCount();
        }
    }

    public async Task RefreshSpotifyFeatureSwitchAsync(bool enabled)
    {
        if (_spotifyEnabled == enabled)
            return;

        _spotifyEnabled = enabled;
        SourceSelectorPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled || SourceCombo.SelectedIndex != 1)
            return;

        _initializing = true;
        SourceCombo.SelectedIndex = 0;
        _initializing = false;
        UpdateSourcePanels();
        SpotifyPlaylistAccessHelp.Visibility = Visibility.Collapsed;
        _selectedSpotifyPlaylist = null;
        _spotifyPlaylists.Clear();
        SpotifyPlaylistCombo.ItemsSource = null;
        _scannedSongs.Clear();
        _recentQuizSongPaths.Clear();
        _recentQuizSongIdentities.Clear();
        if (_musicDirectories.Count > 0)
            await ScanAsync();
        else
            UpdateMatchCount();
    }

    private async void OnSourceTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;

        UpdateSourcePanels();
        SpotifyPlaylistAccessHelp.Visibility = Visibility.Collapsed;
        _scannedSongs.Clear();
        _selectedSpotifyPlaylist = null;
        _recentQuizSongPaths.Clear();
        _recentQuizSongIdentities.Clear();
        if (IsSpotifySource)
            await LoadSpotifyPlaylistsAsync();
        else if (_musicDirectories.Count > 0)
            await ScanAsync();
        else
            UpdateMatchCount();
    }

    private async Task<bool> EnsureSpotifyConnectedAsync()
    {
        if (SpotifyService.Client is not null)
            return true;
        try
        {
            if (await SpotifyService.TryReconnectAsync())
                return true;
            SpotifyStatus.Text = "Connect Spotify in Settings to load playlists.";
            MessageBox.Show("Connect your Spotify account in Settings before selecting a playlist.",
                "Spotify connection required", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            SpotifyStatus.Text = "Spotify reconnection failed.";
            MessageBox.Show($"Could not reconnect to Spotify:\n{exception.Message}",
                "Spotify connection failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return false;
    }

    private async void OnRefreshSpotifyPlaylistsClick(object sender, RoutedEventArgs e)
    {
        _spotifySource?.ClearCache();
        await LoadSpotifyPlaylistsAsync();
    }

    private async void OnCheckSpotifyClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        SetBusy(true);
        try { await CheckSpotifyReadinessAsync(showMessages: true); }
        finally { SetBusy(false); }
    }

    private async Task<bool> CheckSpotifyReadinessAsync(bool showMessages)
    {
        SpotifyReadinessText.Text = "Checking Spotify connection and playback device...";
        if (!await EnsureSpotifyConnectedAsync())
        {
            SpotifyReadinessText.Text = "Spotify is not connected. Connect your account in Settings and try again.";
            return false;
        }

        try
        {
            var client = SpotifyService.Client!;
            var device = await SpotifyService.GetAvailablePlaybackDeviceAsync(client);
            if (!ReferenceEquals(client, SpotifyService.Client))
            {
                SpotifyReadinessText.Text = "The Spotify connection changed. Check the connection again.";
                if (showMessages)
                    MessageBox.Show(SpotifyReadinessText.Text, "Spotify connection changed",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (device is null)
            {
                SpotifyReadinessText.Text = "No usable Spotify Connect device found. Open Spotify on a device signed in to this account, then check again.";
                if (showMessages)
                    MessageBox.Show(SpotifyReadinessText.Text, "Spotify device required",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            SpotifyReadinessText.Text = $"Spotify connected. Available device: {device.Name}. Keep Spotify open; Connect playback requires Premium.";
            return true;
        }
        catch (Exception exception)
        {
            SpotifyReadinessText.Text = $"Could not check Spotify connection or devices: {exception.Message} Reconnect in Settings or try again.";
            if (showMessages)
                MessageBox.Show(SpotifyReadinessText.Text, "Spotify readiness check failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private async Task LoadSpotifyPlaylistsAsync()
    {
        if (_loadingSpotifyPlaylists || !IsSpotifySource)
            return;
        _loadingSpotifyPlaylists = true;
        SetBusy(true);
        SpotifyPlaylistAccessHelp.Visibility = Visibility.Collapsed;
        SpotifyStatus.Text = "Loading Spotify playlists…";
        try
        {
            if (!await EnsureSpotifyConnectedAsync())
            {
                _spotifySource = null;
                _spotifyPlaylistClient = null;
                _spotifyPlaylists.Clear();
                SpotifyPlaylistCombo.ItemsSource = null;
                _selectedSpotifyPlaylist = null;
                _scannedSongs.Clear();
                SpotifyReadinessText.Text = "Spotify is not connected. Connect your account in Settings and try again.";
                UpdateMatchCount();
                return;
            }
            await CheckSpotifyReadinessAsync(showMessages: false);
            var client = SpotifyService.Client;
            if (client is null || !IsSpotifySource || !IsLoaded)
                return;
            _spotifySource = new SpotifyMusicSource(client);
            var sameClient = ReferenceEquals(_spotifyPlaylistClient, client);
            var previousId = sameClient ? _selectedSpotifyPlaylist?.Id : null;
            var deniedIds = sameClient
                ? _spotifyPlaylists.Where(p => p.IsAccessDenied)
                    .Select(p => p.Playlist.Id).OfType<string>()
                    .Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            var playlists = await _spotifySource.GetUserPlaylistsAsync();
            if (!IsSpotifySource || !IsLoaded)
                return;
            if (!ReferenceEquals(client, SpotifyService.Client))
            {
                _spotifyPlaylistClient = null;
                _spotifyPlaylists.Clear();
                SpotifyPlaylistCombo.ItemsSource = null;
                _selectedSpotifyPlaylist = null;
                _scannedSongs.Clear();
                PopulateFilters();
                SpotifyStatus.Text = "The Spotify connection changed. Refresh playlists and select one again.";
                UpdateMatchCount();
                return;
            }
            _spotifyPlaylists = playlists.Select(p => new SpotifyPlaylistChoice(
                p, p.Id is { } id && deniedIds.Contains(id))).ToList();
            _spotifyPlaylistClient = client;
            SpotifyPlaylistCombo.ItemsSource = _spotifyPlaylists;
            _selectedSpotifyPlaylist = null;
            if (!string.IsNullOrWhiteSpace(previousId))
                SpotifyPlaylistCombo.SelectedItem = _spotifyPlaylists.FirstOrDefault(p => p.Playlist.Id == previousId);
            SpotifyStatus.Text = _spotifyPlaylists.Count == 0
                ? "No playlists were found for this account."
                : $"{_spotifyPlaylists.Count:N0} playlists available. Select one to load its tracks.";
            if (SpotifyPlaylistCombo.SelectedItem is SpotifyPlaylistChoice playlist)
                await LoadSpotifyPlaylistAsync(playlist);
            else
                UpdateMatchCount();
        }
        catch (Exception exception)
        {
            SpotifyStatus.Text = "Could not load playlists.";
            MessageBox.Show($"Could not load Spotify playlists:\n{exception.Message}",
                "Spotify playlist load failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _loadingSpotifyPlaylists = false;
            SetBusy(false);
        }
    }

    private async void OnSpotifyPlaylistChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSpotifyPlaylists)
            return;
        UpdateStartQuizAvailability();
        if (SpotifyPlaylistCombo.SelectedItem is not SpotifyPlaylistChoice playlist)
            return;
        await LoadSpotifyPlaylistAsync(playlist);
    }

    private async Task LoadSpotifyPlaylistAsync(SpotifyPlaylistChoice choice)
    {
        var playlist = choice.Playlist;
        SpotifyPlaylistAccessHelp.Visibility = Visibility.Collapsed;
        if (_spotifySource is null)
            return;
        if (string.IsNullOrWhiteSpace(playlist.Id))
        {
            _selectedSpotifyPlaylist = null;
            _scannedSongs.Clear();
            PopulateFilters();
            UpdateMatchCount();
            MessageBox.Show("The selected Spotify playlist does not have a usable playlist ID.",
                "Playlist unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SetBusy(true);
        _selectedSpotifyPlaylist = playlist;
        SpotifyStatus.Text = $"Loading tracks from “{playlist.Name}”…";
        try
        {
            _scannedSongs = (await _spotifySource.GetPlaylistSongsAsync(playlist.Id)).ToList();
            choice.IsAccessDenied = false;
            PopulateFilters();
            if (ExcludeRecentCheck.IsChecked == true)
                await RefreshRecentSongsAsync();
            var previewCount = _scannedSongs.Count(song => !string.IsNullOrWhiteSpace(song.PreviewUrl));
            SpotifyStatus.Text = $"{_scannedSongs.Count:N0} tracks from “{playlist.Name}”; {previewCount:N0} have preview clips. Other tracks require Spotify Premium playback.";
            UpdateMatchCount();
        }
        catch (Exception exception)
        {
            _scannedSongs.Clear();
            PopulateFilters();
            UpdateMatchCount();
            if (exception is SpotifyPlaylistAccessException)
            {
                choice.IsAccessDenied = true;
                SpotifyStatus.Text = "Spotify denied access to the playlist (403). See the instructions below.";
                SpotifyPlaylistAccessHelp.Visibility = Visibility.Visible;
            }
            else
            {
                SpotifyStatus.Text = "Could not load playlist tracks.";
                if (choice.IsAccessDenied)
                    SpotifyPlaylistAccessHelp.Visibility = Visibility.Visible;
                MessageBox.Show($"Could not load tracks from “{playlist.Name}”:\n{exception.Message}",
                    "Spotify playlist load failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnScanClick(object sender, RoutedEventArgs e)
        => await ScanAsync();

    private List<MusicFolderRule> GetFolderRules()
    {
        var rules = new List<MusicFolderRule>();
        foreach (var node in _folderNodes)
            node.AddRules(rules);
        rules.AddRange(_unavailableFolderRules);
        return rules;
    }

    private void SubscribeFolderNode(MusicFolderNode node)
    {
        node.SelectionChanged += () => OnFolderSelectionChanged(node);
        foreach (var child in node.Children)
            SubscribeFolderNode(child);
    }

    private void OnFolderSelectionChanged(MusicFolderNode node)
    {
        _unavailableFolderRules.RemoveAll(rule => MusicFolderSelection.IsSameOrDescendant(rule.Path, node.Path));
        _folderSelectionDirty = true;
        _scannedSongs.Clear();
        ScanStatus.Text = "Folder selection changed. Scan / Refresh to update tracks.";
        UpdateMatchCount();
    }

    private async Task<bool> ScanAsync(IReadOnlyList<MusicFolderRule>? savedRules = null)
    {
        if (_busy)
            return false;
        if (_musicDirectories.Count == 0)
        {
            MessageBox.Show("Add a music folder in Settings before scanning.", "Folder required", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _scanCompletion = completion;
        SetBusy(true);
        ScanStatus.Text = "Scanning local audio files…";
        try
        {
            Cursor = Cursors.Wait;
            ForceCursor = true;
            IReadOnlyList<string> roots = _musicDirectories.ToArray();
            for (var i = 0; i < roots.Count; i++)
            {
                for (var j = i + 1; j < roots.Count; j++)
                {
                    if (MusicFolderSelection.IsSameOrDescendant(roots[i], roots[j])
                        || MusicFolderSelection.IsSameOrDescendant(roots[j], roots[i]))
                        throw new InvalidOperationException(
                            $"Music library roots cannot overlap:\n{roots[i]}\n{roots[j]}\nChoose separate folders or remove the nested root in Settings.");
                }
            }
            var rules = savedRules ?? GetFolderRules();
            var treeIssues = new List<LibraryScanIssue>();
            var nodes = await Task.Run(() => roots.Select(root => MusicFolderNode.Create(root, rules, treeIssues)).ToList());
            var existingPaths = nodes.SelectMany(node => node.Paths()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _unavailableFolderRules = rules.Where(rule => !existingPaths.Contains(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(rule.Path)))).ToList();
            foreach (var node in nodes)
                SubscribeFolderNode(node);
            _folderNodes = nodes;
            MusicFolderTree.ItemsSource = _folderNodes;
            var effectiveRules = GetFolderRules();
            if (_musicSource is not LocalFileMusicSource local)
                throw new InvalidOperationException("The selected source cannot scan local music folders.");
            var scan = await Task.Run(() => local.ScanLibrary(roots, effectiveRules));
            _scannedSongs = scan.Songs.ToList();
            _folderSelectionDirty = false;
            PopulateFilters();
            if (ExcludeRecentCheck.IsChecked == true)
                await RefreshRecentSongsAsync();
            UpdateMatchCount();
            var issues = treeIssues.Concat(scan.Issues)
                .Concat(_unavailableFolderRules.Select(rule =>
                    new LibraryScanIssue(rule.Path, "Saved folder selection is unavailable and has been retained.")))
                .DistinctBy(issue => issue.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            ScanStatus.Text = issues.Length == 0
                ? $"Scan complete: {_scannedSongs.Count:N0} readable tracks ({scan.CachedSongs:N0} cached)."
                : $"Scan complete: {_scannedSongs.Count:N0} readable tracks ({scan.CachedSongs:N0} cached); {issues.Length:N0} warning(s).";
            if (issues.Length > 0)
                MessageBox.Show(LibraryScanIssueSummary.Format(issues),
                    "Library scan warnings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return true;
        }
        catch (Exception ex)
        {
            _scannedSongs.Clear();
            PopulateFilters();
            _folderSelectionDirty = true;
            UpdateMatchCount();
            ScanStatus.Text = "Scan failed.";
            MessageBox.Show($"Could not scan this music folder:\n{ex.Message}", "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            ClearValue(CursorProperty);
            ClearValue(ForceCursorProperty);
            SetBusy(false);
            _scanCompletion = null;
            completion.SetResult();
        }
    }

    private void PopulateFilters()
    {
        var oldGenres = _genres.Where(item => item.IsChecked).Select(item => item.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var oldYears = GetSelectedYears();
        PopulateGenreFilters(oldGenres);
        PopulateYearFilters(oldYears);
    }

    private void PopulateGenreFilters(IReadOnlyCollection<string> selectedGenres)
    {
        var selected = selectedGenres.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _genres.Clear();
        if (IsSpotifySource)
        {
            foreach (var genre in selected.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                _genres.Add(new FilterChoice(genre, 0, _showSongCounts) { IsChecked = true });
            return;
        }
        var counts = _scannedSongs
            .GroupBy(song => string.IsNullOrWhiteSpace(song.Genre) ? LocalFileMusicSource.UnknownMetadataOption : song.Genre.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var genre in selected)
            counts.TryAdd(genre, 0);
        foreach (var (value, count) in counts.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            _genres.Add(new FilterChoice(value, count, _showSongCounts) { IsChecked = selected.Contains(value) });
    }

    private void PopulateYearFilters(IReadOnlyCollection<string> selectedYears)
    {
        var filter = new YearFilter(selectedYears);
        var counts = _scannedSongs.Where(song => song.Year > 0)
            .GroupBy(song => song.Year).ToDictionary(group => group.Key, group => group.Count());
        foreach (var selection in selectedYears)
        {
            if (selection.EndsWith('s') && int.TryParse(selection[..^1], out var decade)
                && decade > 0 && decade % 10 == 0 && decade <= int.MaxValue - 9)
            {
                for (var year = decade; year < decade + 10; year++)
                    counts.TryAdd(year, 0);
            }
            else if (int.TryParse(selection, out var year) && year > 0)
                counts.TryAdd(year, 0);
        }

        _decades.Clear();
        var unknownCount = _scannedSongs.Count(song => song.Year <= 0);
        if (unknownCount > 0 || selectedYears.Any(value => string.Equals(
                value, LocalFileMusicSource.UnknownMetadataOption, StringComparison.OrdinalIgnoreCase)))
            _decades.Add(new YearFilterNode(LocalFileMusicSource.UnknownMetadataOption, unknownCount,
                _showSongCounts, selectedYears.Count > 0 && filter.Matches(0), UpdateMatchCount));
        foreach (var decade in counts.GroupBy(item => item.Key / 10 * 10).OrderBy(group => group.Key))
        {
            var node = new YearFilterNode($"{decade.Key}s", decade.Sum(item => item.Value), _showSongCounts,
                false, UpdateMatchCount);
            foreach (var year in decade.OrderBy(item => item.Key))
                node.Children.Add(new YearFilterNode(year.Key.ToString(), year.Value, _showSongCounts,
                    selectedYears.Count > 0 && filter.Matches(year.Key), UpdateMatchCount, node));
            _decades.Add(node);
        }
    }

    private List<string> GetSelectedYears() => _decades.SelectMany(node => node.SelectedValues()).ToList();

    private List<Song> GetMatchingSongs()
    {
        var selectedGenres = IsSpotifySource
            ? []
            : _genres.Where(item => item.IsChecked).Select(item => item.Value).ToArray();
        var yearFilter = new YearFilter(GetSelectedYears());
        IEnumerable<Song> songs = _scannedSongs;
        if (selectedGenres.Length > 0)
            songs = songs.Where(song => selectedGenres.Any(value => value == LocalFileMusicSource.UnknownMetadataOption
                ? string.IsNullOrWhiteSpace(song.Genre) : string.Equals(value, song.Genre, StringComparison.OrdinalIgnoreCase)));
        songs = songs.Where(song => yearFilter.Matches(song.Year));
        if (TryInt(MinLengthText.Text, 0, 0, 86400, out var minimumSeconds) && minimumSeconds > 0)
            songs = songs.Where(song => song.Duration.TotalSeconds >= minimumSeconds);
        if (TryInt(MaxLengthText.Text, 0, 0, 86400, out var maximumSeconds) && maximumSeconds > 0)
            songs = songs.Where(song => song.Duration.TotalSeconds <= maximumSeconds);
        if (ExcludeRecentCheck.IsChecked == true)
            songs = _recentHistoryLoaded
                ? songs.Where(song => !_recentQuizSongPaths.Contains(song.SourcePath)
                    && (SongIdentity.Key(song) is not { } key || !_recentQuizSongIdentities.Contains(key)))
                : [];
        if (!IsSpotifySource)
            songs = SongIdentity.Distinct(songs);
        if (UniqueArtistsCheck.IsChecked == true)
        {
            var randomizedSongs = songs.ToArray();
            Random.Shared.Shuffle(randomizedSongs);
            songs = randomizedSongs.DistinctBy(
                song => IsSpotifySource ? song.Artist : song.Artist.Trim(), StringComparer.OrdinalIgnoreCase);
        }
        return songs.ToList();
    }

    private void OnFilterCheckedChanged(object sender, RoutedEventArgs e) => UpdateMatchCount();
    private void OnSelectAllGenresClick(object sender, RoutedEventArgs e) => SetFilterChoices(_genres, true);
    private void OnSelectNoGenresClick(object sender, RoutedEventArgs e) => SetFilterChoices(_genres, false);
    private void OnSelectAllDecadesClick(object sender, RoutedEventArgs e) => SetYearChoices(true);
    private void OnSelectNoDecadesClick(object sender, RoutedEventArgs e) => SetYearChoices(false);
    private void OnExpandFoldersClick(object sender, RoutedEventArgs e) => SetFolderExpansion(true);
    private void OnCollapseFoldersClick(object sender, RoutedEventArgs e) => SetFolderExpansion(false);
    private void OnExpandDecadesClick(object sender, RoutedEventArgs e) => SetDecadeExpansion(true);
    private void OnCollapseDecadesClick(object sender, RoutedEventArgs e) => SetDecadeExpansion(false);
    private void OnSelectAllHintsClick(object sender, RoutedEventArgs e) => SetHintTypes(true);
    private void OnSelectNoHintsClick(object sender, RoutedEventArgs e) => SetHintTypes(false);

    private void SetFolderExpansion(bool expanded)
        => SetTreeExpansion(_folderNodes, node => node.Children, (node, value) => node.IsExpanded = value, expanded);

    private void SetDecadeExpansion(bool expanded)
        => SetTreeExpansion(_decades, node => node.Children, (node, value) => node.IsExpanded = value, expanded);

    private static void SetTreeExpansion<T>(IEnumerable<T> roots, Func<T, IEnumerable<T>> children,
        Action<T, bool> setExpanded, bool expanded)
    {
        var pending = new Stack<T>(roots);
        while (pending.TryPop(out var node))
        {
            setExpanded(node, expanded);
            foreach (var child in children(node))
                pending.Push(child);
        }
    }

    private void SetFilterChoices(IEnumerable<FilterChoice> choices, bool isChecked)
    {
        foreach (var choice in choices)
            choice.IsChecked = isChecked;
        UpdateMatchCount();
    }

    private void SetYearChoices(bool isChecked)
    {
        foreach (var node in _decades)
            node.IsChecked = isChecked;
        UpdateMatchCount();
    }

    private void SetHintTypes(bool isChecked)
    {
        foreach (var checkBox in HintTypesPanel.Children.OfType<CheckBox>())
            if (checkBox.IsEnabled)
                checkBox.IsChecked = isChecked;
    }

    private void OnFiltersChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing) UpdateMatchCount();
    }
    private void OnUniqueArtistsChanged(object sender, RoutedEventArgs e) => UpdateMatchCount();
    private async void OnRecentCountChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing)
            return;
        if (ExcludeRecentCheck.IsChecked == true)
        {
            var refresh = RefreshRecentSongsAsync();
            UpdateMatchCount();
            await refresh;
        }
        UpdateMatchCount();
    }

    private async void OnRecentOptionsChanged(object sender, RoutedEventArgs e)
    {
        UpdateRecentCountInput();
        await OnRecentOptionsChangedAsync();
    }

    private void UpdateRecentCountInput()
        => RecentCountPanel.IsEnabled = ExcludeRecentCheck.IsChecked == true;

    private async Task OnRecentOptionsChangedAsync()
    {
        if (_initializing) return;
        _recentRequestVersion++;
        var settings = AppSettings.Load();
        settings.ExcludeRecentQuizSongs = ExcludeRecentCheck.IsChecked == true;
        settings.RecentQuizzesToExclude = Math.Max(1, ParseInt(RecentQuizCountText.Text));
        try { settings.Save(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save recent-quiz options:\n{ex.Message}", "Settings save failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        if (ExcludeRecentCheck.IsChecked == true)
            await RefreshRecentSongsAsync();
        else
        {
            _recentQuizSongPaths.Clear();
            _recentQuizSongIdentities.Clear();
            _recentHistoryLoaded = true;
        }
        UpdateMatchCount();
    }

    private async Task RefreshRecentSongsAsync()
    {
        var requestVersion = ++_recentRequestVersion;
        _recentHistoryLoaded = false;
        _recentQuizSongPaths.Clear();
        _recentQuizSongIdentities.Clear();
        var count = Math.Max(1, ParseInt(RecentQuizCountText.Text));
        try
        {
            var sourceType = IsSpotifySource ? "Spotify" : "Local";
            var sourceDirectory = IsSpotifySource
                ? _selectedSpotifyPlaylist?.Id ?? string.Empty
                : _musicDirectories.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                _recentHistoryLoaded = true;
                return;
            }
            var profileName = AppSettings.Load().ActiveProfileName;
            var recent = await _history.GetRecentQuizSongsAsync(count, sourceType, sourceDirectory, profileName);
            if (requestVersion != _recentRequestVersion || ExcludeRecentCheck.IsChecked != true)
                return;
            foreach (var song in recent)
            {
                if (!string.IsNullOrWhiteSpace(song.SourcePath))
                    _recentQuizSongPaths.Add(song.SourcePath);
                var legacySpotifyPath = IsSpotifySource
                    && !song.SourcePath.StartsWith("spotify:track:", StringComparison.OrdinalIgnoreCase);
                if ((!IsSpotifySource || legacySpotifyPath || string.IsNullOrWhiteSpace(song.SourcePath))
                    && SongIdentity.Key(song) is { } key)
                    _recentQuizSongIdentities.Add(key);
            }
            _recentHistoryLoaded = true;
        }
        catch (Exception ex)
        {
            if (requestVersion != _recentRequestVersion || ExcludeRecentCheck.IsChecked != true)
            {
                System.Diagnostics.Trace.TraceWarning("An outdated recent-history request failed: {0}", ex);
                return;
            }
            MessageBox.Show($"Could not load recently played songs:\n{ex.Message}", "History load failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateMatchCount()
    {
        if (_initializing)
            return;
        _matchingSongCount = GetMatchingSongs().Count;
        MatchCountText.Text = $"{_matchingSongCount:N0} matching tracks";
        UpdateQuizTime();
    }
    private void OnViewSongsClick(object sender, RoutedEventArgs e)
    {
        if (!_busy)
            SongsRequested?.Invoke(GetMatchingSongs());
    }

    private async void OnStartQuizClick(object sender, RoutedEventArgs e)
    {
        if (_busy || IsSelectedSpotifyPlaylistDenied)
            return;
        SetBusy(true);
        try
        {
            if (ExcludeRecentCheck.IsChecked == true)
                await RefreshRecentSongsAsync();
            if (IsSpotifySource && !string.IsNullOrWhiteSpace(_selectedSpotifyPlaylist?.Id))
            {
                if (!await CheckSpotifyReadinessAsync(showMessages: true))
                {
                    SourceTab.IsSelected = true;
                    return;
                }
                if (!IsLoaded || !IsSpotifySource)
                    return;
                if (!ReferenceEquals(_spotifyPlaylistClient, SpotifyService.Client))
                {
                    SourceTab.IsSelected = true;
                    SpotifyReadinessText.Text = "The Spotify connection changed. Refresh playlists and select a playlist again.";
                    MessageBox.Show(SpotifyReadinessText.Text, "Refresh Spotify playlists",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            StartQuiz();
        }
        finally { SetBusy(false); }
    }

    private void StartQuiz()
    {
        if (!IsSpotifySource && _folderSelectionDirty)
        {
            SourceTab.IsSelected = true;
            MessageBox.Show("Scan the selected folders before starting the quiz.",
                "Scan required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var selectedSpotifyPlaylist = _selectedSpotifyPlaylist;
        var spotifyPlaylistId = selectedSpotifyPlaylist?.Id;
        if (IsSpotifySource && string.IsNullOrWhiteSpace(spotifyPlaylistId))
        {
            MessageBox.Show("Select a Spotify playlist before starting the quiz.",
                "Playlist required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var candidates = GetMatchingSongs();
        if (candidates.Count == 0)
        {
            if (_scannedSongs.Count == 0)
                SourceTab.IsSelected = true;
            else
                TrackFiltersTab.IsSelected = true;
            MessageBox.Show("No songs match the selected filters.", "No songs", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var clipStart = 0;
        if (!TryInt(SongCountText.Text, 20, 1, 1000, out var requestedCount)
            || !TryInt(SecondsText.Text, 30, 5, 600, out var seconds)
            || !TryInt(ClipDurationText.Text, 15, 3, 120, out var clipDuration)
            || (RandomClipCheck.IsChecked != true && !TryInt(ClipStartText.Text, 30, 0, 3600, out clipStart)))
        {
            QuizRulesTab.IsSelected = true;
            MessageBox.Show("Check the song count, timer, and clip settings.", "Invalid settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!TryInt(MinLengthText.Text, 0, 0, 86400, out var minLength)
            || !TryInt(MaxLengthText.Text, 0, 0, 86400, out var maxLength)
            || (minLength > 0 && maxLength > 0 && minLength > maxLength))
        {
            TrackFiltersTab.IsSelected = true;
            MessageBox.Show("Check the track-length settings.", "Invalid settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!TryDouble(MulliganPenaltyText.Text, out var mulliganPenalty)
            || !TryDouble(ExtenderPenaltyText.Text, out var extenderPenalty)
            || !TryDouble(ClipRerollPenaltyText.Text, out var clipRerollPenalty)
            || !TryDouble(HintPenaltyText.Text, out var hintPenalty)
            || !TryDouble(MaxPenaltyText.Text, out var maxPenalty))
        {
            PowersTab.IsSelected = true;
            MessageBox.Show("Power penalties must be zero or a positive number.", "Invalid penalty",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!TryGetQuizPointValues(out var quizMode, out var songTitlePoints, out var artistPoints))
            return;
        if (!TryGetQuickGuessSettings(out var quickGuessSeconds, out var quickGuessPoints))
            return;
        if (!TryGetStarPowerSettings(out var starPowerStreak, out var starPowerPoints))
            return;

        var count = Math.Min(requestedCount, candidates.Count);
        var shuffled = candidates.ToArray();
        Random.Shared.Shuffle(shuffled);
        var selected = shuffled.Take(count).ToArray();
        var reserveCount = Math.Min(ParseInt(MulliganCountText.Text), shuffled.Length - count);
        var reserve = shuffled.Skip(count).Take(reserveCount).ToArray();
        var settings = AppSettings.Load();
        settings.DefaultSecondsPerSong = seconds;
        settings.DefaultClipDurationSeconds = clipDuration;
        settings.FuzzyArtistMatching = FuzzyArtistCheck.IsChecked == true;
        settings.FuzzySongTitleMatching = FuzzyTitleCheck.IsChecked == true;
        settings.IgnorePunctuation = IgnorePunctuationCheck.IsChecked == true;
        settings.SkipOnCorrectGuess = SkipCorrectCheck.IsChecked == true;
        settings.ExcludeRecentQuizSongs = ExcludeRecentCheck.IsChecked == true;
        settings.RecentQuizzesToExclude = Math.Max(1, ParseInt(RecentQuizCountText.Text));
        var selectedGenres = _genres.Where(item => item.IsChecked).Select(item => item.Value).ToList();
        var selectedDecades = GetSelectedYears();
        var config = new QuizConfig
        {
            SourceType = IsSpotifySource ? "Spotify" : "Local",
            SourceDirectory = IsSpotifySource
                ? spotifyPlaylistId!
                : _musicDirectories.FirstOrDefault() ?? string.Empty,
            LocalMusicDirectories = IsSpotifySource ? null : [.. _musicDirectories],
            LocalFolderRules = IsSpotifySource ? null : GetFolderRules(),
            SpotifyPlaylistId = IsSpotifySource ? spotifyPlaylistId! : string.Empty,
            FilterGenre = !IsSpotifySource && selectedGenres.Count > 0
                ? string.Join(", ", selectedGenres)
                : null,
            FilterDecade = selectedDecades.Count == 0 ? null : string.Join(", ", selectedDecades),
            MinSongDuration = minLength > 0 ? TimeSpan.FromSeconds(minLength) : null,
            MaxSongDuration = maxLength > 0 ? TimeSpan.FromSeconds(maxLength) : null,
            SongCount = selected.Length,
            UniqueArtistsOnly = UniqueArtistsCheck.IsChecked == true,
            ExcludeRecentQuizSongs = ExcludeRecentCheck.IsChecked == true,
            RecentQuizzesToExclude = Math.Max(1, ParseInt(RecentQuizCountText.Text)),
            TimerMode = TimerModeCombo.SelectedIndex == 1 ? TimerMode.TotalQuiz : TimerMode.PerSong,
            TotalTime = TimeSpan.FromSeconds(seconds * selected.Length),
            TimePerSong = TimeSpan.FromSeconds(seconds),
            ClipStartMode = RandomClipCheck.IsChecked == true ? ClipStartMode.Random : ClipStartMode.Fixed,
            ClipStartTime = TimeSpan.FromSeconds(clipStart),
            ClipDuration = TimeSpan.FromSeconds(clipDuration),
            ClipPlaybackMode = PlaybackCombo.SelectedIndex switch
            {
                1 => ClipPlaybackMode.PlayOnceStop,
                2 => ClipPlaybackMode.PlayOnceAutoAdvance,
                _ => ClipPlaybackMode.Loop
            },
            GridColumns = GridColumnsCombo.SelectedIndex switch
            {
                1 => GridColumns.One, 2 => GridColumns.Two, 3 => GridColumns.Three, 4 => GridColumns.Four, _ => GridColumns.Auto
            },
            QuizMode = quizMode,
            SongTitlePoints = songTitlePoints,
            ArtistPoints = artistPoints,
            QuizTitle = IsSpotifySource
                ? $"{selectedSpotifyPlaylist!.Name} Quiz"
                : QuizNameGenerator.Generate(selected),
            FuzzyArtistMatching = FuzzyArtistCheck.IsChecked == true,
            FuzzySongTitleMatching = FuzzyTitleCheck.IsChecked == true,
            IgnorePunctuation = IgnorePunctuationCheck.IsChecked == true,
            SkipOnCorrectGuess = SkipCorrectCheck.IsChecked == true,
            NearMissFeedbackEnabled = NearMissFeedbackCheck.IsChecked == true,
            QuickGuessBonusEnabled = QuickGuessBonusCheck.IsChecked == true,
            QuickGuessBonusSeconds = quickGuessSeconds,
            QuickGuessBonusPoints = quickGuessPoints,
            StarPowerEnabled = StarPowerCheck.IsChecked == true,
            StarPowerStreakRequired = starPowerStreak,
            StarPowerBonusPoints = starPowerPoints,
            MulliganCount = reserve.Length,
            ExtenderCount = ParseInt(ExtenderCountText.Text),
            ClipRerollCount = ParseInt(ClipRerollCountText.Text),
            HintCount = ParseInt(HintCountText.Text),
            EnabledHintTypes = HintTypesPanel.Children.OfType<CheckBox>()
                .Select((check, index) => new { IsChecked = check.IsChecked == true, HintType = HintTypes[index] })
                .Where(x => x.IsChecked && (!IsSpotifySource || x.HintType != HintType.Genre))
                .Select(x => x.HintType)
                .ToList(),
            ScrambleHintsPerWord = ScrambleHintsPerWordCheck.IsChecked == true,
            MulliganPenalty = mulliganPenalty,
            ExtenderPenalty = extenderPenalty,
            ClipRerollPenalty = clipRerollPenalty,
            HintPenalty = hintPenalty,
            MaxPowerPenaltyPerSong = maxPenalty
        };
        var savedConfig = config.Clone();
        savedConfig.SongCount = requestedCount;
        savedConfig.MulliganCount = ParseInt(MulliganCountText.Text);
        savedConfig.ClipStartTime = TimeSpan.FromSeconds(ParseInt(ClipStartText.Text));
        if (TryPointValue(SongTitlePointsText.Text, out var savedTitlePoints))
            savedConfig.SongTitlePoints = savedTitlePoints;
        if (TryPointValue(ArtistPointsText.Text, out var savedArtistPoints))
            savedConfig.ArtistPoints = savedArtistPoints;
        savedConfig.EnabledHintTypes = HintTypesPanel.Children.OfType<CheckBox>()
            .Select((check, index) => new { IsChecked = check.IsChecked == true, HintType = HintTypes[index] })
            .Where(item => item.IsChecked).Select(item => item.HintType).ToList();
        settings.SetLastQuizSetup(new LocalQuizPreset
        {
            Name = PresetNameText.Text,
            Config = savedConfig,
            Genres = selectedGenres,
            Decades = selectedDecades,
            UniqueArtistsOnly = config.UniqueArtistsOnly,
            FuzzySongTitleMatching = config.FuzzySongTitleMatching,
            ExcludeRecentQuizSongs = config.ExcludeRecentQuizSongs,
            RecentQuizzesToExclude = config.RecentQuizzesToExclude,
            SpotifyPlaylistId = config.SpotifyPlaylistId,
            SpotifyPlaylistName = selectedSpotifyPlaylist?.Name ?? string.Empty
        });
        try { settings.Save(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save quiz preferences:\n{ex.Message}", "Settings save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        QuizRequested?.Invoke(config, selected, reserve);
    }

    private void OnSavePresetClick(object sender, RoutedEventArgs e)
    {
        if (!IsSpotifySource && (_folderSelectionDirty || _folderNodes.Count == 0))
        {
            SourceTab.IsSelected = true;
            MessageBox.Show("Scan the selected folders before saving this setup.",
                "Scan required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var name = PresetNameText.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter a name for this setup.", "Preset name required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryGetQuizPointValues(out var quizMode, out var songTitlePoints, out var artistPoints))
            return;
        if (!TryGetQuickGuessSettings(out var quickGuessSeconds, out var quickGuessPoints))
            return;
        if (!TryGetStarPowerSettings(out var starPowerStreak, out var starPowerPoints))
            return;

        var settings = AppSettings.Load();
        settings.LocalQuizPresets.RemoveAll(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
        TryInt(SecondsText.Text, 30, 5, 600, out var seconds);
        TryInt(ClipDurationText.Text, 15, 3, 120, out var clipDuration);
        TryInt(SongCountText.Text, 20, 1, 1000, out var songCount);
        TryInt(MinLengthText.Text, 0, 0, 86400, out var minLength);
        TryInt(MaxLengthText.Text, 0, 0, 86400, out var maxLength);
        var config = new QuizConfig
        {
            SourceType = IsSpotifySource ? "Spotify" : "Local",
            SourceDirectory = IsSpotifySource ? _selectedSpotifyPlaylist?.Id ?? string.Empty : _musicDirectories.FirstOrDefault() ?? string.Empty,
            LocalMusicDirectories = IsSpotifySource ? null : [.. _musicDirectories],
            LocalFolderRules = IsSpotifySource ? null : GetFolderRules(),
            SpotifyPlaylistId = IsSpotifySource ? _selectedSpotifyPlaylist?.Id ?? string.Empty : string.Empty,
            FilterGenre = !IsSpotifySource && _genres.Any(item => item.IsChecked)
                ? string.Join(", ", _genres.Where(item => item.IsChecked).Select(item => item.Value))
                : null,
            FilterDecade = GetSelectedYears() is { Count: > 0 } years ? string.Join(", ", years) : null,
            SongCount = songCount,
            UniqueArtistsOnly = UniqueArtistsCheck.IsChecked == true,
            ExcludeRecentQuizSongs = ExcludeRecentCheck.IsChecked == true,
            RecentQuizzesToExclude = Math.Max(1, ParseInt(RecentQuizCountText.Text)),
            TimePerSong = TimeSpan.FromSeconds(seconds),
            TotalTime = TimeSpan.FromSeconds(seconds * songCount),
            TimerMode = TimerModeCombo.SelectedIndex == 1 ? TimerMode.TotalQuiz : TimerMode.PerSong,
            ClipStartMode = RandomClipCheck.IsChecked == true ? ClipStartMode.Random : ClipStartMode.Fixed,
            ClipStartTime = TimeSpan.FromSeconds(ParseInt(ClipStartText.Text)),
            ClipDuration = TimeSpan.FromSeconds(clipDuration),
            MinSongDuration = minLength > 0 ? TimeSpan.FromSeconds(minLength) : null,
            MaxSongDuration = maxLength > 0 ? TimeSpan.FromSeconds(maxLength) : null,
            ClipPlaybackMode = PlaybackCombo.SelectedIndex switch { 1 => ClipPlaybackMode.PlayOnceStop, 2 => ClipPlaybackMode.PlayOnceAutoAdvance, _ => ClipPlaybackMode.Loop },
            QuizMode = quizMode,
            SongTitlePoints = songTitlePoints,
            ArtistPoints = artistPoints,
            GridColumns = GridColumnsCombo.SelectedIndex switch { 1 => GridColumns.One, 2 => GridColumns.Two, 3 => GridColumns.Three, 4 => GridColumns.Four, _ => GridColumns.Auto },
            FuzzyArtistMatching = FuzzyArtistCheck.IsChecked == true,
            FuzzySongTitleMatching = FuzzyTitleCheck.IsChecked == true,
            IgnorePunctuation = IgnorePunctuationCheck.IsChecked == true,
            SkipOnCorrectGuess = SkipCorrectCheck.IsChecked == true,
            NearMissFeedbackEnabled = NearMissFeedbackCheck.IsChecked == true,
            QuickGuessBonusEnabled = QuickGuessBonusCheck.IsChecked == true,
            QuickGuessBonusSeconds = quickGuessSeconds,
            QuickGuessBonusPoints = quickGuessPoints,
            StarPowerEnabled = StarPowerCheck.IsChecked == true,
            StarPowerStreakRequired = starPowerStreak,
            StarPowerBonusPoints = starPowerPoints,
            MulliganCount = ParseInt(MulliganCountText.Text),
            ExtenderCount = ParseInt(ExtenderCountText.Text),
            ClipRerollCount = ParseInt(ClipRerollCountText.Text),
            HintCount = ParseInt(HintCountText.Text),
            EnabledHintTypes = HintTypesPanel.Children.OfType<CheckBox>()
                .Select((check, index) => new { IsChecked = check.IsChecked == true, HintType = HintTypes[index] })
                .Where(x => x.IsChecked)
                .Select(x => x.HintType)
                .ToList(),
            ScrambleHintsPerWord = ScrambleHintsPerWordCheck.IsChecked == true,
            MulliganPenalty = ParseDouble(MulliganPenaltyText.Text),
            ExtenderPenalty = ParseDouble(ExtenderPenaltyText.Text),
            ClipRerollPenalty = ParseDouble(ClipRerollPenaltyText.Text),
            HintPenalty = ParseDouble(HintPenaltyText.Text),
            MaxPowerPenaltyPerSong = ParseDouble(MaxPenaltyText.Text)
        };
        settings.LocalQuizPresets.Add(new LocalQuizPreset
        {
            Name = name,
            Config = config,
            Genres = IsSpotifySource ? [] : _genres.Where(item => item.IsChecked).Select(item => item.Value).ToList(),
            Decades = GetSelectedYears(),
            UniqueArtistsOnly = UniqueArtistsCheck.IsChecked == true,
            FuzzySongTitleMatching = FuzzyTitleCheck.IsChecked == true,
            ExcludeRecentQuizSongs = ExcludeRecentCheck.IsChecked == true,
            RecentQuizzesToExclude = Math.Max(1, ParseInt(RecentQuizCountText.Text)),
            SpotifyPlaylistId = IsSpotifySource ? _selectedSpotifyPlaylist?.Id ?? string.Empty : string.Empty,
            SpotifyPlaylistName = IsSpotifySource ? _selectedSpotifyPlaylist?.Name ?? string.Empty : string.Empty
        });
        try
        {
            settings.Save();
            var wasInitializing = _initializing;
            _initializing = true;
            PresetCombo.ItemsSource = settings.LocalQuizPresets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            PresetCombo.SelectedItem = PresetCombo.Items.Cast<LocalQuizPreset>().First(preset => preset.Name == name);
            _initializing = wasInitializing;
            UpdatePresetSaveButton();
        }
        catch (Exception ex)
        {
            _initializing = false;
            MessageBox.Show($"Could not save this preset:\n{ex.Message}", "Preset save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnPresetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;

        if (PresetCombo.SelectedItem is not LocalQuizPreset preset)
        {
            PresetNameText.Clear();
            UpdatePresetSaveButton();
            return;
        }

        PresetNameText.Text = preset.Name;
        await RestoreSetupAsync(preset, restoringLastSetup: false);
    }

    private async Task RestoreSetupAsync(LocalQuizPreset preset, bool restoringLastSetup)
    {
        if (restoringLastSetup)
        {
            _initializing = true;
            PresetCombo.SelectedItem = null;
            PresetNameText.Text = preset.Name;
            _initializing = false;
            UpdatePresetSaveButton();
        }
        if (string.Equals(preset.Config.SourceType, "Spotify", StringComparison.OrdinalIgnoreCase))
        {
            if (!_spotifyEnabled)
            {
                if (!restoringLastSetup)
                {
                    MessageBox.Show("Enable Spotify integration in Settings before using this saved setup.",
                        "Spotify is disabled", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                _initializing = true;
                SourceCombo.SelectedIndex = 0;
                UpdateSourcePanels();
                _initializing = false;
                if (_musicDirectories.Count > 0)
                    await ScanAsync();
                ApplyPreset(preset);
                ScanStatus.Text = "The last quiz used Spotify. Enable Spotify in Settings to use that source.";
                if (ExcludeRecentCheck.IsChecked == true)
                    await RefreshRecentSongsAsync();
                UpdateMatchCount();
                return;
            }

            _initializing = true;
            SourceCombo.SelectedIndex = 1;
            UpdateSourcePanels();
            _selectedSpotifyPlaylist = null;
            _initializing = false;
            await LoadSpotifyPlaylistsAsync();

            var playlistId = string.IsNullOrWhiteSpace(preset.SpotifyPlaylistId)
                ? preset.Config.SpotifyPlaylistId
                : preset.SpotifyPlaylistId;
            var playlist = _spotifyPlaylists.FirstOrDefault(item => item.Playlist.Id == playlistId);
            if (playlist is null)
            {
                if (restoringLastSetup)
                    ApplyPreset(preset);
                MessageBox.Show("The playlist saved in this setup is not available in the connected Spotify account.",
                    "Playlist unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _loadingSpotifyPlaylists = true;
            SpotifyPlaylistCombo.SelectedItem = playlist;
            _loadingSpotifyPlaylists = false;
            await LoadSpotifyPlaylistAsync(playlist);
            ApplyPreset(preset);
        }
        else
        {
            _initializing = true;
            SourceCombo.SelectedIndex = 0;
            UpdateSourcePanels();
            _initializing = false;
            if (!restoringLastSetup && !new HashSet<string>(MusicFolderSelection.Roots(preset.Config), StringComparer.OrdinalIgnoreCase)
                .SetEquals(_musicDirectories))
            {
                MessageBox.Show("This setup was saved with different library folders. Its folder selections will be applied to the folders currently configured in Settings.",
                    "Library folders changed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            if (_musicDirectories.Count > 0 || !restoringLastSetup)
            {
                if (!await ScanAsync(preset.Config.LocalFolderRules ?? []))
                    return;
            }
            ApplyPreset(preset);
        }

        if (ExcludeRecentCheck.IsChecked == true)
            await RefreshRecentSongsAsync();
        UpdateMatchCount();
    }

    private void OnPresetNameChanged(object sender, TextChangedEventArgs e)
        => UpdatePresetSaveButton();

    private void UpdatePresetSaveButton()
    {
        var updatesExistingPreset = PresetCombo.Items.Cast<LocalQuizPreset>().Any(preset =>
            string.Equals(PresetNameText.Text.Trim(), preset.Name, StringComparison.OrdinalIgnoreCase));
        SavePresetButton.Content = updatesExistingPreset ? "Update preset" : "Save as new";
    }

    private void ApplyPreset(LocalQuizPreset preset)
    {
        _initializing = true;
        var config = preset.Config;
        SongCountText.Text = config.SongCount.ToString();
        SecondsText.Text = ((int)config.TimePerSong.TotalSeconds).ToString();
        ClipDurationText.Text = ((int)config.ClipDuration.TotalSeconds).ToString();
        ClipStartText.Text = ((int)config.ClipStartTime.TotalSeconds).ToString();
        MinLengthText.Text = ((int)(config.MinSongDuration?.TotalSeconds ?? 0)).ToString();
        MaxLengthText.Text = ((int)(config.MaxSongDuration?.TotalSeconds ?? 0)).ToString();
        TimerModeCombo.SelectedIndex = config.TimerMode == TimerMode.TotalQuiz ? 1 : 0;
        GridColumnsCombo.SelectedIndex = config.GridColumns switch { GridColumns.One => 1, GridColumns.Two => 2, GridColumns.Three => 3, GridColumns.Four => 4, _ => 0 };
        PlaybackCombo.SelectedIndex = config.ClipPlaybackMode switch { ClipPlaybackMode.PlayOnceStop => 1, ClipPlaybackMode.PlayOnceAutoAdvance => 2, _ => 0 };
        QuizModeCombo.SelectedIndex = config.QuizMode switch { QuizMode.GuessArtist => 1, QuizMode.GuessBoth => 2, _ => 0 };
        SongTitlePointsText.Text = (config.SongTitlePoints ?? (config.QuizMode == QuizMode.GuessBoth ? 1.5 : 2.0)).ToString();
        ArtistPointsText.Text = (config.ArtistPoints ?? (config.QuizMode == QuizMode.GuessBoth ? 0.5 : 2.0)).ToString();
        SongTitlePointsPanel.Visibility = config.QuizMode == QuizMode.GuessArtist
            ? Visibility.Collapsed
            : Visibility.Visible;
        ArtistPointsPanel.Visibility = config.QuizMode == QuizMode.GuessSongTitle
            ? Visibility.Collapsed
            : Visibility.Visible;
        RandomClipCheck.IsChecked = config.ClipStartMode == ClipStartMode.Random;
        FuzzyArtistCheck.IsChecked = config.FuzzyArtistMatching;
        FuzzyTitleCheck.IsChecked = preset.FuzzySongTitleMatching;
        IgnorePunctuationCheck.IsChecked = config.IgnorePunctuation;
        SkipCorrectCheck.IsChecked = config.SkipOnCorrectGuess;
        NearMissFeedbackCheck.IsChecked = config.NearMissFeedbackEnabled;
        QuickGuessBonusCheck.IsChecked = config.QuickGuessBonusEnabled;
        QuickGuessSecondsText.Text = config.QuickGuessBonusSeconds.ToString();
        QuickGuessPointsText.Text = config.QuickGuessBonusPoints.ToString();
        StarPowerCheck.IsChecked = config.StarPowerEnabled;
        StarPowerStreakText.Text = config.StarPowerStreakRequired.ToString();
        StarPowerPointsText.Text = config.StarPowerBonusPoints.ToString();
        MulliganCountText.Text = config.MulliganCount.ToString();
        ExtenderCountText.Text = config.ExtenderCount.ToString();
        ClipRerollCountText.Text = config.ClipRerollCount.ToString();
        HintCountText.Text = config.HintCount.ToString();
        MulliganPenaltyText.Text = config.MulliganPenalty.ToString();
        ExtenderPenaltyText.Text = config.ExtenderPenalty.ToString();
        ClipRerollPenaltyText.Text = config.ClipRerollPenalty.ToString();
        HintPenaltyText.Text = config.HintPenalty.ToString();
        MaxPenaltyText.Text = config.MaxPowerPenaltyPerSong.ToString();
        UniqueArtistsCheck.IsChecked = preset.UniqueArtistsOnly;
        ExcludeRecentCheck.IsChecked = preset.ExcludeRecentQuizSongs;
        RecentQuizCountText.Text = preset.RecentQuizzesToExclude.ToString();
        UpdateRecentCountInput();
        PopulateGenreFilters(preset.Genres);
        PopulateYearFilters(preset.Decades);
        var enabledHints = config.EnabledHintTypes ?? HintTypes.ToList();
        for (var index = 0; index < HintTypesPanel.Children.Count; index++)
            ((CheckBox)HintTypesPanel.Children[index]).IsChecked = enabledHints.Contains(HintTypes[index]);
        ScrambleHintsPerWordCheck.IsChecked = config.ScrambleHintsPerWord;
        _initializing = false;
        UpdateMatchCount();
    }

    private void OnDeletePresetClick(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not LocalQuizPreset preset)
            return;
        var settings = AppSettings.Load();
        settings.LocalQuizPresets.RemoveAll(item => item.Name == preset.Name);
        try
        {
            settings.Save();
            PresetCombo.ItemsSource = settings.LocalQuizPresets.OrderBy(item => item.Name).ToArray();
            PresetCombo.SelectedItem = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete the preset:\n{ex.Message}", "Preset delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool TryGetStarPowerSettings(out int streak, out double points)
    {
        var validStreak = TryInt(StarPowerStreakText.Text, 5, 1, 1000, out streak);
        var validPoints = TryPointValue(StarPowerPointsText.Text, out points);
        if (validStreak && validPoints)
            return true;
        PowersTab.IsSelected = true;
        MessageBox.Show("Star power requires a streak of 1–1000 songs and a nonnegative bonus.",
            "Invalid star-power settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private bool TryGetQuickGuessSettings(out int seconds, out double points)
    {
        var validSeconds = TryInt(QuickGuessSecondsText.Text, 10, 1, 3600, out seconds);
        var validPoints = TryPointValue(QuickGuessPointsText.Text, out points);
        if (validSeconds && validPoints)
            return true;
        PowersTab.IsSelected = true;
        MessageBox.Show("Quick-guess cutoff must be 1–3600 seconds and bonus points must be a nonnegative number.",
            "Invalid quick-guess settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private static bool TryInt(string text, int fallback, int min, int max, out int value)
        => int.TryParse(text, out value) && value >= min && value <= max;
    private static bool TryPointValue(string text, out double value)
        => double.TryParse(text, out value) && double.IsFinite(value) && value >= 0;
    private static int ParseInt(string text) => int.TryParse(text, out var value) ? Math.Max(0, value) : 0;
    private static bool TryDouble(string text, out double value) => double.TryParse(text, out value) && value >= 0;
    private static double ParseDouble(string text) => double.TryParse(text, out var value) ? Math.Max(0, value) : 0;
    private static string HintLabel(HintType type) => type switch
    {
        HintType.TitleInitial => "Title first letter",
        HintType.ArtistInitial => "Artist first letter",
        HintType.ScrambledTitle => "Scrambled title",
        HintType.ScrambledArtist => "Scrambled artist",
        HintType.AlbumCoverArt => "Album cover art",
        _ => type.ToString()
    };

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SourceCombo.IsEnabled = !busy;
        MusicFolderTree.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy;
        SpotifyPlaylistCombo.IsEnabled = !busy;
        RefreshSpotifyPlaylistsButton.IsEnabled = !busy;
        CheckSpotifyButton.IsEnabled = !busy;
        PresetCombo.IsEnabled = !busy;
        GenreList.IsEnabled = !busy;
        DecadeList.IsEnabled = !busy;
        UniqueArtistsCheck.IsEnabled = !busy;
        ExcludeRecentCheck.IsEnabled = !busy;
        ViewSongsButton.IsEnabled = !busy;
        UpdateStartQuizAvailability();
    }

    private void UpdateStartQuizAvailability()
        => StartQuizButton.IsEnabled = !_busy && !IsSelectedSpotifyPlaylistDenied;
}
