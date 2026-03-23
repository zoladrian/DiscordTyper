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

public class MatchCardService
{
    private readonly ILogger<MatchCardService> _logger;
    private readonly DiscordSettings _settings;
    private readonly DiscordLookupService _lookupService;
    private readonly IMatchRepository _matchRepository;

    public MatchCardService(
        ILogger<MatchCardService> logger,
        IOptions<DiscordSettings> settings,
        DiscordLookupService lookupService,
        IMatchRepository matchRepository)
    {
        _logger = logger;
        _settings = settings.Value;
        _lookupService = lookupService;
        _matchRepository = matchRepository;
    }

    public async Task PostMatchCardAsync(Match match, int roundNum, IUserMessage? existingMessage = null)
    {
        var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
        if (predictionsChannel == null)
        {
            _logger.LogError("Kanał typowań nie znaleziony, nie można opublikować karty meczu");
            return;
        }

        var timestamp = match.StartTime.ToUnixTimeSeconds();
        var roundLabel = Application.Services.RoundHelper.GetRoundLabel(roundNum);
        
        var embedBuilder = new EmbedBuilder()
            .WithTitle($"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}");

        // Show result if match is finished
        if (match.Status == MatchStatus.Finished && match.HomeScore.HasValue && match.AwayScore.HasValue)
        {
            embedBuilder
                .WithDescription($"✅ **Mecz zakończony**\n\n🏆 **Wynik: {match.HomeScore}:{match.AwayScore}**")
                .AddField("🏁 Czas rozpoczęcia", $"<t:{timestamp}:F>", inline: true)
                .WithColor(Color.Green);
        }
        else if (match.Status == MatchStatus.Cancelled)
        {
            embedBuilder
                .WithDescription("❌ **Mecz odwołany**\n\nTypy zostały zachowane.")
                .AddField("🏁 Czas rozpoczęcia", $"<t:{timestamp}:F>", inline: true)
                .WithColor(Color.Red);
        }
        else
        {
            embedBuilder
                .WithDescription(
                    "📋 **Zasady typowania:**\n" +
                    "• Typy są tajne (tylko ty je widzisz)\n" +
                    "• Suma musi wynosić 90 punktów (np. 50:40, 46:44, 45:45)\n" +
                    "• Termin: czas rozpoczęcia meczu"
                )
                .AddField("🏁 Czas rozpoczęcia", $"<t:{timestamp}:F>", inline: true)
                .WithColor(Color.Blue);
        }

        var embed = embedBuilder.Build();

        var componentBuilder = new ComponentBuilder();

        // Only show "Typuj wynik" button if match doesn't have a result yet and is not cancelled
        if (match.Status != MatchStatus.Finished && match.Status != MatchStatus.Cancelled)
        {
            var predictButton = new ButtonBuilder()
                .WithCustomId($"predict_match_{match.Id}")
                .WithLabel("🔢 Typuj wynik")
                .WithStyle(ButtonStyle.Primary);
            componentBuilder.WithButton(predictButton, row: 0);
        }

        var setResultButton = new ButtonBuilder()
            .WithCustomId($"admin_set_result_{match.Id}")
            .WithLabel($"✅ Wynik {TeamNameHelper.GetMatchShortcut(match.HomeTeam, match.AwayTeam)}")
            .WithStyle(ButtonStyle.Success);

        var editButton = new ButtonBuilder()
            .WithCustomId($"admin_edit_match_{match.Id}")
            .WithLabel($"✏ {TeamNameHelper.GetMatchShortcut(match.HomeTeam, match.AwayTeam)}")
            .WithStyle(ButtonStyle.Secondary);

        var deleteButton = new ButtonBuilder()
            .WithCustomId($"admin_delete_match_{match.Id}")
            .WithLabel("🗑 Usuń mecz")
            .WithStyle(ButtonStyle.Danger);

        componentBuilder
            .WithButton(setResultButton, row: 0)
            .WithButton(editButton, row: 1)
            .WithButton(deleteButton, row: 1);

        // Add "Reveal Predictions" button for admins if match start time has passed and not yet revealed
        var now = DateTimeOffset.UtcNow;
        if (now >= match.StartTime && !match.PredictionsRevealed && match.Status != MatchStatus.Cancelled)
        {
            var revealButton = new ButtonBuilder()
                .WithCustomId($"admin_reveal_predictions_{match.Id}")
                .WithLabel("👁️ Ujawnij typy")
                .WithStyle(ButtonStyle.Secondary);
            componentBuilder.WithButton(revealButton, row: 2);
        }

        // Add "Send Match Table" button for admins if match is finished
        if (match.Status == MatchStatus.Finished && match.HomeScore.HasValue && match.AwayScore.HasValue)
        {
            var tableButton = new ButtonBuilder()
                .WithCustomId($"admin_send_match_table_{match.Id}")
                .WithLabel("📊 Wyślij tabelę meczu")
                .WithStyle(ButtonStyle.Secondary);
            componentBuilder.WithButton(tableButton, row: 2);
        }

        // Add "Restore Match" button for admins if match is cancelled
        if (match.Status == MatchStatus.Cancelled)
        {
            var restoreButton = new ButtonBuilder()
                .WithCustomId($"admin_restore_match_{match.Id}")
                .WithLabel("♻️ Przywróć mecz")
                .WithStyle(ButtonStyle.Success);
            componentBuilder.WithButton(restoreButton, row: 2);
        }

        // Add "Mention Untyped Players" button for admins if match hasn't started yet
        if (match.Status != MatchStatus.Finished && match.Status != MatchStatus.Cancelled)
        {
            var mentionButton = new ButtonBuilder()
                .WithCustomId($"admin_mention_untyped_{match.Id}")
                .WithLabel("🔔 Zawołaj niezatypowanych")
                .WithStyle(ButtonStyle.Secondary);
            componentBuilder.WithButton(mentionButton, row: 3);
        }

        var component = componentBuilder.Build();

        if (existingMessage != null)
        {
            // Update existing message
            await existingMessage.ModifyAsync(prop => { prop.Embed = embed; prop.Components = component; });
            _logger.LogInformation("Karta meczu zaktualizowana - ID meczu: {MatchId}", match.Id);
        }
        else
        {
            // Check if thread should be created now
            var shouldCreateNow = !match.ThreadCreationTime.HasValue || 
                                  match.ThreadCreationTime.Value <= DateTimeOffset.UtcNow;
            
            if (shouldCreateNow)
            {
                // Check if thread already exists
                SocketThreadChannel? thread = null;
                if (match.ThreadId.HasValue)
                {
                    thread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
                }
                
                if (thread == null)
                {
                    var threadName = $"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}";
                    // Validate thread name length (Discord limit is 100 characters)
                    if (threadName.Length > 100)
                    {
                        threadName = threadName.Substring(0, 97) + "...";
                    }
                    
                    thread = await predictionsChannel.CreateThreadAsync(
                        name: threadName,
                        type: ThreadType.PublicThread
                    );
                    
                    // Save ThreadId to database
                    match.ThreadId = thread.Id;
                    await _matchRepository.UpdateAsync(match);
                }

                var cardMessage = await thread.SendMessageAsync(embed: embed, components: component);
                
                _logger.LogInformation("Karta meczu opublikowana w kanale typowań - ID meczu: {MatchId}, Thread ID: {ThreadId}", match.Id, thread.Id);
            }
            else
            {
                // Thread will be created later by ThreadCreationService
                _logger.LogInformation("Karta meczu będzie utworzona później - ID meczu: {MatchId}, ThreadCreationTime: {Time}", 
                    match.Id, match.ThreadCreationTime?.ToString() ?? "null");
            }
        }
    }
}
