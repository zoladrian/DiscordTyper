using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;

namespace TyperBot.Infrastructure.Repositories;

public interface IMatchRepository
{
    Task<Match?> GetByIdAsync(int id);
    Task<IEnumerable<Match>> GetByRoundIdAsync(int roundId);
    Task<IEnumerable<Match>> GetUpcomingMatchesAsync();
    Task<IEnumerable<Match>> GetAllAsync(); // ← For demo data cleanup
    Task<Match> AddAsync(Match match);
    Task UpdateAsync(Match match);
    Task DeleteAsync(int id);
}

