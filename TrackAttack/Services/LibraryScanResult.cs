using TrackAttack.Models;

namespace TrackAttack.Services;

public sealed record LibraryScanIssue(string Path, string Message);

public sealed record LibraryScanResult(IReadOnlyList<Song> Songs, IReadOnlyList<LibraryScanIssue> Issues);
