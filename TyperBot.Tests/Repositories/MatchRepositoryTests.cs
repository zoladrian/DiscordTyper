using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Data;
using TyperBot.Infrastructure.Repositories;
using Xunit;

namespace TyperBot.Tests.Repositories;

public class MatchRepositoryTests : IDisposable
{
    private readonly TyperContext _context;
    private readonly MatchRepository _repository;
    private readonly RoundRepository _roundRepository;
    private readonly SeasonRepository _seasonRepository;

    public MatchRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<TyperContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TyperContext(options);
        _repository = new MatchRepository(_context);
        _roundRepository = new RoundRepository(_context);
        _seasonRepository = new SeasonRepository(_context);
    }

    private async Task<Round> CreateTestRound()
    {
        var season = await _seasonRepository.AddAsync(new Season
        {
            Name = "Test Season",
            IsActive = true
        });

        return await _roundRepository.AddAsync(new Round
        {
            SeasonId = season.Id,
            Number = 1,
            Description = "Test Round"
        });
    }

    [Fact]
    public async Task AddAsync_NewMatch_AddsSuccessfully()
    {
        // Arrange
        var round = await CreateTestRound();
        var match = new Match
        {
            RoundId = round.Id,
            HomeTeam = "Team A",
            AwayTeam = "Team B",
            StartTime = DateTimeOffset.UtcNow.AddDays(1),
            Status = MatchStatus.Scheduled
        };

        // Act
        var result = await _repository.AddAsync(match);

        // Assert
        result.Id.Should().BeGreaterThan(0);
        result.HomeTeam.Should().Be("Team A");
    }

    [Fact]
    public async Task GetByRoundIdAsync_ReturnsMatchesOrderedByStartTime()
    {
        // Arrange
        var round = await CreateTestRound();
        var baseTime = DateTimeOffset.UtcNow;

        await _repository.AddAsync(new Match
        {
            RoundId = round.Id,
            HomeTeam = "Match2",
            AwayTeam = "Away",
            StartTime = baseTime.AddHours(2),
            Status = MatchStatus.Scheduled
        });

        await _repository.AddAsync(new Match
        {
            RoundId = round.Id,
            HomeTeam = "Match1",
            AwayTeam = "Away",
            StartTime = baseTime.AddHours(1),
            Status = MatchStatus.Scheduled
        });

        // Act
        var result = await _repository.GetByRoundIdAsync(round.Id);

        // Assert
        result.Should().HaveCount(2);
        result.First().HomeTeam.Should().Be("Match1");
        result.Last().HomeTeam.Should().Be("Match2");
    }

    [Fact]
    public async Task GetUpcomingMatchesAsync_ReturnsOnlyScheduledFutureMatches()
    {
        // Arrange
        var round = await CreateTestRound();

        await _repository.AddAsync(new Match
        {
            RoundId = round.Id,
            HomeTeam = "Future",
            AwayTeam = "Away",
            StartTime = DateTimeOffset.UtcNow.AddDays(1),
            Status = MatchStatus.Scheduled
        });

        await _repository.AddAsync(new Match
        {
            RoundId = round.Id,
            HomeTeam = "Past",
            AwayTeam = "Away",
            StartTime = DateTimeOffset.UtcNow.AddDays(-1),
            Status = MatchStatus.Scheduled
        });

        await _repository.AddAsync(new Match
        {
            RoundId = round.Id,
            HomeTeam = "Finished",
            AwayTeam = "Away",
            StartTime = DateTimeOffset.UtcNow.AddDays(1),
            Status = MatchStatus.Finished
        });

        // Act
        var result = await _repository.GetUpcomingMatchesAsync();

        // Assert
        result.Should().HaveCount(1);
        result.First().HomeTeam.Should().Be("Future");
    }

    [Fact]
    public async Task UpdateAsync_ModifiesMatch()
    {
        // Arrange
        var round = await CreateTestRound();
        var match = await _repository.AddAsync(new Match
        {
            RoundId = round.Id,
            HomeTeam = "Original",
            AwayTeam = "Away",
            StartTime = DateTimeOffset.UtcNow.AddDays(1),
            Status = MatchStatus.Scheduled
        });

        // Act
        match.Status = MatchStatus.Finished;
        match.HomeScore = 50;
        match.AwayScore = 40;
        await _repository.UpdateAsync(match);

        // Assert
        var updated = await _repository.GetByIdAsync(match.Id);
        updated!.Status.Should().Be(MatchStatus.Finished);
        updated.HomeScore.Should().Be(50);
        updated.AwayScore.Should().Be(40);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}

