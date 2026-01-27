using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.DiscordBot.Models;
using TyperBot.DiscordBot.Services;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Modules;

public class AdminResultModule : BaseAdminModule
{
    private readonly ILogger<AdminResultModule> _logger;
    private readonly IMatchRepository _matchRepository;
    private readonly MatchResultHandler _matchResultHandler;

    public AdminResultModule(
        ILogger<AdminResultModule> logger,
        IOptions<DiscordSettings> settings,
        IMatchRepository matchRepository,
        MatchResultHandler matchResultHandler) : base(settings.Value)
    {
        _logger = logger;
        _matchRepository = matchRepository;
        _matchResultHandler = matchResultHandler;
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

        var modal = new SetResultModal();
        if (match.HomeScore.HasValue) modal.HomeScore = match.HomeScore.Value.ToString();
        if (match.AwayScore.HasValue) modal.AwayScore = match.AwayScore.Value.ToString();

        await RespondWithModalAsync<SetResultModal>($"admin_set_result_modal_{matchId}", modal);
    }

    [ModalInteraction("admin_set_result_modal_*", true)]
    public async Task HandleSetResultModalAsync(string matchIdStr, SetResultModal modal)
    {
        await _matchResultHandler.HandleSetResultAsync(Context, matchIdStr, modal.HomeScore, modal.AwayScore);
    }
}
