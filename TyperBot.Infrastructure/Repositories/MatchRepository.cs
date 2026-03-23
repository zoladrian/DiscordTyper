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
            .AsNoTracking()
            .Include(m => m.Round)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<IEnumerable<Match>> GetByRoundIdAsync(int roundId)
    {
        // SQLite: ORDER BY DateTimeOffset is not supported — sort after materialization.
        var list = await _context.Matches
            .AsNoTracking()
            .Where(m => m.RoundId == roundId)
            .ToListAsync();
        return list.OrderBy(m => m.StartTime).ToList();
    }

    public async Task<IEnumerable<Match>> GetUpcomingMatchesAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var list = await _context.Matches
            .AsNoTracking()
            .Include(m => m.Round)
            .Where(m => m.Status == MatchStatus.Scheduled && m.StartTime > now)
            .ToListAsync();
        return list.OrderBy(m => m.StartTime).ToList();
    }

    public async Task<IEnumerable<Match>> GetMatchesReadyForThreadCreationAsync(DateTimeOffset now)
    {
        // SQLite + EF Core can fail translating DateTimeOffset comparisons in complex predicates.
        // Keep SQL-side filtering for status/null, and do time-window filtering in memory.
        var candidates = await _context.Matches
            .AsNoTracking()
            .Include(m => m.Round)
            .Where(m =>
                m.Status == MatchStatus.Scheduled &&
                m.ThreadCreationTime != null)
            .ToListAsync();

        return candidates.Where(m =>
            m.ThreadCreationTime <= now &&
            m.StartTime > now);
    }

    public async Task<IEnumerable<Match>> GetMatchesPossiblyAwaitingResultEntryAsync(DateTimeOffset startedOnOrBeforeUtc)
    {
        // SQLite + EF Core can fail translating DateTimeOffset comparisons in complex predicates.
        // Keep SQL-side filtering for status/scores, and do cutoff-time filtering in memory.
        var candidates = await _context.Matches
            .AsNoTracking()
            .Include(m => m.Round)
            .Where(m =>
                m.Status != MatchStatus.Cancelled &&
                m.Status != MatchStatus.Finished &&
                !(m.HomeScore.HasValue && m.AwayScore.HasValue))
            .ToListAsync();

        return candidates.Where(m => m.StartTime <= startedOnOrBeforeUtc);
    }

    public async Task<IEnumerable<Match>> GetAllAsync()
    {
        return await _context.Matches
            .AsNoTracking()
            .Include(m => m.Round)
            .ToListAsync();
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
        // Tracked load — do not use GetByIdAsync (AsNoTracking); Remove must attach a consistent graph for deletes.
        var match = await _context.Matches.FindAsync(id);
        if (match != null)
        {
            _context.Matches.Remove(match);
            await _context.SaveChangesAsync();
        }
    }
}

