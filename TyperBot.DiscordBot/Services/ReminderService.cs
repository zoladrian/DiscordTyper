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

        // Get matches that started > 3 hours ago, are not finished, and not cancelled
        // We look for matches started between 3h and 3h 20m ago to send reminder only once (approx)
        var now = DateTimeOffset.UtcNow;
        var threeHoursAgo = now.AddHours(-3);
        var checkWindowStart = now.AddHours(-3).AddMinutes(-20);

        var allMatches = await matchRepository.GetAllAsync();
        
        var matchesToRemind = allMatches.Where(m => 
            m.Status != MatchStatus.Finished &&
            m.Status != MatchStatus.Cancelled &&
            m.StartTime <= threeHoursAgo &&
            m.StartTime > checkWindowStart
        ).ToList();

        if (!matchesToRemind.Any()) return;

        var channel = await _lookupService.GetAdminChannelAsync();
        if (channel == null) return;

        foreach (var match in matchesToRemind)
        {
            var home = match.HomeTeam;
            var away = match.AwayTeam;
            var shortcut = TeamNameHelper.GetMatchShortcut(home, away);
            
            var embed = new EmbedBuilder()
                .WithTitle("⚠️ Przypomnienie o wyniku")
                .WithDescription($"Mecz **{home} vs {away}** ({shortcut}) rozpoczął się ponad 3 godziny temu, a wynik nie został jeszcze wprowadzony.")
                .AddField("Data rozpoczęcia", $"<t:{match.StartTime.ToUnixTimeSeconds()}:F>")
                .WithColor(Color.Orange)
                .WithFooter($"ID Meczu: {match.Id}");

             var button = new ButtonBuilder()
                .WithCustomId($"admin_set_result_{match.Id}")
                .WithLabel("📝 Wpisz wynik")
                .WithStyle(ButtonStyle.Primary);

            var components = new ComponentBuilder()
                .WithButton(button)
                .Build();

            await channel.SendMessageAsync(embed: embed.Build(), components: components);
            _logger.LogInformation("Sent reminder for match {MatchId} ({Home} vs {Away})", match.Id, home, away);
        }
    }
}
