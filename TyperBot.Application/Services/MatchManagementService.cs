using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.Application.Services;

public class MatchManagementService
{
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRoundRepository _roundRepository;
    private readonly IMatchRepository _matchRepository;

    public MatchManagementService(
        ISeasonRepository seasonRepository,
        IRoundRepository roundRepository,
        IMatchRepository matchRepository)
    {
        _seasonRepository = seasonRepository;
        _roundRepository = roundRepository;
        _matchRepository = matchRepository;
    }

    public async Task<(bool success, string? error, Match? match)> CreateMatchAsync(
        int roundNumber,
        string homeTeam,
        string awayTeam,
        DateTimeOffset startTime)
    {
        // Get or create active season
        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            season = new Season
            {
                Name = "PGE Ekstraliga 2025",
                IsActive = true
            };
            season = await _seasonRepository.AddAsync(season);
        }

        // Get or create round
        var round = await _roundRepository.GetByNumberAsync(season.Id, roundNumber);
        if (round == null)
        {
            round = new Round
            {
                SeasonId = season.Id,
                Number = roundNumber,
                Description = $"Round {roundNumber}"
            };
            round = await _roundRepository.AddAsync(round);
        }

        // Create match
        var match = new Match
        {
            RoundId = round.Id,
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            StartTime = startTime,
            Status = MatchStatus.Scheduled
        };

        match = await _matchRepository.AddAsync(match);

        return (true, null, match);
    }

    public (bool isValid, string? errorMessage) ValidateMatchResult(int homeScore, int awayScore)
    {
        if (homeScore < 0 || awayScore < 0)
        {
            return (false, "Wyniki nie mogą być ujemne.");
        }

        var sum = homeScore + awayScore;
        if (sum != 90)
        {
            return (false, $"Suma wyników musi wynosić 90, a nie {sum}.");
        }

        return (true, null);
    }
}

