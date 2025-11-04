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
            .Include(p => p.Match)
            .Include(p => p.Player)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Prediction?> GetByMatchAndPlayerAsync(int matchId, int playerId)
    {
        return await _context.Predictions
            .Include(p => p.Match)
            .Include(p => p.Player)
            .FirstOrDefaultAsync(p => p.MatchId == matchId && p.PlayerId == playerId);
    }

    public async Task<IEnumerable<Prediction>> GetByMatchIdAsync(int matchId)
    {
        return await _context.Predictions
            .Include(p => p.Player)
            .Where(p => p.MatchId == matchId)
            .ToListAsync();
    }

    public async Task<IEnumerable<Prediction>> GetByPlayerIdAsync(int playerId)
    {
        return await _context.Predictions
            .Include(p => p.Match)
            .Where(p => p.PlayerId == playerId)
            .ToListAsync();
    }

    public async Task<IEnumerable<Prediction>> GetValidPredictionsByMatchAsync(int matchId)
    {
        return await _context.Predictions
            .Include(p => p.Player)
            .Where(p => p.MatchId == matchId && p.IsValid)
            .ToListAsync();
    }

    public async Task<IEnumerable<Prediction>> GetAllAsync()
    {
        return await _context.Predictions.ToListAsync();
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
        var prediction = await GetByIdAsync(id);
        if (prediction != null)
        {
            _context.Predictions.Remove(prediction);
            await _context.SaveChangesAsync();
        }
    }
}

