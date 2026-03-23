using Microsoft.EntityFrameworkCore;
using TyperBot.Domain.Entities;
using TyperBot.Infrastructure.Data;

namespace TyperBot.Infrastructure.Repositories;

public class SeasonRepository : ISeasonRepository
{
    private readonly TyperContext _context;

    public SeasonRepository(TyperContext context)
    {
        _context = context;
    }

    public async Task<Season?> GetByIdAsync(int id)
    {
        return await _context.Seasons.FindAsync(id);
    }

    public async Task<Season?> GetByIdWithRoundsAndMatchesAsync(int id)
    {
        return await _context.Seasons
            .AsNoTracking()
            .AsSplitQuery()
            .Include(s => s.Rounds)
                .ThenInclude(r => r.Matches)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<Season?> GetActiveSeasonAsync()
    {
        // Newest active season wins if multiple rows are marked active (data inconsistency).
        return await _context.Seasons
            .AsNoTracking()
            .AsSplitQuery()
            .Include(s => s.Rounds)
                .ThenInclude(r => r.Matches)
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<Season>> GetAllActiveSeasonsAsync()
    {
        return await _context.Seasons
            .AsNoTracking()
            .Where(s => s.IsActive)
            .ToListAsync();
    }

    public async Task<IEnumerable<Season>> GetAllAsync()
    {
        return await _context.Seasons.AsNoTracking().ToListAsync();
    }

    public async Task<Season> AddAsync(Season season)
    {
        await _context.Seasons.AddAsync(season);
        await _context.SaveChangesAsync();
        return season;
    }

    public async Task UpdateAsync(Season season)
    {
        _context.Seasons.Update(season);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var season = await GetByIdAsync(id);
        if (season != null)
        {
            _context.Seasons.Remove(season);
            await _context.SaveChangesAsync();
        }
    }
}

