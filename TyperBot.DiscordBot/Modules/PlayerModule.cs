using Discord.Interactions;
using Discord.WebSocket;
using TyperBot.DiscordBot.Autocomplete;
using TyperBot.DiscordBot.Services;

namespace TyperBot.DiscordBot.Modules;

public class PlayerModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly PlayerCommandExecutor _executor;

    public PlayerModule(PlayerCommandExecutor executor) => _executor = executor;

    [SlashCommand("moje-typy", "Wyświetl swoje typy: cały aktywny sezon lub jedna kolejka")]
    public async Task MyPredictionsAsync(
        [Summary(description: "Numer kolejki (opcjonalne; bez numeru — wszystkie typy w aktywnym sezonie)")] int? round = null)
    {
        await DeferAsync(ephemeral: true);
        await _executor.ExecuteMyPredictionsAsync(Context, round);
    }

    [SlashCommand("tabela-kolejki", "Wyświetl tabelę dla konkretnej kolejki")]
    public async Task RoundTableAsync([Summary(description: "Numer kolejki")] int round)
    {
        await DeferAsync(ephemeral: true);
        await _executor.ExecuteRoundTableAsync(Context, round, Context.User.Username);
    }

    [SlashCommand("tabela-sezonu", "Wyświetl ogólną tabelę sezonu")]
    public async Task SeasonTableAsync()
    {
        await DeferAsync(ephemeral: true);
        await _executor.ExecuteSeasonTableAsync(Context, Context.User.Username);
    }

    [SlashCommand("pkt-meczu", "PNG: punkty w meczu i różnica względem poprzedniego zakończonego meczu")]
    public async Task PlayerMatchPointsDeltaAsync(
        [Summary(description: "Mecz z listy")]
        [Autocomplete(typeof(AdminMatchChoiceAutocompleteHandler))]
        string mecz)
    {
        await DeferAsync(ephemeral: true);

        if (!int.TryParse(mecz, out var matchId))
        {
            await FollowupAsync("Wybierz mecz z autouzupełniania.", ephemeral: true);
            return;
        }

        await _executor.ExecutePlayerMatchPointsDeltaAsync(Context, matchId);
    }

    [SlashCommand("pkt-kolejki", "PNG: punkty w kolejce i różnica względem poprzedniej kolejki")]
    public async Task PlayerRoundPointsDeltaAsync([Summary(description: "Numer kolejki 1–18")] int numer)
    {
        await DeferAsync(ephemeral: true);
        await _executor.ExecutePlayerRoundPointsDeltaAsync(Context, numer);
    }

    [SlashCommand("wykres-punktow", "PNG: skumulowane punkty — tylko cały aktywny sezon (linie wg graczy, kolejki)")]
    public async Task PlayerSeasonChartAsync()
    {
        await DeferAsync(ephemeral: true);
        await _executor.ExecuteSeasonChartAsync(Context);
    }

    [SlashCommand("tabela-landrynek", "PNG: ranking zakończonych meczów bez ważnego typu (tylko gdy są dane)")]
    public async Task PlayerLandrynkiTableAsync()
    {
        await DeferAsync(ephemeral: true);
        await _executor.ExecuteLandrynkiTableAsync(Context);
    }

    [SlashCommand("rozklad-punktow", "PNG: ile razy dostałeś daną liczbę punktów w meczu — cały aktywny sezon (wszyscy gracze)")]
    public async Task PlayerSeasonPointsHistogramAsync()
    {
        await DeferAsync(ephemeral: true);
        await _executor.ExecuteSeasonPointsHistogramAsync(Context);
    }

    [SlashCommand("kolowy-rozklad-punktow",
        "PNG: Twój wykres kołowy — ile razy która liczba punktów w meczu (tylko Ty, cały aktywny sezon)")]
    public async Task PlayerSeasonPointsPieAsync()
    {
        await DeferAsync(ephemeral: true);
        await _executor.ExecutePlayerSeasonPointsPieAsync(Context);
    }
}
