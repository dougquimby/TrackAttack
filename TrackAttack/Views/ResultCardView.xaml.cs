using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrackAttack.Models;

namespace TrackAttack.Views;

/// <summary>Renders a quiz summary and selected details for image export.</summary>
public partial class ResultCardView : UserControl
{
    private static readonly Brush CardBackground = CreateBrush(37, 58, 84);
    private static readonly Brush Accent = CreateBrush(104, 183, 255);
    private static readonly Brush Muted = CreateBrush(169, 190, 215);

    /// <summary>Creates a result card from the recorded quiz.</summary>
    /// <param name="session">The result to display.</param>
    /// <param name="options">Optional sections to include; null produces the summary-only card.</param>
    public ResultCardView(QuizSession session, ResultCardOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        options ??= new ResultCardOptions();
        if (!Enum.IsDefined(options.Tracks))
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid track selection.");
        if (options.Rank is { } rank && (rank.Position < 1 || rank.Attempts < rank.Position))
            throw new ArgumentException("The leaderboard position is invalid.", nameof(options));
        InitializeComponent();
        TitleText.Text = string.IsNullOrWhiteSpace(session.Config.QuizTitle) ? "Track Attack Quiz" : session.Config.QuizTitle;
        ProfileText.Text = string.IsNullOrWhiteSpace(session.ProfileName) ? "Default" : session.ProfileName;
        PercentageText.Text = $"{session.Percentage:F0}%";
        ScoreText.Text = HomeView.UsesPoints(session.Config)
            ? $"{session.TotalPoints:F1} / {session.MaxPoints:F1} points"
            : $"{session.Score} / {session.Results.Count} correct";
        ModeText.Text = session.Config.QuizMode switch
        {
            QuizMode.GuessArtist => "Artist",
            QuizMode.GuessBoth => "Title + artist",
            _ => "Title"
        };
        TracksText.Text = session.Results.Count.ToString("N0");
        DurationText.Text = $"{(long)session.Duration.TotalMinutes}:{session.Duration.Seconds:D2}";
        DateText.Text = session.Date == default ? "Date unavailable" : session.Date.ToString("D");
        if (options.Rank is { } position)
        {
            var content = AddSection("LEADERBOARD");
            content.Children.Add(CreateText($"#{position.Position} of {position.Attempts} comparable local attempts", 22, Brushes.White));
        }
        if (options.Tracks != ResultCardTracks.None)
            AddTracks(session, options);
        if (options.IncludeSettings)
            AddSettings(session);
        if (DetailsPanel.Children.Count > 0)
        {
            DetailsPanel.Visibility = Visibility.Visible;
            Height = double.NaN;
        }
    }

    private StackPanel AddSection(string heading)
    {
        var content = new StackPanel();
        content.Children.Add(CreateText(heading, 16, Accent, FontWeights.Bold));
        DetailsPanel.Children.Add(new Border
        {
            Background = CardBackground,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(30, 22, 30, 22),
            Margin = new Thickness(0, 0, 0, 16),
            Child = content
        });
        return content;
    }

    private void AddTracks(QuizSession session, ResultCardOptions options)
    {
        var rows = session.Results.SelectMany((result, index) =>
        {
            if (options.Tracks == ResultCardTracks.PointsEarned
                && QuizSession.CalculatePoints(result, session.Config) <= 0)
                return Enumerable.Empty<ResultsRow>();
            var resultRows = ResultsView.CreateRows(result, session.Config, index + 1);
            return options.Tracks == ResultCardTracks.PointsEarned
                ? resultRows.Where(row => row.Status != "Mulligan") : resultRows;
        }).ToArray();
        var content = AddSection(options.Tracks == ResultCardTracks.All ? "TRACKS" : "TRACKS THAT EARNED POINTS");
        if (rows.Length == 0)
        {
            content.Children.Add(CreateText("No tracks earned points.", 18, Brushes.White));
            return;
        }
        var columns = Math.Min(rows.Length > 600 ? 4 : 3, rows.Length);
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        var perColumn = (rows.Length + columns - 1) / columns;
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var list = new StackPanel { Margin = new Thickness(0, 0, column < columns - 1 ? 18 : 0, 0) };
            foreach (var row in rows.Skip(column * perColumn).Take(perColumn))
            {
                var block = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
                var points = row.Status == "Mulligan" ? "" : $" · {row.Points} pts";
                block.Children.Add(CreateText($"{row.Number}. {row.Title} — {row.Artist}", 18, Brushes.White, FontWeights.SemiBold));
                block.Children.Add(CreateText($"{row.Status}{points}", 15, Muted));
                if (options.IncludeGuesses && row.Status != "Mulligan")
                {
                    if (session.Config.QuizMode != QuizMode.GuessArtist)
                        block.Children.Add(CreateText($"Title guess: {GuessOrNone(row.Guess)}", 15, Muted));
                    if (session.Config.QuizMode != QuizMode.GuessSongTitle)
                        block.Children.Add(CreateText($"Artist guess: {GuessOrNone(row.ArtistGuess)}", 15, Muted));
                }
                list.Children.Add(block);
            }
            Grid.SetColumn(list, column);
            grid.Children.Add(list);
        }
        content.Children.Add(grid);
    }

    private void AddSettings(QuizSession session)
    {
        var content = AddSection("QUIZ SETTINGS");
        foreach (var (heading, rows) in HistoryView.BuildSettingsSummary(session))
        {
            content.Children.Add(new TextBlock
            {
                Text = heading, Foreground = Accent, FontSize = 15, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 14, 0, 4)
            });
            foreach (var (label, value) in rows)
                content.Children.Add(CreateText($"{label}: {value}", 17, Brushes.White));
        }
    }

    private static string GuessOrNone(string value) => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value;

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static TextBlock CreateText(string text, double size, Brush color, FontWeight? weight = null)
        => new()
        {
            Text = text, FontSize = size, Foreground = color, FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap
        };
}
