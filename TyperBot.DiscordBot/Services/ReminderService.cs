using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.DiscordBot.Models;
using TyperBot.Infrastructure.Repositories;
using TyperBot.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using TyperBot.Domain.Enums;

namespace TyperBot.DiscordBot.Services;

public class ReminderService : BackgroundService
{
    private readonly ILogger<ReminderService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly DiscordLookupService _lookupService;
    private readonly DiscordSocketClient _client;
    private readonly HashSet<int> _remindedMatches = new(); // Track matches that already received reminders
    private bool _isFirstCheck = true; // Track if this is the first check after startup

    public ReminderService(
        ILogger<ReminderService> logger,
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
        _logger.LogInformation("ReminderService started.");

        // Wait for initial delay to let bot connect
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForMissingResultsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ReminderService");
            }

            // Check every 5 minutes
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task CheckForMissingResultsAsync()
    {
        if (_client.ConnectionState != ConnectionState.Connected)
            return;

        using var scope = _serviceProvider.CreateScope();
        var matchRepository = scope.ServiceProvider.GetRequiredService<IMatchRepository>();

        // Get matches that started > 3 hours ago, are not finished, not cancelled, and don't have results
        var now = DateTimeOffset.UtcNow;
        var threeHoursAgo = now.AddHours(-3);
        var oneDayAgo = now.AddDays(-1);

        var candidates = (await matchRepository.GetMatchesPossiblyAwaitingResultEntryAsync(threeHoursAgo)).ToList();

        var matchesToRemind = candidates.Where(m =>
            !_remindedMatches.Contains(m.Id) &&
            (!_isFirstCheck || m.StartTime >= oneDayAgo))
            .ToList();

        // Mark that first check is done
        if (_isFirstCheck)
        {
            _isFirstCheck = false;
            if (matchesToRemind.Any())
            {
                _logger.LogInformation(
                    "First check after startup - skipped very old matches (>24h), found {Count} matches to remind",
                    matchesToRemind.Count);
            }
        }

        if (!matchesToRemind.Any()) return;

        var channel = await _lookupService.GetAdminChannelAsync();
        if (channel == null)
        {
            _logger.LogWarning("Admin channel not found, cannot send reminders");
            return;
        }

        foreach (var match in matchesToRemind)
        {
            try
            {
                var home = match.HomeTeam;
                var away = match.AwayTeam;
                var shortcut = TeamNameHelper.GetMatchShortcut(home, away);
                var timeSinceStart = now - match.StartTime;
                var hoursSinceStart = (int)timeSinceStart.TotalHours;
                
                var embed = new EmbedBuilder()
                    .WithTitle("⚠️ Przypomnienie o wyniku meczu")
                    .WithDescription(
                        $"Mecz **{home} vs {away}** ({shortcut}) rozpoczął się **{hoursSinceStart} godzin** temu, " +
                        $"a wynik nie został jeszcze wprowadzony.\n\n" +
                        $"**Możliwe akcje:**\n" +
                        $"• Wpisz wynik meczu\n" +
                        $"• Odwołaj mecz (jeśli został przełożony)\n" +
                        $"• Edytuj datę meczu (jeśli został przełożony)")
                    .AddField("Data rozpoczęcia", $"<t:{match.StartTime.ToUnixTimeSeconds()}:F>", inline: true)
                    .AddField("Czas od rozpoczęcia", $"{hoursSinceStart}h {timeSinceStart.Minutes}min", inline: true)
                    .WithColor(Color.Orange)
                    .WithFooter($"ID Meczu: {match.Id}")
                    .WithCurrentTimestamp();

                var setResultButton = new ButtonBuilder()
                    .WithCustomId($"admin_set_result_{match.Id}")
                    .WithLabel("📝 Wpisz wynik")
                    .WithStyle(ButtonStyle.Primary);

                var editButton = new ButtonBuilder()
                    .WithCustomId($"admin_edit_match_{match.Id}")
                    .WithLabel("✏️ Edytuj mecz")
                    .WithStyle(ButtonStyle.Secondary);

                var cancelButton = new ButtonBuilder()
                    .WithCustomId($"admin_cancel_match_{match.Id}")
                    .WithLabel("❌ Odwołaj mecz")
                    .WithStyle(ButtonStyle.Danger);

                var components = new ComponentBuilder()
                    .WithButton(setResultButton, row: 0)
                    .WithButton(editButton, row: 0)
                    .WithButton(cancelButton, row: 1)
                    .Build();

                await channel.SendMessageAsync(embed: embed.Build(), components: components);
                _remindedMatches.Add(match.Id); // Mark as reminded
                
                _logger.LogInformation(
                    "Sent result reminder for match {MatchId} ({Home} vs {Away}), started {HoursAgo}h ago",
                    match.Id, home, away, hoursSinceStart);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending reminder for match {MatchId}", match.Id);
            }
        }
    }
}
