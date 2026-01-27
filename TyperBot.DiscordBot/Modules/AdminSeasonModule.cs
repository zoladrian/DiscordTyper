using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.DiscordBot.Models;
using TyperBot.DiscordBot.Services;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Modules;

public class AdminSeasonModule : BaseAdminModule
{
    private readonly ILogger<AdminSeasonModule> _logger;
    private readonly ISeasonRepository _seasonRepository;
    private readonly SeasonManagementService _seasonManagementService;
    private readonly AdminPanelService _adminPanelService;

    public AdminSeasonModule(
        ILogger<AdminSeasonModule> logger,
        IOptions<DiscordSettings> settings,
        ISeasonRepository seasonRepository,
        SeasonManagementService seasonManagementService,
        AdminPanelService adminPanelService) : base(settings.Value)
    {
        _logger = logger;
        _seasonRepository = seasonRepository;
        _seasonManagementService = seasonManagementService;
        _adminPanelService = adminPanelService;
    }

    [SlashCommand("start-nowego-sezonu", "Rozpocznij nowy sezon typera.")]
    public async Task StartNewSeasonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondWithErrorAsync("Nie masz uprawnień do użycia tej komendy.");
            return;
        }

        await RespondWithModalAsync<StartSeasonModal>("admin_start_season_modal");
    }

    [ModalInteraction("admin_start_season_modal")]
    public async Task HandleStartSeasonModalAsync(StartSeasonModal modal)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        var result = await _seasonManagementService.StartNewSeasonAsync(modal.SeasonName, user.Id, user.Username);
        await RespondAsync(result.success ? $"✅ {result.message}" : $"❌ {result.message}", ephemeral: true);
    }

    [SlashCommand("panel-sezonu", "Otwórz panel sezonu typera.")]
    public async Task AdminPanelAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondWithErrorAsync("Nie masz uprawnień do użycia tej komendy.");
            return;
        }

        var channel = Context.Channel as SocketTextChannel;
        if (!IsAllowedChannel(channel))
        {
            await RespondWithErrorAsync(
                $"Ta komenda może być używana tylko w kanałach: #{Settings.Channels.AdminChannel} lub #{Settings.Channels.PredictionsChannel}",
                $"Używasz: #{channel?.Name ?? "DM"}");
            return;
        }
        
        var allSeasons = (await _seasonRepository.GetAllAsync()).ToList();
        
        if (allSeasons.Count > 1)
        {
            var panel = await _adminPanelService.GetSeasonSelectionPanelAsync();
            await RespondAsync(embed: panel.embed, components: panel.components, ephemeral: true);
            return;
        }

        var season = allSeasons.FirstOrDefault(s => s.IsActive) ?? allSeasons.FirstOrDefault();
        var seasonPanel = await _adminPanelService.GetSeasonPanelAsync(season);
        await RespondAsync(embed: seasonPanel.embed, components: seasonPanel.components, ephemeral: true);
    }

    [ComponentInteraction("admin_select_season")]
    public async Task HandleSelectSeasonAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (selectedValues.Length == 0 || !int.TryParse(selectedValues[0], out var seasonId))
        {
            await RespondAsync("❌ Nieprawidłowy wybór sezonu.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetByIdAsync(seasonId);
        var panel = await _adminPanelService.GetSeasonPanelAsync(season);
        await RespondAsync(embed: panel.embed, components: panel.components, ephemeral: true);
    }

    [ComponentInteraction("admin_end_season_*")]
    public async Task HandleEndSeasonAsync(string seasonIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(seasonIdStr, out var seasonId))
        {
            await RespondAsync("❌ Nieprawidłowy sezon.", ephemeral: true);
            return;
        }

        var result = await _seasonManagementService.EndSeasonAsync(seasonId, user.Id, user.Username);
        await RespondAsync(result.success ? $"✅ {result.message}" : $"❌ {result.message}", ephemeral: true);
    }

    [ComponentInteraction("admin_reactivate_season_*")]
    public async Task HandleReactivateSeasonAsync(string seasonIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(seasonIdStr, out var seasonId))
        {
            await RespondAsync("❌ Nieprawidłowy sezon.", ephemeral: true);
            return;
        }

        var result = await _seasonManagementService.ReactivateSeasonAsync(seasonId, user.Id, user.Username);
        await RespondAsync(result.success ? $"✅ {result.message}" : $"❌ {result.message}", ephemeral: true);
    }
}
