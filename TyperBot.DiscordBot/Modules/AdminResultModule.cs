using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.DiscordBot;
using TyperBot.DiscordBot.Autocomplete;
using TyperBot.DiscordBot.Models;
using TyperBot.DiscordBot.Services;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Modules;

public class AdminResultModule : BaseAdminModule
{
    private readonly ILogger<AdminResultModule> _logger;
    private readonly IMatchRepository _matchRepository;
    private readonly MatchResultHandler _matchResultHandler;
    private readonly DiscordLookupService _lookupService;
    private readonly MatchResultsTableService _matchResultsTableService;

    public AdminResultModule(
        ILogger<AdminResultModule> logger,
        IOptions<DiscordSettings> settings,
        IMatchRepository matchRepository,
        MatchResultHandler matchResultHandler,
        DiscordLookupService lookupService,
        MatchResultsTableService matchResultsTableService) : base(settings.Value)
    {
        _logger = logger;
        _matchRepository = matchRepository;
        _matchResultHandler = matchResultHandler;
        _lookupService = lookupService;
        _matchResultsTableService = matchResultsTableService;
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
                label: DiscordApiLimits.Truncate(match.HomeTeam, DiscordApiLimits.TextInputLabel),
                customId: "home_score",
                style: TextInputStyle.Short,
                placeholder: DiscordApiLimits.Truncate(match.HomeTeam, DiscordApiLimits.TextInputPlaceholder),
                value: match.HomeScore?.ToString() ?? "50",
                required: true)
            .AddTextInput(
                label: DiscordApiLimits.Truncate(match.AwayTeam, DiscordApiLimits.TextInputLabel),
                customId: "away_score",
                style: TextInputStyle.Short,
                placeholder: DiscordApiLimits.Truncate(match.AwayTeam, DiscordApiLimits.TextInputPlaceholder),
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

    [SlashCommand("admin-tabela-meczu", "Wyślij tabelę wyników meczu (embed); opcjonalnie kanał lub wątek")]
    public async Task AdminPostMatchTableSlashAsync(
        [Summary(description: "Wybierz mecz z listy (wpisz fragment nazwy, kolejkę lub ID)")]
        [Autocomplete(typeof(AdminMatchChoiceAutocompleteHandler))]
        string mecz,
        [Summary("kanal_lub_watek", "Kanał lub wątek — wpisz nazwę wątku w tym polu. Puste = wątek meczu.")]
        [ChannelTypes(ChannelType.Text, ChannelType.News, ChannelType.PublicThread, ChannelType.PrivateThread, ChannelType.NewsThread)]
        ITextChannel? kanał = null)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(mecz, out var matchId))
        {
            await RespondAsync("❌ Wybierz mecz z listy autouzupełniania.", ephemeral: true);
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
            if (kanał != null)
            {
                var (target, err) = await AdminTableChannelHelper.ResolveAsync(Context.Guild, kanał, _lookupService);
                if (target == null)
                {
                    await FollowupAsync($"❌ {err}", ephemeral: true);
                    return;
                }

                await _matchResultsTableService.PostToTextChannelAsync(match, target);
                await FollowupAsync($"✅ Tabela wysłana na {MentionUtils.MentionChannel(target.Id)}.", ephemeral: true);
                _logger.LogInformation("Match table sent via slash to channel — User: {User}, Match: {MatchId}", user.Username, mecz);
                return;
            }

            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel == null)
            {
                await FollowupAsync("❌ Nie znaleziono kanału typowania (potrzebnego do wątku meczu).", ephemeral: true);
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
                await FollowupAsync("❌ Nie znaleziono wątku meczu — użyj parametru kanału lub utwórz wątek.", ephemeral: true);
                return;
            }

            await _matchResultsTableService.PostToThreadAsync(match, thread);
            await FollowupAsync("✅ Tabela wysłana do wątku meczu.", ephemeral: true);
            _logger.LogInformation("Match table sent via slash to thread — User: {User}, Match: {MatchId}", user.Username, matchId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending match table (slash) for match {MatchId}", matchId);
            await FollowupAsync("❌ Wystąpił błąd podczas wysyłania tabeli.", ephemeral: true);
        }
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

            await _matchResultsTableService.PostToThreadAsync(match, thread);
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

}
