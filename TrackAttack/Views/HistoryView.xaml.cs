using System.Windows;
using System.Windows.Controls;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Settings;

namespace TrackAttack.Views;

/// <summary>Provides display values for a saved quiz in the history list.</summary>
/// <param name="session">The saved quiz session.</param>
public sealed class HistoryRow(QuizSession session)
{
    /// <summary>Gets the underlying session.</summary>
    public QuizSession Session { get; } = session;
    public bool IsSpotify => string.Equals(Session.Config.SourceType, "Spotify", StringComparison.OrdinalIgnoreCase);
    /// <summary>Gets a display label for the music source.</summary>
    public string SourceLabel => IsSpotify ? "Spotify playlist" : "Local music library";
    /// <summary>Gets the formatted session date.</summary>
    public string DateLabel => Session.Date.ToString("g");
    /// <summary>Gets the quiz title or its source when the title is blank.</summary>
    public string Title => string.IsNullOrWhiteSpace(Session.Config.QuizTitle) ? Session.Config.SourceDirectory : Session.Config.QuizTitle;
    /// <summary>Gets the recorded profile name, defaulting to the default profile label.</summary>
    public string ProfileName => string.IsNullOrWhiteSpace(Session.ProfileName) ? "Default" : Session.ProfileName;
    /// <summary>Gets the formatted score.</summary>
    public string Score => HomeView.UsesPoints(Session.Config)
        ? $"{Session.TotalPoints:F1}/{Session.MaxPoints:F1}" : $"{Session.Score}/{Session.Results.Count}";
    /// <summary>Gets the formatted percentage.</summary>
    public string Percentage => $"{Session.Percentage:F0}%";
    /// <summary>Gets the formatted quiz duration.</summary>
    public string Duration => $"{(int)Session.Duration.TotalMinutes}:{Session.Duration.Seconds:D2}";
}

/// <summary>Represents a profile selection in the history filter.</summary>
/// <param name="Name">The displayed profile or all-profiles label.</param>
/// <param name="IsAll">Whether the selection includes every profile.</param>
public sealed record HistoryProfileFilter(string Name, bool IsAll)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>Provides display and spoiler state for a song result in history.</summary>
/// <param name="Number">The one-based quiz slot number.</param>
/// <param name="Title">The song title.</param>
/// <param name="Artist">The formatted artist and year.</param>
/// <param name="Guess">The accepted title guess.</param>
/// <param name="ArtistGuess">The accepted artist guess.</param>
/// <param name="Points">The formatted points.</param>
/// <param name="Status">The result status.</param>
/// <param name="IsTitleSpoilerHidden">Whether the title is blurred.</param>
/// <param name="IsArtistSpoilerHidden">Whether the artist is blurred.</param>
/// <param name="AreGuessesSpoilerHidden">Whether the guesses are blurred.</param>
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

/// <summary>Provides one ranked attempt for the history leaderboard.</summary>
/// <param name="Rank">The attempt's rank.</param>
/// <param name="ProfileName">The player's profile name.</param>
/// <param name="Score">The formatted score.</param>
/// <param name="Percentage">The formatted percentage.</param>
/// <param name="Date">The formatted attempt date.</param>
public sealed record HistoryLeaderboardRow(
    int Rank,
    string ProfileName,
    string Score,
    string Percentage,
    string Date);

/// <summary>Displays saved quizzes, answers, and comparable attempts.</summary>
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

    /// <summary>Occurs when the player requests a replay of a saved quiz.</summary>
    public event Action<QuizSession>? ReplayRequested;

    /// <summary>Creates the quiz history view.</summary>
    /// <param name="history">The repository used to load and delete sessions.</param>
    public HistoryView(IQuizHistoryRepository history)
    {
        _history = history;
        _activeProfileName = AppSettings.Load().ActiveProfileName;
        InitializeComponent();
        UpdateSpoilerToggle();
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
            ProfileFilterCombo.SelectedItem = currentFilter?.IsAll == true
                ? filters[0]
                : filters.FirstOrDefault(filter =>
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
            UpdateSpoilerToggle();
            return;
        }

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
        => session.Results.SelectMany((result, index) => CreateDetailRows(session, result, index + 1)).ToArray();

    private IEnumerable<HistoryDetailRow> CreateDetailRows(QuizSession session, SongResult result, int number)
    {
        foreach (var song in result.MulliganSongs)
            yield return new HistoryDetailRow(number, song.Title, song.DisplayArtistYear, "", "", "", "Mulligan",
                ShouldBlur(false), ShouldBlur(false), false);

        var titleGuessed = session.Config.QuizMode != QuizMode.GuessArtist && result.IsCorrect;
        var artistGuessed = session.Config.QuizMode == QuizMode.GuessArtist
            ? result.IsCorrect
            : result.ArtistCorrect;

        yield return new HistoryDetailRow(
            number,
            result.Song.Title,
            result.Song.DisplayArtistYear,
            result.IsCorrect && session.Config.QuizMode != QuizMode.GuessArtist
                ? result.UserGuess ?? string.Empty : string.Empty,
            session.Config.QuizMode == QuizMode.GuessArtist && result.IsCorrect
                ? result.UserGuess ?? string.Empty
                : result.ArtistCorrect ? result.ArtistGuess ?? string.Empty : string.Empty,
            $"{QuizSession.CalculatePoints(result, session.Config):F1}",
            result.IsCorrect && (session.Config.QuizMode != QuizMode.GuessBoth || result.ArtistCorrect)
                ? "Correct"
                : result.IsCorrect || result.ArtistCorrect ? "Partial" : "Missed",
            ShouldBlur(titleGuessed),
            ShouldBlur(artistGuessed),
            _spoilerMode == SpoilerMode.BlurAll
                && !string.Equals(session.ProfileName, _activeProfileName, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateLeaderboard(QuizSession selectedSession)
    {
        var matches = _allRows
            .Where(row => IsSameQuiz(row.Session, selectedSession))
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
        var message = new TextBlock
        {
            Text = "Select a quiz to see its settings and power usage.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        message.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        SettingsSummaryPanel.Children.Add(message);
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
            FormatPower("Clip reroll", config.ClipRerollCount, session.ClipRerollsUsed),
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
        var powerCosts = $"Mulligan {config.MulliganPenalty:F1} pts; Extender {config.ExtenderPenalty:F1} pts; Clip reroll {config.ClipRerollPenalty:F1} pts; Hint {config.HintPenalty:F1} pts";
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
            poolRows.Add(("Genres / years", string.Join("\n", filters)));
        if (config.MinSongDuration is not null || config.MaxSongDuration is not null)
            poolRows.Add(("Track length", $"{config.MinSongDuration?.TotalSeconds.ToString("0") ?? "any"}–{config.MaxSongDuration?.TotalSeconds.ToString("0") ?? "any"} seconds"));
        if (config.UniqueArtistsOnly)
            poolRows.Add(("Artists", "One track per artist"));
        if (config.ExcludeRecentQuizSongs)
            poolRows.Add(("Recent quizzes", $"Exclude tracks from the latest {config.RecentQuizzesToExclude}"));
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
        var title = new TextBlock
        {
            Text = heading,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        content.Children.Add(title);

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
                Margin = new Thickness(0, 0, 8, 5),
                TextWrapping = TextWrapping.Wrap
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            Grid.SetRow(label, index);
            rowsGrid.Children.Add(label);

            var value = new TextBlock
            {
                Text = rows[index].Value,
                FontSize = 12,
                LineHeight = 18,
                Margin = new Thickness(0, 0, 0, 5),
                TextWrapping = TextWrapping.Wrap
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
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
