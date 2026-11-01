using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Settings;

namespace TrackAttack.Views;

public partial class MetadataView : UserControl
{
    private const int MaximumDurationDifferenceSeconds = 10;
    private readonly LocalMetadataManager _manager = new();
    private readonly ObservableCollection<MetadataEditEntry> _entries = [];
    private CancellationTokenSource? _activeLookup;
    private MetadataEditEntry? _candidateTarget;
    private bool _initializingProvider = true;

    public MetadataView()
    {
        InitializeComponent();
        MetadataGrid.ItemsSource = _entries;
        FolderText.Text = AppSettings.Load().MusicDirectory;
        ProviderCombo.ItemsSource = Enum.GetValues<MetadataSuggestionProviderKind>();
        ProviderCombo.SelectedItem = AppSettings.Load().MetadataSuggestionProvider;
        _initializingProvider = false;
        ProviderCombo.SelectionChanged += OnProviderChanged;
        Unloaded += (_, _) => _activeLookup?.Cancel();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select music library", FolderName = FolderText.Text };
        if (dialog.ShowDialog() == true)
            FolderText.Text = dialog.FolderName;
    }

    private async void OnScanClick(object sender, RoutedEventArgs e)
    {
        var folder = FolderText.Text.Trim();
        if (!Directory.Exists(folder))
        {
            MessageBox.Show("Select an existing folder to scan.", "Folder required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SetBusy(true);
        StatusText.Text = "Scanning audio files…";
        try
        {
            var progress = new Progress<int>(count => StatusText.Text = $"Scanning… {count:N0} files checked");
            var scanned = await _manager.ScanAsync(folder, progress);
            _entries.Clear();
            foreach (var entry in scanned)
                _entries.Add(entry);
            ApplyFilter();
            UpdateSummary();
            StatusText.Text = $"Scan complete: {_entries.Count:N0} audio files. Edits and suggestions remain staged until Apply Changes.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Scan failed.";
            MessageBox.Show($"Could not scan this folder:\n{ex.Message}", "Metadata scan failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingProvider || ProviderCombo.SelectedItem is not MetadataSuggestionProviderKind provider)
            return;
        var settings = AppSettings.Load();
        settings.MetadataSuggestionProvider = provider;
        try { settings.Save(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the selected lookup provider:\n{ex.Message}", "Settings save failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnLookupSelectedClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MinimumMatchScoreText.Text, out var minimumMatchScore)
            || minimumMatchScore is < 0 or > 100)
        {
            MessageBox.Show("Enter a minimum match score between 0 and 100.", "Invalid score threshold",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            MinimumMatchScoreText.Focus();
            MinimumMatchScoreText.SelectAll();
            return;
        }

        MetadataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        MetadataGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var selected = MetadataGrid.SelectedItems.OfType<MetadataEditEntry>().Where(entry => entry.IsEditable).ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show("Select at least one readable track.", "No tracks selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var representatives = AlbumLookupCheck.IsChecked == true ? SelectAlbumRepresentatives(selected) : selected;
        using var cancellation = new CancellationTokenSource();
        _activeLookup = cancellation;
        SetBusy(true);
        var provider = CreateProvider();
        var staged = 0;
        var unresolved = 0;
        try
        {
            for (var index = 0; index < representatives.Length; index++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var entry = representatives[index];
                var providerName = (ProviderCombo.SelectedItem as MetadataSuggestionProviderKind?)?.ToString()
                    ?? MetadataSuggestionProviderKind.MusicBrainz.ToString();
                StatusText.Text = $"Looking up {index + 1} of {representatives.Length} with {providerName}. Cancel is available.";
                var candidates = await provider.SearchAsync(new MetadataSuggestionSearchRequest(
                    entry.Title, entry.Artist, entry.Album, entry.AudioDuration), cancellation.Token,
                    new Progress<string>(message => StatusText.Text = message));
                CandidatesGrid.ItemsSource = candidates;
                _candidateTarget = entry;
                var candidate = candidates.FirstOrDefault();
                var durationDifference = candidate?.Duration is { } duration ? (duration - entry.AudioDuration).Duration() : (TimeSpan?)null;
                if (candidate is null || candidate.MatchScore < minimumMatchScore
                    || durationDifference is null || durationDifference.Value.TotalSeconds > MaximumDurationDifferenceSeconds)
                {
                    entry.SuggestionReviewStatus = candidate is null ? "No catalog match; review manually."
                        : $"Top match not confident ({candidate.MatchScore}/100, duration difference {FormatDurationDifference(durationDifference)}); review manually.";
                    unresolved++;
                    continue;
                }

                if (AlbumLookupCheck.IsChecked == true)
                    StageAlbumSuggestion(entry, candidate);
                else
                    StageSuggestion(entry, candidate, replaceExisting: true);
                entry.HasStagedCatalogCandidate = true;
                entry.SuggestionMatchScore = candidate.MatchScore;
                entry.SuggestionReviewStatus = $"{candidate.Provider} suggestion staged ({candidate.MatchScore}/100); review before applying.";
                staged++;
                RefreshGrid();
            }
            StatusText.Text = $"Lookup complete: {staged} suggestion(s) staged for review; {unresolved} need manual review. No files written.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText.Text = $"Lookup cancelled. {staged} earlier result(s) remain staged; no files were written.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Catalog lookup failed.";
            MessageBox.Show($"Lookup failed; earlier suggestions remain staged:\n{ex.Message}", "Lookup failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _activeLookup = null;
            SetBusy(false);
            RefreshGrid();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => _activeLookup?.Cancel();

    private void OnStageCandidateClick(object sender, RoutedEventArgs e)
    {
        if (_candidateTarget is null || CandidatesGrid.SelectedItem is not MetadataSuggestion suggestion)
        {
            MessageBox.Show("Select a track and a catalog candidate first.", "Candidate required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        StageSuggestion(_candidateTarget, suggestion, replaceExisting: true);
        _candidateTarget.HasStagedCatalogCandidate = true;
        _candidateTarget.SuggestionMatchScore = suggestion.MatchScore;
        _candidateTarget.SuggestionReviewStatus = $"{suggestion.Provider} candidate staged; review before applying.";
        RefreshGrid();
    }

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        MetadataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        MetadataGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var changed = _entries.Where(entry => entry.IsEditable && entry.IsModified).ToArray();
        if (changed.Length == 0)
        {
            MessageBox.Show("There are no staged metadata changes to apply.", "No changes", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (changed.Any(entry => !MetadataEditEntry.IsValidYear(entry.Year)))
        {
            MessageBox.Show($"Years must be blank or between 1000 and {DateTime.Now.Year + 1}.", "Invalid year",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show($"Write reviewed metadata changes to {changed.Length} file(s)? A recovery snapshot will be created.",
                "Apply metadata changes", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        SetBusy(true);
        StatusText.Text = $"Applying changes to {changed.Length} file(s)…";
        try
        {
            await _manager.ApplyAsync(changed);
            StatusText.Text = $"Applied metadata changes to {changed.Length} file(s).";
            await ScanCurrentFolderAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Metadata apply failed.";
            MessageBox.Show($"Could not apply metadata:\n{ex.Message}", "Metadata apply failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private async void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (!_manager.HasUndoAvailable)
        {
            MessageBox.Show("There is no metadata edit to undo.", "Nothing to undo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show("Restore the previous metadata snapshot?", "Undo metadata changes",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        SetBusy(true);
        try
        {
            await _manager.UndoLastApplyAsync();
            await ScanCurrentFolderAsync();
            StatusText.Text = "Previous metadata changes were restored.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not undo metadata changes:\n{ex.Message}", "Undo failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(RefreshGrid);
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            ApplyFilter();
    }

    private void ApplyFilter()
    {
        MetadataGrid.Items.Filter = item => item is MetadataEditEntry entry
            && (NeedsReviewCheck.IsChecked != true || entry.HasMetadataIssues || entry.IsModified || entry.HasStagedCatalogCandidate);
    }

    private void RefreshGrid()
    {
        MetadataGrid.Items.Refresh();
        ApplyFilter();
        UpdateSummary();
    }

    private async Task ScanCurrentFolderAsync()
    {
        var scanned = await _manager.ScanAsync(FolderText.Text.Trim());
        _entries.Clear();
        foreach (var entry in scanned) _entries.Add(entry);
        ApplyFilter();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var issues = _entries.Count(entry => entry.HasMetadataIssues);
        var staged = _entries.Count(entry => entry.IsEditable && entry.IsModified);
        SummaryText.Text = $"{_entries.Count:N0} audio file(s) scanned; {issues:N0} need review; {staged:N0} change(s) staged.";
    }

    private static MetadataEditEntry[] SelectAlbumRepresentatives(IEnumerable<MetadataEditEntry> entries)
        => entries.GroupBy(entry => string.IsNullOrWhiteSpace(entry.OriginalAlbum) || string.IsNullOrWhiteSpace(entry.OriginalArtist)
                ? $"file:{entry.FilePath}" : $"{entry.OriginalAlbum.Trim()}\0{entry.OriginalArtist.Trim()}",
            StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();

    private void StageAlbumSuggestion(MetadataEditEntry source, MetadataSuggestion suggestion)
    {
        var related = !string.IsNullOrWhiteSpace(source.OriginalAlbum) && !string.IsNullOrWhiteSpace(source.OriginalArtist)
            ? _entries.Where(entry => entry.IsEditable
                && string.Equals(entry.OriginalAlbum, source.OriginalAlbum, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.OriginalArtist, source.OriginalArtist, StringComparison.OrdinalIgnoreCase))
            : [source];
        foreach (var entry in related)
        {
            if (!string.IsNullOrWhiteSpace(suggestion.Genre)) entry.Genre = suggestion.Genre;
            if (MetadataEditEntry.IsValidYear(suggestion.Year) && !string.IsNullOrWhiteSpace(suggestion.Year)) entry.Year = suggestion.Year;
            entry.SuggestionMatchScore = suggestion.MatchScore;
            entry.HasStagedCatalogCandidate = true;
            entry.SuggestionReviewStatus = $"Album year/genre from {Path.GetFileName(source.FilePath)} staged; review before applying.";
        }
    }

    private static void StageSuggestion(MetadataEditEntry entry, MetadataSuggestion suggestion, bool replaceExisting)
    {
        if (!string.IsNullOrWhiteSpace(suggestion.Title) && (replaceExisting || string.IsNullOrWhiteSpace(entry.Title)))
            entry.Title = suggestion.Title;
        if (!string.IsNullOrWhiteSpace(suggestion.Artist) && (replaceExisting || string.IsNullOrWhiteSpace(entry.Artist)))
            entry.Artist = suggestion.Artist;
        if (!string.IsNullOrWhiteSpace(suggestion.Album) && (replaceExisting || string.IsNullOrWhiteSpace(entry.Album)))
            entry.Album = suggestion.Album;
        if (!string.IsNullOrWhiteSpace(suggestion.Genre) && (replaceExisting || string.IsNullOrWhiteSpace(entry.Genre)))
            entry.Genre = suggestion.Genre;
        if (!string.IsNullOrWhiteSpace(suggestion.Year) && MetadataEditEntry.IsValidYear(suggestion.Year)
            && (replaceExisting || string.IsNullOrWhiteSpace(entry.Year))) entry.Year = suggestion.Year;
    }

    private IMetadataSuggestionProvider CreateProvider()
        => ProviderCombo.SelectedItem is MetadataSuggestionProviderKind.TheAudioDB
            ? new TheAudioDbMetadataSuggestionProvider()
            : new MusicBrainzMetadataSuggestionProvider();

    private void SetBusy(bool busy)
    {
        BrowseButton.IsEnabled = !busy;
        FolderText.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy;
        LookupButton.IsEnabled = !busy;
        CancelLookupButton.IsEnabled = busy && _activeLookup is not null;
        ProviderCombo.IsEnabled = !busy;
        AlbumLookupCheck.IsEnabled = !busy;
        MinimumMatchScoreText.IsEnabled = !busy;
        MetadataGrid.IsEnabled = !busy;
        CandidatesGrid.IsEnabled = !busy;
        StageCandidateButton.IsEnabled = !busy;
        ApplyButton.IsEnabled = !busy;
        UndoButton.IsEnabled = !busy;
    }

    private static string FormatDurationDifference(TimeSpan? difference)
        => difference is { } value ? $"{value.TotalSeconds:F0}s" : "unknown";
}
