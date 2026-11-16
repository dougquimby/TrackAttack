using TrackAttack.Models;
using TrackAttack.Services;

namespace TrackAttack.Tests;

public sealed class MetadataSuggestionSelectionTests
{
    [Fact]
    public void TopCandidateStartsCheckedAndChoosingAnotherMovesOnlyThatField()
    {
        var choices = new MetadataSuggestionSelectionSet([Suggestion("First", "1990"), Suggestion("Second", "2000")]);
        var first = choices.Selections[0];
        var second = choices.Selections[1];
        var notified = new List<string?>();
        first.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        Assert.True(first.StageTitle && first.StageArtist && first.StageAlbum && first.StageGenre && first.StageYear);
        Assert.False(second.StageTitle || second.StageArtist || second.StageAlbum || second.StageGenre || second.StageYear);
        choices.SetField(second, MetadataSuggestionField.Artist, true);

        Assert.False(first.StageArtist);
        Assert.True(second.StageArtist);
        Assert.True(first.StageTitle && first.StageAlbum && first.StageGenre && first.StageYear);
        Assert.Contains(nameof(MetadataSuggestionSelection.StageArtist), notified);
    }

    [Fact]
    public void CheckingAndClearingARowPreservesOtherRowsSelections()
    {
        var choices = new MetadataSuggestionSelectionSet([Suggestion("First", "1990"), Suggestion("Second", "2000")]);
        var first = choices.Selections[0];
        var second = choices.Selections[1];
        choices.SetField(second, MetadataSuggestionField.Year, true);
        choices.SetAll(first, false);

        Assert.False(first.StageTitle || first.StageArtist || first.StageAlbum || first.StageGenre || first.StageYear);
        Assert.True(second.StageYear);
        choices.SetAll(second, true);
        Assert.True(second.StageTitle && second.StageArtist && second.StageAlbum && second.StageGenre && second.StageYear);
        choices.SetAll(first, true);
        Assert.False(second.StageTitle || second.StageArtist || second.StageAlbum || second.StageGenre || second.StageYear);
        Assert.True(first.StageTitle && first.StageArtist && first.StageAlbum && first.StageGenre && first.StageYear);
    }

    [Fact]
    public void StagingCombinesCheckedFieldsFromDifferentCandidates()
    {
        var choices = new MetadataSuggestionSelectionSet([Suggestion("First", "1990"), Suggestion("Second", "2000")]);
        choices.SetField(choices.Selections[1], MetadataSuggestionField.Artist, true);
        choices.SetField(choices.Selections[1], MetadataSuggestionField.Year, true);
        var entry = new MetadataEditEntry();

        foreach (var choice in choices.Selections)
            Assert.True(MetadataSuggestionStager.Stage(entry, choice.Suggestion, replaceExisting: true, choice));

        Assert.Equal("First title", entry.Title);
        Assert.Equal("Second artist", entry.Artist);
        Assert.Equal("First album", entry.Album);
        Assert.Equal("First genre", entry.Genre);
        Assert.Equal("2000", entry.Year);
    }

    [Fact]
    public void StagingDoesNotReplaceValuesWithMissingOrInvalidSuggestions()
    {
        var suggestion = Suggestion("First", "bad") with { Genre = "" };
        var selection = new MetadataSuggestionSelection(suggestion);
        selection.StageGenre = true;
        selection.StageYear = true;
        var entry = new MetadataEditEntry { Genre = "Existing", Year = "1980" };

        Assert.False(MetadataSuggestionStager.Stage(entry, suggestion, replaceExisting: true, selection));
        Assert.Equal("Existing", entry.Genre);
        Assert.Equal("1980", entry.Year);
    }

    private static MetadataSuggestion Suggestion(string prefix, string year)
        => new("Test", $"{prefix} title", $"{prefix} artist", $"{prefix} album", $"{prefix} genre",
            year, null, 90, 0, "", "", null);
}
