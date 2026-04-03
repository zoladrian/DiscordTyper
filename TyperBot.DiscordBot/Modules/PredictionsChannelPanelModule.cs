using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using TyperBot.DiscordBot.Constants;
using TyperBot.DiscordBot.Models;
using TyperBot.DiscordBot.Services;

namespace TyperBot.DiscordBot.Modules;

public class PredictionsChannelPanelModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DiscordSettings _settings;
    private readonly DiscordLookupService _lookupService;
    private readonly PlayerCommandExecutor _executor;

    public PredictionsChannelPanelModule(
        IOptions<DiscordSettings> settings,
        DiscordLookupService lookupService,
        PlayerCommandExecutor executor)
    {
        _settings = settings.Value;
        _lookupService = lookupService;
        _executor = executor;
    }

    private bool HasPlayerRole(SocketGuildUser? user) =>
        user?.Roles.Any(r => r.Name == _settings.PlayerRoleName) == true;

    private async Task<bool> EnsurePredictionsChannelAndPlayerRoleAsync()
    {
        var ch = await _lookupService.GetPredictionsChannelAsync();
        if (ch == null || Context.Channel?.Id != ch.Id)
        {
            await FollowupAsync("Te akcje działają tylko na kanale typowania.", ephemeral: true);
            return false;
        }

        var user = Context.User as SocketGuildUser;
        if (!HasPlayerRole(user))
        {
            await FollowupAsync($"Potrzebujesz roli **{_settings.PlayerRoleName}**.", ephemeral: true);
            return false;
        }

        return true;
    }

    [ComponentInteraction(CustomIds.PredictionsPanel.BtnMojeTypySezon)]
    public async Task PanelMojeTypySezonAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await EnsurePredictionsChannelAndPlayerRoleAsync()) return;
        await _executor.ExecuteMyPredictionsAsync(Context, null);
    }

    [ComponentInteraction(CustomIds.PredictionsPanel.BtnTabelaSezonu)]
    public async Task PanelTabelaSezonuAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await EnsurePredictionsChannelAndPlayerRoleAsync()) return;
        await _executor.ExecuteSeasonTableAsync(Context, Context.User.Username);
    }

    [ComponentInteraction(CustomIds.PredictionsPanel.BtnWykres)]
    public async Task PanelWykresAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await EnsurePredictionsChannelAndPlayerRoleAsync()) return;
        await _executor.ExecuteSeasonChartAsync(Context);
    }

    [ComponentInteraction(CustomIds.PredictionsPanel.BtnRozklad)]
    public async Task PanelRozkladAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await EnsurePredictionsChannelAndPlayerRoleAsync()) return;
        await _executor.ExecuteSeasonPointsHistogramAsync(Context);
    }

    [ComponentInteraction(CustomIds.PredictionsPanel.BtnKolowy)]
    public async Task PanelKolowyAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await EnsurePredictionsChannelAndPlayerRoleAsync()) return;
        await _executor.ExecutePlayerSeasonPointsPieAsync(Context);
    }

    [ComponentInteraction(CustomIds.PredictionsPanel.SelMojeTypyRound)]
    public async Task PanelSelMojeTypyAsync(string[] selectedValues)
    {
        await DeferAsync(ephemeral: true);
        if (!await EnsurePredictionsChannelAndPlayerRoleAsync()) return;
        if (selectedValues.Length == 0 || !int.TryParse(selectedValues[0], out var round))
        {
            await FollowupAsync("Nieprawidłowy wybór kolejki.", ephemeral: true);
            return;
        }

        await _executor.ExecuteMyPredictionsAsync(Context, round);
    }

    [ComponentInteraction(CustomIds.PredictionsPanel.SelTabelaRound)]
    public async Task PanelSelTabelaAsync(string[] selectedValues)
    {
        await DeferAsync(ephemeral: true);
        if (!await EnsurePredictionsChannelAndPlayerRoleAsync()) return;
        if (selectedValues.Length == 0 || !int.TryParse(selectedValues[0], out var round))
        {
            await FollowupAsync("Nieprawidłowy wybór kolejki.", ephemeral: true);
            return;
        }

        await _executor.ExecuteRoundTableAsync(Context, round, Context.User.Username);
    }

    [ComponentInteraction(CustomIds.PredictionsPanel.SelPktRound)]
    public async Task PanelSelPktKolejkiAsync(string[] selectedValues)
    {
        await DeferAsync(ephemeral: true);
        if (!await EnsurePredictionsChannelAndPlayerRoleAsync()) return;
        if (selectedValues.Length == 0 || !int.TryParse(selectedValues[0], out var round))
        {
            await FollowupAsync("Nieprawidłowy wybór kolejki.", ephemeral: true);
            return;
        }

        await _executor.ExecutePlayerRoundPointsDeltaAsync(Context, round);
    }

    [ComponentInteraction(CustomIds.PredictionsPanel.SelPktMatch)]
    public async Task PanelSelPktMeczuAsync(string[] selectedValues)
    {
        await DeferAsync(ephemeral: true);
        if (!await EnsurePredictionsChannelAndPlayerRoleAsync()) return;
        if (selectedValues.Length == 0 || !int.TryParse(selectedValues[0], out var matchId))
        {
            await FollowupAsync("Nieprawidłowy wybór meczu.", ephemeral: true);
            return;
        }

        await _executor.ExecutePlayerMatchPointsDeltaAsync(Context, matchId);
    }
}
