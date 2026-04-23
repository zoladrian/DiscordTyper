using FluentAssertions;
using TyperBot.Application.Services;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;

namespace TyperBot.Tests.Services;

public class StandingsLandrynkiTests
{
    [Fact]
    public void BuildLandrynkiBarEntries_OrdersByMissedDesc_ThenName()
    {
        var m1 = new Match { Id = 1, Status = MatchStatus.Finished, HomeScore = 50, AwayScore = 40 };
        var m2 = new Match { Id = 2, Status = MatchStatus.Finished, HomeScore = 45, AwayScore = 45 };
        var round = new Round { Number = 1, Matches = new List<Match> { m1, m2 } };
        var season = new Season { Name = "S", Rounds = new List<Round> { round } };

        var alice = new Player
        {
            Id = 1,
            DiscordUsername = "alice",
            IsActive = true,
            Predictions = new List<Prediction>
            {
                new()
                {
                    MatchId = 1,
                    IsValid = true,
                    HomeTip = 50,
                    AwayTip = 40
                }
            }
        };
        var bob = new Player
        {
            Id = 2,
            DiscordUsername = "bob",
            IsActive = true,
            Predictions = new List<Prediction>()
        };

        var rows = StandingsAnalyticsGenerator.BuildLandrynkiBarEntries(season, new[] { alice, bob }, p => p.DiscordUsername);

        rows.Should().HaveCount(2);
        rows[0].PlayerName.Should().Be("bob");
        rows[0].MissedCount.Should().Be(2);
        rows[1].PlayerName.Should().Be("alice");
        rows[1].MissedCount.Should().Be(1);
    }

    [Fact]
    public void BuildLandrynkiBarEntries_NoFinishedMatches_ReturnsEmpty()
    {
        var m1 = new Match { Id = 1, Status = MatchStatus.Scheduled };
        var round = new Round { Number = 1, Matches = new List<Match> { m1 } };
        var season = new Season { Name = "S", Rounds = new List<Round> { round } };
        var p = new Player { Id = 1, DiscordUsername = "x", IsActive = true };

        var rows = StandingsAnalyticsGenerator.BuildLandrynkiBarEntries(season, new[] { p }, pl => pl.DiscordUsername);

        rows.Should().BeEmpty();
    }

    [Fact]
    public void BuildLandrynkiBarEntries_AllTyped_ReturnsEmpty()
    {
        var m1 = new Match { Id = 10, Status = MatchStatus.Finished, HomeScore = 50, AwayScore = 40 };
        var round = new Round { Number = 1, Matches = new List<Match> { m1 } };
        var season = new Season { Name = "S", Rounds = new List<Round> { round } };
        var p = new Player
        {
            Id = 1,
            DiscordUsername = "full",
            IsActive = true,
            Predictions = new List<Prediction>
            {
                new() { MatchId = 10, IsValid = true, HomeTip = 50, AwayTip = 40 }
            }
        };

        var rows = StandingsAnalyticsGenerator.BuildLandrynkiBarEntries(season, new[] { p }, pl => pl.DiscordUsername);

        rows.Should().BeEmpty();
    }

    [Fact]
    public void TryGenerateLandrynkiTablePng_WithRows_ReturnsPng()
    {
        var m1 = new Match { Id = 3, Status = MatchStatus.Finished, HomeScore = 50, AwayScore = 40 };
        var round = new Round { Number = 1, Matches = new List<Match> { m1 } };
        var season = new Season { Name = "Sezon test", Rounds = new List<Round> { round } };
        var p = new Player { Id = 1, DiscordUsername = "miss", IsActive = true, Predictions = new List<Prediction>() };

        var gen = new StandingsAnalyticsGenerator(new DbUsernameDisplayNameResolver());
        var png = gen.TryGenerateLandrynkiTablePng(season, new List<Player> { p });

        png.Should().NotBeNull();
        png!.Length.Should().BeGreaterThan(500);
        png[0].Should().Be(0x89);
        png[1].Should().Be(0x50);
    }
}
