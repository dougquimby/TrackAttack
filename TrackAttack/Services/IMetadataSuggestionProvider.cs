using TrackAttack.Models;

namespace TrackAttack.Services;

public interface IMetadataSuggestionProvider
{
    Task<IReadOnlyList<MetadataSuggestion>> SearchAsync(
        MetadataSuggestionSearchRequest request,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null);
}
