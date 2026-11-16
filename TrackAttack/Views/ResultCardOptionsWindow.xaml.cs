using System.Windows;
using Microsoft.Win32;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Services;

namespace TrackAttack.Views;

/// <summary>Collects the optional details to include in a saved result card.</summary>
public partial class ResultCardOptionsWindow : Window
{
    private readonly QuizSession _session;
    private readonly IQuizHistoryRepository _history;

    /// <summary>Creates the result-card export options window.</summary>
    /// <param name="session">The result to export.</param>
    /// <param name="history">The repository used to calculate leaderboard position.</param>
    public ResultCardOptionsWindow(QuizSession session, IQuizHistoryRepository history)
    {
        _session = session;
        _history = history;
        InitializeComponent();
    }

    private void OnTrackSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (GuessesCheck is not null)
            GuessesCheck.IsEnabled = NoTracksRadio?.IsChecked != true;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        try
        {
            var options = new ResultCardOptions
            {
                Tracks = AllTracksRadio.IsChecked == true ? ResultCardTracks.All
                    : PointsTracksRadio.IsChecked == true ? ResultCardTracks.PointsEarned : ResultCardTracks.None,
                IncludeGuesses = GuessesCheck.IsEnabled && GuessesCheck.IsChecked == true,
                IncludeSettings = SettingsCheck.IsChecked == true
            };
            if (RankCheck.IsChecked == true)
            {
                var sessions = await _history.GetAllAsync();
                if (!IsVisible)
                    return;
                var ranked = QuizLeaderboard.Rank(sessions, _session);
                var index = _session.Id > 0 ? Array.FindIndex(ranked, attempt => attempt.Id == _session.Id) : -1;
                if (index < 0)
                    throw new InvalidOperationException("This result is not in saved history, so its leaderboard position is unavailable.");
                options.Rank = new ResultCardRank(index + 1, ranked.Length);
            }
            var dialog = new SaveFileDialog
            {
                Title = "Save result card",
                Filter = "PNG image (*.png)|*.png",
                DefaultExt = ".png",
                FileName = "TrackAttack Result.png"
            };
            if (dialog.ShowDialog(this) != true)
                return;
            ResultCardExporter.SavePng(_session, dialog.FileName, options);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            var message = $"Could not save the result card:\n{exception.Message}";
            if (IsVisible)
                MessageBox.Show(this, message, "Result card export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            else
                MessageBox.Show(message, "Result card export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SaveButton.IsEnabled = true; }
    }
}
