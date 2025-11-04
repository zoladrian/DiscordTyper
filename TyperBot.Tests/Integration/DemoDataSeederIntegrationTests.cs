using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TyperBot.Application.Services;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Data;
using TyperBot.Infrastructure.Repositories;
using Xunit;

namespace TyperBot.Tests.Integration;

/// <summary>
/// Integration tests for DemoDataSeeder to verify complete data cleanup and sum 90 rule enforcement
/// </summary>
public class DemoDataSeederIntegrationTests : IDisposable
{
    private readonly DbContextOptions<TyperContext> _options;
    private readonly TyperContext _context;
    private readonly DemoDataSeeder _seeder;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRoundRepository _roundRepository;
    private readonly IMatchRepository _matchRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly IPredictionRepository _predictionRepository;
    private readonly IPlayerScoreRepository _playerScoreRepository;

    public DemoDataSeederIntegrationTests()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        _options = new DbContextOptionsBuilder<TyperContext>()
            .UseSqlite(connection)
            .Options;

        _context = new TyperContext(_options);
        _context.Database.EnsureCreated();

        _seasonRepository = new SeasonRepository(_context);
        _roundRepository = new RoundRepository(_context);
        _matchRepository = new MatchRepository(_context);
        _playerRepository = new PlayerRepository(_context);
        _predictionRepository = new PredictionRepository(_context);
        _playerScoreRepository = new PlayerScoreRepository(_context);
        var scoreCalculator = new ScoreCalculator();

        _seeder = new DemoDataSeeder(
            NullLogger<DemoDataSeeder>.Instance,
            _seasonRepository,
            _roundRepository,
            _matchRepository,
            _playerRepository,
            _predictionRepository,
            _playerScoreRepository,
            scoreCalculator);
    }

    [Fact]
    public async Task SeedDemoDataAsync_CleansUpExistingDataFirst()
    {
        // Arrange: Create some old data
        var oldSeason = new Season { Name = "Old Season", IsActive = true };
        await _seasonRepository.AddAsync(oldSeason);
        var oldPlayer = new Player { DiscordUserId = 999, DiscordUsername = "OldPlayer", IsActive = true };
        await _playerRepository.AddAsync(oldPlayer);

        // Act: Seed demo data
        var result = await _seeder.SeedDemoDataAsync();

        // Assert: Old data should be deleted, new data should exist
        var allSeasons = await _seasonRepository.GetAllAsync();
        var allPlayers = await _playerRepository.GetAllAsync();

        Assert.Equal(1, allSeasons.Count());
        Assert.DoesNotContain(allSeasons, s => s.Name == "Old Season");
        Assert.Contains(allSeasons, s => s.Name.Contains("Demo") || s.Name.Contains("2025"));

        Assert.Equal(5, allPlayers.Count()); // Demo seeder creates exactly 5 players
        Assert.DoesNotContain(allPlayers, p => p.DiscordUsername == "OldPlayer");
    }

    [Fact]
    public async Task SeedDemoDataAsync_CreatesExpectedNumberOfEntities()
    {
        // Act
        var result = await _seeder.SeedDemoDataAsync();

        // Assert
        Assert.Equal(1, result.SeasonsCreated);
        Assert.Equal(18, result.RoundsCreated); // 18 rounds: 14 regular + 4 playoff
        Assert.Equal(18 * 4, result.MatchesCreated); // 4 matches per round
        Assert.Equal(5, result.PlayersCreated);
        Assert.True(result.PredictionsCreated > 0);
        Assert.True(result.PlayerScoresCreated > 0);
    }

    [Fact]
    public async Task SeedDemoDataAsync_AllMatchResultsObeySum90Rule()
    {
        // Act
        await _seeder.SeedDemoDataAsync();

        // Assert: Every finished match must have home + away = 90
        var finishedMatches = await _context.Matches
            .Where(m => m.Status == MatchStatus.Finished)
            .ToListAsync();

        Assert.NotEmpty(finishedMatches);
        Assert.All(finishedMatches, match =>
        {
            Assert.NotNull(match.HomeScore);
            Assert.NotNull(match.AwayScore);
            var sum = match.HomeScore.Value + match.AwayScore.Value;
            Assert.Equal(90, sum);
        });
    }

    [Fact]
    public async Task SeedDemoDataAsync_AllPredictionsObeySum90Rule()
    {
        // Act
        await _seeder.SeedDemoDataAsync();

        // Assert: Every prediction must have homeTip + awayTip = 90
        var allPredictions = await _context.Predictions.ToListAsync();

        Assert.NotEmpty(allPredictions);
        Assert.All(allPredictions, prediction =>
        {
            var sum = prediction.HomeTip + prediction.AwayTip;
            Assert.Equal(90, sum);
        });
    }

    [Fact]
    public async Task SeedDemoDataAsync_CreatesPlayerScoresWithPlayerId()
    {
        // Act
        await _seeder.SeedDemoDataAsync();

        // Assert: All PlayerScores must have PlayerId set (CRITICAL FIX verification)
        var allScores = await _context.PlayerScores
            .Include(ps => ps.Player)
            .Include(ps => ps.Prediction)
            .ToListAsync();

        Assert.NotEmpty(allScores);
        Assert.All(allScores, score =>
        {
            Assert.True(score.PlayerId > 0, "PlayerScore must have PlayerId set");
            Assert.NotNull(score.Player);
            Assert.Equal(score.Prediction.PlayerId, score.PlayerId);
        });
    }

    [Fact]
    public async Task SeedDemoDataAsync_ScoresAreCalculatedCorrectly()
    {
        // Act
        await _seeder.SeedDemoDataAsync();

        // Assert: Verify score calculation logic
        var finishedMatchesWithScores = await _context.Matches
            .Where(m => m.Status == MatchStatus.Finished)
            .Include(m => m.Predictions)
                .ThenInclude(p => p.PlayerScore)
            .ToListAsync();

        foreach (var match in finishedMatchesWithScores)
        {
            foreach (var prediction in match.Predictions.Where(p => p.IsValid))
            {
                Assert.NotNull(prediction.PlayerScore);
                Assert.True(prediction.PlayerScore.Points >= 0 && prediction.PlayerScore.Points <= 50,
                    "Score must be between 0 and 50");
                
                // Verify bucket matches points
                var expectedBucket = (Bucket)prediction.PlayerScore.Points;
                Assert.Equal(expectedBucket, prediction.PlayerScore.Bucket);
            }
        }
    }

    [Fact]
    public async Task SeedDemoDataAsync_CreatesPlayoffRoundsWithCorrectNames()
    {
        // Act
        await _seeder.SeedDemoDataAsync();

        // Assert: Verify all 18 rounds exist with proper names
        var season = await _context.Seasons.SingleAsync(s => s.IsActive);
        var rounds = await _context.Rounds
            .Where(r => r.SeasonId == season.Id)
            .OrderBy(r => r.Number)
            .ToListAsync();

        Assert.Equal(18, rounds.Count);

        // Regular rounds 1-14
        for (int i = 1; i <= 14; i++)
        {
            Assert.Contains(rounds, r => r.Number == i);
        }

        // Playoff rounds 15-18
        Assert.Contains(rounds, r => r.Number == 15); // Quarter-final 1
        Assert.Contains(rounds, r => r.Number == 16); // Quarter-final 2
        Assert.Contains(rounds, r => r.Number == 17); // Semi-final
        Assert.Contains(rounds, r => r.Number == 18); // Final
    }

    [Fact]
    public async Task SeedDemoDataAsync_CanBeRunMultipleTimes()
    {
        // Act: Run seeder twice
        var result1 = await _seeder.SeedDemoDataAsync();
        var result2 = await _seeder.SeedDemoDataAsync();

        // Assert: Second run should clean up first run's data
        var allSeasons = await _seasonRepository.GetAllAsync();
        var allPlayers = await _playerRepository.GetAllAsync();

        Assert.Equal(1, allSeasons.Count()); // Only one season should exist
        Assert.Equal(5, allPlayers.Count()); // Only 5 players should exist
        Assert.Equal(1, result2.SeasonsCreated);
    }

    [Fact]
    public async Task SeedDemoDataAsync_ActivatesOnlyNewSeason()
    {
        // Act
        await _seeder.SeedDemoDataAsync();

        // Assert: Exactly one active season should exist
        var activeSeasons = await _context.Seasons
            .Where(s => s.IsActive)
            .ToListAsync();

        Assert.Single(activeSeasons);
    }

    public void Dispose()
    {
        _context?.Database?.EnsureDeleted();
        _context?.Dispose();
    }
}

