using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Settings;
using SpotifyAPI.Web;

namespace TrackAttack.Views;

public partial class QuizSetupView : UserControl
{
    private static readonly HintType[] HintTypes = Enum.GetValues<HintType>();
    private readonly IMusicSource _musicSource;
    private readonly IQuizHistoryRepository _history;
    private bool _spotifyEnabled;
    private readonly ObservableCollection<FilterChoice> _genres = [];
    private readonly ObservableCollection<FilterChoice> _decades = [];
    private readonly HashSet<string> _recentQuizSongPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _recentQuizSongIdentities = new(StringComparer.OrdinalIgnoreCase);
    private List<Song> _scannedSongs = [];
    private bool _busy;
    private bool _initializing;
    private bool _recentHistoryLoaded = true;
    private bool _showSongCounts = true;
    private bool _loadingSpotifyPlaylists;
    private SpotifyMusicSource? _spotifySource;
    private FullPlaylist? _selectedSpotifyPlaylist;
    private List<FullPlaylist> _spotifyPlaylists = [];

    private bool IsSpotifySource => _spotifyEnabled && SourceCombo.SelectedIndex == 1;

    public event Action<QuizConfig, IReadOnlyList<Song>, IReadOnlyList<Song>>? QuizRequested;
    public event Action<IReadOnlyList<Song>>? SongsRequested;

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

        var settings = AppSettings.Load();
        FolderText.Text = settings.MusicDirectory;
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

        Loaded += async (_, _) =>
        {
            if (IsSpotifySource)
                await LoadSpotifyPlaylistsAsync();
            else if (Directory.Exists(FolderText.Text))
                await ScanAsync(FolderText.Text);
        };
    }

    private void UpdateSourcePanels()
    {
        var spotify = IsSpotifySource;
        LocalSourcePanel.Visibility = spotify ? Visibility.Collapsed : Visibility.Visible;
        SpotifySourcePanel.Visibility = spotify ? Visibility.Visible : Visibility.Collapsed;
        GenreFilterPanel.Visibility = spotify ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSectionNavigationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string targetName }
            && FindName(targetName) is FrameworkElement target)
        {
            var targetTop = target.TransformToAncestor(SetupScrollViewer)
                .Transform(new Point(0, 0)).Y;
            SetupScrollViewer.ScrollToVerticalOffset(
                SetupScrollViewer.VerticalOffset + targetTop - 8);
        }
    }

    private void OnRandomClipCheckChanged(object sender, RoutedEventArgs e)
        => UpdateClipStartInput();

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

    public async Task RefreshProfileSettingsAsync()
    {
        var settings = AppSettings.Load();
        var previousFolder = FolderText.Text.Trim();
        _initializing = true;
        FolderText.Text = settings.MusicDirectory;
        SecondsText.Text = settings.DefaultSecondsPerSong.ToString();
        ClipDurationText.Text = settings.DefaultClipDurationSeconds.ToString();
        FuzzyArtistCheck.IsChecked = settings.FuzzyArtistMatching;
        FuzzyTitleCheck.IsChecked = settings.FuzzySongTitleMatching;
        IgnorePunctuationCheck.IsChecked = settings.IgnorePunctuation;
        SkipCorrectCheck.IsChecked = settings.SkipOnCorrectGuess;
        ExcludeRecentCheck.IsChecked = settings.ExcludeRecentQuizSongs;
        RecentQuizCountText.Text = settings.RecentQuizzesToExclude.ToString();
        UpdateRecentCountInput();
        _initializing = false;

        if (!IsSpotifySource && !string.Equals(previousFolder, FolderText.Text.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(FolderText.Text.Trim()))
                await ScanAsync(FolderText.Text.Trim());
            else
            {
                _scannedSongs.Clear();
                _genres.Clear();
                _decades.Clear();
                ScanStatus.Text = "Select an existing music folder.";
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
        _selectedSpotifyPlaylist = null;
        _spotifyPlaylists.Clear();
        SpotifyPlaylistCombo.ItemsSource = null;
        _scannedSongs.Clear();
        _recentQuizSongPaths.Clear();
        _recentQuizSongIdentities.Clear();
        if (Directory.Exists(FolderText.Text.Trim()))
            await ScanAsync(FolderText.Text.Trim());
        else
            UpdateMatchCount();
    }

    private async void OnSourceTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;

        UpdateSourcePanels();
        _scannedSongs.Clear();
        _selectedSpotifyPlaylist = null;
        _recentQuizSongPaths.Clear();
        _recentQuizSongIdentities.Clear();
        if (IsSpotifySource)
            await LoadSpotifyPlaylistsAsync();
        else if (Directory.Exists(FolderText.Text.Trim()))
            await ScanAsync(FolderText.Text.Trim());
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

    private async Task LoadSpotifyPlaylistsAsync()
    {
        if (_loadingSpotifyPlaylists || !IsSpotifySource)
            return;
        _loadingSpotifyPlaylists = true;
        SetBusy(true);
        SpotifyStatus.Text = "Loading Spotify playlists…";
        try
        {
            if (!await EnsureSpotifyConnectedAsync())
            {
                _spotifySource = null;
                _spotifyPlaylists.Clear();
                SpotifyPlaylistCombo.ItemsSource = null;
                _selectedSpotifyPlaylist = null;
                _scannedSongs.Clear();
                UpdateMatchCount();
                return;
            }
            _spotifySource = new SpotifyMusicSource(SpotifyService.Client!);
            var previousId = _selectedSpotifyPlaylist?.Id;
            _spotifyPlaylists = (await _spotifySource.GetUserPlaylistsAsync()).ToList();
            SpotifyPlaylistCombo.ItemsSource = _spotifyPlaylists;
            _selectedSpotifyPlaylist = null;
            if (!string.IsNullOrWhiteSpace(previousId))
                SpotifyPlaylistCombo.SelectedItem = _spotifyPlaylists.FirstOrDefault(p => p.Id == previousId);
            SpotifyStatus.Text = _spotifyPlaylists.Count == 0
                ? "No playlists were found for this account."
                : $"{_spotifyPlaylists.Count:N0} playlists available. Select one to load its tracks.";
            if (SpotifyPlaylistCombo.SelectedItem is FullPlaylist playlist)
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
        if (_loadingSpotifyPlaylists || SpotifyPlaylistCombo.SelectedItem is not FullPlaylist playlist)
            return;
        await LoadSpotifyPlaylistAsync(playlist);
    }

    private async Task LoadSpotifyPlaylistAsync(FullPlaylist playlist)
    {
        if (_spotifySource is null)
            return;
        if (string.IsNullOrWhiteSpace(playlist.Id))
        {
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
            SpotifyStatus.Text = "Could not load playlist tracks.";
            MessageBox.Show($"Could not load tracks from “{playlist.Name}”:\n{exception.Message}",
                "Spotify playlist load failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select your music library", FolderName = FolderText.Text };
        if (dialog.ShowDialog() == true)
            FolderText.Text = dialog.FolderName;
    }

    private async void OnScanClick(object sender, RoutedEventArgs e)
        => await ScanAsync(FolderText.Text.Trim());

    private async Task<bool> ScanAsync(string folder)
    {
        if (_busy)
            return false;
        if (!Directory.Exists(folder))
        {
            MessageBox.Show("Select an existing music folder.", "Folder required", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        SetBusy(true);
        ScanStatus.Text = "Scanning local audio files…";
        try
        {
            _scannedSongs = await Task.Run(() => _musicSource is LocalFileMusicSource local
                ? local.ScanDirectory(folder).ToList()
                : new List<Song>());
            PopulateFilters();
            if (ExcludeRecentCheck.IsChecked == true)
                await RefreshRecentSongsAsync();
            UpdateMatchCount();
            ScanStatus.Text = $"Scan complete: {_scannedSongs.Count:N0} readable tracks.";
            return true;
        }
        catch (Exception ex)
        {
            ScanStatus.Text = "Scan failed.";
            MessageBox.Show($"Could not scan this music folder:\n{ex.Message}", "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally { SetBusy(false); }
    }

    private void PopulateFilters()
    {
        var oldGenres = _genres.Where(item => item.IsChecked).Select(item => item.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var oldDecades = _decades.Where(item => item.IsChecked).Select(item => item.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _genres.Clear();
        _decades.Clear();
        if (!IsSpotifySource)
        {
            foreach (var (value, count) in _scannedSongs
                         .GroupBy(song => string.IsNullOrWhiteSpace(song.Genre) ? LocalFileMusicSource.UnknownMetadataOption : song.Genre.Trim(),
                             StringComparer.OrdinalIgnoreCase)
                         .Select(group => (group.Key, group.Count()))
                         .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                _genres.Add(new FilterChoice(value, count, _showSongCounts) { IsChecked = oldGenres.Contains(value) });
        }
        foreach (var (value, count) in _scannedSongs
                     .GroupBy(song => song.Year > 0 ? $"{song.Year / 10 * 10}s" : LocalFileMusicSource.UnknownMetadataOption,
                         StringComparer.OrdinalIgnoreCase)
                     .Select(group => (group.Key, group.Count()))
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            _decades.Add(new FilterChoice(value, count, _showSongCounts) { IsChecked = oldDecades.Contains(value) });
    }

    private List<Song> GetMatchingSongs()
    {
        var selectedGenres = IsSpotifySource
            ? []
            : _genres.Where(item => item.IsChecked).Select(item => item.Value).ToArray();
        var selectedDecades = _decades.Where(item => item.IsChecked).Select(item => item.Value).ToArray();
        IEnumerable<Song> songs = _scannedSongs;
        if (selectedGenres.Length > 0)
            songs = songs.Where(song => selectedGenres.Any(value => value == LocalFileMusicSource.UnknownMetadataOption
                ? string.IsNullOrWhiteSpace(song.Genre) : string.Equals(value, song.Genre, StringComparison.OrdinalIgnoreCase)));
        if (selectedDecades.Length > 0)
            songs = songs.Where(song => selectedDecades.Any(value => value == LocalFileMusicSource.UnknownMetadataOption
                ? song.Year <= 0 : int.TryParse(value.TrimEnd('s'), out var decade) && song.Year >= decade && song.Year < decade + 10));
        if (TryInt(MinLengthText.Text, 0, 0, 86400, out var minimumSeconds) && minimumSeconds > 0)
            songs = songs.Where(song => song.Duration.TotalSeconds >= minimumSeconds);
        if (TryInt(MaxLengthText.Text, 0, 0, 86400, out var maximumSeconds) && maximumSeconds > 0)
            songs = songs.Where(song => song.Duration.TotalSeconds <= maximumSeconds);
        if (ExcludeRecentCheck.IsChecked == true)
            songs = _recentHistoryLoaded
                ? songs.Where(song => !_recentQuizSongPaths.Contains(song.SourcePath)
                    && !_recentQuizSongIdentities.Contains(GetSongIdentity(song)))
                : [];
        if (UniqueArtistsCheck.IsChecked == true)
        {
            var randomizedSongs = songs.ToArray();
            Random.Shared.Shuffle(randomizedSongs);
            songs = randomizedSongs.DistinctBy(song => song.Artist, StringComparer.OrdinalIgnoreCase);
        }
        return songs.ToList();
    }

    private void OnFilterCheckedChanged(object sender, RoutedEventArgs e) => UpdateMatchCount();
    private void OnSelectAllGenresClick(object sender, RoutedEventArgs e) => SetFilterChoices(_genres, true);
    private void OnSelectNoGenresClick(object sender, RoutedEventArgs e) => SetFilterChoices(_genres, false);
    private void OnSelectAllDecadesClick(object sender, RoutedEventArgs e) => SetFilterChoices(_decades, true);
    private void OnSelectNoDecadesClick(object sender, RoutedEventArgs e) => SetFilterChoices(_decades, false);
    private void OnSelectAllHintsClick(object sender, RoutedEventArgs e) => SetHintTypes(true);
    private void OnSelectNoHintsClick(object sender, RoutedEventArgs e) => SetHintTypes(false);

    private void SetFilterChoices(IEnumerable<FilterChoice> choices, bool isChecked)
    {
        foreach (var choice in choices)
            choice.IsChecked = isChecked;
        UpdateMatchCount();
    }

    private void SetHintTypes(bool isChecked)
    {
        foreach (var checkBox in HintTypesPanel.Children.OfType<CheckBox>())
            checkBox.IsChecked = isChecked;
    }

    private void OnFiltersChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing) UpdateMatchCount();
    }
    private void OnUniqueArtistsChanged(object sender, RoutedEventArgs e) => UpdateMatchCount();
    private void OnRecentCountChanged(object sender, TextChangedEventArgs e)
    {
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
        var settings = AppSettings.Load();
        settings.ExcludeRecentQuizSongs = ExcludeRecentCheck.IsChecked == true;
        settings.RecentQuizzesToExclude = Math.Max(1, ParseInt(RecentQuizCountText.Text));
        try { settings.Save(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save recent-quiz options:\n{ex.Message}", "Settings save failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
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
        _recentHistoryLoaded = false;
        _recentQuizSongPaths.Clear();
        _recentQuizSongIdentities.Clear();
        var count = Math.Max(1, ParseInt(RecentQuizCountText.Text));
        try
        {
            var sourceType = IsSpotifySource ? "Spotify" : "Local";
            var sourceDirectory = IsSpotifySource
                ? _selectedSpotifyPlaylist?.Id ?? string.Empty
                : FolderText.Text.Trim();
            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                _recentHistoryLoaded = true;
                return;
            }
            var profileName = AppSettings.Load().ActiveProfileName;
            var recent = await _history.GetRecentQuizSongsAsync(count, sourceType, sourceDirectory, profileName);
            foreach (var song in recent)
            {
                if (!string.IsNullOrWhiteSpace(song.SourcePath))
                    _recentQuizSongPaths.Add(song.SourcePath);
                var legacySpotifyPath = IsSpotifySource
                    && !song.SourcePath.StartsWith("spotify:track:", StringComparison.OrdinalIgnoreCase);
                if ((legacySpotifyPath || string.IsNullOrWhiteSpace(song.SourcePath))
                    && !string.IsNullOrWhiteSpace(song.Title)
                    && !string.IsNullOrWhiteSpace(song.Artist))
                    _recentQuizSongIdentities.Add(GetSongIdentity(song));
            }
            _recentHistoryLoaded = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load recently played songs:\n{ex.Message}", "History load failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateMatchCount()
    {
        if (_initializing)
            return;
        MatchCountText.Text = $"{GetMatchingSongs().Count:N0} matching tracks";
    }
    private void OnViewSongsClick(object sender, RoutedEventArgs e)
    {
        if (!_busy)
            SongsRequested?.Invoke(GetMatchingSongs());
    }

    private async void OnStartQuizClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        SetBusy(true);
        try
        {
            if (ExcludeRecentCheck.IsChecked == true)
                await RefreshRecentSongsAsync();
            StartQuiz();
        }
        finally { SetBusy(false); }
    }

    private void StartQuiz()
    {
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
            MessageBox.Show("No songs match the selected filters.", "No songs", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var clipStart = 0;
        if (!TryInt(SongCountText.Text, 20, 1, 1000, out var requestedCount)
            || !TryInt(SecondsText.Text, 30, 5, 600, out var seconds)
            || !TryInt(ClipDurationText.Text, 15, 3, 120, out var clipDuration)
            || (RandomClipCheck.IsChecked != true && !TryInt(ClipStartText.Text, 30, 0, 3600, out clipStart))
            || !TryInt(MinLengthText.Text, 0, 0, 86400, out var minLength)
            || !TryInt(MaxLengthText.Text, 0, 0, 86400, out var maxLength)
            || (minLength > 0 && maxLength > 0 && minLength > maxLength))
        {
            MessageBox.Show("Check the song count, timer, clip, and track-length settings.", "Invalid settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!TryDouble(MulliganPenaltyText.Text, out var mulliganPenalty)
            || !TryDouble(ExtenderPenaltyText.Text, out var extenderPenalty)
            || !TryDouble(HintPenaltyText.Text, out var hintPenalty)
            || !TryDouble(MaxPenaltyText.Text, out var maxPenalty))
        {
            MessageBox.Show("Power penalties must be zero or a positive number.", "Invalid penalty",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!TryGetQuizPointValues(out var quizMode, out var songTitlePoints, out var artistPoints))
            return;

        var count = Math.Min(requestedCount, candidates.Count);
        var shuffled = candidates.ToArray();
        Random.Shared.Shuffle(shuffled);
        var selected = shuffled.Take(count).ToArray();
        var reserveCount = Math.Min(ParseInt(MulliganCountText.Text), shuffled.Length - count);
        var reserve = shuffled.Skip(count).Take(reserveCount).ToArray();
        var settings = AppSettings.Load();
        if (!IsSpotifySource)
            settings.MusicDirectory = FolderText.Text.Trim();
        settings.DefaultSecondsPerSong = seconds;
        settings.DefaultClipDurationSeconds = clipDuration;
        settings.FuzzyArtistMatching = FuzzyArtistCheck.IsChecked == true;
        settings.FuzzySongTitleMatching = FuzzyTitleCheck.IsChecked == true;
        settings.IgnorePunctuation = IgnorePunctuationCheck.IsChecked == true;
        settings.SkipOnCorrectGuess = SkipCorrectCheck.IsChecked == true;
        settings.ExcludeRecentQuizSongs = ExcludeRecentCheck.IsChecked == true;
        settings.RecentQuizzesToExclude = Math.Max(1, ParseInt(RecentQuizCountText.Text));
        try { settings.Save(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save quiz preferences:\n{ex.Message}", "Settings save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        var selectedGenres = _genres.Where(item => item.IsChecked).Select(item => item.Value).ToList();
        var selectedDecades = _decades.Where(item => item.IsChecked).Select(item => item.Value).ToList();
        var config = new QuizConfig
        {
            SourceType = IsSpotifySource ? "Spotify" : "Local",
            SourceDirectory = IsSpotifySource
                ? spotifyPlaylistId!
                : FolderText.Text.Trim(),
            SpotifyPlaylistId = IsSpotifySource ? spotifyPlaylistId! : string.Empty,
            FilterGenre = !IsSpotifySource && selectedGenres.Count > 0
                ? string.Join(", ", selectedGenres)
                : null,
            FilterDecade = selectedDecades.Count == 0 ? null : string.Join(", ", selectedDecades),
            MinSongDuration = minLength > 0 ? TimeSpan.FromSeconds(minLength) : null,
            MaxSongDuration = maxLength > 0 ? TimeSpan.FromSeconds(maxLength) : null,
            SongCount = selected.Length,
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
            MulliganCount = reserve.Length,
            ExtenderCount = ParseInt(ExtenderCountText.Text),
            HintCount = ParseInt(HintCountText.Text),
            EnabledHintTypes = HintTypesPanel.Children.OfType<CheckBox>().Where(check => check.IsChecked == true)
                .Select((check, index) => HintTypes[index]).ToList(),
            ScrambleHintsPerWord = ScrambleHintsPerWordCheck.IsChecked == true,
            MulliganPenalty = mulliganPenalty,
            ExtenderPenalty = extenderPenalty,
            HintPenalty = hintPenalty,
            MaxPowerPenaltyPerSong = maxPenalty
        };
        QuizRequested?.Invoke(config, selected, reserve);
    }

    private static string GetSongIdentity(Song song)
        => $"{song.Title.Trim()}\0{song.Artist.Trim()}";

    private void OnSavePresetClick(object sender, RoutedEventArgs e)
    {
        var name = PresetNameText.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter a name for this setup.", "Preset name required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryGetQuizPointValues(out var quizMode, out var songTitlePoints, out var artistPoints))
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
            SourceDirectory = IsSpotifySource ? _selectedSpotifyPlaylist?.Id ?? string.Empty : FolderText.Text.Trim(),
            SpotifyPlaylistId = IsSpotifySource ? _selectedSpotifyPlaylist?.Id ?? string.Empty : string.Empty,
            FilterGenre = !IsSpotifySource && _genres.Any(item => item.IsChecked)
                ? string.Join(", ", _genres.Where(item => item.IsChecked).Select(item => item.Value))
                : null,
            FilterDecade = _decades.Any(item => item.IsChecked)
                ? string.Join(", ", _decades.Where(item => item.IsChecked).Select(item => item.Value))
                : null,
            SongCount = songCount,
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
            MulliganCount = ParseInt(MulliganCountText.Text),
            ExtenderCount = ParseInt(ExtenderCountText.Text),
            HintCount = ParseInt(HintCountText.Text),
            EnabledHintTypes = HintTypesPanel.Children.OfType<CheckBox>().Where(check => check.IsChecked == true)
                .Select((check, index) => HintTypes[index]).ToList(),
            ScrambleHintsPerWord = ScrambleHintsPerWordCheck.IsChecked == true,
            MulliganPenalty = ParseDouble(MulliganPenaltyText.Text),
            ExtenderPenalty = ParseDouble(ExtenderPenaltyText.Text),
            HintPenalty = ParseDouble(HintPenaltyText.Text),
            MaxPowerPenaltyPerSong = ParseDouble(MaxPenaltyText.Text)
        };
        settings.LocalQuizPresets.Add(new LocalQuizPreset
        {
            Name = name,
            Config = config,
            Genres = IsSpotifySource ? [] : _genres.Where(item => item.IsChecked).Select(item => item.Value).ToList(),
            Decades = _decades.Where(item => item.IsChecked).Select(item => item.Value).ToList(),
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

        if (string.Equals(preset.Config.SourceType, "Spotify", StringComparison.OrdinalIgnoreCase))
        {
            if (!_spotifyEnabled)
            {
                MessageBox.Show("Enable Spotify integration in Settings before using this saved setup.",
                    "Spotify is disabled", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var playlist = _spotifyPlaylists.FirstOrDefault(item => item.Id == playlistId);
            if (playlist is null)
            {
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
            var wasSpotify = IsSpotifySource;
            _initializing = true;
            SourceCombo.SelectedIndex = 0;
            UpdateSourcePanels();
            var folderChanged = !string.Equals(FolderText.Text, preset.Config.SourceDirectory, StringComparison.OrdinalIgnoreCase);
            FolderText.Text = preset.Config.SourceDirectory;
            _initializing = false;
            if ((wasSpotify || folderChanged || _scannedSongs.Count == 0)
                && !await ScanAsync(preset.Config.SourceDirectory))
                return;
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
        var updatesSelectedPreset = PresetCombo.SelectedItem is LocalQuizPreset preset
            && string.Equals(PresetNameText.Text.Trim(), preset.Name, StringComparison.OrdinalIgnoreCase);
        SavePresetButton.Content = updatesSelectedPreset ? "Update preset" : "Save as new";
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
        MulliganCountText.Text = config.MulliganCount.ToString();
        ExtenderCountText.Text = config.ExtenderCount.ToString();
        HintCountText.Text = config.HintCount.ToString();
        MulliganPenaltyText.Text = config.MulliganPenalty.ToString();
        ExtenderPenaltyText.Text = config.ExtenderPenalty.ToString();
        HintPenaltyText.Text = config.HintPenalty.ToString();
        MaxPenaltyText.Text = config.MaxPowerPenaltyPerSong.ToString();
        UniqueArtistsCheck.IsChecked = preset.UniqueArtistsOnly;
        ExcludeRecentCheck.IsChecked = preset.ExcludeRecentQuizSongs;
        RecentQuizCountText.Text = preset.RecentQuizzesToExclude.ToString();
        UpdateRecentCountInput();
        var genres = preset.Genres.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var decades = preset.Decades.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _genres) item.IsChecked = genres.Contains(item.Value);
        foreach (var item in _decades) item.IsChecked = decades.Contains(item.Value);
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
        BrowseLibraryButton.IsEnabled = !busy;
        FolderText.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy;
        SpotifyPlaylistCombo.IsEnabled = !busy;
        RefreshSpotifyPlaylistsButton.IsEnabled = !busy;
        PresetCombo.IsEnabled = !busy;
        GenreList.IsEnabled = !busy;
        DecadeList.IsEnabled = !busy;
        UniqueArtistsCheck.IsEnabled = !busy;
        ExcludeRecentCheck.IsEnabled = !busy;
        ViewSongsButton.IsEnabled = !busy;
        StartQuizButton.IsEnabled = !busy;
    }
}
