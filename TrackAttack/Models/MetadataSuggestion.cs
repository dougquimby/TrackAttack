namespace TrackAttack.Models;

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

public sealed record MetadataSuggestionSearchRequest(
    string Title,
    string Artist,
    string Album,
    TimeSpan? Duration);
