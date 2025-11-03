using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.Application.Services;

public class RoundManager
{
    private readonly IRoundRepository _roundRepository;
    private readonly IMatchRepository _matchRepository;

    public RoundManager(IRoundRepository roundRepository, IMatchRepository matchRepository)
    {
        _roundRepository = roundRepository;
        _matchRepository = matchRepository;
    }

    public async Task<IEnumerable<Match>> GetMatchesByRoundAsync(int seasonId, int roundNumber)
    {
        var round = await _roundRepository.GetByNumberAsync(seasonId, roundNumber);
        if (round == null)
        {
            return Enumerable.Empty<Match>();
        }

        return round.Matches.OrderBy(m => m.StartTime);
    }

    public async Task<bool> IsRoundCompleteAsync(int seasonId, int roundNumber)
    {
        var round = await _roundRepository.GetByNumberAsync(seasonId, roundNumber);
        if (round == null)
        {
            return false;
        }

        return round.Matches.All(m => m.Status == MatchStatus.Finished || m.Status == MatchStatus.Cancelled);
    }

    public async Task<Dictionary<int, bool>> GetRoundCompletionStatusAsync(int seasonId)
    {
        var rounds = await _roundRepository.GetBySeasonIdAsync(seasonId);
        var status = new Dictionary<int, bool>();

        foreach (var round in rounds)
        {
            bool isComplete = round.Matches.All(m => m.Status == MatchStatus.Finished || m.Status == MatchStatus.Cancelled);
            status[round.Number] = isComplete;
        }

        return status;
    }
}

