namespace TrackAttack.Models;

/// <summary>Determines how quiz time is allocated.</summary>
public enum TimerMode
{
    /// <summary>Use one countdown for the entire quiz.</summary>
    TotalQuiz,
    /// <summary>Use an independent countdown for each song.</summary>
    PerSong
}

/// <summary>Determines how a clip start position is selected.</summary>
public enum ClipStartMode
{
    /// <summary>Use the configured fixed start.</summary>
    Fixed,
    /// <summary>Choose and retain a random start for each song.</summary>
    Random
}

/// <summary>Determines what happens when a clip reaches its end.</summary>
public enum ClipPlaybackMode
{
    /// <summary>Repeat the clip.</summary>
    Loop,
    /// <summary>Stop playback without advancing.</summary>
    PlayOnceStop,
    /// <summary>Advance after playing the clip once.</summary>
    PlayOnceAutoAdvance
}

/// <summary>Specifies the number of columns in the song grid.</summary>
public enum GridColumns
{
    /// <summary>Choose the layout automatically.</summary>
    Auto,
    /// <summary>Use one column.</summary>
    One,
    /// <summary>Use two columns.</summary>
    Two,
    /// <summary>Use three columns.</summary>
    Three,
    /// <summary>Use four columns.</summary>
    Four
}

/// <summary>Specifies which song information players must guess.</summary>
public enum QuizMode
{
    /// <summary>Guess the song title.</summary>
    GuessSongTitle,
    /// <summary>Guess the artist.</summary>
    GuessArtist,
    /// <summary>Guess the title and artist separately.</summary>
    GuessBoth
}

/// <summary>Identifies a type of clue that can be revealed for a song.</summary>
public enum HintType
{
    /// <summary>Reveal the title's initial.</summary>
    TitleInitial,
    /// <summary>Reveal the artist's initial.</summary>
    ArtistInitial,
    /// <summary>Reveal a scrambled title.</summary>
    ScrambledTitle,
    /// <summary>Reveal a scrambled artist.</summary>
    ScrambledArtist,
    /// <summary>Reveal the genre.</summary>
    Genre,
    /// <summary>Reveal the year.</summary>
    Year,
    /// <summary>Show the album cover.</summary>
    AlbumCoverArt
}
