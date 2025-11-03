using FluentAssertions;
using Moq;
using TyperBot.Application.Services;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;
using Xunit;
using DomainMatch = TyperBot.Domain.Entities.Match;
using DomainRound = TyperBot.Domain.Entities.Round;

namespace TyperBot.Tests.Services;

public class RoundManagerTests
{
    private readonly Mock<IRoundRepository> _roundRepo;
    private readonly Mock<IMatchRepository> _matchRepo;
    private readonly RoundManager _manager;

    public RoundManagerTests()
    {
        _roundRepo = new Mock<IRoundRepository>();
        _matchRepo = new Mock<IMatchRepository>();
        _manager = new RoundManager(_roundRepo.Object, _matchRepo.Object);
    }

    [Fact]
    public async Task GetMatchesByRoundAsync_RoundNotFound_ReturnsEmpty()
    {
        // Arrange
        _roundRepo.Setup(x => x.GetByNumberAsync(1, 1))
            .ReturnsAsync((DomainRound?)null);

        // Act
        var result = await _manager.GetMatchesByRoundAsync(1, 1);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMatchesByRoundAsync_RoundExists_ReturnsOrderedMatches()
    {
        // Arrange
        var round = new DomainRound
        {
            Id = 1,
            Number = 1,
            Matches = new List<DomainMatch>
            {
                new DomainMatch { Id = 2, StartTime = DateTimeOffset.UtcNow.AddHours(2) },
                new DomainMatch { Id = 1, StartTime = DateTimeOffset.UtcNow.AddHours(1) },
                new DomainMatch { Id = 3, StartTime = DateTimeOffset.UtcNow.AddHours(3) }
            }
        };
        _roundRepo.Setup(x => x.GetByNumberAsync(1, 1)).ReturnsAsync(round);

        // Act
        var result = await _manager.GetMatchesByRoundAsync(1, 1);

        // Assert
        result.Should().HaveCount(3);
        result.Select(m => m.Id).Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public async Task IsRoundCompleteAsync_RoundNotFound_ReturnsFalse()
    {
        // Arrange
        _roundRepo.Setup(x => x.GetByNumberAsync(1, 1))
            .ReturnsAsync((DomainRound?)null);

        // Act
        var result = await _manager.IsRoundCompleteAsync(1, 1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsRoundCompleteAsync_AllMatchesFinished_ReturnsTrue()
    {
        // Arrange
        var round = new DomainRound
        {
            Id = 1,
            Matches = new List<DomainMatch>
            {
                new DomainMatch { Status = MatchStatus.Finished },
                new DomainMatch { Status = MatchStatus.Finished },
                new DomainMatch { Status = MatchStatus.Cancelled }
            }
        };
        _roundRepo.Setup(x => x.GetByNumberAsync(1, 1)).ReturnsAsync(round);

        // Act
        var result = await _manager.IsRoundCompleteAsync(1, 1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsRoundCompleteAsync_SomeMatchesPending_ReturnsFalse()
    {
        // Arrange
        var round = new DomainRound
        {
            Id = 1,
            Matches = new List<DomainMatch>
            {
                new DomainMatch { Status = MatchStatus.Finished },
                new DomainMatch { Status = MatchStatus.Scheduled },
                new DomainMatch { Status = MatchStatus.InProgress }
            }
        };
        _roundRepo.Setup(x => x.GetByNumberAsync(1, 1)).ReturnsAsync(round);

        // Act
        var result = await _manager.IsRoundCompleteAsync(1, 1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetRoundCompletionStatusAsync_ReturnsCorrectStatus()
    {
        // Arrange
        var rounds = new List<DomainRound>
        {
            new DomainRound
            {
                Number = 1,
                Matches = new List<DomainMatch>
                {
                    new DomainMatch { Status = MatchStatus.Finished },
                    new DomainMatch { Status = MatchStatus.Finished }
                }
            },
            new DomainRound
            {
                Number = 2,
                Matches = new List<DomainMatch>
                {
                    new DomainMatch { Status = MatchStatus.Finished },
                    new DomainMatch { Status = MatchStatus.Scheduled }
                }
            }
        };
        _roundRepo.Setup(x => x.GetBySeasonIdAsync(1)).ReturnsAsync(rounds);

        // Act
        var result = await _manager.GetRoundCompletionStatusAsync(1);

        // Assert
        result.Should().HaveCount(2);
        result[1].Should().BeTrue();
        result[2].Should().BeFalse();
    }
}

