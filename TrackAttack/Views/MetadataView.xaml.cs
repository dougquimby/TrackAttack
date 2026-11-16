using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Settings;

namespace TrackAttack.Views;

/// <summary>Displays and stages local-library metadata edits.</summary>
public partial class MetadataView : UserControl
{
    private const int MaximumDurationDifferenceSeconds = 10;
    private readonly LocalMetadataManager _manager = new();
    private readonly ObservableCollection<MetadataEditEntry> _entries = [];
    private CancellationTokenSource? _activeLookup;
    private MetadataEditEntry? _candidateTarget;
    private MetadataSuggestionSelection? _candidateContextTarget;
    private MetadataSuggestionSelectionSet _candidateSelections = new([]);
    private bool _initializingProvider = true;
    private bool _gridRefreshPending;

    /// <summary>Creates the metadata editing view.</summary>
    public MetadataView() : this(AppSettings.Load())
    {
    }

    /// <summary>Creates the metadata editing view with the supplied settings.</summary>
    /// <param name="settings">The settings used to initialize the view.</param>
    public MetadataView(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        InitializeComponent();
        MetadataGrid.ItemsSource = _entries;
        FolderText.Text = settings.MusicDirectory;
        ProviderCombo.ItemsSource = Enum.GetValues<MetadataSuggestionProviderKind>();
        ProviderCombo.SelectedItem = settings.MetadataSuggestionProvider;
        _initializingProvider = false;
        ProviderCombo.SelectionChanged += OnProviderChanged;
        Unloaded += (_, _) => _activeLookup?.Cancel();
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "1. Scan your music folder, then select one or more tracks.\n\n" +
            "2. Choose MusicBrainz or TheAudioDB and click Lookup selected. Artist searches retry common punctuation and spacing variants (for example, Blink 182 and Blink-182).\n\n" +
            "3. For one selected track, check the fields you want from the candidates. Only one candidate can be checked per field; the top candidate starts checked. Stage checked fields combines the choices. Right-click a candidate row to check or uncheck all its fields. For multiple tracks, confident top matches are staged automatically; enable the bulk album option to look up once per original album and stage year/genre to matching tracks.\n\n" +
            "4. To apply checked genre/year values to other tracks from the same original album and artist, check the album propagation option above the candidate grid.\n\n" +
            "Staging never writes files. Choose Apply changes to review the before-and-after values for the entire batch. A recovery snapshot is created before writing, and Undo last apply restores it.",
            "Metadata fixer help",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
            await ScanFolderAsync(folder, progress);
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
        var readyForReview = 0;
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
                _candidateSelections = new MetadataSuggestionSelectionSet(candidates);
                CandidatesGrid.ItemsSource = _candidateSelections.Selections;
                CandidatesGrid.SelectedItem = _candidateSelections.Selections.FirstOrDefault();
                _candidateTarget = entry;
                CandidateTargetText.Text = candidates.Count == 0
                    ? $"No candidates for {entry.Title} — {entry.Artist}"
                    : $"For: {entry.Title} — {entry.Artist}";
                var candidate = candidates.FirstOrDefault();
                var durationDifference = candidate?.Duration is { } duration ? (duration - entry.AudioDuration).Duration() : (TimeSpan?)null;

                if (selected.Length == 1 && AlbumLookupCheck.IsChecked != true)
                {
                    entry.SuggestionReviewStatus = candidate is null
                        ? "No catalog match; review manually."
                        : $"{candidates.Count} candidate(s) found; select fields and stage a match.";
                    if (candidate is null)
                        unresolved++;
                    else
                        readyForReview++;
                    continue;
                }

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
                    MetadataSuggestionStager.Stage(entry, candidate, replaceExisting: true);
                entry.HasStagedCatalogCandidate = true;
                entry.SuggestionMatchScore = candidate.MatchScore;
                entry.SuggestionReviewStatus = $"{candidate.Provider} suggestion staged ({candidate.MatchScore}/100); review before applying.";
                staged++;
                RefreshGrid();
            }
            StatusText.Text = $"Lookup complete: {staged} suggestion(s) staged; {readyForReview} candidate set(s) ready for field review; {unresolved} need manual review. No files written.";
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

    private void OnMetadataGridRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(MetadataGrid, source) is DataGridRow row
            && !row.IsSelected)
        {
            MetadataGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
    }

    private void OnSelectAllTracksClick(object sender, RoutedEventArgs e) => MetadataGrid.SelectAll();

    private void OnClearTrackSelectionClick(object sender, RoutedEventArgs e) => MetadataGrid.UnselectAll();

    private void OnCandidateFieldCheckClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: MetadataSuggestionSelection selection, Tag: MetadataSuggestionField field } checkbox)
        {
            _candidateSelections.SetField(selection, field, checkbox.IsChecked == true);
            CandidatesGrid.SelectedItem = selection;
        }
    }

    private void OnCheckCandidateRowClick(object sender, RoutedEventArgs e) => SetCandidateRowFields(true);

    private void OnUncheckCandidateRowClick(object sender, RoutedEventArgs e) => SetCandidateRowFields(false);

    private void SetCandidateRowFields(bool selected)
    {
        if (_candidateContextTarget is not null)
            _candidateSelections.SetAll(_candidateContextTarget, selected);
    }

    private void OnCandidateContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = Mouse.DirectlyOver is DependencyObject source
            ? ItemsControl.ContainerFromElement(CandidatesGrid, source) as DataGridRow
            : null;
        _candidateContextTarget = row?.Item as MetadataSuggestionSelection;
        if (e.CursorLeft == -1)
            _candidateContextTarget = CandidatesGrid.SelectedItem as MetadataSuggestionSelection;
        if (_candidateContextTarget is not null)
            CandidatesGrid.SelectedItem = _candidateContextTarget;
        CheckCandidateRowMenu.IsEnabled = _candidateContextTarget is not null;
        UncheckCandidateRowMenu.IsEnabled = _candidateContextTarget is not null;
    }

    private void OnStageCandidateClick(object sender, RoutedEventArgs e)
    {
        if (_candidateTarget is null)
        {
            MessageBox.Show("Look up a track and choose catalog fields first.", "Candidate required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var stagedCandidates = new List<MetadataSuggestion>();
        var propagatedCandidates = new List<MetadataSuggestion>();
        foreach (var selection in _candidateSelections.Selections)
        {
            var suggestion = selection.Suggestion;
            if (!MetadataSuggestionStager.Stage(_candidateTarget, suggestion, replaceExisting: true, selection))
                continue;
            stagedCandidates.Add(suggestion);
            var includeGenre = selection.StageGenre && !string.IsNullOrWhiteSpace(suggestion.Genre);
            var includeYear = selection.StageYear && !string.IsNullOrWhiteSpace(suggestion.Year)
                && MetadataEditEntry.IsValidYear(suggestion.Year);
            if (PropagateAlbumFieldsCheck.IsChecked == true && (includeGenre || includeYear))
            {
                StageAlbumSuggestion(_candidateTarget, suggestion, includeGenre, includeYear);
                propagatedCandidates.Add(suggestion);
            }
        }

        if (stagedCandidates.Count == 0)
        {
            MessageBox.Show("Check at least one available candidate field to stage.", "No fields selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (propagatedCandidates.Count > 1)
        {
            foreach (var entry in GetAlbumTracks(_candidateTarget))
            {
                entry.SuggestionMatchScore = null;
                entry.SuggestionReviewStatus = "Album genre/year from multiple catalog candidates staged; review before applying.";
            }
        }
        _candidateTarget.HasStagedCatalogCandidate = true;
        _candidateTarget.SuggestionMatchScore = stagedCandidates.Count == 1 ? stagedCandidates[0].MatchScore : null;
        var description = stagedCandidates.Count == 1
            ? $"{stagedCandidates[0].Provider} candidate fields"
            : "Fields from multiple catalog candidates";
        _candidateTarget.SuggestionReviewStatus = propagatedCandidates.Count > 0
            ? $"{description} and selected album fields staged; review before applying."
            : $"{description} staged; review before applying.";
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
        SetBusy(true);
        var applying = false;
        try
        {
            StatusText.Text = "Preparing metadata change preview…";
            var changes = await _manager.PreviewAsync(changed);
            var preview = new MetadataChangesPreviewWindow(changes) { Owner = Window.GetWindow(this) };
            if (preview.ShowDialog() != true)
            {
                StatusText.Text = "Metadata changes remain staged.";
                return;
            }
            applying = true;
            StatusText.Text = $"Applying changes to {changed.Length} file(s)…";
            await _manager.ApplyAsync(changed);
            StatusText.Text = $"Applied metadata changes to {changed.Length} file(s).";
            await ScanFolderAsync(FolderText.Text.Trim());
        }
        catch (Exception ex)
        {
            StatusText.Text = applying ? "Metadata apply failed." : "Metadata preview failed.";
            MessageBox.Show($"Could not {(applying ? "apply" : "preview")} metadata:\n{ex.Message}",
                applying ? "Metadata apply failed" : "Metadata preview failed",
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
            await ScanFolderAsync(FolderText.Text.Trim());
            StatusText.Text = "Previous metadata changes were restored.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not undo metadata changes:\n{ex.Message}", "Undo failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private void OnRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        _gridRefreshPending = true;
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                if (!_gridRefreshPending)
                    return;

                _gridRefreshPending = false;
                RefreshGrid();
            }));
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
        if (MetadataGrid.Items is IEditableCollectionView { IsAddingNew: true }
            or IEditableCollectionView { IsEditingItem: true })
        {
            _gridRefreshPending = true;
            return;
        }

        MetadataGrid.Items.Refresh();
        ApplyFilter();
        UpdateSummary();
    }

    private async Task ScanFolderAsync(string folder, IProgress<int>? progress = null)
    {
        Cursor = Cursors.Wait;
        ForceCursor = true;
        try
        {
            var scanned = await _manager.ScanAsync(folder, progress);
            _entries.Clear();
            foreach (var entry in scanned)
                _entries.Add(entry);
            ApplyFilter();
            UpdateSummary();
        }
        finally
        {
            ClearValue(CursorProperty);
            ClearValue(ForceCursorProperty);
        }
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

    private void StageAlbumSuggestion(
        MetadataEditEntry source,
        MetadataSuggestion suggestion,
        bool includeGenre = true,
        bool includeYear = true)
    {
        foreach (var entry in GetAlbumTracks(source))
        {
            if (includeGenre && !string.IsNullOrWhiteSpace(suggestion.Genre))
                entry.Genre = suggestion.Genre;
            if (includeYear && MetadataEditEntry.IsValidYear(suggestion.Year) && !string.IsNullOrWhiteSpace(suggestion.Year))
                entry.Year = suggestion.Year;
            entry.SuggestionMatchScore = suggestion.MatchScore;
            entry.HasStagedCatalogCandidate = true;
            entry.SuggestionReviewStatus = $"Album year/genre from {Path.GetFileName(source.FilePath)} staged; review before applying.";
        }
    }

    private IEnumerable<MetadataEditEntry> GetAlbumTracks(MetadataEditEntry source)
        => !string.IsNullOrWhiteSpace(source.OriginalAlbum) && !string.IsNullOrWhiteSpace(source.OriginalArtist)
            ? _entries.Where(entry => entry.IsEditable
                && string.Equals(entry.OriginalAlbum, source.OriginalAlbum, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.OriginalArtist, source.OriginalArtist, StringComparison.OrdinalIgnoreCase))
            : [source];

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
        PropagateAlbumFieldsCheck.IsEnabled = !busy;
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
