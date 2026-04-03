using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Data;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.Tests.Integration;

public class PredictionRepositorySqliteIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TyperContext _context;
    private readonly PredictionRepository _repository;

    public PredictionRepositorySqliteIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TyperContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TyperContext(options);
        _context.Database.EnsureCreated();
        _repository = new PredictionRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetByMatchIdsAsync_empty_returns_empty()
    {
        var result = await _repository.GetByMatchIdsAsync(Array.Empty<int>());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByMatchIdsAsync_returns_predictions_with_players_for_each_match()
    {
        var (m1Id, m2Id, _) = await SeedTwoMatchesAndPlayersAsync();

        var list = await _repository.GetByMatchIdsAsync(new[] { m1Id, m2Id });

        list.Should().HaveCount(2);
        list.Should().OnlyContain(p => p.Player != null && !string.IsNullOrEmpty(p.Player.DiscordUsername));
        list.Select(p => p.MatchId).Should().BeEquivalentTo(new[] { m1Id, m2Id });
    }

    [Fact]
    public async Task GetByPlayerIdAndMatchIdsAsync_includes_PlayerScore_when_present()
    {
        var season = new Season { Name = "S", IsActive = true };
        _context.Seasons.Add(season);
        await _context.SaveChangesAsync();

        var round = new Round { SeasonId = season.Id, Number = 1, Description = "K1" };
        _context.Rounds.Add(round);
        await _context.SaveChangesAsync();

        var start = DateTimeOffset.UtcNow;
        var m1 = new Match
        {
            RoundId = round.Id,
            HomeTeam = "H",
            AwayTeam = "A",
            StartTime = start,
            Status = MatchStatus.Finished,
            HomeScore = 50,
            AwayScore = 40
        };
        _context.Matches.Add(m1);
        await _context.SaveChangesAsync();

        var player = new Player
        {
            DiscordUserId = 100,
            DiscordUsername = "scored_user",
            IsActive = true
        };
        _context.Players.Add(player);
        await _context.SaveChangesAsync();

        var prediction = new Prediction
        {
            MatchId = m1.Id,
            PlayerId = player.Id,
            HomeTip = 50,
            AwayTip = 40,
            CreatedAt = DateTimeOffset.UtcNow,
            IsValid = true
        };
        _context.Predictions.Add(prediction);
        await _context.SaveChangesAsync();

        var score = new PlayerScore
        {
            PredictionId = prediction.Id,
            PlayerId = player.Id,
            Points = 35,
            Bucket = Bucket.P35
        };
        _context.PlayerScores.Add(score);
        await _context.SaveChangesAsync();

        var loaded = (await _repository.GetByPlayerIdAndMatchIdsAsync(player.Id, new[] { m1.Id })).ToList();

        loaded.Should().ContainSingle();
        var p = loaded[0];
        p.PlayerScore.Should().NotBeNull();
        p.PlayerScore!.Points.Should().Be(35);
        p.PlayerScore.Bucket.Should().Be(Bucket.P35);
    }

    [Fact]
    public async Task GetByPlayerIdAndMatchIdsAsync_PlayerScore_null_when_no_score_row()
    {
        var (m1Id, _, playerId) = await SeedTwoMatchesAndPlayersAsync();

        var loaded = (await _repository.GetByPlayerIdAndMatchIdsAsync(playerId, new[] { m1Id })).ToList();
        loaded.Should().ContainSingle();
        loaded[0].PlayerScore.Should().BeNull();
    }

    private async Task<(int m1Id, int m2Id, int player1Id)> SeedTwoMatchesAndPlayersAsync()
    {
        var season = new Season { Name = "S2", IsActive = true };
        _context.Seasons.Add(season);
        await _context.SaveChangesAsync();

        var round = new Round { SeasonId = season.Id, Number = 1, Description = "K1" };
        _context.Rounds.Add(round);
        await _context.SaveChangesAsync();

        var start = DateTimeOffset.UtcNow;
        var m1 = new Match
        {
            RoundId = round.Id,
            HomeTeam = "Home1",
            AwayTeam = "Away1",
            StartTime = start,
            Status = MatchStatus.Scheduled
        };
        var m2 = new Match
        {
            RoundId = round.Id,
            HomeTeam = "Home2",
            AwayTeam = "Away2",
            StartTime = start.AddHours(1),
            Status = MatchStatus.Scheduled
        };
        _context.Matches.AddRange(m1, m2);
        await _context.SaveChangesAsync();

        var p1 = new Player { DiscordUserId = 1, DiscordUsername = "u1", IsActive = true };
        var p2 = new Player { DiscordUserId = 2, DiscordUsername = "u2", IsActive = true };
        _context.Players.AddRange(p1, p2);
        await _context.SaveChangesAsync();

        _context.Predictions.AddRange(
            new Prediction
            {
                MatchId = m1.Id,
                PlayerId = p1.Id,
                HomeTip = 45,
                AwayTip = 45,
                CreatedAt = DateTimeOffset.UtcNow,
                IsValid = true
            },
            new Prediction
            {
                MatchId = m2.Id,
                PlayerId = p2.Id,
                HomeTip = 50,
                AwayTip = 40,
                CreatedAt = DateTimeOffset.UtcNow,
                IsValid = true
            });
        await _context.SaveChangesAsync();

        return (m1.Id, m2.Id, p1.Id);
    }
}
