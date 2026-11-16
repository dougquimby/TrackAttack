using TrackAttack.Models;

namespace TrackAttack.Data;

/// <summary>Persists quiz sessions and retrieves their recorded results.</summary>
public interface IQuizHistoryRepository
{
    /// <summary>Saves a session and its song results.</summary>
    /// <param name="session">The completed or abandoned session to save.</param>
    Task SaveAsync(QuizSession session);
    /// <summary>Retrieves saved sessions with their song results, newest first.</summary>
    /// <returns>The saved sessions.</returns>
    Task<IEnumerable<QuizSession>> GetAllAsync();
    /// <summary>Retrieves a saved session and its song results.</summary>
    /// <param name="id">The saved session identifier.</param>
    /// <returns>The session, or <see langword="null"/> if it does not exist.</returns>
    Task<QuizSession?> GetByIdAsync(int id);
    /// <summary>Retrieves songs recorded in recent quizzes for a profile and source.</summary>
    /// <param name="quizCount">The maximum number of recent quizzes to consider.</param>
    /// <param name="sourceType">The source type stored with the quizzes.</param>
    /// <param name="sourceDirectory">The source identifier stored with the quizzes.</param>
    /// <param name="profileName">The player profile to match.</param>
    /// <returns>The recorded songs, including replaced local tracks when available.</returns>
    Task<IReadOnlyList<Song>> GetRecentQuizSongsAsync(
        int quizCount, string sourceType, string sourceDirectory, string profileName);
    /// <summary>Deletes a saved session and its song results.</summary>
    /// <param name="id">The saved session identifier.</param>
    /// <returns><see langword="true"/> if the session existed and was deleted.</returns>
    Task<bool> DeleteAsync(int id);
    /// <summary>Deletes all saved sessions and their song results.</summary>
    Task ClearAllAsync();
}
