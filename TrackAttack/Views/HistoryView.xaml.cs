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
            SettingsSummaryText.Text = "Select a quiz to see its settings and power usage.";
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
        SettingsSummaryText.Text = BuildSettingsSummary(row.Session);
    }

    private static string BuildSettingsSummary(QuizSession session)
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
            var pointsBeforePenalty = config.QuizMode == QuizMode.GuessBoth
                ? (result.IsCorrect ? 1.5 : 0) + (result.ArtistCorrect ? 0.5 : 0)
                : result.IsCorrect ? 1.0 : 0;
            return Math.Max(0, pointsBeforePenalty - QuizSession.CalculatePoints(result, config));
        });
        var powerCosts = $"Mulligan {config.MulliganPenalty:F1} pts; Extender {config.ExtenderPenalty:F1} pts; Hint {config.HintPenalty:F1} pts";
        var lines = new List<string>
        {
            $"Mode: {mode}  ·  Timer: {timer}  ·  Clip: {config.ClipDuration.TotalSeconds:0}s, {clipStart}, {playback}",
            $"Matching: {matching}  ·  Advance on correct: {(config.SkipOnCorrectGuess ? "immediately" : "after clip")}",
            $"Powers available / used: {powers}",
            $"Penalty per use: {powerCosts}  ·  Cap: {config.MaxPowerPenaltyPerSong:F1} pts/song  ·  Score deducted: {scoreDeduction:F1} pts"
        };
        if (config.HintCount > 0)
            lines.Add($"Available hint types: {(string.IsNullOrEmpty(hintTypes) ? "none" : hintTypes)}");

        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.FilterGenre))
            filters.Add($"Genres: {config.FilterGenre}");
        if (!string.IsNullOrWhiteSpace(config.FilterDecade))
            filters.Add($"Decades: {config.FilterDecade}");
        if (config.MinSongDuration is not null || config.MaxSongDuration is not null)
            filters.Add($"Track length: {config.MinSongDuration?.TotalSeconds.ToString("0") ?? "any"}–{config.MaxSongDuration?.TotalSeconds.ToString("0") ?? "any"}s");
        if (filters.Count > 0)
            lines.Insert(0, $"Song pool: {string.Join("  ·  ", filters)}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatPower(string name, int available, int? used)
        => $"{name} {available} available, {(used?.ToString() ?? "not recorded")} used";
}
