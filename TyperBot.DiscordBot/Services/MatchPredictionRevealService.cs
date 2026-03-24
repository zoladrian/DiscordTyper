using Discord;
using Microsoft.Extensions.Logging;
using TyperBot.DiscordBot;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Services;

/// <summary>
/// Wspólna logika przycisku „Ujawnij typy” i komendy — te same warunki co na karcie meczu.
/// </summary>
public sealed class MatchPredictionRevealService
{
    private readonly ILogger<MatchPredictionRevealService> _logger;
    private readonly IMatchRepository _matchRepository;
    private readonly IPredictionRepository _predictionRepository;
    private readonly DiscordLookupService _lookupService;
    private readonly MatchCardService _matchCardService;

    public MatchPredictionRevealService(
        ILogger<MatchPredictionRevealService> logger,
        IMatchRepository matchRepository,
        IPredictionRepository predictionRepository,
        DiscordLookupService lookupService,
        MatchCardService matchCardService)
    {
        _logger = logger;
        _matchRepository = matchRepository;
        _predictionRepository = predictionRepository;
        _lookupService = lookupService;
        _matchCardService = matchCardService;
    }

    /// <summary>Zwraca listę powodów blokady (pusta = można ujawniać).</summary>
    public static IReadOnlyList<string> GetRevealBlockers(Match match, DateTimeOffset nowUtc)
    {
        var list = new List<string>();
        if (match.Status == MatchStatus.Cancelled)
            list.Add("mecz jest **odwołany** — ujawnianie typów jest wyłączone");
        if (match.PredictionsRevealed)
            list.Add("typy dla tego meczu zostały **już ujawnione**");
        if (nowUtc < match.StartTime)
        {
            var ts = match.StartTime.ToUnixTimeSeconds();
            list.Add($"mecz **jeszcze się nie rozpoczął** (start: <t:{ts}:F>)");
        }

        return list;
    }

    public async Task<(bool Success, string EphemeralMessage)> RevealForMatchIdAsync(int matchId, DateTimeOffset nowUtc)
    {
        var match = await _matchRepository.GetByIdAsync(matchId, includeRound: true);
        if (match == null)
            return (false, "❌ Nie znaleziono meczu w bazie.");

        return await RevealCoreAsync(match, nowUtc);
    }

    public async Task<(bool Success, string EphemeralMessage)> RevealForThreadIdAsync(ulong threadId, DateTimeOffset nowUtc)
    {
        var match = await _matchRepository.GetByThreadIdAsync(threadId);
        if (match == null)
            return (false, "❌ Ten wątek nie jest powiązany z żadnym meczem w bazie (otwórz wątek meczu z kanału typowanie).");

        return await RevealCoreAsync(match, nowUtc);
    }

    private async Task<(bool Success, string EphemeralMessage)> RevealCoreAsync(Match match, DateTimeOffset nowUtc)
    {
        var blockers = GetRevealBlockers(match, nowUtc);
        if (blockers.Count > 0)
        {
            var text = "**Nie ujawniam typów**, bo:\n• " + string.Join("\n• ", blockers);
            return (false, text);
        }

        if (!match.ThreadId.HasValue)
            return (false, "❌ Mecz nie ma zapisanego wątku Discord — nie ma gdzie wysłać tabeli.");

        var predictions = await _predictionRepository.GetByMatchIdAsync(match.Id);
        var predictionsList = predictions.Where(p => p.IsValid).ToList();

        var tableLines = new List<string>
        {
            "```",
            "┌─────────────────────┬───────────┐",
            "│ Gracz               │ Typ       │",
            "├─────────────────────┼───────────┤"
        };

        if (predictionsList.Any())
        {
            foreach (var pred in predictionsList.OrderBy(p => p.Player.DiscordUsername))
            {
                var playerName = pred.Player.DiscordUsername;
                if (playerName.Length > 19)
                    playerName = playerName.Substring(0, 16) + "...";
                tableLines.Add($"│ {playerName,-19} │ {pred.HomeTip,2}:{pred.AwayTip,-2} │");
            }
        }
        else
            tableLines.Add("│ Brak typów          │ -- : --   │");

        tableLines.Add("└─────────────────────┴───────────┘");
        tableLines.Add("```");

        var tableText = string.Join("\n", tableLines);

        var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
        if (predictionsChannel == null)
            return (false, "❌ Nie znaleziono kanału typowanie — skonfiguruj serwer.");

        var thread = await _lookupService.TryGetMatchThreadAsync(match.ThreadId.Value);
        if (thread == null)
            return (false, "❌ Nie znaleziono wątku meczu na Discordzie (mógł zostać usunięty albo bot nie ma do niego dostępu).");

        var embed = new EmbedBuilder()
            .WithTitle(DiscordApiLimits.Truncate($"👁️ Ujawnione typy: {match.HomeTeam} vs {match.AwayTeam}", DiscordApiLimits.EmbedTitle))
            .WithDescription(DiscordApiLimits.Truncate(tableText, DiscordApiLimits.EmbedDescription))
            .WithColor(Color.Gold)
            .WithCurrentTimestamp()
            .Build();

        IUserMessage revealMessage;
        try
        {
            revealMessage = await thread.SendMessageAsync(embed: embed);
            await revealMessage.PinAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reveal: failed to send/pin in thread {ThreadId} for match {MatchId}", thread.Id, match.Id);
            return (false, "❌ Nie udało się wysłać lub przypiąć wiadomości w wątku. Sprawdź uprawnienia bota.");
        }

        match.PredictionsRevealed = true;
        await _matchRepository.UpdateAsync(match);

        try
        {
            var cardMessage = await _lookupService.FindMatchCardMessageAsync(thread);
            if (cardMessage != null)
            {
                var roundNum = match.Round?.Number ?? 0;
                await _matchCardService.PostMatchCardAsync(match, roundNum, cardMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reveal ok but match card refresh failed for match {MatchId}", match.Id);
        }

        return (true, "✅ Typy zostały ujawnione i przypięte w wątku meczu. Karta meczu została zaktualizowana.");
    }
}
