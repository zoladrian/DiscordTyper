using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.DiscordBot;
using TyperBot.DiscordBot.Models;
using TyperBot.DiscordBot.Services;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Modules;

public class AdminPredictionModule : BaseAdminModule
{
    private readonly ILogger<AdminPredictionModule> _logger;
    private readonly IMatchRepository _matchRepository;
    private readonly IPredictionRepository _predictionRepository;
    private readonly DiscordLookupService _lookupService;
    private readonly MatchPredictionRevealService _revealService;

    public AdminPredictionModule(
        ILogger<AdminPredictionModule> logger,
        IOptions<DiscordSettings> settings,
        IMatchRepository matchRepository,
        IPredictionRepository predictionRepository,
        DiscordLookupService lookupService,
        MatchPredictionRevealService revealService) : base(settings.Value)
    {
        _logger = logger;
        _matchRepository = matchRepository;
        _predictionRepository = predictionRepository;
        _lookupService = lookupService;
        _revealService = revealService;
    }

    [ComponentInteraction("admin_mention_untyped_*")]
    public async Task HandleMentionUntypedPlayersButtonAsync(string matchIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Nieprawidłowy mecz.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            var match = await _matchRepository.GetByIdAsync(matchId, includeRound: false);
            if (match == null)
            {
                await FollowupAsync("❌ Mecz nie znaleziony.", ephemeral: true);
                return;
            }

            // Get all players with Typer role
            var playersWithRole = await _lookupService.GetPlayersWithRoleAsync();
            
            // Get players who already predicted
            var predictions = await _predictionRepository.GetByMatchIdAsync(matchId);
            var playersWithPredictions = predictions.Select(p => p.Player.DiscordUserId).ToHashSet();

            // Find players who haven't predicted (exclude bots — e.g. bot with Typer role must not mention itself)
            var untypedPlayers = playersWithRole
                .Where(p => !p.IsBot && !playersWithPredictions.Contains(p.Id))
                .ToList();

            if (!untypedPlayers.Any())
            {
                await FollowupAsync("✅ Wszyscy gracze złożyli już typy dla tego meczu!", ephemeral: true);
                return;
            }

            // Get match thread
            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel == null)
            {
                await FollowupAsync("❌ Kanał typowań nie znaleziony.", ephemeral: true);
                return;
            }

            SocketThreadChannel? thread = null;
            if (match.ThreadId.HasValue)
            {
                thread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
            }

            if (thread == null)
            {
                await FollowupAsync("❌ Wątek meczu nie został znaleziony.", ephemeral: true);
                return;
            }

            var playerMentions = untypedPlayers.Select(p => MentionUtils.MentionUser(p.Id)).ToList();
            
            // Discord limit: max 50 mentions per message
            const int maxMentionsPerMessage = 50;
            
            if (playerMentions.Count <= maxMentionsPerMessage)
            {
                var mentionMessage = $"Przypomnienie: mecz do zatypowania! {string.Join(" ", playerMentions)}";
                await thread.SendMessageAsync(mentionMessage);
            }
            else
            {
                for (int i = 0; i < playerMentions.Count; i += maxMentionsPerMessage)
                {
                    var batch = playerMentions.Skip(i).Take(maxMentionsPerMessage);
                    var mentionMessage = i == 0 
                        ? $"Przypomnienie: mecz do zatypowania! {string.Join(" ", batch)}"
                        : string.Join(" ", batch);
                    await thread.SendMessageAsync(mentionMessage);
                }
            }

            await FollowupAsync($"✅ Zawołano {untypedPlayers.Count} niezatypowanych graczy.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mentioning players without predictions for match {MatchId}", matchId);
            await FollowupAsync("❌ Wystąpił błąd podczas wołania graczy.", ephemeral: true);
        }
    }

    [ComponentInteraction("admin_reveal_predictions_*")]
    public async Task HandleRevealPredictionsButtonAsync(string matchIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Nieprawidłowy mecz.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            var (_, message) = await _revealService.RevealForMatchIdAsync(matchId, DateTimeOffset.UtcNow);
            await FollowupAsync(message, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revealing predictions for match {MatchId}", matchId);
            await FollowupAsync("❌ Wystąpił błąd podczas ujawniania typów.", ephemeral: true);
        }
    }

    [SlashCommand("admin-ujawnij-typy", "Ujawnia typy dla meczu tego wątku (jak przycisk na karcie). Użyj wyłącznie w wątku meczu z kanału typowanie.")]
    public async Task SlashRevealPredictionsInThreadAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień administratora (rola admin lub uprawnienie Administrator).", ephemeral: true);
            return;
        }

        if (Context.Channel is not SocketThreadChannel thread)
        {
            await RespondAsync(
                "❌ **Nie ujawniam typów** — tej komendy możesz użyć tylko **w wątku meczu** (wejdź w wątek z kanału typowanie i wpisz komendę tam).",
                ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            var (_, message) = await _revealService.RevealForThreadIdAsync(thread.Id, DateTimeOffset.UtcNow);
            await FollowupAsync(message, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Slash admin-ujawnij-typy failed for thread {ThreadId}", thread.Id);
            await FollowupAsync("❌ Wystąpił błąd podczas ujawniania typów.", ephemeral: true);
        }
    }
}
