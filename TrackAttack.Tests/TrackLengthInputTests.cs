using TrackAttack.Models;

namespace TrackAttack.Tests;

public sealed class TrackLengthInputTests
{
    [Fact]
    public void MissingStoredLimitFormatsAsUnlimited()
        => Assert.Equal("0:00", TrackLengthInput.Format(null));

    [Theory]
    [InlineData("0:00", 0)]
    [InlineData("1:00", 60)]
    [InlineData("2:30", 150)]
    [InlineData("9:5", 545)]
    [InlineData("59:00", 3540)]
    [InlineData("1440:00", 86400)]
    public void ParsesMinutesAndSeconds(string text, int seconds)
    {
        Assert.True(TrackLengthInput.TryParse(text, out var actual));
        Assert.Equal(seconds, actual);
        Assert.Equal(seconds == 0 ? "0:00" : $"{seconds / 60}:{seconds % 60:00}",
            TrackLengthInput.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("60")]
    [InlineData("1:")]
    [InlineData(":30")]
    [InlineData("1:60")]
    [InlineData("1:005")]
    [InlineData("1:2:3")]
    [InlineData("-1:30")]
    [InlineData("1440:01")]
    [InlineData("999999999999:00")]
    public void RejectsInvalidOrOutOfRangeValues(string text)
        => Assert.False(TrackLengthInput.TryParse(text, out _));

    [Theory]
    [InlineData("0:00", "0:00", true)]
    [InlineData("1:30", "0:00", true)]
    [InlineData("0:00", "2:00", true)]
    [InlineData("1:30", "1:30", true)]
    [InlineData("2:00", "1:59", false)]
    [InlineData("1:60", "2:00", false)]
    public void ValidatesBothBoundaries(string minimum, string maximum, bool expected)
        => Assert.Equal(expected, TrackLengthInput.TryParseRange(minimum, maximum, out _, out _));
}
