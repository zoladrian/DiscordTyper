using FluentAssertions;
using TyperBot.Application.Services;

namespace TyperBot.Tests.Services;

public class MatchResultsTableImageGeneratorTests
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private readonly MatchResultsTableImageGenerator _generator = new();

    [Fact]
    public void Generate_withResultRowAndPlayers_produces_valid_png()
    {
        var rows = new[]
        {
            new MatchResultTableRow("Wynik rzeczywisty", "52:38", "-"),
            new MatchResultTableRow("Gracz1", "50:40", "18")
        };

        var bytes = _generator.Generate(rows, "Kolejka 1 · 2 wpisy");

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(PngSignature.Length);
        bytes.Should().StartWith(PngSignature);
    }

    [Fact]
    public void Generate_emptyRows_throws()
    {
        var act = () => _generator.Generate(Array.Empty<MatchResultTableRow>(), "footer");
        act.Should().Throw<ArgumentException>().WithParameterName("rows");
    }

    [Fact]
    public void Generate_onlyResultRow_produces_valid_png()
    {
        var rows = new[] { new MatchResultTableRow("Wynik rzeczywisty", "45:45", "-") };

        var bytes = _generator.Generate(rows, "tylko wynik");

        bytes.Should().StartWith(PngSignature);
    }
}
