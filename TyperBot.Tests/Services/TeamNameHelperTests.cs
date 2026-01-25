using FluentAssertions;
using TyperBot.Application.Services;
using Xunit;

namespace TyperBot.Tests.Services;

public class TeamNameHelperTests
{
    [Theory]
    [InlineData("Motor Lublin", "LUB")]
    [InlineData("Sparta Wrocław", "WRO")]
    [InlineData("Betard Sparta Wrocław", "WRO")]
    [InlineData("Apator Toruń", "TOR")]
    [InlineData("GKM Grudziądz", "GRU")]
    [InlineData("Falubaz Zielona Góra", "ZIE")]
    [InlineData("Stal Gorzów", "GOR")]
    [InlineData("Włókniarz Częstochowa", "CZE")]
    [InlineData("Unia Leszno", "LES")]
    [InlineData("Fogo Unia Leszno", "LES")]
    public void GetTeamShortcut_KnownTeams_ReturnsCorrectShortcut(string teamName, string expectedShortcut)
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut(teamName);

        // Assert
        result.Should().Be(expectedShortcut);
    }

    [Fact]
    public void GetTeamShortcut_EmptyString_ReturnsQuestionMarks()
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut("");

        // Assert
        result.Should().Be("???");
    }

    [Fact]
    public void GetTeamShortcut_WhitespaceOnly_ReturnsQuestionMarks()
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut("   ");

        // Assert
        result.Should().Be("???");
    }

    [Fact]
    public void GetTeamShortcut_Null_ReturnsQuestionMarks()
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut(null!);

        // Assert
        result.Should().Be("???");
    }

    [Fact]
    public void GetTeamShortcut_CaseInsensitive_ReturnsCorrectShortcut()
    {
        // Arrange
        var variations = new[]
        {
            "motor lublin",
            "MOTOR LUBLIN",
            "Motor Lublin",
            "MoToR lUbLiN"
        };

        // Act & Assert
        foreach (var teamName in variations)
        {
            var result = TeamNameHelper.GetTeamShortcut(teamName);
            result.Should().Be("LUB", $"'{teamName}' should return 'LUB'");
        }
    }

    [Fact]
    public void GetTeamShortcut_UnknownTeam_UsesLastWord()
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut("New Team Poznań");

        // Assert
        result.Should().Be("POZ");
    }

    [Fact]
    public void GetTeamShortcut_UnknownTeamWithZielonaGora_ReturnsZIE()
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut("New Team Zielona Góra");

        // Assert
        result.Should().Be("ZIE");
    }

    [Fact]
    public void GetTeamShortcut_PolishCharacters_AreNormalized()
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut("Team Łódź");

        // Assert
        result.Should().Be("LOD");
    }

    [Theory]
    [InlineData("Motor Lublin", "Sparta Wrocław", "LUB-WRO")]
    [InlineData("Apator Toruń", "Unia Leszno", "TOR-LES")]
    [InlineData("Falubaz Zielona Góra", "Stal Gorzów", "ZIE-GOR")]
    [InlineData("GKM Grudziądz", "Włókniarz Częstochowa", "GRU-CZE")]
    public void GetMatchShortcut_ValidTeams_ReturnsCorrectFormat(
        string homeTeam, string awayTeam, string expectedShortcut)
    {
        // Act
        var result = TeamNameHelper.GetMatchShortcut(homeTeam, awayTeam);

        // Assert
        result.Should().Be(expectedShortcut);
    }

    [Fact]
    public void GetMatchShortcut_EmptyTeams_ReturnsQuestionMarks()
    {
        // Act
        var result = TeamNameHelper.GetMatchShortcut("", "");

        // Assert
        result.Should().Be("???-???");
    }

    [Fact]
    public void GetTeamShortcut_ShortTeamName_PadsWithX()
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut("Team AB");

        // Assert
        result.Should().Be("ABX");
    }

    [Fact]
    public void GetTeamShortcut_SingleCharacter_PadsWithX()
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut("Team A");

        // Assert
        result.Should().Be("AXX");
    }

    [Theory]
    [InlineData("Włókniarz", "WLO")]
    [InlineData("Częstochowa", "CZE")]
    [InlineData("Gorzów", "GOR")]
    [InlineData("Grudziądz", "GRU")]
    [InlineData("Toruń", "TOR")]
    [InlineData("Wrocław", "WRO")]
    [InlineData("Zielona", "ZIE")]
    [InlineData("Łódź", "LOD")]
    public void GetTeamShortcut_PolishCities_ReturnsNormalizedShortcut(string city, string expectedShortcut)
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut($"Team {city}");

        // Assert
        result.Should().Be(expectedShortcut);
    }

    [Fact]
    public void GetTeamShortcut_WithDashes_ParsesCorrectly()
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut("Team-Lublin");

        // Assert
        result.Should().Be("LUB");
    }

    [Fact]
    public void GetTeamShortcut_WithUnderscores_ParsesCorrectly()
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut("Team_Lublin");

        // Assert
        result.Should().Be("LUB");
    }

    [Fact]
    public void GetTeamShortcut_WithMultipleSpaces_ParsesCorrectly()
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut("   Motor   Lublin   ");

        // Assert
        result.Should().Be("LUB");
    }

    [Fact]
    public void GetTeamShortcut_AllUppercase_ReturnsUppercase()
    {
        // Act
        var result = TeamNameHelper.GetTeamShortcut("MOTOR LUBLIN");

        // Assert
        result.Should().Be("LUB");
        result.Should().Match(s => s == s.ToUpper(), "result should be all uppercase");
    }
}
