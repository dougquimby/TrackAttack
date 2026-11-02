using System.Windows;
using System.Windows.Controls;
using TrackAttack.Data;
using TrackAttack.Models;

namespace TrackAttack.Views;

public sealed class HistoryRow(QuizSession session)
{
    public QuizSession Session { get; } = session;
    public bool IsSpotify => string.Equals(Session.Config.SourceType, "Spotify", StringComparison.OrdinalIgnoreCase);
    public string SourceLabel => IsSpotify ? "Spotify playlist" : "Local music library";
    public string DateLabel => Session.Date.ToString("g");
    public string Title => string.IsNullOrWhiteSpace(Session.Config.QuizTitle) ? Session.Config.SourceDirectory : Session.Config.QuizTitle;
    public string Score => HomeView.UsesPoints(Session.Config)
        ? $"{Session.TotalPoints:F1}/{Session.MaxPoints:F1}" : $"{Session.Score}/{Session.Results.Count}";
    public string Percentage => $"{Session.Percentage:F0}%";
    public string Duration => $"{(int)Session.Duration.TotalMinutes}:{Session.Duration.Seconds:D2}";
}

public sealed record HistoryDetailRow(int Number, string Title, string Artist, string Guess, string ArtistGuess, string Points, string Status);

public partial class HistoryView : UserControl
{
    private readonly IQuizHistoryRepository _history;
    private bool _clearingHistory;

    public HistoryView(IQuizHistoryRepository history)
    {
        _history = history;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var rows = (await _history.GetAllAsync()).Select(session => new HistoryRow(session)).ToArray();
            HistoryGrid.ItemsSource = rows;
            ClearHistoryButton.IsEnabled = rows.Length > 0 && !_clearingHistory;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load quiz history:\n{ex.Message}", "History load failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        if (_clearingHistory || HistoryGrid.Items.Count == 0)
            return;
        if (MessageBox.Show("Permanently delete all quiz history? This cannot be undone.", "Clear quiz history",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        _clearingHistory = true;
        ClearHistoryButton.IsEnabled = false;
        try
        {
            await _history.ClearAllAsync();
            HistoryGrid.SelectedItem = null;
            HistoryGrid.ItemsSource = null;
            DetailGrid.ItemsSource = null;
            DetailHeading.Text = "Select a quiz to review its answers";
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
        catch (Exception ex)
        {
            MessageBox.Show($"Could not clear quiz history:\n{ex.Message}", "History clear failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            await LoadAsync();
        }
        finally
        {
            _clearingHistory = false;
            ClearHistoryButton.IsEnabled = HistoryGrid.Items.Count > 0;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryRow row)
            return;
        DetailHeading.Text = $"{row.Title} — {row.Score} ({row.Percentage})";
        DetailGrid.ItemsSource = row.Session.Results.Select((result, index) => new HistoryDetailRow(
            index + 1,
            result.Song.Title,
            result.Song.DisplayArtistYear,
            result.IsCorrect ? result.UserGuess ?? string.Empty : string.Empty,
            result.ArtistCorrect ? result.ArtistGuess ?? string.Empty : string.Empty,
            $"{QuizSession.CalculatePoints(result, row.Session.Config):F1}",
            result.IsCorrect && (row.Session.Config.QuizMode != QuizMode.GuessBoth || result.ArtistCorrect)
                ? "Correct"
                : result.IsCorrect || result.ArtistCorrect ? "Partial" : "Missed")).ToArray();
        ShowSettingsSummary(row.Session);
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
