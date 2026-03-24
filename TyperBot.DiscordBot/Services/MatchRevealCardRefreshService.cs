using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Services;

/// <summary>
/// Discord nie odświeża sam komponentów wiadomości — przycisk „Ujawnij typy” pojawia się tylko po edycji karty.
/// Co kilka minut aktualizujemy karty meczów, które już wystartowały, a typów jeszcze nie ujawniono.
/// </summary>
public sealed class MatchRevealCardRefreshService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxAgeSinceStart = TimeSpan.FromDays(30);

    private readonly ILogger<MatchRevealCardRefreshService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly DiscordSocketClient _client;

    public MatchRevealCardRefreshService(
        ILogger<MatchRevealCardRefreshService> logger,
        IServiceProvider serviceProvider,
        DiscordSocketClient client)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _client = client;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MatchRevealCardRefreshService started.");
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_client.ConnectionState == ConnectionState.Connected)
                    await RefreshCardsOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MatchRevealCardRefreshService iteration failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RefreshCardsOnceAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var matchRepository = scope.ServiceProvider.GetRequiredService<IMatchRepository>();
        var matchCardService = scope.ServiceProvider.GetRequiredService<MatchCardService>();
        var lookupService = scope.ServiceProvider.GetRequiredService<DiscordLookupService>();

        var now = DateTimeOffset.UtcNow;
        var matches = (await matchRepository.GetMatchesNeedingRevealCardRefreshAsync(now, MaxAgeSinceStart)).ToList();
        if (matches.Count == 0)
            return;

        var predictionsChannel = await lookupService.GetPredictionsChannelAsync();
        if (predictionsChannel == null)
            return;

        foreach (var match in matches)
        {
            if (ct.IsCancellationRequested) break;
            if (!match.ThreadId.HasValue) continue;

            try
            {
                var thread = await lookupService.TryGetMatchThreadAsync(match.ThreadId.Value);
                if (thread == null)
                    continue;

                var cardMessage = await lookupService.FindMatchCardMessageAsync(thread);
                if (cardMessage == null)
                    continue;

                var roundNum = match.Round?.Number ?? 0;
                await matchCardService.PostMatchCardAsync(match, roundNum, cardMessage);
                _logger.LogDebug("Refreshed match card for reveal button — match {MatchId}", match.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh match card for match {MatchId}", match.Id);
            }
        }
    }
}
