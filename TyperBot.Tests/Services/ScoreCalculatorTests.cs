using FluentAssertions;
using TyperBot.Application.Services;
using TyperBot.Domain.Enums;
using Xunit;

namespace TyperBot.Tests.Services;

public class ScoreCalculatorTests
{
    private readonly ScoreCalculator _calculator;

    public ScoreCalculatorTests()
    {
        _calculator = new ScoreCalculator();
    }

    [Fact]
    public void CalculateScore_WrongWinner_ReturnsZeroPoints()
    {
        // Arrange: Real home wins, predicted away wins
        int realHome = 50, realAway = 40;
        int tipHome = 40, tipAway = 50;

        // Act
        var (points, bucket) = _calculator.CalculateScore(realHome, realAway, tipHome, tipAway);

        // Assert
        points.Should().Be(0);
        bucket.Should().Be(Bucket.P0);
    }

    [Fact]
    public void CalculateScore_PerfectDraw_Returns50Points()
    {
        // Arrange: Perfect draw 45:45
        int realHome = 45, realAway = 45;
        int tipHome = 45, tipAway = 45;

        // Act
        var (points, bucket) = _calculator.CalculateScore(realHome, realAway, tipHome, tipAway);

        // Assert
        points.Should().Be(50);
        bucket.Should().Be(Bucket.P50);
    }

    [Fact]
    public void CalculateScore_ExactScore_Returns35Points()
    {
        // Arrange: Exact score match
        int realHome = 52, realAway = 38;
        int tipHome = 52, tipAway = 38;

        // Act
        var (points, bucket) = _calculator.CalculateScore(realHome, realAway, tipHome, tipAway);

        // Assert
        points.Should().Be(35);
        bucket.Should().Be(Bucket.P35);
    }

    [Theory]
    [InlineData(50, 40, 51, 39, 20)] // Real margin=10, Tip margin=12, marginDiff=2, penalty=0, totalDiff=2 -> 20 points
    [InlineData(50, 40, 48, 42, 18)] // Real margin=10, Tip margin=6, marginDiff=4, penalty=0, totalDiff=4 -> 18 points
    [InlineData(50, 40, 47, 43, 16)] // Real margin=10, Tip margin=4, marginDiff=6, penalty=0, totalDiff=6 -> 16 points
    [InlineData(50, 40, 46, 44, 14)] // Real margin=10, Tip margin=2, marginDiff=8, penalty=0, totalDiff=8 -> 14 points
    [InlineData(50, 40, 55, 35, 12)] // Real margin=10, Tip margin=20, marginDiff=10, penalty=0, totalDiff=10 -> 12 points
    [InlineData(50, 40, 54, 36, 14)] // Real margin=10, Tip margin=18, marginDiff=8, penalty=0, totalDiff=8 -> 14 points
    public void CalculateScore_DifferentMargins_ReturnsCorrectPoints(
        int realHome, int realAway, int tipHome, int tipAway, int expectedPoints)
    {
        // Act
        var (points, bucket) = _calculator.CalculateScore(realHome, realAway, tipHome, tipAway);

        // Assert
        points.Should().Be(expectedPoints);
    }

    [Fact]
    public void CalculateScore_LargeMarginDifference_Returns2Points()
    {
        // Arrange: totalDiff >= 19 (marginDiff=8 + penalty=0 = 8... need larger diff)
        // Real: 50-40 = 10 margin; Tip: 80-10 = 70 margin; marginDiff = 60; totalDiff = 60 -> 2 points
        int realHome = 50, realAway = 40;
        int tipHome = 80, tipAway = 10;

        // Act
        var (points, bucket) = _calculator.CalculateScore(realHome, realAway, tipHome, tipAway);

        // Assert
        points.Should().Be(2);
        bucket.Should().Be(Bucket.P2);
    }

    [Fact]
    public void CalculateScore_HomeWinPredictedCorrectly_ReturnsNonZero()
    {
        // Arrange: Home wins in both
        int realHome = 52, realAway = 38;
        int tipHome = 48, tipAway = 42;

        // Act
        var (points, bucket) = _calculator.CalculateScore(realHome, realAway, tipHome, tipAway);

        // Assert
        points.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CalculateScore_AwayWinPredictedCorrectly_ReturnsNonZero()
    {
        // Arrange: Away wins in both
        int realHome = 38, realAway = 52;
        int tipHome = 42, tipAway = 48;

        // Act
        var (points, bucket) = _calculator.CalculateScore(realHome, realAway, tipHome, tipAway);

        // Assert
        points.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CalculateScore_DrawPredictedAsHomeWin_ReturnsZero()
    {
        // Arrange: Real draw, predicted home win
        int realHome = 45, realAway = 45;
        int tipHome = 50, tipAway = 40;

        // Act
        var (points, bucket) = _calculator.CalculateScore(realHome, realAway, tipHome, tipAway);

        // Assert
        points.Should().Be(0);
        bucket.Should().Be(Bucket.P0);
    }
}

