using TrackAttack.Models;

namespace TrackAttack.Services;

public interface IMusicSource
{
    string SourceType { get; }
    Task<IEnumerable<Song>> GetSongsAsync(QuizConfig config);
    Task<IEnumerable<string>> GetAvailableGenresAsync(string sourceIdentifier);
    Task<IEnumerable<string>> GetAvailableDecadesAsync(string sourceIdentifier);
    IEnumerable<Song> ScanDirectory(string directory);
}
