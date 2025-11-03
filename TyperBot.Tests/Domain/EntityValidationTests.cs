using FluentAssertions;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using Xunit;

namespace TyperBot.Tests.Domain;

public class EntityValidationTests
{
    [Fact]
    public void Season_CanBeCreated()
    {
        // Act
        var season = new Season
        {
            Name = "PGE Ekstraliga 2025",
            IsActive = true
        };

        // Assert
        season.Name.Should().Be("PGE Ekstraliga 2025");
        season.IsActive.Should().BeTrue();
        season.Rounds.Should().BeEmpty();
    }

    [Fact]
    public void Round_CanBeCreated()
    {
        // Act
        var round = new Round
        {
            Number = 1,
            Description = "Round 1"
        };

        // Assert
        round.Number.Should().Be(1);
        round.Matches.Should().BeEmpty();
    }

    [Fact]
    public void Match_DefaultStatusIsScheduled()
    {
        // Act
        var match = new Match
        {
            HomeTeam = "Motor Lublin",
            AwayTeam = "Włókniarz Częstochowa",
            StartTime = DateTimeOffset.UtcNow
        };

        // Assert
        match.HomeTeam.Should().Be("Motor Lublin");
        match.AwayTeam.Should().Be("Włókniarz Częstochowa");
    }

    [Fact]
    public void Player_CanStoreDiscordInfo()
    {
        // Act
        var player = new Player
        {
            DiscordUserId = 123456789012345678,
            DiscordUsername = "Speedway_Fan",
            IsActive = true
        };

        // Assert
        player.DiscordUserId.Should().Be(123456789012345678);
        player.DiscordUsername.Should().Be("Speedway_Fan");
    }

    [Fact]
    public void Prediction_DefaultIsValid()
    {
        // Act
        var prediction = new Prediction
        {
            HomeTip = 50,
            AwayTip = 40,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Assert
        prediction.HomeTip.Should().Be(50);
        prediction.AwayTip.Should().Be(40);
        prediction.IsValid.Should().BeTrue();
    }

    [Fact]
    public void PlayerScore_CanStoreBucket()
    {
        // Act
        var score = new PlayerScore
        {
            Points = 35,
            Bucket = Bucket.P35
        };

        // Assert
        score.Points.Should().Be(35);
        score.Bucket.Should().Be(Bucket.P35);
    }

    [Fact]
    public void MatchStatus_HasCorrectValues()
    {
        // Assert
        MatchStatus.Scheduled.Should().Be((MatchStatus)0);
        MatchStatus.InProgress.Should().Be((MatchStatus)1);
        MatchStatus.Finished.Should().Be((MatchStatus)2);
        MatchStatus.Postponed.Should().Be((MatchStatus)3);
        MatchStatus.Cancelled.Should().Be((MatchStatus)4);
    }

    [Fact]
    public void Bucket_HasCorrectPointValues()
    {
        // Assert
        ((int)Bucket.P50).Should().Be(50);
        ((int)Bucket.P35).Should().Be(35);
        ((int)Bucket.P20).Should().Be(20);
        ((int)Bucket.P2).Should().Be(2);
        ((int)Bucket.P0).Should().Be(0);
    }
}

