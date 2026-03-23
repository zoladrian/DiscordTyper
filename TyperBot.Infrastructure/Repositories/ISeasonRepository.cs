using TyperBot.Domain.Entities;

namespace TyperBot.Infrastructure.Repositories;

public interface ISeasonRepository
{
    Task<Season?> GetByIdAsync(int id);
    Task<Season?> GetByIdWithRoundsAndMatchesAsync(int id);
    Task<Season?> GetActiveSeasonAsync();
    Task<IEnumerable<Season>> GetAllActiveSeasonsAsync();
    Task<IEnumerable<Season>> GetAllAsync(); // ← Already exists
    Task<Season> AddAsync(Season season);
    Task UpdateAsync(Season season);
    Task DeleteAsync(int id);
}

