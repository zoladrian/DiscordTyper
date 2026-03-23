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

public class AdminRoundModule : BaseAdminModule
{
    private readonly ILogger<AdminRoundModule> _logger;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRoundRepository _roundRepository;
    private readonly IMatchRepository _matchRepository;
    private readonly RoundManagementService _roundManagementService;
    private readonly AdminPanelService _adminPanelService;
    private readonly AdminMatchCreationStateService _matchCreationState;

    public AdminRoundModule(
        ILogger<AdminRoundModule> logger,
        IOptions<DiscordSettings> settings,
        ISeasonRepository seasonRepository,
        IRoundRepository roundRepository,
        IMatchRepository matchRepository,
        RoundManagementService roundManagementService,
        AdminPanelService adminPanelService,
        AdminMatchCreationStateService matchCreationState) : base(settings.Value)
    {
        _logger = logger;
        _seasonRepository = seasonRepository;
        _roundRepository = roundRepository;
        _matchRepository = matchRepository;
        _roundManagementService = roundManagementService;
        _adminPanelService = adminPanelService;
        _matchCreationState = matchCreationState;
    }

    [ComponentInteraction("admin_add_kolejka")]
    public async Task HandleAddRoundButtonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        await RespondWithModalAsync<AddRoundModal>("admin_add_kolejka_modal");
    }

    [ModalInteraction("admin_add_kolejka_modal")]
    public async Task HandleAddRoundModalAsync(AddRoundModal modal)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null || user == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        var guild = Context.Guild;

        if (!int.TryParse(modal.RoundNumber, out var roundNumber))
        {
            await RespondAsync("❌ Nieprawidłowy numer kolejki.", ephemeral: true);
            return;
        }

        if (!int.TryParse(modal.MatchCount.Trim(), out var matchCount) || matchCount < 0 || matchCount > 18)
        {
            await RespondAsync("❌ Liczba meczów musi być z zakresu 0–18.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var result = await _roundManagementService.AddRoundAsync(roundNumber, user.Id, user.Username);
        if (!result.success)
        {
            await FollowupAsync($"❌ {result.message}", ephemeral: true);
            return;
        }

        if (matchCount == 0)
        {
            await FollowupAsync(
                $"✅ {result.message}\n\nKolejka jest pusta — dodaj mecze przyciskiem **⚽ Dodaj mecz** lub **Zarządzaj kolejką**.",
                ephemeral: true);
            return;
        }

        _matchCreationState.ClearState(guild.Id, user.Id);
        _matchCreationState.InitializeBatchRoundCreation(guild.Id, user.Id, roundNumber, matchCount);

        var roundLabel = RoundHelper.GetRoundLabel(roundNumber);
        var openModalButton = new ButtonBuilder()
            .WithCustomId("admin_kolejka_open_match_modal_1")
            .WithLabel($"📝 Dodaj mecz 1/{matchCount}")
            .WithStyle(ButtonStyle.Primary);
        var component = new ComponentBuilder().WithButton(openModalButton).Build();

        await FollowupAsync(
            $"✅ {result.message}\n\n📝 **{roundLabel} — mecz 1/{matchCount}**\nKliknij przycisk, aby wypełnić dane meczu (powtórz dla każdego meczu w kolejce).",
            components: component,
            ephemeral: true);
    }

    [ComponentInteraction("admin_manage_kolejka")]
    public async Task HandleManageRoundButtonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var rounds = (await _roundRepository.GetBySeasonIdAsync(season.Id))
            .OrderBy(r => r.Number)
            .ToList();

        if (!rounds.Any())
        {
            await FollowupAsync("❌ Brak kolejek w sezonie. Użyj 'Dodaj kolejkę' aby utworzyć pierwszą.", ephemeral: true);
            return;
        }

        var roundOptions = rounds.Select(r => new SelectMenuOptionBuilder()
            .WithLabel(RoundHelper.GetRoundLabel(r.Number))
            .WithValue(r.Id.ToString())
            .WithDescription($"Kolejka {r.Number} - {r.Matches.Count} meczów"))
            .ToList();

        var selectMenu = new SelectMenuBuilder()
            .WithCustomId("admin_manage_kolejka_select")
            .WithPlaceholder("Wybierz kolejkę do zarządzania")
            .WithOptions(roundOptions)
            .WithMinValues(1)
            .WithMaxValues(1);

        var component = new ComponentBuilder()
            .WithSelectMenu(selectMenu)
            .Build();

        await FollowupAsync("Wybierz kolejkę do zarządzania:", components: component, ephemeral: true);
    }

    [ComponentInteraction("admin_manage_kolejka_select")]
    public async Task HandleManageRoundSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null || selectedValues.Length == 0)
        {
            if (!Context.Interaction.HasResponded)
                await RespondAsync("❌ Nieprawidłowy wybór.", ephemeral: true);
            return;
        }

        if (!int.TryParse(selectedValues[0], out var roundId))
        {
            if (!Context.Interaction.HasResponded)
                await RespondAsync("❌ Nieprawidłowy ID kolejki.", ephemeral: true);
            return;
        }

        if (!Context.Interaction.HasResponded)
            await DeferAsync(ephemeral: true);

        var round = await _roundRepository.GetByIdAsync(roundId);
        if (round == null)
        {
            await FollowupAsync("❌ Kolejka nie znaleziona.", ephemeral: true);
            return;
        }

        var roundLabel = RoundHelper.GetRoundLabel(round.Number);
        var matches = round.Matches.OrderBy(m => m.StartTime).ToList();

        var embed = new EmbedBuilder()
            .WithTitle($"Zarządzanie kolejką: {roundLabel}")
            .WithDescription($"Kolejka zawiera {matches.Count} meczów.")
            .WithColor(Color.Gold);

        foreach (var match in matches)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(match.StartTime.UtcDateTime, tz);
            var status = match.Status switch
            {
                MatchStatus.Scheduled => "⏳ Zaplanowany",
                MatchStatus.InProgress => "▶️ W trakcie",
                MatchStatus.Finished => $"✅ Zakończony ({match.HomeScore}:{match.AwayScore})",
                MatchStatus.Cancelled => "❌ Odwołany",
                _ => "❓ Nieznany"
            };

            embed.AddField(
                $"{match.HomeTeam} vs {match.AwayTeam}",
                $"{status}\nData: {localTime:yyyy-MM-dd HH:mm}\nID: {match.Id}",
                inline: true);
        }

        var component = new ComponentBuilder();

        int buttonCount = 0;
        foreach (var match in matches.Take(8))
        {
            int row = buttonCount / 3;

            var editButton = new ButtonBuilder()
                .WithCustomId($"admin_edit_match_{match.Id}")
                .WithLabel($"✏️ {TeamNameHelper.GetMatchShortcut(match.HomeTeam, match.AwayTeam)}")
                .WithStyle(ButtonStyle.Secondary);
            component.WithButton(editButton, row: row);
            buttonCount++;

            var resultLabel = match.HomeScore.HasValue ? "📝 Zmień wynik" : "🏁 Wynik";
            var resultStyle = match.HomeScore.HasValue ? ButtonStyle.Secondary : ButtonStyle.Success;
            var resultButton = new ButtonBuilder()
                .WithCustomId($"admin_set_result_{match.Id}")
                .WithLabel(resultLabel)
                .WithStyle(resultStyle);
            component.WithButton(resultButton, row: row);
            buttonCount++;

            var deleteButton = new ButtonBuilder()
                .WithCustomId($"admin_delete_match_{match.Id}")
                .WithLabel("🗑️ Usuń")
                .WithStyle(ButtonStyle.Danger);
            component.WithButton(deleteButton, row: row);
            buttonCount++;
        }

        var addMatchButton = new ButtonBuilder()
            .WithCustomId($"admin_add_match_to_round_{round.Id}")
            .WithLabel("➕ Dodaj mecz do tej kolejki")
            .WithStyle(ButtonStyle.Primary);
        component.WithButton(addMatchButton, row: 4);

        await FollowupAsync(embed: embed.Build(), components: component.Build(), ephemeral: true);
    }
}
