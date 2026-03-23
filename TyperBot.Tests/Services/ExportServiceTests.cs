using System.Text;
using FluentAssertions;
using TyperBot.Application.Services;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;

namespace TyperBot.Tests.Services;

public class ExportServiceTests
{
    private readonly ExportService _sut = new();

    [Fact]
    public void ExportSeasonToCsv_IncludesSeasonNameStandingsAndRoundSection()
    {
        var match = new Match
        {
            Id = 10,
            RoundId = 1,
            HomeTeam = "Team A",
            AwayTeam = "Team B",
            Status = MatchStatus.Finished,
            HomeScore = 50,
            AwayScore = 40,
            StartTime = DateTimeOffset.UtcNow
        };
        var round = new Round
        {
            Id = 1,
            SeasonId = 1,
            Number = 2,
            Description = "K2",
            Matches = new List<Match> { match }
        };
        var season = new Season
        {
            Id = 1,
            Name = "Test Season",
            Rounds = new List<Round> { round }
        };

        var player = new Player
        {
            Id = 1,
            DiscordUsername = "alice",
            IsActive = true
        };
        var prediction = new Prediction
        {
            Id = 100,
            MatchId = match.Id,
            PlayerId = player.Id,
            HomeTip = 51,
            AwayTip = 39,
            IsValid = true,
            Match = match,
            Player = player
        };
        var score = new PlayerScore
        {
            Id = 200,
            PredictionId = prediction.Id,
            PlayerId = player.Id,
            Points = 35,
            Bucket = Bucket.P35,
            Prediction = prediction,
            Player = player
        };
        prediction.PlayerScore = score;
        player.Predictions.Add(prediction);
        player.PlayerScores.Add(score);

        var csv = Encoding.UTF8.GetString(_sut.ExportSeasonToCsv(season, new List<Player> { player }));

        csv.Should().Contain("PGE Ekstraliga Test Season - Season Export");
        csv.Should().Contain("=== SEASON STANDINGS ===");
        csv.Should().Contain("alice");
        csv.Should().Contain(",35,");
        csv.Should().Contain("=== ROUND 2 ===");
        csv.Should().Contain("Team A vs Team B");
        csv.Should().Contain("50:40");
    }

    [Fact]
    public void ExportSeasonToCsv_EscapesCommaInTeamAndPlayerNames()
    {
        var match = new Match
        {
            Id = 1,
            RoundId = 1,
            HomeTeam = "Gorzów, WLKP",
            AwayTeam = "Leszno",
            Status = MatchStatus.Scheduled,
            StartTime = DateTimeOffset.UtcNow
        };
        var round = new Round { Id = 1, SeasonId = 1, Number = 1, Matches = new List<Match> { match } };
        var season = new Season { Id = 1, Name = "S", Rounds = new List<Round> { round } };
        var player = new Player { Id = 1, DiscordUsername = "bob, jr", IsActive = true };
        var prediction = new Prediction
        {
            MatchId = 1,
            PlayerId = 1,
            HomeTip = 45,
            AwayTip = 45,
            IsValid = true,
            Match = match,
            Player = player
        };
        player.Predictions.Add(prediction);

        var csv = Encoding.UTF8.GetString(_sut.ExportSeasonToCsv(season, new List<Player> { player }));

        csv.Should().Contain("\"Gorzów, WLKP vs Leszno\"");
        csv.Should().Contain("\"bob, jr\"");
    }

    [Fact]
    public void ExportRoundToCsv_IncludesStandingsAndMatchRows()
    {
        var match = new Match
        {
            Id = 5,
            RoundId = 1,
            HomeTeam = "H",
            AwayTeam = "A",
            Status = MatchStatus.Finished,
            HomeScore = 45,
            AwayScore = 45,
            StartTime = DateTimeOffset.UtcNow
        };
        var round = new Round { Id = 1, SeasonId = 1, Number = 4, Matches = new List<Match> { match } };
        var player = new Player { Id = 1, DiscordUsername = "carol", IsActive = true };
        var prediction = new Prediction
        {
            MatchId = 5,
            PlayerId = 1,
            HomeTip = 45,
            AwayTip = 45,
            IsValid = true,
            Match = match,
            Player = player
        };
        var score = new PlayerScore
        {
            PredictionId = prediction.Id,
            PlayerId = 1,
            Points = 50,
            Bucket = Bucket.P50,
            Prediction = prediction,
            Player = player
        };
        prediction.PlayerScore = score;
        player.Predictions.Add(prediction);
        player.PlayerScores.Add(score);

        var csv = Encoding.UTF8.GetString(_sut.ExportRoundToCsv(round, new List<Player> { player }));

        csv.Should().Contain("PGE Ekstraliga Round 4 - Export");
        csv.Should().Contain("=== ROUND STANDINGS ===");
        csv.Should().Contain("carol");
        csv.Should().Contain(",50,");
        csv.Should().Contain("=== MATCH DETAILS ===");
        csv.Should().Contain("45:45");
    }

    [Fact]
    public void ExportSeasonToCsv_InactivePlayersOmittedFromStandings()
    {
        var season = new Season { Id = 1, Name = "S", Rounds = new List<Round>() };
        var active = new Player { DiscordUsername = "on", IsActive = true };
        var inactive = new Player { DiscordUsername = "off", IsActive = false };

        var csv = Encoding.UTF8.GetString(_sut.ExportSeasonToCsv(season, new List<Player> { active, inactive }));

        csv.Should().Contain("on");
        csv.Should().NotContain("off");
    }
}
