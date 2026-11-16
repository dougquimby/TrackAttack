using TrackAttack.Models;

namespace TrackAttack.Services;

/// <summary>Searches a catalog for metadata candidates to review before editing local tags.</summary>
public interface IMetadataSuggestionProvider
{
    /// <summary>Searches for and ranks metadata candidates.</summary>
    /// <param name="request">The existing track information used for the search and scoring.</param>
    /// <param name="cancellationToken">A token that cancels the search.</param>
    /// <param name="progress">An optional recipient for progress messages.</param>
    /// <returns>The candidates returned by the provider.</returns>
    Task<IReadOnlyList<MetadataSuggestion>> SearchAsync(
        MetadataSuggestionSearchRequest request,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null);
}
