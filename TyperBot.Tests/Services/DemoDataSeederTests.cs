namespace TyperBot.Tests.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TyperBot.Application.Services;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Data;
using TyperBot.Infrastructure.Repositories;
using Xunit;

public class DemoDataSeederTests
{
    private readonly DbContextOptions<TyperContext> _options;
    private readonly TyperContext _context;
    private readonly DemoDataSeeder _seeder;
    private readonly SeasonRepository _seasonRepository;
    private readonly RoundRepository _roundRepository;
    private readonly MatchRepository _matchRepository;
    private readonly PlayerRepository _playerRepository;
    private readonly PredictionRepository _predictionRepository;
    private readonly PlayerScoreRepository _playerScoreRepository;

    public DemoDataSeederTests()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        _options = new DbContextOptionsBuilder<TyperContext>()
            .UseSqlite(connection)
            .Options;

        _context = new TyperContext(_options);
        _context.Database.EnsureCreated();

        var logger = NullLogger<DemoDataSeeder>.Instance;
        _seasonRepository = new SeasonRepository(_context);
        _roundRepository = new RoundRepository(_context);
        _matchRepository = new MatchRepository(_context);
        _playerRepository = new PlayerRepository(_context);
        _predictionRepository = new PredictionRepository(_context);
        _playerScoreRepository = new PlayerScoreRepository(_context);
        var scoreCalculator = new ScoreCalculator();

        _seeder = new DemoDataSeeder(
            logger,
            _seasonRepository,
            _roundRepository,
            _matchRepository,
            _playerRepository,
            _predictionRepository,
            _playerScoreRepository,
            scoreCalculator);
    }

    [Fact]
    public async Task SeedDemoDataAsync_CreatesExpectedEntities()
    {
        // Act
        var result = await _seeder.SeedDemoDataAsync();

        // Assert
        Assert.Equal(1, result.SeasonsCreated);
        Assert.Equal(5, result.RoundsCreated);
        Assert.True(result.MatchesCreated > 0);
        Assert.Equal(5, result.PlayersCreated);
        Assert.True(result.PredictionsCreated > 0);
        Assert.True(result.ScoresCreated > 0);

        var season = await _context.Seasons.SingleOrDefaultAsync(s => s.Name == "Demo Season 2025");
        Assert.NotNull(season);
        Assert.True(season.IsActive);

        var rounds = await _context.Rounds.CountAsync();
        Assert.Equal(5, rounds);

        var matches = await _context.Matches.CountAsync();
        Assert.Equal(result.MatchesCreated, matches);

        var players = await _context.Players.CountAsync();
        Assert.Equal(5, players);
        
        var predictions = await _context.Predictions.CountAsync();
        Assert.Equal(result.PredictionsCreated, predictions);

        var scores = await _context.PlayerScores.CountAsync();
        Assert.Equal(result.ScoresCreated, scores);
    }

    [Fact]
    public async Task SeedDemoDataAsync_DeactivatesPreviousActiveSeason()
    {
        // Arrange
        var oldSeason = new Season { Name = "Old Active Season", IsActive = true };
        await _seasonRepository.AddAsync(oldSeason);

        // Act
        await _seeder.SeedDemoDataAsync();

        // Assert
        var oldSeasonAfterSeed = await _seasonRepository.GetByIdAsync(oldSeason.Id);
        Assert.NotNull(oldSeasonAfterSeed);
        Assert.False(oldSeasonAfterSeed.IsActive);

        var newSeason = await _context.Seasons.SingleOrDefaultAsync(s => s.IsActive);
        Assert.NotNull(newSeason);
        Assert.Equal("Demo Season 2025", newSeason.Name);
    }

    [Fact]
    public async Task SeedDemoDataAsync_EnforcesSum90Rule()
    {
        // Act
        await _seeder.SeedDemoDataAsync();

        // Assert
        var finishedMatches = await _context.Matches.Where(m => m.Status == MatchStatus.Finished).ToListAsync();
        Assert.All(finishedMatches, m => Assert.Equal(90, m.HomeScore + m.AwayScore));

        var predictions = await _context.Predictions.ToListAsync();
        Assert.All(predictions, p => Assert.Equal(90, p.HomeTip + p.AwayTip));
    }
}
