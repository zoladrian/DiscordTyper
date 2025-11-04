using Microsoft.EntityFrameworkCore;
using TyperBot.Domain.Entities;
using TyperBot.Infrastructure.Data;

namespace TyperBot.Infrastructure.Repositories;

public class RoundRepository : IRoundRepository
{
    private readonly TyperContext _context;

    public RoundRepository(TyperContext context)
    {
        _context = context;
    }

    public async Task<Round?> GetByIdAsync(int id)
    {
        return await _context.Rounds
            .Include(r => r.Matches)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<Round?> GetByNumberAsync(int seasonId, int number)
    {
        return await _context.Rounds
            .Include(r => r.Matches)
            .FirstOrDefaultAsync(r => r.SeasonId == seasonId && r.Number == number);
    }

    public async Task<IEnumerable<Round>> GetBySeasonIdAsync(int seasonId)
    {
        return await _context.Rounds
            .Include(r => r.Matches)
            .Where(r => r.SeasonId == seasonId)
            .OrderBy(r => r.Number)
            .ToListAsync();
    }

    public async Task<IEnumerable<Round>> GetAllAsync()
    {
        return await _context.Rounds.ToListAsync();
    }

    public async Task<Round> AddAsync(Round round)
    {
        await _context.Rounds.AddAsync(round);
        await _context.SaveChangesAsync();
        return round;
    }

    public async Task UpdateAsync(Round round)
    {
        _context.Rounds.Update(round);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var round = await GetByIdAsync(id);
        if (round != null)
        {
            _context.Rounds.Remove(round);
            await _context.SaveChangesAsync();
        }
    }
}

