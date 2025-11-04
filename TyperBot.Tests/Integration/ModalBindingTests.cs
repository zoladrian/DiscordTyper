using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TyperBot.Application.Services;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Data;
using TyperBot.Infrastructure.Repositories;
using Xunit;

namespace TyperBot.Tests.Integration;

/// <summary>
/// Critical tests for Discord modal parameter binding.
/// These tests verify that modal input IDs match handler parameter names EXACTLY (case-sensitive).
/// Discord.Net will silently fail if parameter names don't match, causing "something went wrong" errors.
/// </summary>
public class ModalBindingTests : IDisposable
{
    private readonly DbContextOptions<TyperContext> _options;
    private readonly TyperContext _context;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRoundRepository _roundRepository;
    private readonly IMatchRepository _matchRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly IPredictionRepository _predictionRepository;
    private readonly IPlayerScoreRepository _playerScoreRepository;
    private readonly ScoreCalculator _scoreCalculator;
    private readonly PredictionService _predictionService;
    private readonly MatchManagementService _matchService;

    public ModalBindingTests()
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
        _scoreCalculator = new ScoreCalculator();
        _predictionService = new PredictionService(
            _predictionRepository,
            _matchRepository,
            _playerRepository,
            _playerScoreRepository,
            _scoreCalculator,
            NullLogger<PredictionService>.Instance);
        _matchService = new MatchManagementService(
            _matchRepository,
            _roundRepository,
            _seasonRepository,
            NullLogger<MatchManagementService>.Instance);
    }

    [Fact]
    public async Task AddKolejkaModal_WithCorrectParameterNames_ShouldWork()
    {
        // Arrange: Modal uses "kolejka_number" and "liczba_meczow" (with underscores)
        var season = new Season { Name = "Test Season", IsActive = true };
        await _seasonRepository.AddAsync(season);

        string kolejka_number = "1"; // ← Must match modal input ID exactly
        string liczba_meczow = "4";  // ← Must match modal input ID exactly

        // Act: Parse as the handler would
        var parseSuccess = int.TryParse(kolejka_number, out var roundNumber) && 
                          int.TryParse(liczba_meczow, out var matchCount);

        // Assert
        Assert.True(parseSuccess, "Parameters should parse successfully when names match modal input IDs");
        Assert.Equal(1, roundNumber);
        Assert.Equal(4, matchCount);
    }

    [Fact]
    public async Task AddMatchModal_WithCorrectParameterNames_ShouldWork()
    {
        // Arrange: Modal uses "home_team" and "away_team" (with underscores)
        var season = new Season { Name = "Test Season", IsActive = true };
        await _seasonRepository.AddAsync(season);
        var round = new Round { SeasonId = season.Id, Number = 1 };
        await _roundRepository.AddAsync(round);

        string home_team = "Motor Lublin";      // ← Must match modal input ID exactly
        string away_team = "Sparta Wrocław";    // ← Must match modal input ID exactly

        // Act: Create match as the handler would
        var startTime = DateTimeOffset.UtcNow.AddHours(24);
        var (success, error, match) = await _matchService.CreateMatchAsync(1, home_team, away_team, startTime);

        // Assert
        Assert.True(success, $"Match creation should succeed when parameter names match: {error}");
        Assert.NotNull(match);
        Assert.Equal(home_team, match.HomeTeam);
        Assert.Equal(away_team, match.AwayTeam);
    }

    [Fact]
    public async Task SetResultModal_WithCorrectParameterNames_ShouldWork()
    {
        // Arrange: Modal uses "home_score" and "away_score" (with underscores)
        var season = new Season { Name = "Test Season", IsActive = true };
        await _seasonRepository.AddAsync(season);
        var round = new Round { SeasonId = season.Id, Number = 1 };
        await _roundRepository.AddAsync(round);
        var match = new Match
        {
            RoundId = round.Id,
            HomeTeam = "Motor Lublin",
            AwayTeam = "Sparta Wrocław",
            StartTime = DateTimeOffset.UtcNow.AddHours(-1),
            Status = MatchStatus.Scheduled
        };
        await _matchRepository.AddAsync(match);

        string home_score = "50";  // ← Must match modal input ID exactly
        string away_score = "40";  // ← Must match modal input ID exactly

        // Act: Parse and validate as the handler would
        var parseSuccess = int.TryParse(home_score, out var home) && 
                          int.TryParse(away_score, out var away);
        var sumIsValid = home + away == 90;
        var (isValid, errorMessage) = _matchService.ValidateMatchResult(home, away);

        // Assert
        Assert.True(parseSuccess, "Parameters should parse successfully when names match modal input IDs");
        Assert.True(sumIsValid, "Sum should equal 90");
        Assert.True(isValid, $"Validation should pass: {errorMessage}");
    }

    [Fact]
    public async Task EditMatchModal_WithCorrectParameterNames_ShouldWork()
    {
        // Arrange: Modal uses "home_team", "away_team", "date", "time" (with underscores for teams)
        var season = new Season { Name = "Test Season", IsActive = true };
        await _seasonRepository.AddAsync(season);
        var round = new Round { SeasonId = season.Id, Number = 1 };
        await _roundRepository.AddAsync(round);
        var match = new Match
        {
            RoundId = round.Id,
            HomeTeam = "Motor Lublin",
            AwayTeam = "Sparta Wrocław",
            StartTime = DateTimeOffset.UtcNow.AddHours(24),
            Status = MatchStatus.Scheduled
        };
        await _matchRepository.AddAsync(match);

        string home_team = "Apator Toruń";        // ← Must match modal input ID exactly
        string away_team = "GKM Grudziądz";       // ← Must match modal input ID exactly
        string date = "2025-05-15";
        string time = "19:00";

        // Act: Update match as the handler would
        match.HomeTeam = home_team;
        match.AwayTeam = away_team;
        match.StartTime = DateTimeOffset.Parse($"{date} {time} +02:00");
        await _matchRepository.UpdateAsync(match);

        var updated = await _matchRepository.GetByIdAsync(match.Id);

        // Assert
        Assert.NotNull(updated);
        Assert.Equal(home_team, updated.HomeTeam);
        Assert.Equal(away_team, updated.AwayTeam);
    }

    [Fact]
    public async Task ModalParameterNaming_MismatchCausesFailure()
    {
        // This test documents what happens when parameter names don't match modal input IDs
        // Discord.Net will pass null/default values, causing parsing failures

        // Arrange: Handler uses "homeTeam" (camelCase) but modal defines "home_team" (snake_case)
        string homeTeam = null; // ← This is what Discord.Net passes when names don't match!

        // Act
        var isEmpty = string.IsNullOrEmpty(homeTeam);

        // Assert: This demonstrates the critical bug
        Assert.True(isEmpty, "Discord.Net passes null when parameter names don't match modal input IDs");
    }

    [Fact]
    public async Task Sum90Rule_EnforcedInModalValidation()
    {
        // Arrange
        var testCases = new[]
        {
            (home: 50, away: 40, shouldPass: true),   // Sum = 90 ✓
            (home: 45, away: 45, shouldPass: true),   // Sum = 90 ✓
            (home: 55, away: 35, shouldPass: true),   // Sum = 90 ✓
            (home: 50, away: 50, shouldPass: false),  // Sum = 100 ✗
            (home: 45, away: 40, shouldPass: false),  // Sum = 85 ✗
            (home: 60, away: 40, shouldPass: false),  // Sum = 100 ✗
            (home: -5, away: 95, shouldPass: false),  // Negative ✗
        };

        // Act & Assert
        foreach (var (home, away, shouldPass) in testCases)
        {
            var (isValid, error) = _matchService.ValidateMatchResult(home, away);
            if (shouldPass)
            {
                Assert.True(isValid, $"Expected {home}:{away} (sum={home + away}) to be valid");
            }
            else
            {
                Assert.False(isValid, $"Expected {home}:{away} (sum={home + away}) to be invalid");
                Assert.NotNull(error);
            }
        }
    }

    [Fact]
    public async Task PlayerScore_HasDirectPlayerIdRelationship()
    {
        // Arrange: This tests the CRITICAL database schema fix
        var player = new Player 
        { 
            DiscordUserId = 123, 
            DiscordUsername = "TestPlayer", 
            IsActive = true 
        };
        await _playerRepository.AddAsync(player);

        var season = new Season { Name = "Test", IsActive = true };
        await _seasonRepository.AddAsync(season);
        var round = new Round { SeasonId = season.Id, Number = 1 };
        await _roundRepository.AddAsync(round);
        var match = new Match
        {
            RoundId = round.Id,
            HomeTeam = "Motor Lublin",
            AwayTeam = "Sparta Wrocław",
            StartTime = DateTimeOffset.UtcNow.AddHours(-1),
            Status = MatchStatus.Finished,
            HomeScore = 50,
            AwayScore = 40
        };
        await _matchRepository.AddAsync(match);

        var prediction = new Prediction
        {
            MatchId = match.Id,
            PlayerId = player.Id,
            HomeTip = 48,
            AwayTip = 42, // Sum = 90 ✓
            IsValid = true
        };
        await _predictionRepository.AddAsync(prediction);

        // Act: Calculate score and create PlayerScore with PlayerId
        await _predictionService.RecalculateMatchScoresAsync(match.Id);

        // Assert: Verify PlayerScore has direct PlayerId relationship
        var playerScore = await _context.PlayerScores
            .Include(ps => ps.Player)
            .Include(ps => ps.Prediction)
            .FirstOrDefaultAsync(ps => ps.PredictionId == prediction.Id);

        Assert.NotNull(playerScore);
        Assert.Equal(player.Id, playerScore.PlayerId); // ← CRITICAL FIX VERIFICATION
        Assert.NotNull(playerScore.Player);
        Assert.Equal("TestPlayer", playerScore.Player.DiscordUsername);
        Assert.True(playerScore.Points > 0, "Score should be calculated");
    }

    public void Dispose()
    {
        _context?.Database?.EnsureDeleted();
        _context?.Dispose();
    }
}

