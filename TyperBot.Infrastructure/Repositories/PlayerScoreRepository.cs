using Microsoft.EntityFrameworkCore;
using TyperBot.Domain.Entities;
using TyperBot.Infrastructure.Data;

namespace TyperBot.Infrastructure.Repositories;

public class PlayerScoreRepository : IPlayerScoreRepository
{
    private readonly TyperContext _context;

    public PlayerScoreRepository(TyperContext context)
    {
        _context = context;
    }

    public async Task<PlayerScore?> GetByIdAsync(int id)
    {
        return await _context.PlayerScores
            .Include(ps => ps.Prediction)
            .FirstOrDefaultAsync(ps => ps.Id == id);
    }

    public async Task<IEnumerable<PlayerScore>> GetByPlayerIdAsync(int playerId)
    {
        return await _context.PlayerScores
            .Include(ps => ps.Prediction)
            .Where(ps => ps.Prediction.PlayerId == playerId)
            .ToListAsync();
    }

    public async Task<PlayerScore> AddAsync(PlayerScore score)
    {
        await _context.PlayerScores.AddAsync(score);
        await _context.SaveChangesAsync();
        return score;
    }

    public async Task UpdateAsync(PlayerScore score)
    {
        _context.PlayerScores.Update(score);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var score = await GetByIdAsync(id);
        if (score != null)
        {
            _context.PlayerScores.Remove(score);
            await _context.SaveChangesAsync();
        }
    }
}

