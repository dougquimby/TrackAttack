using TrackAttack.Services;

namespace TrackAttack.Tests;

public sealed class BroadGenreMatcherTests
{
    [Theory]
    [InlineData("Rock", "Grunge")]
    [InlineData("Rock", "Alternative rock")]
    [InlineData("Rock", "Classic Rock")]
    [InlineData("Rock", "AlternRock Alt. Rock")]
    [InlineData("Rock", "Alternative")]
    [InlineData("Rock", "Pop, Rock")]
    [InlineData("Rock", "Folk-Rock")]
    [InlineData("Hip-Hop", "Alternative, Pop, HIP-hop")]
    [InlineData("Hip-Hop", "Hip Hop Rap")]
    [InlineData("Hip-Hop", "Trip-Hop")]
    [InlineData("Instrumental", "Instrumental")]
    [InlineData("Instrumental", "Instrumental Rock")]
    [InlineData("Instrumental", "OST")]
    [InlineData("Instrumental", "Soundtrack")]
    [InlineData("Instrumental", "Classical")]
    [InlineData("Instrumental", "Film Soundtracks")]
    [InlineData("Pop", "Synthpop")]
    [InlineData("Pop", "Electro-Pop")]
    [InlineData("Metal", "Nu-Metal")]
    [InlineData("Electronic", "Dubstep")]
    [InlineData("R&B / Soul", "R&B, Pop")]
    [InlineData("R&B / Soul", "Rhythm and Blues")]
    [InlineData("Jazz", "Acapella Jazz")]
    [InlineData("Folk", "Folk-Rock")]
    public void MatchesCommonGenreLabels(string category, string genre)
        => Assert.True(BroadGenreMatcher.Matches(category, genre));

    [Theory]
    [InlineData("Rock", "Alternative hip-hop")]
    [InlineData("Rock", "Rockefeller")]
    [InlineData("Pop", "Popular")]
    [InlineData("Hip-Hop", "Hipster")]
    [InlineData("Instrumental", "Lost")]
    [InlineData("Rock", "! Unknown")]
    [InlineData("Rock", "")]
    public void DoesNotMatchUnrelatedLabels(string category, string genre)
        => Assert.False(BroadGenreMatcher.Matches(category, genre));

    [Fact]
    public void RejectsUnknownCategories()
        => Assert.Throws<ArgumentException>(() => BroadGenreMatcher.Matches("Not a category", "Rock"));
}
