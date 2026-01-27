using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.DiscordBot.Models;
using TyperBot.DiscordBot.Services;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Modules;

public class AdminRoundModule : BaseAdminModule
{
    private readonly ILogger<AdminRoundModule> _logger;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRoundRepository _roundRepository;
    private readonly RoundManagementService _roundManagementService;
    private readonly AdminPanelService _adminPanelService;

    public AdminRoundModule(
        ILogger<AdminRoundModule> logger,
        IOptions<DiscordSettings> settings,
        ISeasonRepository seasonRepository,
        IRoundRepository roundRepository,
        RoundManagementService roundManagementService,
        AdminPanelService adminPanelService) : base(settings.Value)
    {
        _logger = logger;
        _seasonRepository = seasonRepository;
        _roundRepository = roundRepository;
        _roundManagementService = roundManagementService;
        _adminPanelService = adminPanelService;
    }

    [ComponentInteraction("admin_add_kolejka")]
    public async Task HandleAddKolejkaButtonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        await RespondWithModalAsync<AddKolejkaModal>("admin_add_kolejka_modal");
    }

    [ModalInteraction("admin_add_kolejka_modal")]
    public async Task HandleAddKolejkaModalAsync(AddKolejkaModal modal)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(modal.KolejkaNumber, out var roundNumber))
        {
            await RespondAsync("❌ Nieprawidłowy numer kolejki.", ephemeral: true);
            return;
        }

        var result = await _roundManagementService.AddRoundAsync(roundNumber, user.Id, user.Username);
        await RespondAsync(result.success ? $"✅ {result.message}" : $"❌ {result.message}", ephemeral: true);
    }

    [ComponentInteraction("admin_manage_kolejka")]
    public async Task HandleManageKolejkaButtonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await RespondAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var rounds = (await _roundRepository.GetBySeasonIdAsync(season.Id))
            .OrderBy(r => r.Number)
            .ToList();

        if (!rounds.Any())
        {
            await RespondAsync("❌ Brak dodanych kolejek w tym sezonie.", ephemeral: true);
            return;
        }

        var selectMenu = new SelectMenuBuilder()
            .WithCustomId("admin_manage_kolejka_select")
            .WithPlaceholder("Wybierz kolejkę do zarządzania...");

        foreach (var round in rounds)
        {
            selectMenu.AddOption($"Kolejka {round.Number}", round.Id.ToString());
        }

        var component = new ComponentBuilder()
            .WithSelectMenu(selectMenu)
            .Build();

        await RespondAsync("Wybierz kolejkę, którą chcesz zarządzać:", components: component, ephemeral: true);
    }

    [ComponentInteraction("admin_manage_kolejka_select")]
    public async Task HandleManageKolejkaSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (selectedValues.Length == 0 || !int.TryParse(selectedValues[0], out var roundId))
        {
            await RespondAsync("❌ Nieprawidłowy wybór kolejki.", ephemeral: true);
            return;
        }

        var round = await _roundRepository.GetByIdAsync(roundId);
        if (round == null)
        {
            await RespondAsync("❌ Kolejka nie znaleziona.", ephemeral: true);
            return;
        }

        // This would normally show a panel for managing the round
        await RespondAsync($"Zarządzanie kolejką {round.Number} (ID: {round.Id}) - Funkcjonalność w trakcie implementacji.", ephemeral: true);
    }
}
