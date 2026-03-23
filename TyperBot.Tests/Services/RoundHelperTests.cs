using FluentAssertions;
using TyperBot.Application.Services;

namespace TyperBot.Tests.Services;

public class RoundHelperTests
{
    [Theory]
    [InlineData(1, "Runda 1")]
    [InlineData(14, "Runda 14")]
    [InlineData(15, "1/4 finału – 1")]
    [InlineData(16, "1/4 finału – 2")]
    [InlineData(17, "1/2 finału")]
    [InlineData(18, "Finał")]
    public void GetRoundLabel_KnownRounds_ReturnsPolishLabel(int round, string expected)
    {
        RoundHelper.GetRoundLabel(round).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "Runda 0")]
    [InlineData(19, "Runda 19")]
    [InlineData(-1, "Runda -1")]
    public void GetRoundLabel_OutOfStandardRange_UsesFallbackFormat(int round, string expected)
    {
        RoundHelper.GetRoundLabel(round).Should().Be(expected);
    }

    [Fact]
    public void GetRoundShortLabel_MatchesGetRoundLabel()
    {
        foreach (var n in RoundHelper.GetAllRoundNumbers())
        {
            RoundHelper.GetRoundShortLabel(n).Should().Be(RoundHelper.GetRoundLabel(n));
        }
    }

    [Theory]
    [InlineData(1, "Regularna kolejka 1")]
    [InlineData(14, "Regularna kolejka 14")]
    [InlineData(15, "Pierwsza kolejka ćwierćfinałów")]
    [InlineData(16, "Druga kolejka ćwierćfinałów")]
    [InlineData(17, "Półfinały")]
    [InlineData(18, "Mecz finałowy")]
    public void GetRoundDescription_KnownRounds_ReturnsExpected(int round, string expected)
    {
        RoundHelper.GetRoundDescription(round).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(18, true)]
    [InlineData(19, false)]
    public void IsValidRoundNumber_Boundaries(int round, bool valid)
    {
        RoundHelper.IsValidRoundNumber(round).Should().Be(valid);
    }

    [Fact]
    public void GetAllRoundNumbers_ReturnsOneThroughEighteen()
    {
        RoundHelper.GetAllRoundNumbers().Should().Equal(Enumerable.Range(1, 18));
    }
}
