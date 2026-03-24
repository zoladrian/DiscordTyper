using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;

namespace TyperBot.Infrastructure.Repositories;

public interface IMatchRepository
{
    Task<Match?> GetByIdAsync(int id);

    /// <summary>When <paramref name="includeRound"/> is false, skips loading <see cref="Match.Round"/> (faster when only match row fields are needed).</summary>
    Task<Match?> GetByIdAsync(int id, bool includeRound);
    Task<IEnumerable<Match>> GetByRoundIdAsync(int roundId);
    Task<IEnumerable<Match>> GetUpcomingMatchesAsync();

    /// <summary>
    /// Scheduled matches whose thread-creation time has passed and that have not started yet (for background thread creation).
    /// </summary>
    Task<IEnumerable<Match>> GetMatchesReadyForThreadCreationAsync(DateTimeOffset now);

    /// <summary>
    /// Non-finished, non-cancelled matches that started on or before <paramref name="startedOnOrBeforeUtc"/> and do not have both scores set (for admin reminders).
    /// </summary>
    Task<IEnumerable<Match>> GetMatchesPossiblyAwaitingResultEntryAsync(DateTimeOffset startedOnOrBeforeUtc);

    /// <summary>
    /// Mecze z wątkiem, bez ujawnionych typów, nieodwołane — do okresowej aktualizacji karty (przycisk „Ujawnij typy” po starcie meczu).
    /// Filtrowanie „start już minął” w pamięci (SQLite/DateTimeOffset).
    /// </summary>
    Task<IEnumerable<Match>> GetMatchesNeedingRevealCardRefreshAsync(DateTimeOffset nowUtc, TimeSpan maxAgeSinceStart);

    Task<Match?> GetByThreadIdAsync(ulong threadId);

    Task<IEnumerable<Match>> GetAllAsync(); // ← For demo data cleanup
    Task<Match> AddAsync(Match match);
    Task UpdateAsync(Match match);
    Task DeleteAsync(int id);
}

