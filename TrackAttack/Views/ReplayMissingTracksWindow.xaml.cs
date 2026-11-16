using System.Windows;
using TrackAttack.Models;

namespace TrackAttack.Views;

/// <summary>Describes how to proceed when replay tracks are unavailable.</summary>
public enum MissingTrackChoice
{
    /// <summary>Cancel the replay.</summary>
    Cancel,
    /// <summary>Replay only the available original tracks.</summary>
    Remaining,
    /// <summary>Replace all missing tracks with eligible songs.</summary>
    Replace
}

/// <summary>Prompts for a replay action when original tracks are unavailable.</summary>
public partial class ReplayMissingTracksWindow : Window
{
    /// <summary>Gets the selected replay action.</summary>
    public MissingTrackChoice Choice { get; private set; }

    /// <summary>Creates the missing-track prompt.</summary>
    /// <param name="missing">The unavailable original tracks.</param>
    /// <param name="availableCount">The number of available original tracks.</param>
    /// <param name="replacementCount">The number of eligible replacement tracks.</param>
    public ReplayMissingTracksWindow(IReadOnlyList<Song> missing, int availableCount, int replacementCount)
    {
        InitializeComponent();
        SummaryText.Text = $"{missing.Count} track(s) are unavailable; {availableCount} original track(s) can be replayed.";
        TracksText.Text = string.Join(Environment.NewLine,
            missing.Take(5).Select(song => $"{song.Artist} — {song.Title}"))
            + (missing.Count > 5 ? $"{Environment.NewLine}...and {missing.Count - 5} more." : string.Empty);
        ReplacementText.Text = replacementCount >= missing.Count
            ? "Replace missing tracks with other tracks that match the original quiz filters, or play only the remaining tracks."
            : $"Only {replacementCount} eligible replacement track(s) are available; {missing.Count} are needed to maintain the original quiz size.";
        RemainingButton.IsEnabled = availableCount > 0;
        ReplaceButton.IsEnabled = replacementCount >= missing.Count;
    }

    private void OnRemainingClick(object sender, RoutedEventArgs e)
    {
        Choice = MissingTrackChoice.Remaining;
        DialogResult = true;
    }

    private void OnReplaceClick(object sender, RoutedEventArgs e)
    {
        Choice = MissingTrackChoice.Replace;
        DialogResult = true;
    }
}
