using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using TyperBot.Application.Services;
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
    private readonly IPlayerRepository _playerRepository;
    private readonly DiscordLookupService _lookupService;
    private readonly MatchCardService _matchCardService;
    private readonly RevealedPredictionsTableImageGenerator _revealTablePng;

    public MatchPredictionRevealService(
        ILogger<MatchPredictionRevealService> logger,
        IMatchRepository matchRepository,
        IPredictionRepository predictionRepository,
        IPlayerRepository playerRepository,
        DiscordLookupService lookupService,
        MatchCardService matchCardService,
        RevealedPredictionsTableImageGenerator revealTablePng)
    {
        _logger = logger;
        _matchRepository = matchRepository;
        _predictionRepository = predictionRepository;
        _playerRepository = playerRepository;
        _lookupService = lookupService;
        _matchCardService = matchCardService;
        _revealTablePng = revealTablePng;
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

        var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
        if (predictionsChannel == null)
            return (false, "❌ Nie znaleziono kanału typowanie — skonfiguruj serwer.");

        var thread = await _lookupService.TryGetMatchThreadAsync(match.ThreadId.Value);
        if (thread == null)
            return (false, "❌ Nie znaleziono wątku meczu na Discordzie (mógł zostać usunięty albo bot nie ma do niego dostępu).");

        var guild = thread.Guild;

        var rows = new List<RevealedTipRow>();
        if (predictionsList.Count > 0)
        {
            foreach (var pred in predictionsList.OrderBy(p =>
                         DiscordDisplayNameHelper.ForPlayerInGuild(p.Player, guild),
                     StringComparer.OrdinalIgnoreCase))
            {
                var playerName = DiscordDisplayNameHelper.ForPlayerInGuild(pred.Player, guild);
                rows.Add(new RevealedTipRow(playerName, $"{pred.HomeTip}:{pred.AwayTip}"));
            }
        }
        else
            rows.Add(new RevealedTipRow("Brak typów", "—"));

        var roundLabel = match.Round != null ? RoundHelper.GetRoundLabel(match.Round.Number) : "Kolejka";
        var footer = predictionsList.Count == 0
            ? $"{match.HomeTeam} vs {match.AwayTeam} · {roundLabel} · brak typów"
            : $"{match.HomeTeam} vs {match.AwayTeam} · {roundLabel} · {predictionsList.Count} typów";

        byte[] pngBytes;
        try
        {
            pngBytes = _revealTablePng.Generate(rows, footer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reveal: PNG table generation failed for match {MatchId}", match.Id);
            return (false, "❌ Nie udało się wygenerować tabeli PNG. Sprawdź logi bota.");
        }

        var embed = new EmbedBuilder()
            .WithTitle(DiscordApiLimits.Truncate($"👁️ Ujawnione typy: {match.HomeTeam} vs {match.AwayTeam}", DiscordApiLimits.EmbedTitle))
            .WithDescription("Tabela typów w załączniku (PNG).")
            .WithColor(Color.Gold)
            .WithCurrentTimestamp()
            .Build();

        IUserMessage revealMessage;
        try
        {
            await using var stream = new MemoryStream(pngBytes, writable: false);
            revealMessage = await thread.SendFileAsync(stream, $"ujawnione_typy_{match.Id}.png", embed: embed);
            await revealMessage.PinAsync();

            var easterEggMessage = await BuildNoTipEasterEggMessageAsync(predictionsList, guild);
            if (!string.IsNullOrWhiteSpace(easterEggMessage))
                await thread.SendMessageAsync(easterEggMessage);
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

    private async Task<string?> BuildNoTipEasterEggMessageAsync(IReadOnlyList<Prediction> predictionsList, SocketGuild guild)
    {
        var activePlayers = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (activePlayers.Count == 0)
            return null;

        var typedPlayerIds = predictionsList
            .Select(p => p.PlayerId)
            .ToHashSet();

        var noTipNames = activePlayers
            .Where(p => !typedPlayerIds.Contains(p.Id))
            .Select(p => DiscordDisplayNameHelper.ForPlayerInGuild(p, guild))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return BuildNoTipEasterEggMessage(noTipNames);
    }

    public static string? BuildNoTipEasterEggMessage(IReadOnlyList<string> noTipNames)
    {
        if (noTipNames.Count == 0)
            return null;

        var playersText = string.Join(", ", noTipNames);
        return noTipNames.Count == 1
            ? $"Informacja: dla gracza {playersText} punkty za ten mecz będą liczone z mnożnikiem x2."
            : $"Informacja: dla graczy {playersText} punkty za ten mecz będą liczone z mnożnikiem x2.";
    }
}
