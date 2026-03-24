using FluentAssertions;
using TyperBot.DiscordBot.Services;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;

namespace TyperBot.Tests.Services;

public class MatchPredictionRevealServiceTests
{
    [Fact]
    public void GetRevealBlockers_WhenCancelled_ReturnsReason()
    {
        var m = new Match
        {
            Status = MatchStatus.Cancelled,
            PredictionsRevealed = false,
            StartTime = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var b = MatchPredictionRevealService.GetRevealBlockers(m, DateTimeOffset.UtcNow);
        b.Should().Contain(s => s.Contains("odwołany"));
    }

    [Fact]
    public void GetRevealBlockers_WhenAlreadyRevealed_ReturnsReason()
    {
        var m = new Match
        {
            Status = MatchStatus.Scheduled,
            PredictionsRevealed = true,
            StartTime = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var b = MatchPredictionRevealService.GetRevealBlockers(m, DateTimeOffset.UtcNow);
        b.Should().Contain(s => s.Contains("już ujawnione"));
    }

    [Fact]
    public void GetRevealBlockers_WhenBeforeStart_ReturnsReason()
    {
        var start = DateTimeOffset.UtcNow.AddHours(2);
        var m = new Match
        {
            Status = MatchStatus.Scheduled,
            PredictionsRevealed = false,
            StartTime = start
        };
        var b = MatchPredictionRevealService.GetRevealBlockers(m, DateTimeOffset.UtcNow);
        b.Should().Contain(s => s.Contains("nie rozpoczął"));
    }

    [Fact]
    public void GetRevealBlockers_WhenOk_ReturnsEmpty()
    {
        var m = new Match
        {
            Status = MatchStatus.Scheduled,
            PredictionsRevealed = false,
            StartTime = DateTimeOffset.UtcNow.AddHours(-1)
        };
        MatchPredictionRevealService.GetRevealBlockers(m, DateTimeOffset.UtcNow).Should().BeEmpty();
    }
}
