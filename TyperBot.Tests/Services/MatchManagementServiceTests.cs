using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TyperBot.Application.Services;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Data;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.Tests.Services;

public class MatchManagementServiceTests
{
    [Theory]
    [InlineData(45, 45, true, null)]
    [InlineData(0, 90, true, null)]
    [InlineData(90, 0, true, null)]
    [InlineData(50, 40, true, null)]
    [InlineData(44, 45, true, null)]
    [InlineData(46, 45, true, null)]
    [InlineData(-1, 91, false, "Wyniki nie mogą być ujemne.")]
    [InlineData(45, -1, false, "Wyniki nie mogą być ujemne.")]
    public void ValidateMatchResult_ReturnsExpected(
        int home,
        int away,
        bool valid,
        string? errorSubstring)
    {
        var sut = new MatchManagementService(
            Mock.Of<TyperBot.Infrastructure.Repositories.ISeasonRepository>(),
            Mock.Of<TyperBot.Infrastructure.Repositories.IRoundRepository>(),
            Mock.Of<TyperBot.Infrastructure.Repositories.IMatchRepository>());

        var (isValid, message) = sut.ValidateMatchResult(home, away);
        isValid.Should().Be(valid);
        if (errorSubstring != null)
        {
            message.Should().Contain(errorSubstring);
        }
        else
        {
            message.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(2025, 6, 18, 18, 0, 2025, 6, 18, 8, 0)] // Wed 18:00 → same Wed 08:00
    [InlineData(2025, 6, 18, 6, 0, 2025, 6, 11, 8, 0)] // Wed before 08:00 → previous Wed 08:00
    [InlineData(2025, 6, 21, 18, 0, 2025, 6, 18, 8, 0)] // Sat → preceding Wed 08:00
    public void ComputeDefaultThreadCreationTime_MatchesLegacyRules(
        int sy, int sm, int sd, int sh, int smin,
        int ey, int em, int ed, int eh, int emin)
    {
        var start = new DateTimeOffset(sy, sm, sd, sh, smin, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(ey, em, ed, eh, emin, 0, TimeSpan.Zero);

        var actual = MatchManagementService.ComputeDefaultThreadCreationTime(start);

        actual.Should().Be(expected);
    }
}

public class MatchManagementServiceIntegrationTests : IDisposable
{
    private readonly TyperContext _context;
    private readonly MatchManagementService _sut;

    public MatchManagementServiceIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<TyperContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new TyperContext(options);
        _sut = new MatchManagementService(
            new SeasonRepository(_context),
            new RoundRepository(_context),
            new MatchRepository(_context));
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task CreateMatchAsync_NoSeason_CreatesActiveSeasonRoundAndMatch()
    {
        var start = new DateTimeOffset(2025, 6, 18, 18, 0, 0, TimeSpan.Zero);

        var (success, error, match) = await _sut.CreateMatchAsync(3, "Gorzów", "Leszno", start);

        success.Should().BeTrue();
        error.Should().BeNull();
        match.Should().NotBeNull();
        match!.HomeTeam.Should().Be("Gorzów");
        match.AwayTeam.Should().Be("Leszno");
        match.Status.Should().Be(MatchStatus.Scheduled);

        var seasons = await _context.Seasons.ToListAsync();
        seasons.Should().ContainSingle(s => s.IsActive && s.Name == "PGE Ekstraliga 2025");

        var rounds = await _context.Rounds.ToListAsync();
        rounds.Should().ContainSingle(r => r.Number == 3);

        var stored = await _context.Matches.SingleAsync();
        stored.Id.Should().Be(match.Id);
    }

    [Fact]
    public async Task CreateMatchAsync_ReusesActiveSeasonAndRound_ForSecondMatch()
    {
        var season = await _context.Seasons.AddAsync(new Season { Name = "Existing", IsActive = true });
        await _context.SaveChangesAsync();
        var round = await _context.Rounds.AddAsync(new Round
        {
            SeasonId = season.Entity.Id,
            Number = 1,
            Description = "R1"
        });
        await _context.SaveChangesAsync();

        var t0 = DateTimeOffset.UtcNow.AddDays(2);
        var r1 = await _sut.CreateMatchAsync(1, "A", "B", t0);
        var r2 = await _sut.CreateMatchAsync(1, "C", "D", t0.AddHours(3));

        r1.success.Should().BeTrue();
        r2.success.Should().BeTrue();
        r1.match!.RoundId.Should().Be(r2.match!.RoundId);
        r1.match.RoundId.Should().Be(round.Entity.Id);

        (await _context.Matches.CountAsync()).Should().Be(2);
    }
}
