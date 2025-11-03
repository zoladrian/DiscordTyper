using TyperBot.Domain.Entities;

namespace TyperBot.Infrastructure.Repositories;

public interface IPredictionRepository
{
    Task<Prediction?> GetByIdAsync(int id);
    Task<Prediction?> GetByMatchAndPlayerAsync(int matchId, int playerId);
    Task<IEnumerable<Prediction>> GetByMatchIdAsync(int matchId);
    Task<IEnumerable<Prediction>> GetValidPredictionsByMatchAsync(int matchId);
    Task<Prediction> AddAsync(Prediction prediction);
    Task UpdateAsync(Prediction prediction);
    Task DeleteAsync(int id);
}

