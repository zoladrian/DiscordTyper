using FluentAssertions;
using TyperBot.Application.Services;

namespace TyperBot.Tests.Services;

public class RevealedPredictionsTableImageGeneratorTests
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private readonly RevealedPredictionsTableImageGenerator _generator = new();

    [Fact]
    public void Generate_singleRow_produces_valid_png()
    {
        var rows = new[] { new RevealedTipRow("TestUser", "50:40") };

        var bytes = _generator.Generate(rows, "Footer line");

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(PngSignature.Length);
        bytes.Should().StartWith(PngSignature);
    }

    [Fact]
    public void Generate_moreRows_produces_larger_file_than_oneRow()
    {
        var one = new[] { new RevealedTipRow("A", "1:0") };
        var many = Enumerable.Range(1, 15)
            .Select(i => new RevealedTipRow($"Player{i}", $"{40 + i}:{50 - i}"))
            .ToArray();

        var small = _generator.Generate(one, "f");
        var large = _generator.Generate(many, "f");

        large.Length.Should().BeGreaterThan(small.Length);
    }

    [Fact]
    public void Generate_emptyRows_throws()
    {
        var act = () => _generator.Generate(Array.Empty<RevealedTipRow>(), "x");
        act.Should().Throw<ArgumentException>().WithParameterName("rows");
    }

    [Fact]
    public void Generate_nullRows_throws()
    {
        var act = () => _generator.Generate(null!, "x");
        act.Should().Throw<ArgumentNullException>().WithParameterName("rows");
    }
}
