using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.DiscordBot;
using TyperBot.DiscordBot.Models;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Services;

public class MatchResultHandler
{
    private readonly ILogger<MatchResultHandler> _logger;
    private readonly DiscordSettings _settings;
    private readonly DiscordLookupService _lookupService;
    private readonly IMatchRepository _matchRepository;
    private readonly PredictionService _predictionService;
    private readonly MatchCardService _matchCardService;
    private readonly MatchManagementService _matchService;
    private readonly MatchResultsTableService _matchResultsTableService;

    public MatchResultHandler(
        ILogger<MatchResultHandler> logger,
        IOptions<DiscordSettings> settings,
        DiscordLookupService lookupService,
        IMatchRepository matchRepository,
        PredictionService predictionService,
        MatchCardService matchCardService,
        MatchManagementService matchService,
        MatchResultsTableService matchResultsTableService)
    {
        _logger = logger;
        _settings = settings.Value;
        _lookupService = lookupService;
        _matchRepository = matchRepository;
        _predictionService = predictionService;
        _matchCardService = matchCardService;
        _matchService = matchService;
        _matchResultsTableService = matchResultsTableService;
    }

    public async Task HandleSetResultAsync(SocketInteractionContext context, string matchIdStr, string homeScore, string awayScore)
    {
        var user = context.User as SocketGuildUser;
        if (user == null || context.Guild == null)
        {
            await context.Interaction.RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await context.Interaction.RespondAsync("❌ Nieprawidłowy mecz.", ephemeral: true);
            return;
        }

        if (!int.TryParse(homeScore, out var home) || !int.TryParse(awayScore, out var away))
        {
            _logger.LogWarning("Invalid score format - User: {User}, homeScore: '{Home}', awayScore: '{Away}'", 
                user.Username, homeScore, awayScore);
            await context.Interaction.RespondAsync("❌ Wprowadź prawidłowe liczby dla obu wyników.", ephemeral: true);
            return;
        }

        var (isValid, errorMessage) = _matchService.ValidateMatchResult(home, away);
        if (!isValid)
        {
            await context.Interaction.RespondAsync($"❌ {errorMessage}", ephemeral: true);
            return;
        }

        await context.Interaction.DeferAsync(ephemeral: true);

        try
        {
            var match = await _matchRepository.GetByIdAsync(matchId);
            if (match == null)
            {
                await context.Interaction.FollowupAsync("❌ Mecz nie znaleziony.", ephemeral: true);
                return;
            }

            var wasFinished = match.Status == MatchStatus.Finished;
            var oldHomeScore = match.HomeScore;
            var oldAwayScore = match.AwayScore;

            match.HomeScore = home;
            match.AwayScore = away;
            match.Status = MatchStatus.Finished;
            await _matchRepository.UpdateAsync(match);

            await _predictionService.RecalculateMatchScoresAsync(matchId);

            await context.Interaction.FollowupAsync($"✅ Wynik ustawiony: **{home}:{away}**\nPunkty obliczone!", ephemeral: true);
            _logger.LogInformation(
                "Match result set - Match ID: {MatchId}, Score: {Home}:{Away}, Points calculated. Guild: {GuildId}, Channel: {ChannelId}",
                matchId,
                home,
                away,
                context.Guild?.Id,
                context.Channel.Id);

            await PostSetResultSideEffectsAsync(
                context, user, match, matchId, wasFinished, oldHomeScore, oldAwayScore, home, away);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set match result or recalculate scores - Match ID: {MatchId}", matchId);
            try
            {
                await context.Interaction.FollowupAsync(
                    "❌ Wystąpił błąd podczas zapisywania wyniku lub przeliczania punktów.",
                    ephemeral: true);
            }
            catch (Exception followEx)
            {
                _logger.LogWarning(followEx, "Could not send error follow-up for set-result interaction");
            }
        }
    }

    private async Task PostSetResultSideEffectsAsync(
        SocketInteractionContext context,
        SocketGuildUser user,
        Domain.Entities.Match match,
        int matchId,
        bool wasFinished,
        int? oldHomeScore,
        int? oldAwayScore,
        int home,
        int away)
    {
        // If match was already finished, post notification to predictions channel
        if (wasFinished && oldHomeScore.HasValue && oldAwayScore.HasValue)
        {
            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel != null)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("⚠️ Zmiana wyniku zakończonego meczu")
                    .WithDescription(
                        $"**{match.HomeTeam} vs {match.AwayTeam}**\n\n" +
                        $"Stary wynik: **{oldHomeScore}:{oldAwayScore}**\n" +
                        $"Nowy wynik: **{home}:{away}**\n\n" +
                        $"Punkty wszystkich graczy zostały przeliczone.")
                    .WithColor(Color.Orange)
                    .WithFooter($"Zmienione przez: {DiscordDisplayNameHelper.ForDisplay(user)}")
                    .WithCurrentTimestamp()
                    .Build();

                await predictionsChannel.SendMessageAsync(embed: embed);
                _logger.LogInformation(
                    "Published score change notice for match {MatchId} in predictions channel",
                    matchId);
            }
        }

        // Update match card in thread
        try
        {
            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel != null && match.ThreadId.HasValue)
            {
                var thread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
                if (thread != null)
                {
                    var cardMessage = await _lookupService.FindMatchCardMessageAsync(thread);
                    if (cardMessage != null)
                    {
                        var round = match.Round;
                        var roundNum = round?.Number ?? 0;
                        await _matchCardService.PostMatchCardAsync(match, roundNum, cardMessage);
                        _logger.LogInformation("Updated match card in thread after setting result - Match ID: {MatchId}", matchId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating match card in thread - Match ID: {MatchId}", matchId);
        }

        try
        {
            var matchForTable = await _matchRepository.GetByIdAsync(matchId);
            if (matchForTable != null)
                await _matchResultsTableService.TryPostToMatchThreadAsync(matchForTable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error posting match results table to thread - Match ID: {MatchId}", matchId);
        }
    }

    public async Task HandleCancelMatchAsync(SocketInteractionContext context, int matchId)
    {
        var user = context.User as SocketGuildUser;
        if (user == null || context.Guild == null)
        {
            await context.Interaction.RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        await context.Interaction.DeferAsync(ephemeral: true);

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await context.Interaction.FollowupAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        match.Status = MatchStatus.Cancelled;
        await _matchRepository.UpdateAsync(match);
        
        _logger.LogInformation(
            "Match cancelled - User: {Username} (ID: {UserId}), Match ID: {MatchId}, {Home} vs {Away}",
            user.Username, user.Id, matchId, match.HomeTeam, match.AwayTeam);
        
        await context.Interaction.FollowupAsync("✅ Mecz został odwołany (status: Cancelled). Typy zostały zachowane.", ephemeral: true);

        // Update match card
        try
        {
            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel != null && match.ThreadId.HasValue)
            {
                var thread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
                if (thread != null)
                {
                    var cardMessage = await _lookupService.FindMatchCardMessageAsync(thread);
                    if (cardMessage != null)
                    {
                        var round = match.Round;
                        var roundNum = round?.Number ?? 0;
                        await _matchCardService.PostMatchCardAsync(match, roundNum, cardMessage);
                    }

                    // Send message to all users in thread
                    var publicEmbed = new EmbedBuilder()
                        .WithTitle("❌ Mecz odwołany")
                        .WithDescription(
                            $"Mecz **{match.HomeTeam} vs {match.AwayTeam}** został odwołany.\n\n" +
                            $"✅ **Typy zostały zachowane**\n" +
                            $"📅 **Nowa data meczu:** jeszcze nie ustawiona")
                        .WithColor(Color.Orange)
                        .WithCurrentTimestamp()
                        .Build();

                    await thread.SendMessageAsync(embed: publicEmbed);

                    // Send admin-only embed with buttons
                    var adminEmbed = new EmbedBuilder()
                        .WithTitle("🔧 Panel administracyjny - Mecz odwołany")
                        .WithDescription(
                            $"Mecz **{match.HomeTeam} vs {match.AwayTeam}** został odwołany.\n\n" +
                            $"**Dostępne akcje:**")
                        .WithColor(Color.Blue)
                        .Build();

                    var restoreButton = new ButtonBuilder()
                        .WithCustomId($"admin_restore_match_{match.Id}")
                        .WithLabel("♻️ Przywróć mecz")
                        .WithStyle(ButtonStyle.Success);

                    var setDateButton = new ButtonBuilder()
                        .WithCustomId($"admin_set_cancelled_match_date_{match.Id}")
                        .WithLabel("📅 Ustaw datę meczu")
                        .WithStyle(ButtonStyle.Primary);

                    var adminComponents = new ComponentBuilder()
                        .WithButton(restoreButton, row: 0)
                        .WithButton(setDateButton, row: 0)
                        .Build();

                    // Send admin message (only visible to admins)
                    await thread.SendMessageAsync(embed: adminEmbed, components: adminComponents);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update match card after cancellation - Match ID: {MatchId}", matchId);
        }
    }

    public async Task HandleHardDeleteMatchAsync(SocketInteractionContext context, int matchId)
    {
        await context.Interaction.DeferAsync(ephemeral: true);

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await context.Interaction.FollowupAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        // Send message to thread before deletion if thread exists
        try
        {
            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel != null && match.ThreadId.HasValue)
            {
                var thread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
                if (thread != null)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("⚠️ Mecz usunięty z bazy danych")
                        .WithDescription(
                            $"Mecz **{match.HomeTeam} vs {match.AwayTeam}** został trwale usunięty z bazy danych.\n\n" +
                            $"❌ **Wszystkie typy użytkowników przepadły**")
                        .WithColor(Color.Red)
                        .WithCurrentTimestamp()
                        .Build();

                    await thread.SendMessageAsync(embed: embed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send deletion notification - Match ID: {MatchId}", matchId);
        }

        await _matchRepository.DeleteAsync(matchId);
        await context.Interaction.FollowupAsync("✅ Mecz został trwale usunięty z bazy danych. ⚠️ Wszystkie typy użytkowników przepadły.", ephemeral: true);
    }

    public async Task HandleRestoreMatchAsync(SocketInteractionContext context, int matchId)
    {
        var user = context.User as SocketGuildUser;
        if (user == null || context.Guild == null)
        {
            await context.Interaction.RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        await context.Interaction.DeferAsync(ephemeral: true);

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await context.Interaction.FollowupAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        if (match.Status != MatchStatus.Cancelled)
        {
            await context.Interaction.FollowupAsync("❌ Ten mecz nie jest odwołany.", ephemeral: true);
            return;
        }

        // Restore match to Scheduled status and clear any old result
        match.Status = MatchStatus.Scheduled;
        match.HomeScore = null;
        match.AwayScore = null;
        match.PredictionsRevealed = false;
        await _matchRepository.UpdateAsync(match);

        // Remove stale PlayerScore rows for this match's predictions
        await _predictionService.ClearMatchScoresAsync(matchId);
        
        _logger.LogInformation(
            "Match restored - User: {Username} (ID: {UserId}), Match ID: {MatchId}, {Home} vs {Away}",
            user.Username, user.Id, matchId, match.HomeTeam, match.AwayTeam);
        
        await context.Interaction.FollowupAsync($"✅ Mecz **{match.HomeTeam} vs {match.AwayTeam}** został przywrócony (status: Scheduled).", ephemeral: true);

        // Update match card
        try
        {
            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel != null && match.ThreadId.HasValue)
            {
                var thread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
                if (thread != null)
                {
                    var cardMessage = await _lookupService.FindMatchCardMessageAsync(thread);
                    if (cardMessage != null)
                    {
                        var round = match.Round;
                        var roundNum = round?.Number ?? 0;
                        await _matchCardService.PostMatchCardAsync(match, roundNum, cardMessage);
                        _logger.LogInformation("Updated match card after restoration - Match ID: {MatchId}", matchId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update match card after restoration - Match ID: {MatchId}", matchId);
        }
    }

    public async Task HandleSetCancelledMatchDateAsync(SocketInteractionContext context, int matchId, DateTimeOffset newStartTime)
    {
        var user = context.User as SocketGuildUser;
        if (user == null || context.Guild == null)
        {
            await context.Interaction.RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        await context.Interaction.DeferAsync(ephemeral: true);

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await context.Interaction.FollowupAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        if (match.Status != MatchStatus.Cancelled)
        {
            await context.Interaction.FollowupAsync("❌ Ten mecz nie jest odwołany.", ephemeral: true);
            return;
        }

        var oldStartTime = match.StartTime;
        match.StartTime = newStartTime;
        match.TypingDeadline = null; // Reset — domyślnie typy do momentu StartTime (ustaw deadline w edycji meczu)
        match.Status = MatchStatus.Scheduled;
        await _matchRepository.UpdateAsync(match);

        _logger.LogInformation(
            "Set new date for cancelled match - User: {Username} (ID: {UserId}), Match ID: {MatchId}, New date: {NewDate}",
            user.Username, user.Id, matchId, newStartTime);

        await context.Interaction.FollowupAsync($"✅ Ustawiono nową datę meczu: **{newStartTime:yyyy-MM-dd HH:mm}**", ephemeral: true);

        // Notify all users in thread
        try
        {
            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel != null && match.ThreadId.HasValue)
            {
                var thread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
                if (thread != null)
                {
                    var timestamp = newStartTime.ToUnixTimeSeconds();

                    var embed = new EmbedBuilder()
                        .WithTitle("📅 Nowa data meczu")
                        .WithDescription(
                            $"Mecz **{match.HomeTeam} vs {match.AwayTeam}** został przełożony.\n\n" +
                            $"**Nowa data:** <t:{timestamp}:F> (<t:{timestamp}:R>)\n\n" +
                            $"✅ Typy zostały zachowane")
                        .WithColor(Color.Green)
                        .WithCurrentTimestamp()
                        .Build();

                    await thread.SendMessageAsync(embed: embed);

                    // Update match card
                    var cardMessage = await _lookupService.FindMatchCardMessageAsync(thread);
                    if (cardMessage != null)
                    {
                        var round = match.Round;
                        var roundNum = round?.Number ?? 0;
                        await _matchCardService.PostMatchCardAsync(match, roundNum, cardMessage);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify users about new date - Match ID: {MatchId}", matchId);
        }
    }
}
