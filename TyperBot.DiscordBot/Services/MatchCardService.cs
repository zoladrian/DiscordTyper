using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.DiscordBot;
using TyperBot.DiscordBot.Constants;
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

    /// <param name="forceImmediateThread">If true, creates the thread and posts the card even when <see cref="Match.ThreadCreationTime"/> is still in the future (admin force-publish).</param>
    public async Task PostMatchCardAsync(Match match, int roundNum, IUserMessage? existingMessage = null, bool forceImmediateThread = false)
    {
        var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
        if (predictionsChannel == null)
        {
            _logger.LogError("Predictions channel not found, cannot publish match card");
            return;
        }

        var timestamp = match.StartTime.ToUnixTimeSeconds();
        var roundLabel = Application.Services.RoundHelper.GetRoundLabel(roundNum);
        
        var embedBuilder = new EmbedBuilder()
            .WithTitle(DiscordApiLimits.Truncate($"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}", DiscordApiLimits.EmbedTitle));

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
                    "• Domyślnie typy **do startu meczu**; wcześniejszy deadline ustawia admin w **edycji meczu**"
                )
                .AddField("🏁 Czas rozpoczęcia", $"<t:{timestamp}:F>", inline: true)
                .WithColor(Color.Blue);
            if (match.TypingDeadline.HasValue)
            {
                var dl = match.TypingDeadline.Value.ToUnixTimeSeconds();
                embedBuilder.AddField("🔒 Deadline typowania", $"<t:{dl}:F>", inline: true);
            }
        }

        var embed = embedBuilder.Build();

        var componentBuilder = new ComponentBuilder();

        // Only show "Typuj wynik" button if match doesn't have a result yet and is not cancelled
        if (match.Status != MatchStatus.Finished && match.Status != MatchStatus.Cancelled)
        {
            var predictButton = new ButtonBuilder()
                .WithCustomId($"{CustomIds.Prediction.PredictMatch}{match.Id}")
                .WithLabel("🔢 Typuj wynik")
                .WithStyle(ButtonStyle.Primary);
            var myTypButton = new ButtonBuilder()
                .WithCustomId($"{CustomIds.Prediction.MyMatchPrediction}{match.Id}")
                .WithLabel("🔎 Mój typ")
                .WithStyle(ButtonStyle.Secondary);
            componentBuilder
                .WithButton(predictButton, row: 0)
                .WithButton(myTypButton, row: 0);
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
            _logger.LogInformation("Match card updated - Match ID: {MatchId}", match.Id);
        }
        else
        {
            var shouldCreateNow = forceImmediateThread ||
                                  !match.ThreadCreationTime.HasValue ||
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
                
                _logger.LogInformation("Match card published in predictions channel - Match ID: {MatchId}, Thread ID: {ThreadId}", match.Id, thread.Id);
            }
            else
            {
                // Thread will be created later by ThreadCreationService
                _logger.LogInformation("Match card will be created later - Match ID: {MatchId}, ThreadCreationTime: {Time}", 
                    match.Id, match.ThreadCreationTime?.ToString() ?? "null");
            }
        }
    }

    /// <summary>
    /// Creates the match thread (if missing), syncs <see cref="Match.ThreadId"/>, and posts or updates the match card.
    /// Use when a match was scheduled for automatic Wednesday publishing but you need the thread immediately (e.g. mid-week catch-up).
    /// </summary>
    public async Task<(bool success, string userMessage)> ForcePublishMatchCardAsync(Match match, int roundNum)
    {
        if (match.Status == MatchStatus.Finished || match.Status == MatchStatus.Cancelled)
        {
            return (false, "❌ Nie można wymusić publikacji dla meczu zakończonego lub odwołanego.");
        }

        var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
        if (predictionsChannel == null)
        {
            return (false, "❌ Nie znaleziono kanału typowanie.");
        }

        SocketThreadChannel? thread = null;
        if (match.ThreadId.HasValue)
        {
            thread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
        }

        if (thread == null)
        {
            var roundLabel = RoundHelper.GetRoundLabel(roundNum);
            var threadName = $"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}";
            var searchName = threadName.Length > 100 ? threadName.Substring(0, 97) + "..." : threadName;
            thread = predictionsChannel.Threads.FirstOrDefault(t => t.Name == searchName);
            if (thread != null)
            {
                match.ThreadId = thread.Id;
            }
        }

        match.ThreadCreationTime = DateTimeOffset.UtcNow;
        await _matchRepository.UpdateAsync(match);

        IUserMessage? existingMessage = null;
        if (thread != null)
        {
            existingMessage = await _lookupService.FindMatchCardMessageAsync(thread);
        }

        await PostMatchCardAsync(match, roundNum, existingMessage, forceImmediateThread: true);

        return (true, "✅ Wątek i karta meczu są dostępne w kanale typowanie (publikacja wymuszona).");
    }
}
