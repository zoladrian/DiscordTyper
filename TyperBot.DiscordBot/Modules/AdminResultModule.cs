using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.DiscordBot.Models;
using TyperBot.DiscordBot.Services;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Modules;

public class AdminResultModule : BaseAdminModule
{
    private readonly ILogger<AdminResultModule> _logger;
    private readonly IMatchRepository _matchRepository;
    private readonly IPredictionRepository _predictionRepository;
    private readonly MatchResultHandler _matchResultHandler;
    private readonly DiscordLookupService _lookupService;

    public AdminResultModule(
        ILogger<AdminResultModule> logger,
        IOptions<DiscordSettings> settings,
        IMatchRepository matchRepository,
        IPredictionRepository predictionRepository,
        MatchResultHandler matchResultHandler,
        DiscordLookupService lookupService) : base(settings.Value)
    {
        _logger = logger;
        _matchRepository = matchRepository;
        _predictionRepository = predictionRepository;
        _matchResultHandler = matchResultHandler;
        _lookupService = lookupService;
    }

    [ComponentInteraction("admin_set_result_*")]
    public async Task HandleSetResultButtonAsync(string matchIdStr)
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

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await RespondAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        if (match.Status == MatchStatus.Finished && match.HomeScore.HasValue && match.AwayScore.HasValue)
        {
            var embed = new EmbedBuilder()
                .WithTitle("⚠️ Uwaga! Zmiana wyniku zakończonego meczu")
                .WithDescription(
                    $"**Na pewno chcesz zmienić wynik meczu który się już odbył?**\n\n" +
                    $"**{match.HomeTeam} vs {match.AwayTeam}**\n" +
                    $"Aktualny wynik: **{match.HomeScore}:{match.AwayScore}**\n\n" +
                    $"Zmiana wyniku spowoduje przeliczenie punktów wszystkich graczy. " +
                    $"Informacja o tej zmianie zostanie opublikowana na kanale dostępnym dla wszystkich.")
                .WithColor(Color.Orange)
                .Build();

            var confirmButton = new ButtonBuilder()
                .WithCustomId($"admin_confirm_change_result_{matchId}")
                .WithLabel("✅ Tak, zmień wynik")
                .WithStyle(ButtonStyle.Danger);

            var cancelButton = new ButtonBuilder()
                .WithCustomId($"admin_cancel_change_result_{matchId}")
                .WithLabel("❌ Anuluj")
                .WithStyle(ButtonStyle.Secondary);

            var component = new ComponentBuilder()
                .WithButton(confirmButton, row: 0)
                .WithButton(cancelButton, row: 0)
                .Build();

            await RespondAsync(embed: embed, components: component, ephemeral: true);
            return;
        }

        var modal = new ModalBuilder()
            .WithTitle("Ustaw wynik meczu")
            .WithCustomId($"admin_set_result_modal_{matchId}")
            .AddTextInput(
                label: match.HomeTeam,
                customId: "home_score",
                style: TextInputStyle.Short,
                placeholder: "50",
                value: match.HomeScore?.ToString() ?? "50",
                required: true)
            .AddTextInput(
                label: match.AwayTeam,
                customId: "away_score",
                style: TextInputStyle.Short,
                placeholder: "40",
                value: match.AwayScore?.ToString() ?? "40",
                required: true)
            .Build();

        await RespondWithModalAsync(modal);

        _logger.LogInformation(
            "Set result button clicked - User: {Username}, Match ID: {MatchId}",
            Context.User.Username, matchId);
    }

    [ComponentInteraction("admin_confirm_change_result_*")]
    public async Task HandleConfirmChangeResultAsync(string matchIdStr)
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

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await RespondAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        var modal = new ModalBuilder()
            .WithTitle("Ustaw wynik meczu")
            .WithCustomId($"admin_set_result_modal_{matchId}")
            .AddTextInput(
                label: match.HomeTeam,
                customId: "home_score",
                style: TextInputStyle.Short,
                placeholder: "50",
                value: match.HomeScore?.ToString() ?? "50",
                required: true)
            .AddTextInput(
                label: match.AwayTeam,
                customId: "away_score",
                style: TextInputStyle.Short,
                placeholder: "40",
                value: match.AwayScore?.ToString() ?? "40",
                required: true)
            .Build();

        await RespondWithModalAsync(modal);
    }

    [ComponentInteraction("admin_cancel_change_result_*")]
    public async Task HandleCancelChangeResultAsync(string matchIdStr)
    {
        await RespondAsync("❌ Anulowano zmianę wyniku.", ephemeral: true);
    }

    [ModalInteraction("admin_set_result_modal_*", true)]
    public async Task HandleSetResultModalAsync(string matchIdStr, SetResultModal modal)
    {
        await _matchResultHandler.HandleSetResultAsync(Context, matchIdStr, modal.HomeScore, modal.AwayScore);
    }

    [ComponentInteraction("admin_send_match_table_*")]
    public async Task HandleSendMatchTableButtonAsync(string matchIdStr)
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

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await FollowupAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        if (match.Status != MatchStatus.Finished || !match.HomeScore.HasValue || !match.AwayScore.HasValue)
        {
            await FollowupAsync("❌ Mecz nie ma jeszcze wyniku.", ephemeral: true);
            return;
        }

        try
        {
            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel == null)
            {
                await FollowupAsync("❌ Nie znaleziono kanału typowanie.", ephemeral: true);
                return;
            }

            SocketThreadChannel? thread = null;
            if (match.ThreadId.HasValue)
                thread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);

            if (thread == null)
            {
                var roundLabel = Application.Services.RoundHelper.GetRoundLabel(match.Round?.Number ?? 0);
                var threadName = $"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}";
                thread = predictionsChannel.Threads.FirstOrDefault(t => t.Name == threadName);
            }

            if (thread == null)
            {
                await FollowupAsync("❌ Nie znaleziono wątku meczu.", ephemeral: true);
                return;
            }

            await PostMatchResultsTableAsync(match, thread);
            await FollowupAsync("✅ Tabela meczu została wysłana do wątku meczu.", ephemeral: true);

            _logger.LogInformation(
                "Match table sent manually - User: {Username}, Match ID: {MatchId}",
                user?.Username ?? "?", matchId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending match table for match {MatchId}", matchId);
            await FollowupAsync("❌ Wystąpił błąd podczas wysyłania tabeli.", ephemeral: true);
        }
    }

    private async Task PostMatchResultsTableAsync(Domain.Entities.Match match, IThreadChannel thread)
    {
        var predictions = (await _predictionRepository.GetValidPredictionsByMatchAsync(match.Id))
            .OrderByDescending(p => p.PlayerScore?.Points ?? -1)
            .ThenByDescending(p =>
                p.PlayerScore != null && (p.PlayerScore.Bucket == Bucket.P35 || p.PlayerScore.Bucket == Bucket.P50))
            .ToList();

        var embed = new EmbedBuilder()
            .WithTitle($"⚽ Wynik meczu: {match.HomeTeam} vs {match.AwayTeam}")
            .WithDescription($"**Wynik rzeczywisty:** {match.HomeScore?.ToString() ?? "?"}:{match.AwayScore?.ToString() ?? "?"}")
            .WithColor(Color.Green)
            .WithCurrentTimestamp();

        var round = match.Round;
        if (round != null)
        {
            var roundLabel = Application.Services.RoundHelper.GetRoundLabel(round.Number);
            embed.AddField("Kolejka", roundLabel, inline: true);
        }

        if (predictions.Any())
        {
            var table = "```\n";
            table += "Gracz                  Typ      Pkt\n";
            table += "════════════════════════════════════\n";
            table += $"🏆 Wynik rzeczywisty  {match.HomeScore?.ToString() ?? "?",2}:{match.AwayScore?.ToString() ?? "?",2}     -\n";
            table += "────────────────────────────────────\n";

            foreach (var pred in predictions)
            {
                var playerName = pred.Player.DiscordUsername;
                if (playerName.Length > 20) playerName = playerName.Substring(0, 17) + "...";

                if (pred.PlayerScore == null)
                {
                    table += $"{playerName,-20}  {pred.HomeTip,2}:{pred.AwayTip,2}       —   ⏳\n";
                    continue;
                }

                var statusIcon = pred.PlayerScore.Bucket is Bucket.P35 or Bucket.P50
                    ? "👑"
                    : pred.PlayerScore.Points > 0 ? "👍" : "💩";

                table += $"{playerName,-20}  {pred.HomeTip,2}:{pred.AwayTip,2}     {pred.PlayerScore.Points,3}   {statusIcon}\n";
            }
            table += "```";

            embed.AddField("Typy graczy", table, false);
            embed.WithFooter("👑 = Celny wynik | 👍 = Poprawny zwycięzca | 💩 = Brak punktów | ⏳ = Punkty jeszcze nie naliczone");
        }
        else
        {
            embed.AddField("Typy graczy", "*Brak typów dla tego meczu*", false);
        }

        await thread.SendMessageAsync(embed: embed.Build());
        _logger.LogInformation("Match results table published - Match ID: {MatchId}", match.Id);
    }
}
