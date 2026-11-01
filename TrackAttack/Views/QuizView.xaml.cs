using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TrackAttack.Data;
using TrackAttack.Engine;
using TrackAttack.Models;
using TrackAttack.Services;

namespace TrackAttack.Views;

public partial class QuizView : UserControl, IDisposable
{
    private readonly QuizConfig _config;
    private readonly IQuizHistoryRepository _history;
    private readonly IPlaybackService _playback = new NAudioPlaybackService();
    private readonly QuizEngine _engine;
    private readonly ObservableCollection<QuizRow> _rows = [];
    private readonly List<QuizGridRow> _gridRows = [];
    private readonly Dictionary<string, (int Row, int Group)> _songSlots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _hintsBySong = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Song? _displayedSong;
    private long _lastClipPositionSecond = -1;
    private long _lastClipDurationSecond = -1;
    private long _lastSongPositionSecond = -1;
    private long _lastSongDurationSecond = -1;
    private bool _finished;
    private bool _disposed;

    public event Action<QuizSession>? QuizFinished;

    public QuizView(QuizConfig config, IReadOnlyList<Song> songs, IReadOnlyList<Song> reserve, IQuizHistoryRepository history)
    {
        _config = config;
        _history = history;
        _engine = new QuizEngine(config, songs, reserve, _playback);
        InitializeComponent();
        for (var index = 0; index < songs.Count; index++)
            _rows.Add(new QuizRow(songs[index], index + 1));
        ConfigureSongGrid();
        _engine.SongChanged += OnSongChanged;
        _engine.SongCorrect += OnSongCorrect;
        _engine.ArtistBonusGuessed += OnArtistGuessed;
        _engine.SongPassed += OnSongPassed;
        _engine.TimerTick += OnTimerTick;
        _engine.QuizEnded += OnQuizEnded;
        _engine.MulliganUsed += OnMulliganUsed;
        _engine.ExtenderUsed += (_, _) => UpdatePowers();
        _engine.HintUsed += OnHintUsed;
        _progressTimer.Tick += OnProgressTick;
        TimerText.Text = FormatTime(_engine.RemainingSeconds);
        Loaded += (_, _) =>
        {
            CompositionTarget.Rendering += OnProgressRendering;
            _progressTimer.Start();
            _engine.Start();
            AnswerText.Focus();
        };
        Unloaded += (_, _) => Dispose();
        UpdatePrompt();
        UpdatePowers();
        UpdateScore();
    }

    private void OnSongChanged(object? sender, SongChangedEventArgs e)
    {
        if (_displayedSong is not null && _displayedSong.SourcePath != e.Song.SourcePath)
        {
            var previousRow = FindRow(_displayedSong);
            if (previousRow.Status == "Playing")
                previousRow.Status = string.Empty;
        }
        _displayedSong = e.Song;
        NowPlayingText.Text = $"Song {e.SongNumber} of {_engine.TotalSongs}";
        var currentRow = EnsureRow(e.Song, e.SongNumber);
        if (string.IsNullOrEmpty(currentRow.Status))
            currentRow.Status = "Playing";
        AnswerText.Clear();
        FeedbackText.Text = string.Empty;
        HintsList.ItemsSource = _hintsBySong.TryGetValue(e.Song.SourcePath, out var hints) ? hints.ToArray() : null;
        UpdatePrompt();
        ClipProgressFill.Width = 0;
        ClipPauseButton.Content = "Ⅱ";
        ClipPauseButton.ToolTip = "Pause only the song clip";
        AutomationProperties.SetName(ClipPauseButton, "Pause only the song clip");
        UpdatePowers();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdatePowers));
        SongsGrid.SelectedItem = null;
        if (_songSlots.TryGetValue(e.Song.SourcePath, out var slot))
            SongsGrid.ScrollIntoView(_gridRows[slot.Row]);
    }

    private void OnSongCorrect(object? sender, SongCorrectEventArgs e)
    {
        var row = FindRow(e.Song);
        row.Title = e.Song.Title;
        row.Artist = _config.QuizMode == QuizMode.GuessBoth && !_engine.GuessBothArtistDone ? "?" : e.Song.DisplayArtistYear;
        row.Status = _config.QuizMode == QuizMode.GuessBoth && !_engine.GuessBothArtistDone ? "Title guessed" : "Correct";
        FeedbackText.Text = "Correct!";
        AnswerText.Clear();
        UpdatePrompt();
        UpdatePowers();
        UpdateScore();
    }

    private void OnArtistGuessed(object? sender, SongCorrectEventArgs e)
    {
        var row = FindRow(e.Song);
        row.Artist = e.Song.DisplayArtistYear;
        row.Title = _engine.GuessBothTitleDone ? e.Song.Title : "?";
        row.Status = _engine.GuessBothTitleDone ? "Correct" : "Artist guessed";
        FeedbackText.Text = "Artist correct!";
        AnswerText.Clear();
        UpdatePrompt();
        UpdatePowers();
        UpdateScore();
    }

    private void OnSongPassed(object? sender, EventArgs e)
    {
        if (_engine.CurrentSong is { } song)
        {
            var row = FindRow(song);
            if (row.Status == "Playing")
            {
                row.Status = string.Empty;
            }
        }
        AnswerText.Clear();
        FeedbackText.Text = string.Empty;
        UpdatePowers();
    }

    private void OnTimerTick(object? sender, int remaining)
    {
        TimerText.Text = $"{remaining / 60:D2}:{remaining % 60:D2}";
        UpdateScore();
    }

    private void OnMulliganUsed(object? sender, MulliganUsedEventArgs e)
    {
        var row = FindRow(e.OldSong);
        row.Title = string.Empty;
        row.Artist = string.Empty;
        row.Status = string.Empty;
        row.ReplaceSong(e.NewSong);
        row.Status = "Playing";
        _displayedSong = e.NewSong;
        _songSlots.Remove(e.OldSong.SourcePath);
        if (_songSlots.TryGetValue(e.NewSong.SourcePath, out var existingSlot))
            _gridRows[existingSlot.Row].Cells[existingSlot.Group] = null;
        var index = Array.IndexOf(_rows.ToArray(), row);
        var rowCount = _gridRows.Count;
        var group = index / rowCount;
        var tableRow = index % rowCount;
        _gridRows[tableRow].Cells[group] = row;
        _songSlots[e.NewSong.SourcePath] = (tableRow, group);
        NowPlayingText.Text = $"Song {e.SongNumber} of {_engine.TotalSongs}";
        AnswerText.Clear();
        HintsList.ItemsSource = _hintsBySong.TryGetValue(e.NewSong.SourcePath, out var hints) ? hints.ToArray() : null;
        ClipProgressFill.Width = 0;
        ClipPauseButton.Content = "Ⅱ";
        ClipPauseButton.ToolTip = "Pause only the song clip";
        AutomationProperties.SetName(ClipPauseButton, "Pause only the song clip");
        UpdatePrompt();
        UpdatePowers();
        UpdateScore();
    }

    private void OnHintUsed(object? sender, HintUsedEventArgs e)
    {
        if (_engine.CurrentSong is { } song)
        {
            if (!_hintsBySong.TryGetValue(song.SourcePath, out var hints))
                _hintsBySong[song.SourcePath] = hints = [];
            hints.Add($"{e.Label}: {e.Hint}");
            HintsList.ItemsSource = hints.ToArray();
        }
        UpdatePowers();
        UpdateScore();
    }

    private async void OnQuizEnded(object? sender, QuizEndedEventArgs e)
    {
        if (_finished)
            return;
        _finished = true;
        _progressTimer.Stop();
        var session = new QuizSession { Date = DateTime.Now, Config = _config, Results = e.Results.ToList(), Duration = e.Duration };
        try { await _history.SaveAsync(session); }
        catch (Exception ex)
        {
            MessageBox.Show($"Quiz finished, but history could not be saved:\n{ex.Message}",
                "History save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        foreach (var row in _rows.Where(row => string.IsNullOrEmpty(row.Status)))
        {
            row.Title = row.Song.Title;
            row.Artist = row.Song.DisplayArtistYear;
            row.Status = "Missed";
        }
        QuizFinished?.Invoke(session);
    }

    private QuizRow FindRow(Song song)
        => _rows.FirstOrDefault(row => string.Equals(row.Song.SourcePath, song.SourcePath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Song '{song.SourcePath}' is not represented in the quiz grid.");

    private QuizRow EnsureRow(Song song, int songNumber)
    {
        var existing = _rows.FirstOrDefault(row =>
            string.Equals(row.Song.SourcePath, song.SourcePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        var row = _rows.FirstOrDefault(item => item.Number == songNumber);
        if (row is null)
            throw new InvalidOperationException($"Song '{song.SourcePath}' (quiz slot {songNumber}) is not represented in the quiz grid.");

        if (_songSlots.TryGetValue(row.Song.SourcePath, out var slot))
        {
            _songSlots.Remove(row.Song.SourcePath);
            row.Title = string.Empty;
            row.Artist = string.Empty;
            row.Status = string.Empty;
            row.ReplaceSong(song);
            _songSlots[song.SourcePath] = slot;
        }
        return row;
    }

    private void OnProgressTick(object? sender, EventArgs e)
    {
        var duration = _playback.ClipDuration;
        var clipPosition = _playback.CurrentPosition;
        var songDuration = _playback.SongDuration;
        var songPosition = _playback.SongPosition;
        UpdateClipProgress(duration, clipPosition);
        var clipPositionSecond = (long)clipPosition.TotalSeconds;
        var clipDurationSecond = (long)duration.TotalSeconds;
        if (clipPositionSecond != _lastClipPositionSecond || clipDurationSecond != _lastClipDurationSecond)
        {
            ClipTimeText.Text = $"{clipPosition:mm\\:ss} / {duration:mm\\:ss}";
            _lastClipPositionSecond = clipPositionSecond;
            _lastClipDurationSecond = clipDurationSecond;
        }
        var songPositionSecond = (long)songPosition.TotalSeconds;
        var songDurationSecond = (long)songDuration.TotalSeconds;
        if (songPositionSecond != _lastSongPositionSecond || songDurationSecond != _lastSongDurationSecond)
        {
            SongTimeText.Text = $"Song {songPosition:mm\\:ss} / {songDuration:mm\\:ss}";
            _lastSongPositionSecond = songPositionSecond;
            _lastSongDurationSecond = songDurationSecond;
        }
        if (ExtenderButton.IsEnabled != _engine.CanUseExtender)
            ExtenderButton.IsEnabled = _engine.CanUseExtender;
    }

    private void OnProgressRendering(object? sender, EventArgs e)
        => UpdateClipProgress(_playback.ClipDuration, _playback.CurrentPosition);

    private void UpdateClipProgress(TimeSpan duration, TimeSpan position)
    {
        var progress = duration <= TimeSpan.Zero ? 0 : Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1);
        ClipProgressFill.Width = ClipProgress.ActualWidth * progress;
    }

    private void OnClipProgressClick(object sender, MouseButtonEventArgs e)
    {
        var duration = _playback.ClipDuration;
        if (ClipProgress.ActualWidth <= 0 || duration <= TimeSpan.Zero
            || (!_playback.IsPlaying && !_playback.IsPaused))
            return;

        var fraction = Math.Clamp(e.GetPosition(ClipProgress).X / ClipProgress.ActualWidth, 0, 1);
        _playback.SeekClip(TimeSpan.FromTicks((long)(duration.Ticks * fraction)));
        OnProgressTick(sender, EventArgs.Empty);
        e.Handled = true;
    }

    private static string FormatTime(int totalSeconds) => $"{totalSeconds / 60:D2}:{totalSeconds % 60:D2}";

    private void UpdatePrompt()
    {
        AnswerPrompt.Text = _config.QuizMode switch
        {
            QuizMode.GuessArtist => "Type the artist name.",
            QuizMode.GuessBoth => _engine.GuessBothTitleDone ? "Now guess the artist." :
                _engine.GuessBothArtistDone ? "Now guess the song title." : "Guess the title or artist.",
            _ => "Type the song title."
        };
    }

    private void UpdatePowers()
    {
        var hasPowers = _config.MulliganCount > 0 || _config.ExtenderCount > 0 || _config.HintCount > 0;
        PowerGroupPanel.Visibility = hasPowers ? Visibility.Visible : Visibility.Collapsed;
        MulliganButton.Content = $"Mulligan ({_engine.MulligansRemaining})";
        MulliganButton.IsEnabled = _engine.CanUseMulligan;
        MulliganButton.Visibility = _config.MulliganCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        ExtenderButton.Content = $"Extender ({_engine.ExtendersRemaining})";
        ExtenderButton.IsEnabled = _engine.CanUseExtender;
        ExtenderButton.Visibility = _config.ExtenderCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        HintButton.Content = $"Hint ({_engine.HintsRemaining})";
        HintButton.IsEnabled = _engine.CanUseHint;
        HintButton.Visibility = _config.HintCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateScore() => ScoreText.Text = HomeView.UsesPoints(_config)
        ? $"{_engine.TotalPoints:F1} / {_engine.MaxPoints:F1} pts · {_engine.CorrectCount} correct"
        : $"{_engine.CorrectCount} / {_engine.TotalSongs} correct";

    private void OnAnswerTextChanged(object sender, TextChangedEventArgs e) => SubmitAnswer();
    private void OnAnswerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SubmitAnswer();
        }
    }
    private void SubmitAnswer()
    {
        _engine.SubmitGuess(AnswerText.Text);
        AnswerText.Focus();
    }
    private void OnClipPauseClick(object sender, RoutedEventArgs e)
    {
        if (_engine.IsPaused)
            return;
        if (_playback.IsPaused)
        {
            _playback.Resume();
            ClipPauseButton.Content = "Ⅱ";
        }
        else if (!_playback.IsPlaying)
        {
            return;
        }
        else
        {
            _playback.Pause();
            ClipPauseButton.Content = "▶";
        }
        AutomationProperties.SetName(ClipPauseButton, _playback.IsPaused ? "Resume song clip" : "Pause song clip");
        ClipPauseButton.ToolTip = _playback.IsPaused ? "Resume song clip" : "Pause only the song clip";
        UpdatePowers();
    }

    private void OnQuizPauseClick(object sender, RoutedEventArgs e)
    {
        if (_engine.IsPaused)
        {
            _engine.Resume();
            QuizPauseButton.Content = "Ⅱ";
            ClipPauseButton.IsEnabled = true;
        }
        else
        {
            _engine.Pause();
            QuizPauseButton.Content = "▶";
            ClipPauseButton.IsEnabled = false;
        }
        AutomationProperties.SetName(QuizPauseButton, _engine.IsPaused ? "Resume the entire quiz" : "Pause the entire quiz");
        QuizPauseButton.ToolTip = _engine.IsPaused ? "Resume the entire quiz" : "Pause the entire quiz";
        UpdatePowers();
    }

    private void OnQuizPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Space)
        {
            OnQuizPauseClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.P)
        {
            _engine.PassCurrentSong();
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.R)
        {
            _engine.RestartCurrentSong();
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.Space)
        {
            OnClipPauseClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.None && e.Key == Key.Tab)
        {
            _engine.PassCurrentSong();
            e.Handled = true;
        }
    }
    private void OnReplayClick(object sender, RoutedEventArgs e) => _engine.RestartCurrentSong();
    private void OnPassClick(object sender, RoutedEventArgs e) => _engine.PassCurrentSong();
    private void OnMulliganClick(object sender, RoutedEventArgs e) => _engine.UseMulligan();
    private void OnExtenderClick(object sender, RoutedEventArgs e) => _engine.UseExtender();
    private void OnHintClick(object sender, RoutedEventArgs e) => _engine.UseHint();
    private void OnEndClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("End this quiz now?", "End quiz", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _engine.End();
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _playback.Volume = (float)e.NewValue;
    }

    private void ConfigureSongGrid()
    {
        var groupCount = _config.GridColumns switch
        {
            GridColumns.One => 1,
            GridColumns.Two => 2,
            GridColumns.Three => 3,
            GridColumns.Four => 4,
            _ => _rows.Count switch { <= 15 => 1, <= 40 => 2, _ => 3 }
        };
        var rowCount = (int)Math.Ceiling((double)_rows.Count / groupCount);
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            _gridRows.Add(new QuizGridRow(groupCount));
        for (var index = 0; index < _rows.Count; index++)
        {
            var group = index / rowCount;
            var row = index % rowCount;
            _gridRows[row].Cells[group] = _rows[index];
            _songSlots[_rows[index].Song.SourcePath] = (row, group);
        }

        SongsGrid.Columns.Clear();
        for (var group = 0; group < groupCount; group++)
        {
            var statusPath = $"Cells[{group}].Status";
            var cellStyle = CreateQuizCellStyle(statusPath);
            SongsGrid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding($"Cells[{group}].Number"), Width = 38, CellStyle = cellStyle });
            SongsGrid.Columns.Add(new DataGridTextColumn { Header = "Title", Binding = new Binding($"Cells[{group}].Title"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), CellStyle = cellStyle });
            SongsGrid.Columns.Add(new DataGridTextColumn { Header = "Artist", Binding = new Binding($"Cells[{group}].Artist"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), CellStyle = cellStyle });
        }
        SongsGrid.ItemsSource = _gridRows;
    }

    private static Style CreateQuizCellStyle(string statusPath)
    {
        var baseStyle = Application.Current.TryFindResource(typeof(DataGridCell)) as Style;
        var style = new Style(typeof(DataGridCell), baseStyle);
        AddStatusTrigger(style, statusPath, "Correct", "#C6EFCE", "#006100");
        AddStatusTrigger(style, statusPath, "Title guessed", "#FFEB96", "#644600");
        AddStatusTrigger(style, statusPath, "Artist guessed", "#ADD8E6", "#00376E");
        AddStatusTrigger(style, statusPath, "Playing", "#D2E6FF", "#1E3C82");
        AddStatusTrigger(style, statusPath, "Missed", "#FFC7CE", "#9C0006");
        return style;
    }

    private static void AddStatusTrigger(Style style, string statusPath, string status, string background, string foreground)
    {
        var trigger = new DataTrigger
        {
            Binding = new Binding(statusPath),
            Value = status
        };
        trigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(background))));
        trigger.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(foreground))));
        trigger.Setters.Add(new Setter(DataGridCell.BorderBrushProperty, Brushes.White));
        trigger.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        style.Triggers.Add(trigger);
    }

    private void OnSongGridMouseDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null and not DataGridCell)
            source = VisualTreeHelper.GetParent(source);
        if (source is not DataGridCell cell
            || ItemsControl.ContainerFromElement(SongsGrid, cell) is not DataGridRow row
            || row.Item is not QuizGridRow gridRow)
            return;

        var group = cell.Column.DisplayIndex / 3;
        if (group < gridRow.Cells.Length && gridRow.Cells[group] is { } songRow)
            _engine.JumpToSong(songRow.Song);
    }

    public void Abandon()
    {
        _finished = true;
        _progressTimer.Stop();
        _engine.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CompositionTarget.Rendering -= OnProgressRendering;
        _progressTimer.Stop();
        _engine.Dispose();
        _playback.Dispose();
    }
}
