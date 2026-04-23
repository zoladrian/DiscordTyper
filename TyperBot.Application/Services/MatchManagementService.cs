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
        DateTimeOffset startTime,
        DateTimeOffset? threadCreationTime = null)
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

        // Get or create round (prefer graph from GetActiveSeasonAsync to avoid extra query / mismatch)
        var round = season.FindRoundByNumber(roundNumber)
            ?? await _roundRepository.GetByNumberAsync(season.Id, roundNumber);
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

        if (!threadCreationTime.HasValue)
            threadCreationTime = ComputeDefaultThreadCreationTime(startTime);

        // Create match
        var match = new Match
        {
            RoundId = round.Id,
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            StartTime = startTime,
            ThreadCreationTime = threadCreationTime,
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

        return (true, null);
    }

    /// <summary>
    /// Default: Wednesday 8:00 (local offset of <paramref name="startTime"/>) in the week before the match start.
    /// Shared with edit-match flow so rescheduled matches get a sensible auto-publish time.
    /// </summary>
    public static DateTimeOffset ComputeDefaultThreadCreationTime(DateTimeOffset startTime)
    {
        var matchDate = startTime.Date;
        var daysSinceWednesday = ((int)matchDate.DayOfWeek - (int)DayOfWeek.Wednesday + 7) % 7;

        if (daysSinceWednesday == 0 && startTime.TimeOfDay.TotalHours < 8)
            daysSinceWednesday = 7;

        var wednesdayDate = matchDate.AddDays(-daysSinceWednesday);
        var threadCreationTime = new DateTimeOffset(wednesdayDate.AddHours(8), startTime.Offset);

        if (threadCreationTime >= startTime)
            threadCreationTime = threadCreationTime.AddDays(-7);

        return threadCreationTime;
    }
}

