using Microsoft.EntityFrameworkCore;
using TyperBot.Domain.Entities;
using TyperBot.Infrastructure.Data;

namespace TyperBot.Infrastructure.Repositories;

public class PlayerRepository : IPlayerRepository
{
    private readonly TyperContext _context;

    public PlayerRepository(TyperContext context)
    {
        _context = context;
    }

    public async Task<Player?> GetByIdAsync(int id)
    {
        return await _context.Players.FindAsync(id);
    }

    public async Task<Player?> GetByDiscordUserIdAsync(ulong discordUserId)
    {
        return await _context.Players
            .Include(p => p.PlayerScores)
            .Include(p => p.Predictions)
                .ThenInclude(pr => pr.PlayerScore)
            .FirstOrDefaultAsync(p => p.DiscordUserId == discordUserId);
    }

    public async Task<IEnumerable<Player>> GetActivePlayersAsync()
    {
        return await _context.Players
            .Where(p => p.IsActive)
            .Include(p => p.PlayerScores)
            .Include(p => p.Predictions)
                .ThenInclude(pr => pr.PlayerScore)
            .ToListAsync();
    }

    public async Task<Player> AddAsync(Player player)
    {
        await _context.Players.AddAsync(player);
        await _context.SaveChangesAsync();
        return player;
    }

    public async Task UpdateAsync(Player player)
    {
        _context.Players.Update(player);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var player = await GetByIdAsync(id);
        if (player != null)
        {
            _context.Players.Remove(player);
            await _context.SaveChangesAsync();
        }
    }
}

