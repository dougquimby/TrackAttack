using TrackAttack.Models;

namespace TrackAttack.Data;

public interface IQuizHistoryRepository
{
    Task SaveAsync(QuizSession session);
    Task<IEnumerable<QuizSession>> GetAllAsync();
    Task<QuizSession?> GetByIdAsync(int id);
    Task<IReadOnlyList<Song>> GetRecentQuizSongsAsync(int quizCount, string sourceType, string sourceDirectory);
    Task ClearAllAsync();
}
