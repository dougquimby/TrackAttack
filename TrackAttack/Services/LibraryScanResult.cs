using TrackAttack.Models;

namespace TrackAttack.Services;

/// <summary>Describes a problem encountered while scanning a library.</summary>
/// <param name="Path">The affected path.</param>
/// <param name="Message">The reason the path could not be scanned.</param>
public sealed record LibraryScanIssue(string Path, string Message);

/// <summary>Contains the songs and issues found in a local library scan.</summary>
/// <param name="Songs">The readable songs.</param>
/// <param name="Issues">Problems encountered while scanning.</param>
public sealed record LibraryScanResult(IReadOnlyList<Song> Songs, IReadOnlyList<LibraryScanIssue> Issues)
{
    /// <summary>Gets the number of tracks reused from the metadata cache.</summary>
    public int CachedSongs { get; init; }
}
