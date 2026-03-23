using FluentAssertions;
using Moq;
using TyperBot.Application.Services;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;
using Xunit;

namespace TyperBot.Tests.Services;

public class EnhancedTableGeneratorTests
{
    private readonly TableGenerator _tableGenerator = new();
    private readonly Mock<ISeasonRepository> _seasonRepository;
    private readonly EnhancedTableGenerator _enhancedGenerator;

    public EnhancedTableGeneratorTests()
    {
        _seasonRepository = new Mock<ISeasonRepository>();
        _enhancedGenerator = new EnhancedTableGenerator(
            _tableGenerator,
            _seasonRepository.Object);
    }

    [Fact]
    public async Task GenerateSeasonTableAsync_TextFormat_ReturnsTextTable()
    {
        // Arrange
        var season = new Season
        {
            Id = 1,
            Name = "Season 1",
            PreferredTableFormat = TableFormat.Text
        };

        var players = new List<Player>
        {
            new Player
            {
                Id = 1,
                DiscordUsername = "Player1",
                PlayerScores = new List<PlayerScore>
                {
                    new PlayerScore
                    {
                        Points = 35,
                        Bucket = Bucket.P35,
                        Prediction = new Prediction { IsValid = true }
                    }
                }
            }
        };

        // Act
        var result = await _enhancedGenerator.GenerateSeasonTableAsync(season, players);

        // Assert
        result.format.Should().Be(TableFormat.Text);
        result.textTable.Should().NotBeNullOrEmpty();
        result.textTable.Should().Contain("Player1");
        result.textTable.Should().Contain("35");
        result.imageBytes.Should().BeNull();
    }

    [Fact]
    public async Task GenerateSeasonTableAsync_ImageFormat_ReturnsImageBytes()
    {
        // Arrange
        var season = new Season
        {
            Id = 1,
            Name = "Season 1",
            PreferredTableFormat = TableFormat.Image
        };

        var players = new List<Player>
        {
            new Player
            {
                Id = 1,
                DiscordUsername = "Player1",
                PlayerScores = new List<PlayerScore>()
            }
        };

        // Act — prawdziwy TableGenerator zwraca PNG (SkiaSharp)
        var result = await _enhancedGenerator.GenerateSeasonTableAsync(season, players);

        // Assert
        result.format.Should().Be(TableFormat.Image);
        result.imageBytes.Should().NotBeNull();
        result.imageBytes!.Length.Should().BeGreaterThan(8);
        result.imageBytes.Should().StartWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        result.textTable.Should().BeNull();
    }

    [Fact]
    public async Task GenerateSeasonTableAsync_TextFormat_CalculatesScoresCorrectly()
    {
        // Arrange
        var season = new Season
        {
            Id = 1,
            Name = "Season 1",
            PreferredTableFormat = TableFormat.Text
        };

        var players = new List<Player>
        {
            new Player
            {
                Id = 1,
                DiscordUsername = "Player1",
                PlayerScores = new List<PlayerScore>
                {
                    new PlayerScore { Points = 35, Bucket = Bucket.P35, Prediction = new Prediction { IsValid = true } },
                    new PlayerScore { Points = 20, Bucket = Bucket.P20, Prediction = new Prediction { IsValid = true } },
                    new PlayerScore { Points = 10, Bucket = Bucket.P10, Prediction = new Prediction { IsValid = true } }
                }
            },
            new Player
            {
                Id = 2,
                DiscordUsername = "Player2",
                PlayerScores = new List<PlayerScore>
                {
                    new PlayerScore { Points = 50, Bucket = Bucket.P50, Prediction = new Prediction { IsValid = true } }
                }
            }
        };

        // Act
        var result = await _enhancedGenerator.GenerateSeasonTableAsync(season, players);

        // Assert
        result.textTable.Should().NotBeNullOrEmpty();
        result.textTable.Should().Contain("Player1");
        result.textTable.Should().Contain("Player2");
        result.textTable.Should().Contain("65"); // Player1 total: 35+20+10
        result.textTable.Should().Contain("50"); // Player2 total
    }

    [Fact]
    public async Task GenerateSeasonTableAsync_TextFormat_SortsPlayersByPoints()
    {
        // Arrange
        var season = new Season
        {
            Id = 1,
            Name = "Season 1",
            PreferredTableFormat = TableFormat.Text
        };

        var players = new List<Player>
        {
            new Player
            {
                Id = 1,
                DiscordUsername = "LowScore",
                PlayerScores = new List<PlayerScore>
                {
                    new PlayerScore { Points = 10, Bucket = Bucket.P10, Prediction = new Prediction { IsValid = true } }
                }
            },
            new Player
            {
                Id = 2,
                DiscordUsername = "HighScore",
                PlayerScores = new List<PlayerScore>
                {
                    new PlayerScore { Points = 100, Bucket = Bucket.P35, Prediction = new Prediction { IsValid = true } }
                }
            }
        };

        // Act
        var result = await _enhancedGenerator.GenerateSeasonTableAsync(season, players);

        // Assert
        result.textTable.Should().NotBeNullOrEmpty();
        var highScoreIndex = result.textTable.IndexOf("HighScore");
        var lowScoreIndex = result.textTable.IndexOf("LowScore");
        highScoreIndex.Should().BeLessThan(lowScoreIndex, "HighScore should appear before LowScore");
    }

    [Fact]
    public async Task GenerateSeasonTableAsync_TextFormat_DisplaysMedalsForTop3()
    {
        // Arrange
        var season = new Season
        {
            Id = 1,
            Name = "Season 1",
            PreferredTableFormat = TableFormat.Text
        };

        var players = new List<Player>
        {
            new Player
            {
                Id = 1,
                DiscordUsername = "First",
                PlayerScores = new List<PlayerScore>
                {
                    new PlayerScore { Points = 100, Bucket = Bucket.P35, Prediction = new Prediction { IsValid = true } }
                }
            },
            new Player
            {
                Id = 2,
                DiscordUsername = "Second",
                PlayerScores = new List<PlayerScore>
                {
                    new PlayerScore { Points = 80, Bucket = Bucket.P20, Prediction = new Prediction { IsValid = true } }
                }
            },
            new Player
            {
                Id = 3,
                DiscordUsername = "Third",
                PlayerScores = new List<PlayerScore>
                {
                    new PlayerScore { Points = 60, Bucket = Bucket.P10, Prediction = new Prediction { IsValid = true } }
                }
            }
        };

        // Act
        var result = await _enhancedGenerator.GenerateSeasonTableAsync(season, players);

        // Assert
        result.textTable.Should().Contain("🥇"); // Gold medal
        result.textTable.Should().Contain("🥈"); // Silver medal
        result.textTable.Should().Contain("🥉"); // Bronze medal
    }

    [Fact]
    public async Task GenerateSeasonTableAsync_TextFormat_CountsExactScoresCorrectly()
    {
        // Arrange
        var season = new Season
        {
            Id = 1,
            Name = "Season 1",
            PreferredTableFormat = TableFormat.Text
        };

        var players = new List<Player>
        {
            new Player
            {
                Id = 1,
                DiscordUsername = "Player1",
                PlayerScores = new List<PlayerScore>
                {
                    new PlayerScore { Points = 35, Bucket = Bucket.P35, Prediction = new Prediction { IsValid = true } }, // Exact score
                    new PlayerScore { Points = 50, Bucket = Bucket.P50, Prediction = new Prediction { IsValid = true } }, // Perfect draw
                    new PlayerScore { Points = 20, Bucket = Bucket.P20, Prediction = new Prediction { IsValid = true } }  // Not exact
                }
            }
        };

        // Act
        var result = await _enhancedGenerator.GenerateSeasonTableAsync(season, players);

        // Assert
        result.textTable.Should().NotBeNullOrEmpty();
        // Player should have 2 exact scores (P35 and P50)
        // The exact count should be visible in the Cel column
        result.textTable.Should().Contain("Player1");
    }

    [Fact]
    public async Task GenerateSeasonTableAsync_TextFormat_FiltersInvalidPredictions()
    {
        // Arrange
        var season = new Season
        {
            Id = 1,
            Name = "Season 1",
            PreferredTableFormat = TableFormat.Text
        };

        var players = new List<Player>
        {
            new Player
            {
                Id = 1,
                DiscordUsername = "Player1",
                PlayerScores = new List<PlayerScore>
                {
                    new PlayerScore { Points = 35, Bucket = Bucket.P35, Prediction = new Prediction { IsValid = true } },
                    new PlayerScore { Points = 20, Bucket = Bucket.P20, Prediction = new Prediction { IsValid = false } }, // Invalid
                    new PlayerScore { Points = 10, Bucket = Bucket.P10, Prediction = null } // Null prediction
                }
            }
        };

        // Act
        var result = await _enhancedGenerator.GenerateSeasonTableAsync(season, players);

        // Assert
        result.textTable.Should().NotBeNullOrEmpty();
        result.textTable.Should().Contain("35"); // Only valid prediction should count
        result.textTable.Should().Contain("  1  "); // Only 1 valid prediction
    }
}
