using System.Diagnostics;
using FluentAssertions;
using TyperBot.Application.Services;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;

namespace TyperBot.Tests.Services;

/// <summary>
/// Soft performance guards — generous thresholds to avoid flaky CI; filter with trait when needed.
/// </summary>
[Trait("Category", "Performance")]
public class ExportServicePerformanceTests
{
    [Fact]
    public void ExportSeasonToCsv_ManyActivePlayers_CompletesWithinReasonableTime()
    {
        var svc = new ExportService();
        var season = new Season { Name = "PerfEmptyRounds", Rounds = new List<Round>() };
        var players = Enumerable.Range(1, 2000)
            .Select(i => new Player { DiscordUsername = $"user{i}", IsActive = true })
            .ToList();

        var sw = Stopwatch.StartNew();
        var bytes = svc.ExportSeasonToCsv(season, players);
        sw.Stop();

        bytes.Should().NotBeEmpty();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void ExportSeasonToCsv_LargePredictionGrid_CompletesWithinReasonableTime()
    {
        var svc = new ExportService();
        const int roundCount = 10;
        const int matchesPerRound = 4;
        const int playerCount = 80;

        var matchId = 1;
        var rounds = new List<Round>();
        for (var r = 1; r <= roundCount; r++)
        {
            var matches = new List<Match>();
            for (var m = 0; m < matchesPerRound; m++)
            {
                matches.Add(new Match
                {
                    Id = matchId++,
                    RoundId = r,
                    HomeTeam = "H",
                    AwayTeam = "A",
                    Status = MatchStatus.Finished,
                    HomeScore = 50,
                    AwayScore = 40,
                    StartTime = DateTimeOffset.UtcNow
                });
            }

            rounds.Add(new Round
            {
                Id = r,
                SeasonId = 1,
                Number = r,
                Matches = matches
            });
        }

        var season = new Season { Id = 1, Name = "PerfGrid", Rounds = rounds };
        var allMatches = rounds.SelectMany(x => x.Matches).ToList();
        var players = new List<Player>();

        for (var p = 1; p <= playerCount; p++)
        {
            var pl = new Player { Id = p, DiscordUsername = $"p{p}", IsActive = true };
            foreach (var mat in allMatches)
            {
                var pred = new Prediction
                {
                    MatchId = mat.Id,
                    PlayerId = p,
                    HomeTip = 45,
                    AwayTip = 45,
                    IsValid = true,
                    Match = mat,
                    Player = pl
                };
                var ps = new PlayerScore
                {
                    Points = 10,
                    Bucket = Bucket.P10,
                    Prediction = pred,
                    Player = pl,
                    PlayerId = p
                };
                pred.PlayerScore = ps;
                pl.Predictions.Add(pred);
                pl.PlayerScores.Add(ps);
            }

            players.Add(pl);
        }

        var sw = Stopwatch.StartNew();
        var bytes = svc.ExportSeasonToCsv(season, players);
        sw.Stop();

        bytes.Length.Should().BeGreaterThan(50_000);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
    }
}
