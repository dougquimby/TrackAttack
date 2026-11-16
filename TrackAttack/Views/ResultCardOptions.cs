namespace TrackAttack.Views;

/// <summary>Specifies which quiz tracks are included in a result card.</summary>
public enum ResultCardTracks
{
    /// <summary>Do not include track details.</summary>
    None,
    /// <summary>Include all tracks, including mulligan replacements.</summary>
    All,
    /// <summary>Include only final tracks that earned points after penalties.</summary>
    PointsEarned
}

/// <summary>Controls optional sections on an exported result card.</summary>
public sealed class ResultCardOptions
{
    /// <summary>Gets or sets the tracks to include.</summary>
    public ResultCardTracks Tracks { get; set; }
    /// <summary>Gets or sets whether to include recorded accepted guesses.</summary>
    public bool IncludeGuesses { get; set; }
    /// <summary>Gets or sets whether to include the quiz settings.</summary>
    public bool IncludeSettings { get; set; }
    /// <summary>Gets or sets the leaderboard position, when selected and available.</summary>
    public ResultCardRank? Rank { get; set; }
}

/// <summary>Describes a result's position among comparable saved attempts.</summary>
/// <param name="Position">The one-based position.</param>
/// <param name="Attempts">The number of comparable attempts.</param>
public sealed record ResultCardRank(int Position, int Attempts);
