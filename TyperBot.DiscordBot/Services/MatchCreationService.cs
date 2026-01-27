using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.DiscordBot.Models;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Services;

public class MatchCreationService
{
    private readonly ILogger<MatchCreationService> _logger;
    private readonly DiscordSettings _settings;
    private readonly MatchManagementService _matchService;
    private readonly IMatchRepository _matchRepository;
    private readonly IRoundRepository _roundRepository;
    private readonly ISeasonRepository _seasonRepository;
    private readonly AdminMatchCreationStateService _stateService;
    private readonly MatchCardService _matchCardService;

    public MatchCreationService(
        ILogger<MatchCreationService> logger,
        IOptions<DiscordSettings> settings,
        MatchManagementService matchService,
        IMatchRepository matchRepository,
        IRoundRepository roundRepository,
        ISeasonRepository seasonRepository,
        AdminMatchCreationStateService stateService,
        MatchCardService matchCardService)
    {
        _logger = logger;
        _settings = settings.Value;
        _matchService = matchService;
        _matchRepository = matchRepository;
        _roundRepository = roundRepository;
        _seasonRepository = seasonRepository;
        _stateService = stateService;
        _matchCardService = matchCardService;
    }

    public async Task HandleAddMatchModalAsync(SocketInteractionContext context, string roundNumberStr, string homeTeam, string awayTeam, string matchDate, string matchTime)
    {
        var user = context.User as SocketGuildUser;
        if (user == null) return;

        if (!int.TryParse(roundNumberStr, out var roundNumber))
        {
            await context.Interaction.RespondAsync("❌ Nieprawidłowy numer kolejki.", ephemeral: true);
            return;
        }

        DateTimeOffset startTime;
        try
        {
            if (!DateTime.TryParse($"{matchDate} {matchTime}", out var localTime))
            {
                await context.Interaction.RespondAsync("❌ Nieprawidłowy format daty lub godziny.", ephemeral: true);
                return;
            }

            var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
            var localDateTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
            startTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);
        }
        catch (Exception)
        {
            await context.Interaction.RespondAsync("❌ Błąd podczas parsowania daty/godziny.", ephemeral: true);
            return;
        }

        var result = await _matchService.CreateMatchAsync(roundNumber, homeTeam, awayTeam, startTime);
        if (result.success && result.match != null)
        {
            await context.Interaction.RespondAsync($"✅ Mecz utworzony: **{homeTeam} vs {awayTeam}** (Kolejka {roundNumber})", ephemeral: true);
            await _matchCardService.PostMatchCardAsync(result.match, roundNumber);
        }
        else
        {
            await context.Interaction.RespondAsync($"❌ Błąd: {result.error}", ephemeral: true);
        }
    }
}
