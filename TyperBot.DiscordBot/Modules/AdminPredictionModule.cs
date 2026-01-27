using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    public AdminPredictionModule(
        ILogger<AdminPredictionModule> logger,
        IOptions<DiscordSettings> settings,
        IMatchRepository matchRepository,
        IPredictionRepository predictionRepository,
        DiscordLookupService lookupService) : base(settings.Value)
    {
        _logger = logger;
        _matchRepository = matchRepository;
        _predictionRepository = predictionRepository;
        _lookupService = lookupService;
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
            var match = await _matchRepository.GetByIdAsync(matchId);
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

            // Find players who haven't predicted
            var untypedPlayers = playersWithRole
                .Where(p => !playersWithPredictions.Contains(p.Id))
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
            _logger.LogError(ex, "Błąd podczas wołania niezatypowanych graczy dla meczu {MatchId}", matchId);
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
            var match = await _matchRepository.GetByIdAsync(matchId);
            if (match == null)
            {
                await FollowupAsync("❌ Mecz nie znaleziony.", ephemeral: true);
                return;
            }

            if (match.PredictionsRevealed)
            {
                await FollowupAsync("✅ Typy dla tego meczu zostały już ujawnione.", ephemeral: true);
                return;
            }

            // Get all predictions for this match
            var predictions = await _predictionRepository.GetByMatchIdAsync(match.Id);
            var predictionsList = predictions.ToList();

            // Build predictions table
            var tableLines = new List<string>();
            tableLines.Add("```");
            tableLines.Add("┌─────────────────────┬───────────┐");
            tableLines.Add("│ Gracz               │ Typ       │");
            tableLines.Add("├─────────────────────┼───────────┤");

            if (predictionsList.Any())
            {
                foreach (var pred in predictionsList.OrderBy(p => p.Player.DiscordUsername))
                {
                    var playerName = pred.Player.DiscordUsername;
                    if (playerName.Length > 19)
                    {
                        playerName = playerName.Substring(0, 16) + "...";
                    }
                    tableLines.Add($"│ {playerName,-19} │ {pred.HomeTip,2}:{pred.AwayTip,-2} │");
                }
            }
            else
            {
                tableLines.Add("│ Brak typów          │ -- : --   │");
            }
            tableLines.Add("└─────────────────────┴───────────┘");
            tableLines.Add("```");

            var tableText = string.Join("\n", tableLines);

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

            // Send table message
            var embed = new EmbedBuilder()
                .WithTitle($"👁️ Ujawnione typy: {match.HomeTeam} vs {match.AwayTeam}")
                .WithDescription(tableText)
                .WithColor(Color.Gold)
                .WithCurrentTimestamp()
                .Build();

            var revealMessage = await thread.SendMessageAsync(embed: embed);
            await revealMessage.PinAsync();

            // Mark as revealed
            match.PredictionsRevealed = true;
            await _matchRepository.UpdateAsync(match);

            await FollowupAsync("✅ Typy zostały ujawnione i przypięte w wątku meczu.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas ujawniania typów dla meczu {MatchId}", matchId);
            await FollowupAsync("❌ Wystąpił błąd podczas ujawniania typów.", ephemeral: true);
        }
    }
}
