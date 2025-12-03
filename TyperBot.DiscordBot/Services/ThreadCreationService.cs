using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TyperBot.DiscordBot.Services;
using TyperBot.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using TyperBot.Domain.Enums;
using TyperBot.Application.Services;

namespace TyperBot.DiscordBot.Services;

public class ThreadCreationService : BackgroundService
{
    private readonly ILogger<ThreadCreationService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly DiscordLookupService _lookupService;
    private readonly DiscordSocketClient _client;

    public ThreadCreationService(
        ILogger<ThreadCreationService> logger,
        IServiceProvider serviceProvider,
        DiscordLookupService lookupService,
        DiscordSocketClient client)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _lookupService = lookupService;
        _client = client;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ThreadCreationService started.");

        // Wait for initial delay to let bot connect
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CreatePendingThreadsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ThreadCreationService");
            }

            // Check every 5 minutes
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task CreatePendingThreadsAsync()
    {
        if (_client.ConnectionState != ConnectionState.Connected)
            return;

        using var scope = _serviceProvider.CreateScope();
        var matchRepository = scope.ServiceProvider.GetRequiredService<IMatchRepository>();
        var lookupService = scope.ServiceProvider.GetRequiredService<DiscordLookupService>();
        var settings = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TyperBot.DiscordBot.Models.DiscordSettings>>();

        // Get matches that should have threads created now
        var now = DateTimeOffset.UtcNow;
        var allMatches = await matchRepository.GetAllAsync();
        
        var matchesToCreateThreads = allMatches.Where(m => 
            m.Status == MatchStatus.Scheduled &&
            m.ThreadCreationTime.HasValue &&
            m.ThreadCreationTime.Value <= now &&
            m.StartTime > now // Only create threads for future matches
        ).ToList();

        if (!matchesToCreateThreads.Any()) return;

        var predictionsChannel = await lookupService.GetPredictionsChannelAsync();
        if (predictionsChannel == null) return;

        foreach (var match in matchesToCreateThreads)
        {
            try
            {
                var roundLabel = Application.Services.RoundHelper.GetRoundLabel(match.Round?.Number ?? 0);
                var threadName = $"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}";
                
                // Check if thread already exists by ThreadId
                SocketThreadChannel? existingThread = null;
                if (match.ThreadId.HasValue)
                {
                    existingThread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
                }
                
                // Fallback to name search if ThreadId not found
                if (existingThread == null)
                {
                    existingThread = predictionsChannel.Threads.FirstOrDefault(t => t.Name == threadName);
                }
                
                if (existingThread != null)
                {
                    // Update ThreadId if it wasn't set
                    if (!match.ThreadId.HasValue)
                    {
                        match.ThreadId = existingThread.Id;
                        await matchRepository.UpdateAsync(match);
                    }
                    _logger.LogInformation("Thread already exists for match {MatchId}, skipping", match.Id);
                    continue;
                }

                // Create thread and post match card
                var tz = TimeZoneInfo.FindSystemTimeZoneById(settings.Value.Timezone);
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(match.StartTime.UtcDateTime, tz);
                var timestamp = ((DateTimeOffset)localTime).ToUnixTimeSeconds();

                var embed = new EmbedBuilder()
                    .WithTitle($"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}")
                    .WithDescription(
                        "📋 **Zasady typowania:**\n" +
                        "• Typy są tajne (tylko ty je widzisz)\n" +
                        "• Suma musi wynosić 90 punktów (np. 50:40, 46:44, 45:45)\n" +
                        "• Termin: czas rozpoczęcia meczu"
                    )
                    .AddField("🏁 Czas rozpoczęcia", $"<t:{timestamp}:F>", inline: true)
                    .WithColor(Color.Blue)
                    .Build();

                var predictButton = new ButtonBuilder()
                    .WithCustomId($"predict_match_{match.Id}")
                    .WithLabel("🔢 Typuj wynik")
                    .WithStyle(ButtonStyle.Primary);

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

                var componentBuilder = new ComponentBuilder()
                    .WithButton(predictButton, row: 0)
                    .WithButton(setResultButton, row: 0)
                    .WithButton(editButton, row: 1)
                    .WithButton(deleteButton, row: 1);

                // Add "Reveal Predictions" button for admins if match start time has passed and not yet revealed
                if (now >= match.StartTime && !match.PredictionsRevealed)
                {
                    var revealButton = new ButtonBuilder()
                        .WithCustomId($"admin_reveal_predictions_{match.Id}")
                        .WithLabel("👁️ Ujawnij typy")
                        .WithStyle(ButtonStyle.Secondary);
                    componentBuilder.WithButton(revealButton, row: 2);
                }

                var component = componentBuilder.Build();

                // Validate thread name length (Discord limit is 100 characters)
                if (threadName.Length > 100)
                {
                    threadName = threadName.Substring(0, 97) + "...";
                }

                var thread = await predictionsChannel.CreateThreadAsync(
                    name: threadName,
                    type: ThreadType.PublicThread
                );

                var cardMessage = await thread.SendMessageAsync(embed: embed, components: component);
                
                // Save ThreadId to database
                match.ThreadId = thread.Id;
                await matchRepository.UpdateAsync(match);
                
                // Mention all players with Typer role
                try
                {
                    var players = await _lookupService.GetPlayersWithRoleAsync();
                    var playerMentions = players.Select(p => p.Mention).ToList();
                    if (playerMentions.Any())
                    {
                        var mentionMessage = $"Nowy mecz do zatypowania! {string.Join(" ", playerMentions)}";
                        await thread.SendMessageAsync(mentionMessage);
                        _logger.LogInformation("Wspomniano {Count} graczy przy tworzeniu wątku dla meczu {MatchId}", playerMentions.Count, match.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Nie udało się wspomnieć graczy przy tworzeniu wątku dla meczu {MatchId}", match.Id);
                }
                
                _logger.LogInformation("Thread created for match {MatchId} ({Home} vs {Away}), Thread ID: {ThreadId}", 
                    match.Id, match.HomeTeam, match.AwayTeam, thread.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating thread for match {MatchId}", match.Id);
            }
        }
    }
}

