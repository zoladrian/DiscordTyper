using TyperBot.Domain.Entities;

namespace TyperBot.Infrastructure.Repositories;

public interface IPlayerRepository
{
    Task<Player?> GetByIdAsync(int id);
    Task<Player?> GetByDiscordUserIdAsync(ulong discordUserId);
    Task<IEnumerable<Player>> GetActivePlayersAsync();
    Task<IEnumerable<Player>> GetAllAsync(); // ← For demo data cleanup
    Task<Player> AddAsync(Player player);
    Task UpdateAsync(Player player);
    Task DeleteAsync(int id);
}

