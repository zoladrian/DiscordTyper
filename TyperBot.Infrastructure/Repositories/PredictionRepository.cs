using Microsoft.EntityFrameworkCore;
using TyperBot.Domain.Entities;
using TyperBot.Infrastructure.Data;

namespace TyperBot.Infrastructure.Repositories;

public class PredictionRepository : IPredictionRepository
{
    private readonly TyperContext _context;

    public PredictionRepository(TyperContext context)
    {
        _context = context;
    }

    public async Task<Prediction?> GetByIdAsync(int id)
    {
        return await _context.Predictions
            .AsNoTracking()
            .Include(p => p.Match)
            .Include(p => p.Player)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Prediction?> GetByMatchAndPlayerAsync(int matchId, int playerId)
    {
        return await _context.Predictions
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Match)
            .Include(p => p.Player)
            .Include(p => p.PlayerScore)
            .FirstOrDefaultAsync(p => p.MatchId == matchId && p.PlayerId == playerId);
    }

    public async Task<IEnumerable<Prediction>> GetByMatchIdAsync(int matchId)
    {
        return await _context.Predictions
            .AsNoTracking()
            .Include(p => p.Player)
            .Where(p => p.MatchId == matchId)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Prediction>> GetByMatchIdsAsync(IEnumerable<int> matchIds)
    {
        var ids = matchIds.Distinct().ToList();
        if (ids.Count == 0)
            return Array.Empty<Prediction>();

        return await _context.Predictions
            .AsNoTracking()
            .Include(p => p.Player)
            .Where(p => ids.Contains(p.MatchId))
            .ToListAsync();
    }

    public async Task<IEnumerable<Prediction>> GetByPlayerIdAsync(int playerId)
    {
        return await _context.Predictions
            .AsNoTracking()
            .Include(p => p.Match)
            .Where(p => p.PlayerId == playerId)
            .ToListAsync();
    }

    public async Task<IEnumerable<Prediction>> GetByPlayerIdAndMatchIdsAsync(int playerId, IEnumerable<int> matchIds)
    {
        var matchIdList = matchIds.ToList();
        if (!matchIdList.Any())
        {
            return Enumerable.Empty<Prediction>();
        }

        return await _context.Predictions
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Match)
                .ThenInclude(m => m.Round)
            .Where(p => p.PlayerId == playerId && matchIdList.Contains(p.MatchId))
            .ToListAsync();
    }

    public async Task<IEnumerable<Prediction>> GetValidPredictionsByMatchAsync(int matchId)
    {
        return await _context.Predictions
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Player)
            .Include(p => p.PlayerScore)
            .Where(p => p.MatchId == matchId && p.IsValid)
            .ToListAsync();
    }

    public async Task<IEnumerable<Prediction>> GetAllAsync()
    {
        return await _context.Predictions.AsNoTracking().ToListAsync();
    }

    public async Task<Prediction> AddAsync(Prediction prediction)
    {
        await _context.Predictions.AddAsync(prediction);
        await _context.SaveChangesAsync();
        return prediction;
    }

    public async Task UpdateAsync(Prediction prediction)
    {
        _context.Predictions.Update(prediction);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        // Tracked load — do not use GetByIdAsync (AsNoTracking).
        var prediction = await _context.Predictions.FindAsync(id);
        if (prediction != null)
        {
            _context.Predictions.Remove(prediction);
            await _context.SaveChangesAsync();
        }
    }
}

