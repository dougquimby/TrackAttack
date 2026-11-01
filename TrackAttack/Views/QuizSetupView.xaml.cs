using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Settings;

namespace TrackAttack.Views;

public partial class QuizSetupView : UserControl
{
    private static readonly HintType[] HintTypes = Enum.GetValues<HintType>();
    private readonly IMusicSource _musicSource;
    private readonly IQuizHistoryRepository _history;
    private readonly ObservableCollection<FilterChoice> _genres = [];
    private readonly ObservableCollection<FilterChoice> _decades = [];
    private readonly HashSet<string> _recentQuizSongPaths = new(StringComparer.OrdinalIgnoreCase);
    private List<Song> _scannedSongs = [];
    private bool _busy;
    private bool _initializing;
    private bool _recentHistoryLoaded = true;
    private bool _showSongCounts = true;

    public event Action<QuizConfig, IReadOnlyList<Song>, IReadOnlyList<Song>>? QuizRequested;
    public event Action<IReadOnlyList<Song>>? SongsRequested;

    public QuizSetupView(IMusicSource musicSource, IQuizHistoryRepository history)
    {
        _musicSource = musicSource;
        _history = history;
        _initializing = true;
        InitializeComponent();
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
        _showSongCounts = settings.ShowSongCountsInLists;
        PresetCombo.ItemsSource = settings.LocalQuizPresets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        _initializing = false;

        Loaded += async (_, _) =>
        {
            if (Directory.Exists(FolderText.Text))
                await ScanAsync(FolderText.Text);
        };
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
        foreach (var (value, count) in _scannedSongs
                     .GroupBy(song => string.IsNullOrWhiteSpace(song.Genre) ? LocalFileMusicSource.UnknownMetadataOption : song.Genre.Trim(),
                         StringComparer.OrdinalIgnoreCase)
                     .Select(group => (group.Key, group.Count()))
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            _genres.Add(new FilterChoice(value, count, _showSongCounts) { IsChecked = oldGenres.Contains(value) });
        foreach (var (value, count) in _scannedSongs
                     .GroupBy(song => song.Year > 0 ? $"{song.Year / 10 * 10}s" : LocalFileMusicSource.UnknownMetadataOption,
                         StringComparer.OrdinalIgnoreCase)
                     .Select(group => (group.Key, group.Count()))
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            _decades.Add(new FilterChoice(value, count, _showSongCounts) { IsChecked = oldDecades.Contains(value) });
    }

    private List<Song> GetMatchingSongs()
    {
        var selectedGenres = _genres.Where(item => item.IsChecked).Select(item => item.Value).ToArray();
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
            songs = _recentHistoryLoaded ? songs.Where(song => !_recentQuizSongPaths.Contains(song.SourcePath)) : [];
        if (UniqueArtistsCheck.IsChecked == true)
            songs = songs.OrderBy(_ => Random.Shared.Next()).DistinctBy(song => song.Artist, StringComparer.OrdinalIgnoreCase);
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
        await OnRecentOptionsChangedAsync();
    }

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
            _recentHistoryLoaded = true;
        }
        UpdateMatchCount();
    }

    private async Task RefreshRecentSongsAsync()
    {
        _recentHistoryLoaded = false;
        _recentQuizSongPaths.Clear();
        var count = Math.Max(1, ParseInt(RecentQuizCountText.Text));
        try
        {
            var recent = await _history.GetRecentQuizSongsAsync(count, "Local", FolderText.Text.Trim());
            foreach (var song in recent.Where(song => !string.IsNullOrWhiteSpace(song.SourcePath)))
                _recentQuizSongPaths.Add(song.SourcePath);
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
        var candidates = GetMatchingSongs();
        if (candidates.Count == 0)
        {
            MessageBox.Show("No songs match the selected filters.", "No songs", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryInt(SongCountText.Text, 20, 1, 1000, out var requestedCount)
            || !TryInt(SecondsText.Text, 30, 5, 600, out var seconds)
            || !TryInt(ClipDurationText.Text, 15, 3, 120, out var clipDuration)
            || !TryInt(ClipStartText.Text, 30, 0, 3600, out var clipStart)
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

        var count = Math.Min(requestedCount, candidates.Count);
        var shuffled = candidates.OrderBy(_ => Random.Shared.Next()).ToList();
        var selected = shuffled.Take(count).ToArray();
        var reserveCount = Math.Min(ParseInt(MulliganCountText.Text), shuffled.Count - count);
        var reserve = shuffled.Skip(count).Take(reserveCount).ToArray();
        var settings = AppSettings.Load();
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
            SourceDirectory = FolderText.Text.Trim(),
            FilterGenre = selectedGenres.Count == 0 ? null : string.Join(", ", selectedGenres),
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
            QuizMode = QuizModeCombo.SelectedIndex switch { 1 => QuizMode.GuessArtist, 2 => QuizMode.GuessBoth, _ => QuizMode.GuessSongTitle },
            QuizTitle = QuizNameGenerator.Generate(selected),
            FuzzyArtistMatching = FuzzyArtistCheck.IsChecked == true,
            FuzzySongTitleMatching = FuzzyTitleCheck.IsChecked == true,
            IgnorePunctuation = IgnorePunctuationCheck.IsChecked == true,
            SkipOnCorrectGuess = SkipCorrectCheck.IsChecked == true,
            MulliganCount = reserve.Length,
            ExtenderCount = ParseInt(ExtenderCountText.Text),
            HintCount = ParseInt(HintCountText.Text),
            EnabledHintTypes = HintTypesPanel.Children.OfType<CheckBox>().Where(check => check.IsChecked == true)
                .Select((check, index) => HintTypes[index]).ToList(),
            MulliganPenalty = mulliganPenalty,
            ExtenderPenalty = extenderPenalty,
            HintPenalty = hintPenalty,
            MaxPowerPenaltyPerSong = maxPenalty
        };
        QuizRequested?.Invoke(config, selected, reserve);
    }

    private void OnSavePresetClick(object sender, RoutedEventArgs e)
    {
        var name = PresetNameText.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter a name for this setup.", "Preset name required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var settings = AppSettings.Load();
        settings.LocalQuizPresets.RemoveAll(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
        TryInt(SecondsText.Text, 30, 5, 600, out var seconds);
        TryInt(ClipDurationText.Text, 15, 3, 120, out var clipDuration);
        TryInt(SongCountText.Text, 20, 1, 1000, out var songCount);
        TryInt(MinLengthText.Text, 0, 0, 86400, out var minLength);
        TryInt(MaxLengthText.Text, 0, 0, 86400, out var maxLength);
        var config = new QuizConfig
        {
            SourceDirectory = FolderText.Text.Trim(),
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
            QuizMode = QuizModeCombo.SelectedIndex switch { 1 => QuizMode.GuessArtist, 2 => QuizMode.GuessBoth, _ => QuizMode.GuessSongTitle },
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
            MulliganPenalty = ParseDouble(MulliganPenaltyText.Text),
            ExtenderPenalty = ParseDouble(ExtenderPenaltyText.Text),
            HintPenalty = ParseDouble(HintPenaltyText.Text),
            MaxPowerPenaltyPerSong = ParseDouble(MaxPenaltyText.Text)
        };
        settings.LocalQuizPresets.Add(new LocalQuizPreset
        {
            Name = name,
            Config = config,
            Genres = _genres.Where(item => item.IsChecked).Select(item => item.Value).ToList(),
            Decades = _decades.Where(item => item.IsChecked).Select(item => item.Value).ToList(),
            UniqueArtistsOnly = UniqueArtistsCheck.IsChecked == true,
            FuzzySongTitleMatching = FuzzyTitleCheck.IsChecked == true,
            ExcludeRecentQuizSongs = ExcludeRecentCheck.IsChecked == true,
            RecentQuizzesToExclude = Math.Max(1, ParseInt(RecentQuizCountText.Text))
        });
        try
        {
            settings.Save();
            PresetCombo.ItemsSource = settings.LocalQuizPresets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            PresetCombo.SelectedItem = PresetCombo.Items.Cast<LocalQuizPreset>().First(preset => preset.Name == name);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save this preset:\n{ex.Message}", "Preset save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnPresetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || PresetCombo.SelectedItem is not LocalQuizPreset preset)
            return;
        _initializing = true;
        try
        {
            var folderChanged = !string.Equals(FolderText.Text, preset.Config.SourceDirectory, StringComparison.OrdinalIgnoreCase);
            FolderText.Text = preset.Config.SourceDirectory;
            if ((folderChanged || _scannedSongs.Count == 0)
                && !await ScanAsync(preset.Config.SourceDirectory))
                return;
            ApplyPreset(preset);
        }
        finally { _initializing = false; }
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
        var genres = preset.Genres.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var decades = preset.Decades.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _genres) item.IsChecked = genres.Contains(item.Value);
        foreach (var item in _decades) item.IsChecked = decades.Contains(item.Value);
        var enabledHints = config.EnabledHintTypes ?? HintTypes.ToList();
        for (var index = 0; index < HintTypesPanel.Children.Count; index++)
            ((CheckBox)HintTypesPanel.Children[index]).IsChecked = enabledHints.Contains(HintTypes[index]);
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
    private static int ParseInt(string text) => int.TryParse(text, out var value) ? Math.Max(0, value) : 0;
    private static bool TryDouble(string text, out double value) => double.TryParse(text, out value) && value >= 0;
    private static double ParseDouble(string text) => double.TryParse(text, out var value) ? Math.Max(0, value) : 0;
    private static string HintLabel(HintType type) => type switch
    {
        HintType.TitleInitial => "Title first letter",
        HintType.ArtistInitial => "Artist first letter",
        HintType.ScrambledTitle => "Scrambled title",
        HintType.ScrambledArtist => "Scrambled artist",
        _ => type.ToString()
    };

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BrowseLibraryButton.IsEnabled = !busy;
        FolderText.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy;
        PresetCombo.IsEnabled = !busy;
        GenreList.IsEnabled = !busy;
        DecadeList.IsEnabled = !busy;
        UniqueArtistsCheck.IsEnabled = !busy;
        ExcludeRecentCheck.IsEnabled = !busy;
        ViewSongsButton.IsEnabled = !busy;
        StartQuizButton.IsEnabled = !busy;
    }
}
