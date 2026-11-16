using System.Windows;
using TrackAttack.Models;

namespace TrackAttack.Views;

public enum MissingTrackChoice { Cancel, Remaining, Replace }

public partial class ReplayMissingTracksWindow : Window
{
    public MissingTrackChoice Choice { get; private set; }

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
