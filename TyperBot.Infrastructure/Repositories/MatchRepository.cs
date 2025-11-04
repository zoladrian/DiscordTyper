using Microsoft.EntityFrameworkCore;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Data;

namespace TyperBot.Infrastructure.Repositories;

public class MatchRepository : IMatchRepository
{
    private readonly TyperContext _context;

    public MatchRepository(TyperContext context)
    {
        _context = context;
    }

    public async Task<Match?> GetByIdAsync(int id)
    {
        return await _context.Matches
            .Include(m => m.Round)
            .Include(m => m.Predictions)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<IEnumerable<Match>> GetByRoundIdAsync(int roundId)
    {
        return await _context.Matches
            .Where(m => m.RoundId == roundId)
            .OrderBy(m => m.StartTime)
            .ToListAsync();
    }

    public async Task<IEnumerable<Match>> GetUpcomingMatchesAsync()
    {
        return await _context.Matches
            .Where(m => m.Status == MatchStatus.Scheduled && m.StartTime > DateTimeOffset.UtcNow)
            .OrderBy(m => m.StartTime)
            .ToListAsync();
    }

    public async Task<IEnumerable<Match>> GetAllAsync()
    {
        return await _context.Matches.ToListAsync();
    }

    public async Task<Match> AddAsync(Match match)
    {
        await _context.Matches.AddAsync(match);
        await _context.SaveChangesAsync();
        return match;
    }

    public async Task UpdateAsync(Match match)
    {
        _context.Matches.Update(match);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var match = await GetByIdAsync(id);
        if (match != null)
        {
            _context.Matches.Remove(match);
            await _context.SaveChangesAsync();
        }
    }
}

