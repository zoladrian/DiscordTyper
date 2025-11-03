using FluentAssertions;
using Moq;
using TyperBot.Application.Services;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;
using Xunit;
using DomainMatch = TyperBot.Domain.Entities.Match;
using DomainPlayer = TyperBot.Domain.Entities.Player;
using DomainPrediction = TyperBot.Domain.Entities.Prediction;
using DomainPlayerScore = TyperBot.Domain.Entities.PlayerScore;

namespace TyperBot.Tests.Services;

public class PredictionServiceTests
{
    private readonly Mock<IPredictionRepository> _predictionRepo;
    private readonly Mock<IMatchRepository> _matchRepo;
    private readonly Mock<IPlayerRepository> _playerRepo;
    private readonly Mock<IPlayerScoreRepository> _playerScoreRepo;
    private readonly ScoreCalculator _scoreCalculator;
    private readonly PredictionService _service;

    public PredictionServiceTests()
    {
        _predictionRepo = new Mock<IPredictionRepository>();
        _matchRepo = new Mock<IMatchRepository>();
        _playerRepo = new Mock<IPlayerRepository>();
        _playerScoreRepo = new Mock<IPlayerScoreRepository>();
        _scoreCalculator = new ScoreCalculator();
        _service = new PredictionService(
            _predictionRepo.Object,
            _matchRepo.Object,
            _playerRepo.Object,
            _scoreCalculator,
            _playerScoreRepo.Object);
    }

    [Fact]
    public async Task ValidatePrediction_NegativeValues_ReturnsError()
    {
        // Act
        var result = await _service.ValidatePrediction(123, 1, -5, 95);

        // Assert
        result.isValid.Should().BeFalse();
        result.errorMessage.Should().Contain("greater than or equal to 0");
    }

    [Theory]
    [InlineData(50, 41)] // Sum = 91
    [InlineData(46, 43)] // Sum = 89
    [InlineData(60, 20)] // Sum = 80
    public async Task ValidatePrediction_SumNotEqual90_ReturnsError(int home, int away)
    {
        // Act
        var result = await _service.ValidatePrediction(123, 1, home, away);

        // Assert
        result.isValid.Should().BeFalse();
        result.errorMessage.Should().Contain("must equal 90");
    }

    [Fact]
    public async Task ValidatePrediction_MatchNotFound_ReturnsError()
    {
        // Arrange
        _matchRepo.Setup(x => x.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((DomainMatch?)null);

        // Act
        var result = await _service.ValidatePrediction(123, 1, 50, 40);

        // Assert
        result.isValid.Should().BeFalse();
        result.errorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ValidatePrediction_AfterStartTime_ReturnsError()
    {
        // Arrange
        var match = new DomainMatch
        {
            Id = 1,
            StartTime = DateTimeOffset.UtcNow.AddHours(-1),
            Status = MatchStatus.Scheduled
        };
        _matchRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(match);

        // Act
        var result = await _service.ValidatePrediction(123, 1, 50, 40);

        // Assert
        result.isValid.Should().BeFalse();
        result.errorMessage.Should().Contain("expired");
    }

    [Fact]
    public async Task ValidatePrediction_CancelledMatch_ReturnsError()
    {
        // Arrange
        var match = new DomainMatch
        {
            Id = 1,
            StartTime = DateTimeOffset.UtcNow.AddHours(1),
            Status = MatchStatus.Cancelled
        };
        _matchRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(match);

        // Act
        var result = await _service.ValidatePrediction(123, 1, 50, 40);

        // Assert
        result.isValid.Should().BeFalse();
        result.errorMessage.Should().Contain("cancelled");
    }

    [Fact]
    public async Task ValidatePrediction_ValidPrediction_ReturnsTrue()
    {
        // Arrange
        var match = new DomainMatch
        {
            Id = 1,
            StartTime = DateTimeOffset.UtcNow.AddHours(1),
            Status = MatchStatus.Scheduled
        };
        _matchRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(match);

        // Act
        var result = await _service.ValidatePrediction(123, 1, 50, 40);

        // Assert
        result.isValid.Should().BeTrue();
        result.errorMessage.Should().BeNull();
    }

    [Fact]
    public async Task CreateOrUpdatePredictionAsync_PlayerNotFound_ReturnsNull()
    {
        // Arrange
        _playerRepo.Setup(x => x.GetByDiscordUserIdAsync(It.IsAny<ulong>()))
            .ReturnsAsync((DomainPlayer?)null);

        // Act
        var result = await _service.CreateOrUpdatePredictionAsync(123, 1, 50, 40);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateOrUpdatePredictionAsync_NewPrediction_CreatesSuccessfully()
    {
        // Arrange
        var player = new DomainPlayer { Id = 1, DiscordUserId = 123 };
        _playerRepo.Setup(x => x.GetByDiscordUserIdAsync(123)).ReturnsAsync(player);
        _predictionRepo.Setup(x => x.GetByMatchAndPlayerAsync(1, 1))
            .ReturnsAsync((DomainPrediction?)null);
        _predictionRepo.Setup(x => x.AddAsync(It.IsAny<DomainPrediction>()))
            .ReturnsAsync((DomainPrediction p) => p);

        // Act
        var result = await _service.CreateOrUpdatePredictionAsync(123, 1, 50, 40);

        // Assert
        result.Should().NotBeNull();
        result!.HomeTip.Should().Be(50);
        result.AwayTip.Should().Be(40);
        _predictionRepo.Verify(x => x.AddAsync(It.IsAny<DomainPrediction>()), Times.Once);
    }

    [Fact]
    public async Task CreateOrUpdatePredictionAsync_ExistingPrediction_UpdatesSuccessfully()
    {
        // Arrange
        var player = new DomainPlayer { Id = 1, DiscordUserId = 123 };
        var existing = new DomainPrediction { Id = 1, MatchId = 1, PlayerId = 1, HomeTip = 45, AwayTip = 45 };
        _playerRepo.Setup(x => x.GetByDiscordUserIdAsync(123)).ReturnsAsync(player);
        _predictionRepo.Setup(x => x.GetByMatchAndPlayerAsync(1, 1)).ReturnsAsync(existing);

        // Act
        var result = await _service.CreateOrUpdatePredictionAsync(123, 1, 52, 38);

        // Assert
        result.Should().NotBeNull();
        result!.HomeTip.Should().Be(52);
        result.AwayTip.Should().Be(38);
        result.UpdatedAt.Should().NotBeNull();
        _predictionRepo.Verify(x => x.UpdateAsync(It.IsAny<DomainPrediction>()), Times.Once);
    }

    [Fact]
    public async Task RecalculateMatchScoresAsync_MatchNotFinished_DoesNothing()
    {
        // Arrange
        var match = new DomainMatch { Id = 1, Status = MatchStatus.Scheduled };
        _matchRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(match);

        // Act
        await _service.RecalculateMatchScoresAsync(1);

        // Assert
        _predictionRepo.Verify(x => x.GetValidPredictionsByMatchAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RecalculateMatchScoresAsync_FinishedMatch_CalculatesScores()
    {
        // Arrange
        var match = new DomainMatch
        {
            Id = 1,
            Status = MatchStatus.Finished,
            HomeScore = 50,
            AwayScore = 40
        };
        var predictions = new List<DomainPrediction>
        {
            new DomainPrediction { Id = 1, HomeTip = 50, AwayTip = 40, PlayerId = 1 },
            new DomainPrediction { Id = 2, HomeTip = 48, AwayTip = 42, PlayerId = 2 }
        };

        _matchRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(match);
        _predictionRepo.Setup(x => x.GetValidPredictionsByMatchAsync(1))
            .ReturnsAsync(predictions);

        // Act
        await _service.RecalculateMatchScoresAsync(1);

        // Assert
        _playerScoreRepo.Verify(x => x.AddAsync(It.IsAny<DomainPlayerScore>()), Times.Exactly(2));
    }
}

