using TrackAttack.Models;

namespace TrackAttack.Services;

/// <summary>Provides songs and filter values from a music library.</summary>
public interface IMusicSource
{
    /// <summary>Gets the source identifier stored in quiz configuration and history.</summary>
    string SourceType { get; }
    /// <summary>Retrieves songs using the supplied source and filter configuration.</summary>
    /// <param name="config">The requested source, filters, and song count.</param>
    /// <returns>The matching songs.</returns>
    Task<IEnumerable<Song>> GetSongsAsync(QuizConfig config);
    /// <summary>Retrieves the available genre filter values for a source.</summary>
    /// <param name="sourceIdentifier">The source to inspect.</param>
    /// <returns>The available genre values.</returns>
    Task<IEnumerable<string>> GetAvailableGenresAsync(string sourceIdentifier);
    /// <summary>Retrieves the available decade filter values for a source.</summary>
    /// <param name="sourceIdentifier">The source to inspect.</param>
    /// <returns>The available decade values.</returns>
    Task<IEnumerable<string>> GetAvailableDecadesAsync(string sourceIdentifier);
    /// <summary>Scans a directory for supported songs.</summary>
    /// <param name="directory">The root directory to scan.</param>
    /// <returns>The songs found under the directory.</returns>
    IEnumerable<Song> ScanDirectory(string directory);
}
