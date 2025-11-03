using TyperBot.Domain.Entities;

namespace TyperBot.Infrastructure.Repositories;

public interface ISeasonRepository
{
    Task<Season?> GetByIdAsync(int id);
    Task<Season?> GetActiveSeasonAsync();
    Task<IEnumerable<Season>> GetAllAsync();
    Task<Season> AddAsync(Season season);
    Task UpdateAsync(Season season);
    Task DeleteAsync(int id);
}

