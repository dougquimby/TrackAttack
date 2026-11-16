using TrackAttack.Models;

namespace TrackAttack.Services;

/// <summary>Describes a file or folder that could not be scanned.</summary>
/// <param name="Path">The affected path.</param>
/// <param name="Message">The reason the path could not be scanned.</param>
public sealed record LibraryScanIssue(string Path, string Message);

/// <summary>Contains the songs and issues found in a local library scan.</summary>
/// <param name="Songs">The readable songs.</param>
/// <param name="Issues">The paths that could not be scanned.</param>
public sealed record LibraryScanResult(IReadOnlyList<Song> Songs, IReadOnlyList<LibraryScanIssue> Issues);
