using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Data;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.Tests.Integration;

public class MatchRepositorySqliteIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TyperContext _context;
    private readonly MatchRepository _repository;

    public MatchRepositorySqliteIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TyperContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TyperContext(options);
        _context.Database.EnsureCreated();
        _repository = new MatchRepository(_context);
    }

    [Fact]
    public async Task DateTimeOffset_based_queries_work_on_sqlite()
    {
        var season = new Season { Name = "Sqlite Season", IsActive = true };
        _context.Seasons.Add(season);
        await _context.SaveChangesAsync();

        var round = new Round { SeasonId = season.Id, Number = 1, Description = "R1" };
        _context.Rounds.Add(round);
        await _context.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        _context.Matches.AddRange(
            new Match
            {
                RoundId = round.Id,
                HomeTeam = "Ready",
                AwayTeam = "Away",
                StartTime = now.AddHours(2),
                ThreadCreationTime = now.AddMinutes(-5),
                Status = MatchStatus.Scheduled
            },
            new Match
            {
                RoundId = round.Id,
                HomeTeam = "NeedsReminder",
                AwayTeam = "Away",
                StartTime = now.AddHours(-4),
                Status = MatchStatus.InProgress
            });
        await _context.SaveChangesAsync();

        var ready = (await _repository.GetMatchesReadyForThreadCreationAsync(now)).ToList();
        var reminders = (await _repository.GetMatchesPossiblyAwaitingResultEntryAsync(now.AddHours(-3))).ToList();

        ready.Should().ContainSingle(m => m.HomeTeam == "Ready");
        reminders.Should().ContainSingle(m => m.HomeTeam == "NeedsReminder");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
