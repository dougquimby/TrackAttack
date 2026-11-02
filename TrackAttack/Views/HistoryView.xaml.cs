using System.Windows;
using System.Windows.Controls;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Settings;

namespace TrackAttack.Views;

public sealed class HistoryRow(QuizSession session)
{
    public QuizSession Session { get; } = session;
    public bool IsSpotify => string.Equals(Session.Config.SourceType, "Spotify", StringComparison.OrdinalIgnoreCase);
    public string SourceLabel => IsSpotify ? "Spotify playlist" : "Local music library";
    public string DateLabel => Session.Date.ToString("g");
    public string Title => string.IsNullOrWhiteSpace(Session.Config.QuizTitle) ? Session.Config.SourceDirectory : Session.Config.QuizTitle;
    public string ProfileName => string.IsNullOrWhiteSpace(Session.ProfileName) ? "Default" : Session.ProfileName;
    public string Score => HomeView.UsesPoints(Session.Config)
        ? $"{Session.TotalPoints:F1}/{Session.MaxPoints:F1}" : $"{Session.Score}/{Session.Results.Count}";
    public string Percentage => $"{Session.Percentage:F0}%";
    public string Duration => $"{(int)Session.Duration.TotalMinutes}:{Session.Duration.Seconds:D2}";
}

public sealed record HistoryProfileFilter(string Name, bool IsAll)
{
    public override string ToString() => Name;
}

public sealed record HistoryDetailRow(
    int Number,
    string Title,
    string Artist,
    string Guess,
    string ArtistGuess,
    string Points,
    string Status,
    bool IsTitleSpoilerHidden,
    bool IsArtistSpoilerHidden,
    bool AreGuessesSpoilerHidden);

public sealed record HistoryLeaderboardRow(
    int Rank,
    string ProfileName,
    string Score,
    string Percentage,
    string Date);

public partial class HistoryView : UserControl
{
    private enum SpoilerMode
    {
        BlurAll,
        BlurUnguessed,
        ShowAll
    }

    private readonly IQuizHistoryRepository _history;
    private readonly string _activeProfileName;
    private HistoryRow[] _allRows = [];
    private bool _initializingProfileFilter = true;
    private SpoilerMode _spoilerMode = SpoilerMode.BlurAll;

    public event Action<QuizSession>? ReplayRequested;

    public HistoryView(IQuizHistoryRepository history)
    {
        _history = history;
        _activeProfileName = AppSettings.Load().ActiveProfileName;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _allRows = (await _history.GetAllAsync()).Select(session => new HistoryRow(session)).ToArray();
            var profileNames = AppSettings.Load().Profiles.Select(profile => profile.Name)
                .Concat(_allRows.Select(row => row.ProfileName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var filters = new List<HistoryProfileFilter> { new("All profiles", true) };
            filters.AddRange(profileNames.Select(name => new HistoryProfileFilter(name, false)));

            var currentFilter = ProfileFilterCombo.SelectedItem as HistoryProfileFilter;
            var selectedName = currentFilter is { IsAll: false } ? currentFilter.Name : _activeProfileName;
            selectedName = selectedName is not null && profileNames.Contains(selectedName, StringComparer.OrdinalIgnoreCase)
                ? selectedName
                : _activeProfileName;
            _initializingProfileFilter = true;
            ProfileFilterCombo.ItemsSource = filters;
            ProfileFilterCombo.SelectedItem = filters.FirstOrDefault(filter =>
                !filter.IsAll && string.Equals(filter.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                ?? filters[0];
            _initializingProfileFilter = false;
            ApplyProfileFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load quiz history:\n{ex.Message}", "History load failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryRow row)
        {
            DetailGrid.ItemsSource = null;
            LeaderboardGrid.ItemsSource = null;
            LeaderboardGrid.Visibility = Visibility.Collapsed;
            LeaderboardEmptyText.Visibility = Visibility.Visible;
            ReplayQuizButton.IsEnabled = false;
            DeleteQuizButton.IsEnabled = false;
            ToggleSpoilersButton.IsEnabled = false;
            _spoilerMode = SpoilerMode.BlurAll;
            UpdateSpoilerToggle();
            return;
        }

        _spoilerMode = SpoilerMode.BlurAll;
        DetailHeading.Text = $"{row.Title} — {row.Score} ({row.Percentage})";
        DetailGrid.ItemsSource = CreateDetailRows(row.Session);
        UpdateLeaderboard(row.Session);
        ReplayQuizButton.IsEnabled = row.Session.Results.Count > 0;
        DeleteQuizButton.IsEnabled = true;
        ToggleSpoilersButton.IsEnabled = row.Session.Results.Count > 0;
        UpdateSpoilerToggle();
        ShowSettingsSummary(row.Session);
    }

    private void OnProfileFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializingProfileFilter)
            ApplyProfileFilter();
    }

    private void ApplyProfileFilter()
    {
        var filter = ProfileFilterCombo.SelectedItem as HistoryProfileFilter;
        var rows = filter is null || filter.IsAll
            ? _allRows
            : _allRows.Where(row =>
                string.Equals(row.ProfileName, filter.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        HistoryGrid.SelectedItem = null;
        HistoryGrid.ItemsSource = rows;
        ProfileColumn.Visibility = filter is null || filter.IsAll
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private HistoryDetailRow[] CreateDetailRows(QuizSession session)
        => session.Results.Select((result, index) =>
        {
            var titleGuessed = session.Config.QuizMode != QuizMode.GuessArtist && result.IsCorrect;
            var artistGuessed = session.Config.QuizMode == QuizMode.GuessArtist
                ? result.IsCorrect
                : result.ArtistCorrect;

            return new HistoryDetailRow(
                index + 1,
                result.Song.Title,
                result.Song.DisplayArtistYear,
                result.IsCorrect ? result.UserGuess ?? string.Empty : string.Empty,
                result.ArtistCorrect ? result.ArtistGuess ?? string.Empty : string.Empty,
                $"{QuizSession.CalculatePoints(result, session.Config):F1}",
                result.IsCorrect && (session.Config.QuizMode != QuizMode.GuessBoth || result.ArtistCorrect)
                    ? "Correct"
                    : result.IsCorrect || result.ArtistCorrect ? "Partial" : "Missed",
                ShouldBlur(titleGuessed),
                ShouldBlur(artistGuessed),
                _spoilerMode == SpoilerMode.BlurAll
                    && !string.Equals(session.ProfileName, _activeProfileName, StringComparison.OrdinalIgnoreCase));
        }).ToArray();

    private void UpdateLeaderboard(QuizSession selectedSession)
    {
        var matches = _allRows
            .Where(row => IsSameQuiz(row.Session, selectedSession))
            .GroupBy(row => row.ProfileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(row => row.Session.Percentage)
                .ThenByDescending(row => row.Session.TotalPoints)
                .First())
            .OrderByDescending(row => row.Session.Percentage)
            .ThenByDescending(row => row.Session.TotalPoints)
            .ThenBy(row => row.Session.Date)
            .Select((row, index) => new HistoryLeaderboardRow(
                index + 1,
                row.ProfileName,
                $"{row.Session.TotalPoints:F1}/{row.Session.MaxPoints:F1}",
                $"{row.Session.Percentage:F0}%",
                row.Session.Date.ToString("g")))
            .ToArray();

        LeaderboardGrid.ItemsSource = matches;
        var hasLeaderboard = matches.Length > 0;
        LeaderboardGrid.Visibility = hasLeaderboard ? Visibility.Visible : Visibility.Collapsed;
        LeaderboardEmptyText.Visibility = hasLeaderboard ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool IsSameQuiz(QuizSession first, QuizSession second)
    {
        if (!string.Equals(first.Config.QuizTitle, second.Config.QuizTitle, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(first.Config.SourceType, second.Config.SourceType, StringComparison.OrdinalIgnoreCase)
            || first.Config.QuizMode != second.Config.QuizMode
            || first.Results.Count != second.Results.Count)
            return false;

        for (var index = 0; index < first.Results.Count; index++)
        {
            var firstSong = first.Results[index].Song;
            var secondSong = second.Results[index].Song;
            var firstIdentity = GetSongIdentity(firstSong);
            var secondIdentity = GetSongIdentity(secondSong);
            if (!string.Equals(firstIdentity, secondIdentity, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string GetSongIdentity(Song song)
        => string.IsNullOrWhiteSpace(song.SourcePath)
            ? $"{song.Artist.Trim()}|{song.Title.Trim()}"
            : song.SourcePath.Trim();

    private bool ShouldBlur(bool wasGuessed)
        => _spoilerMode == SpoilerMode.BlurAll
            || (_spoilerMode == SpoilerMode.BlurUnguessed && !wasGuessed);

    private void OnToggleSpoilersClick(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryRow row)
            return;

        _spoilerMode = _spoilerMode switch
        {
            SpoilerMode.BlurAll => SpoilerMode.BlurUnguessed,
            SpoilerMode.BlurUnguessed => SpoilerMode.ShowAll,
            _ => SpoilerMode.BlurAll
        };
        DetailGrid.ItemsSource = CreateDetailRows(row.Session);
        UpdateSpoilerToggle();
    }

    private void UpdateSpoilerToggle()
    {
        ToggleSpoilersButton.Content = _spoilerMode switch
        {
            SpoilerMode.BlurAll => "◌",
            SpoilerMode.BlurUnguessed => "◐",
            _ => "◉"
        };
        ToggleSpoilersButton.ToolTip = _spoilerMode switch
        {
            SpoilerMode.BlurAll => "Titles and artists blurred; guesses from other profiles are blurred too",
            SpoilerMode.BlurUnguessed => "Unguessed titles and artists blurred; click to show all",
            _ => "All titles and artists visible; click to blur all"
        };
    }

    private void OnReplayQuizClick(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is HistoryRow row && row.Session.Results.Count > 0)
            ReplayRequested?.Invoke(row.Session);
    }

    private async void OnDeleteQuizClick(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryRow row)
            return;

        var title = row.Title;
        if (MessageBox.Show(
                $"Delete “{title}” and its song results from quiz history? This cannot be undone.",
                "Delete quiz result",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        DeleteQuizButton.IsEnabled = false;
        ReplayQuizButton.IsEnabled = false;
        try
        {
            if (!await _history.DeleteAsync(row.Session.Id))
            {
                MessageBox.Show("The selected quiz was not found in history. The list will be refreshed.",
                    "Quiz not found", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            HistoryGrid.SelectedItem = null;
            HistoryGrid.ItemsSource = null;
            DetailGrid.ItemsSource = null;
            DetailHeading.Text = "Select a quiz to review its answers";
            _spoilerMode = SpoilerMode.BlurAll;
            UpdateSpoilerToggle();
            ResetSettingsSummary();
            await LoadAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Could not delete the quiz from history:\n{exception.Message}",
                "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
            DeleteQuizButton.IsEnabled = true;
            ReplayQuizButton.IsEnabled = row.Session.Results.Count > 0;
        }
    }

    private void ResetSettingsSummary()
    {
        SettingsSummaryPanel.Children.Clear();
        SettingsSummaryPanel.Children.Add(new TextBlock
        {
            Text = "Select a quiz to see its settings and power usage.",
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x34, 0x44, 0x5C)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private void ShowSettingsSummary(QuizSession session)
    {
        var config = session.Config;
        var mode = config.QuizMode switch
        {
            QuizMode.GuessArtist => "Artist",
            QuizMode.GuessBoth => "Title + artist",
            _ => "Song title"
        };
        var timer = config.TimerMode == TimerMode.TotalQuiz
            ? $"Total quiz ({config.TotalTime.TotalSeconds:0}s)"
            : $"Per song ({config.TimePerSong.TotalSeconds:0}s)";
        var clipStart = config.ClipStartMode == ClipStartMode.Random
            ? "random start"
            : $"fixed start at {config.ClipStartTime.TotalSeconds:0}s";
        var playback = config.ClipPlaybackMode switch
        {
            ClipPlaybackMode.PlayOnceStop => "play once",
            ClipPlaybackMode.PlayOnceAutoAdvance => "play once, auto-advance",
            _ => "loop"
        };
        var matching = string.Join(", ",
            config.FuzzySongTitleMatching ? "fuzzy title matching" : "exact title matching",
            config.FuzzyArtistMatching ? "fuzzy artist matching" : "exact artist matching",
            config.IgnorePunctuation ? "ignore punctuation" : "punctuation matters");
        var powers = string.Join("; ",
            FormatPower("Mulligan", config.MulliganCount, session.MulligansUsed),
            FormatPower("Extender", config.ExtenderCount, session.ExtendersUsed),
            FormatPower("Hint", config.HintCount, session.HintsUsed));
        var hintTypes = string.Join(", ", (config.EnabledHintTypes ?? [])
            .Select(hint => hint switch
            {
                HintType.TitleInitial => "title initial",
                HintType.ArtistInitial => "artist initial",
                HintType.ScrambledTitle => "scrambled title",
                HintType.ScrambledArtist => "scrambled artist",
                HintType.Genre => "genre",
                HintType.Year => "year",
                HintType.AlbumCoverArt => "album cover art",
                _ => hint.ToString()
            }));
        var scoreDeduction = session.Results.Sum(result =>
        {
            var pointsBeforePenalty = QuizSession.CalculateEarnedPoints(result, config);
            return Math.Max(0, pointsBeforePenalty - QuizSession.CalculatePoints(result, config));
        });
        var powerCosts = $"Mulligan {config.MulliganPenalty:F1} pts; Extender {config.ExtenderPenalty:F1} pts; Hint {config.HintPenalty:F1} pts";
        var filters = new List<string>();
        SettingsSummaryPanel.Children.Clear();
        AddSettingsSection("QUIZ RULES",
            ("Answer mode", mode),
            ("Timer", timer),
            ("Clip", $"{config.ClipDuration.TotalSeconds:0}s, {clipStart}, {playback}"),
            ("Advance", config.SkipOnCorrectGuess ? "Immediately on correct answer" : "After clip finishes"));
        AddSettingsSection("ANSWER MATCHING", ("Matching rules", matching));
        AddSettingsSection("POWERS & SCORING",
            ("Correct answer points", GetQuizPointSummary(config)),
            ("Available / used", powers),
            ("Cost per use", powerCosts),
            ("Penalty cap", $"{config.MaxPowerPenaltyPerSong:F1} pts per song"),
            ("Points deducted", $"{scoreDeduction:F1}"));
        if (config.HintCount > 0)
        {
            var hintRows = new List<(string Label, string Value)>
            {
                ("Hint types", string.IsNullOrEmpty(hintTypes) ? "None" : hintTypes)
            };
            if ((config.EnabledHintTypes ?? []).Any(hint =>
                    hint is HintType.ScrambledTitle or HintType.ScrambledArtist))
                hintRows.Add(("Scrambling", config.ScrambleHintsPerWord
                    ? "Each word independently"
                    : "Across the entire title or artist"));
            AddSettingsSection("AVAILABLE HINTS", hintRows.ToArray());
        }

        if (!string.IsNullOrWhiteSpace(config.FilterGenre))
            filters.Add(config.FilterGenre);
        if (!string.IsNullOrWhiteSpace(config.FilterDecade))
            filters.Add(config.FilterDecade);
        var poolRows = new List<(string Label, string Value)>();
        if (filters.Count > 0)
            poolRows.Add(("Genres / decades", string.Join("\n", filters)));
        if (config.MinSongDuration is not null || config.MaxSongDuration is not null)
            poolRows.Add(("Track length", $"{config.MinSongDuration?.TotalSeconds.ToString("0") ?? "any"}–{config.MaxSongDuration?.TotalSeconds.ToString("0") ?? "any"} seconds"));
        if (poolRows.Count == 0)
            poolRows.Add(("Filters", "All tracks"));
        AddSettingsSection("SONG POOL", poolRows.ToArray());
    }

    private static string GetQuizPointSummary(QuizConfig config)
        => config.QuizMode switch
        {
            QuizMode.GuessArtist => $"{config.ArtistPoints ?? 1.0:F1} per correct artist",
            QuizMode.GuessBoth => $"{config.SongTitlePoints ?? 1.5:F1} title, {config.ArtistPoints ?? 0.5:F1} artist",
            _ => $"{config.SongTitlePoints ?? 1.0:F1} per correct title"
        };

    private void AddSettingsSection(string heading, params (string Label, string Value)[] rows)
    {
        var content = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        content.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x65, 0x73, 0x8A)),
            Margin = new Thickness(0, 0, 0, 5)
        });

        var rowsGrid = new Grid();
        rowsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        rowsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < rows.Length; index++)
        {
            rowsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock
            {
                Text = rows[index].Label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x65, 0x73, 0x8A)),
                Margin = new Thickness(0, 0, 8, 5),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(label, index);
            rowsGrid.Children.Add(label);

            var value = new TextBlock
            {
                Text = rows[index].Value,
                FontSize = 12,
                LineHeight = 18,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x34, 0x44, 0x5C)),
                Margin = new Thickness(0, 0, 0, 5),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(value, index);
            Grid.SetColumn(value, 1);
            rowsGrid.Children.Add(value);
        }

        content.Children.Add(rowsGrid);
        SettingsSummaryPanel.Children.Add(content);
    }

    private static string FormatPower(string name, int available, int? used)
        => $"{name} {available} available, {(used?.ToString() ?? "not recorded")} used";
}
