using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TrackAttack.Models;
using TrackAttack.Settings;
using TrackAttack.Views;

namespace TrackAttack.Tests;

[CollectionDefinition("Metadata WPF", DisableParallelization = true)]
public sealed class MetadataWpfCollection { }

[Collection("Metadata WPF")]
public sealed class MetadataViewInteractionTests
{
    [Fact]
    public async Task CandidateMenusAndCheckboxesStageFieldsFromDifferentRows()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var application = new Application();
            Exception? failure = null;
            try
            {
                application.Resources.Add(typeof(Button), new Style(typeof(Button)));
                application.Resources.Add("ActionButton", new Style(typeof(Button)));
                var view = new MetadataView(new AppSettings());
                var candidates = new MetadataSuggestionSelectionSet(
                    [Suggestion("First", "1990"), Suggestion("Second", "2000")]);
                var entry = new MetadataEditEntry();
                var related = new MetadataEditEntry();
                foreach (var track in new[] { entry, related })
                {
                    SetOriginal(track, "OriginalAlbum", "Original album");
                    SetOriginal(track, "OriginalArtist", "Original artist");
                }
                var entries = Assert.IsType<ObservableCollection<MetadataEditEntry>>(
                    typeof(MetadataView).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view));
                entries.Add(entry);
                entries.Add(related);
                SetField(view, "_candidateSelections", candidates);
                SetField(view, "_candidateTarget", entry);

                var grid = Assert.IsType<DataGrid>(view.FindName("CandidatesGrid"));
                grid.ItemsSource = candidates.Selections;
                grid.SelectedItem = candidates.Selections[1];
                SetField(view, "_candidateContextTarget", candidates.Selections[1]);
                var checkAll = Assert.IsType<MenuItem>(view.FindName("CheckCandidateRowMenu"));
                checkAll.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.False(candidates.Selections[0].StageTitle);
                Assert.True(candidates.Selections[1].StageTitle && candidates.Selections[1].StageYear);
                var uncheckAll = Assert.IsType<MenuItem>(view.FindName("UncheckCandidateRowMenu"));
                uncheckAll.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.False(candidates.Selections[1].StageTitle || candidates.Selections[1].StageYear);

                candidates.SetAll(candidates.Selections[0], true);
                var artistColumn = Assert.IsType<DataGridTemplateColumn>(grid.Columns[2]);
                var cell = Assert.IsType<DockPanel>(artistColumn.CellTemplate.LoadContent());
                cell.DataContext = candidates.Selections[1];
                var checkbox = Assert.IsType<CheckBox>(cell.Children[0]);
                checkbox.IsChecked = true;
                checkbox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.False(candidates.Selections[0].StageArtist);
                Assert.True(candidates.Selections[1].StageArtist);
                candidates.SetField(candidates.Selections[1], MetadataSuggestionField.Year, true);
                Assert.IsType<CheckBox>(view.FindName("PropagateAlbumFieldsCheck")).IsChecked = true;

                var stage = Assert.IsType<Button>(view.FindName("StageCandidateButton"));
                stage.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal("First title", entry.Title);
                Assert.Equal("Second artist", entry.Artist);
                Assert.Equal("2000", entry.Year);
                Assert.Null(entry.SuggestionMatchScore);
                Assert.Contains("multiple", entry.SuggestionReviewStatus);
                Assert.Equal("First genre", related.Genre);
                Assert.Equal("2000", related.Year);
                Assert.Null(related.SuggestionMatchScore);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                application.Shutdown();
            }
            if (failure is null)
                completion.SetResult();
            else
                completion.SetException(failure);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }

    private static void SetField(MetadataView view, string name, object value)
        => typeof(MetadataView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(view, value);

    private static void SetOriginal(MetadataEditEntry entry, string name, string value)
        => typeof(MetadataEditEntry).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entry, value);

    private static MetadataSuggestion Suggestion(string prefix, string year)
        => new("Test", $"{prefix} title", $"{prefix} artist", $"{prefix} album", $"{prefix} genre",
            year, null, 90, 0, "", "", null);
}
