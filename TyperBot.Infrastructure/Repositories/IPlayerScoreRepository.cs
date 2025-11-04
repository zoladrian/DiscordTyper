using TyperBot.Domain.Entities;

namespace TyperBot.Infrastructure.Repositories;

public interface IPlayerScoreRepository
{
    Task<PlayerScore?> GetByIdAsync(int id);
    Task<IEnumerable<PlayerScore>> GetByPlayerIdAsync(int playerId);
    Task<IEnumerable<PlayerScore>> GetAllAsync(); // ← For demo data cleanup
    Task<PlayerScore> AddAsync(PlayerScore score);
    Task UpdateAsync(PlayerScore score);
    Task DeleteAsync(int id);
}

