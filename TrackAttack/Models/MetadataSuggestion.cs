namespace TrackAttack.Models;

/// <summary>Describes a catalog candidate and its match scores.</summary>
/// <param name="Provider">The catalog that supplied the candidate.</param>
/// <param name="Title">The suggested track title.</param>
/// <param name="Artist">The suggested artist.</param>
/// <param name="Album">The suggested album.</param>
/// <param name="Genre">The suggested genre, when available.</param>
/// <param name="Year">The suggested year, when available.</param>
/// <param name="Duration">The track duration, when available.</param>
/// <param name="MatchScore">The application match score from 0 to 100.</param>
/// <param name="MusicBrainzSearchScore">The catalog search score used as a tie-breaker when available.</param>
/// <param name="MatchDetails">A description of the match.</param>
/// <param name="ProviderItemId">The catalog's item identifier.</param>
/// <param name="SourceUri">An optional link to the catalog entry.</param>
public sealed record MetadataSuggestion(
    string Provider,
    string Title,
    string Artist,
    string Album,
    string Genre,
    string Year,
    TimeSpan? Duration,
    int MatchScore,
    byte MusicBrainzSearchScore,
    string MatchDetails,
    string ProviderItemId,
    Uri? SourceUri);

/// <summary>Contains existing track information used to search and score catalog candidates.</summary>
/// <param name="Title">The existing track title.</param>
/// <param name="Artist">The existing artist.</param>
/// <param name="Album">The existing album.</param>
/// <param name="Duration">The track duration, when known.</param>
public sealed record MetadataSuggestionSearchRequest(
    string Title,
    string Artist,
    string Album,
    TimeSpan? Duration);
