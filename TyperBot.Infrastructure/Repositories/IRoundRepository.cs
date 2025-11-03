using TyperBot.Domain.Entities;

namespace TyperBot.Infrastructure.Repositories;

public interface IRoundRepository
{
    Task<Round?> GetByIdAsync(int id);
    Task<Round?> GetByNumberAsync(int seasonId, int number);
    Task<IEnumerable<Round>> GetBySeasonIdAsync(int seasonId);
    Task<Round> AddAsync(Round round);
    Task UpdateAsync(Round round);
    Task DeleteAsync(int id);
}

