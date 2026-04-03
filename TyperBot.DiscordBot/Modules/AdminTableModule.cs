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
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Modules;

public class AdminTableModule : BaseAdminModule
{
    private readonly ILogger<AdminTableModule> _logger;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRoundRepository _roundRepository;
    private readonly IMatchRepository _matchRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly ExportService _exportService;
    private readonly TableGenerator _tableGenerator;
    private readonly StandingsAnalyticsGenerator _analyticsGenerator;

    public AdminTableModule(
        ILogger<AdminTableModule> logger,
        IOptions<DiscordSettings> settings,
        ISeasonRepository seasonRepository,
        IRoundRepository roundRepository,
        IMatchRepository matchRepository,
        IPlayerRepository playerRepository,
        ExportService exportService,
        TableGenerator tableGenerator,
        StandingsAnalyticsGenerator analyticsGenerator) : base(settings.Value)
    {
        _logger = logger;
        _seasonRepository = seasonRepository;
        _roundRepository = roundRepository;
        _matchRepository = matchRepository;
        _playerRepository = playerRepository;
        _exportService = exportService;
        _tableGenerator = tableGenerator;
        _analyticsGenerator = analyticsGenerator;
    }

    /// <summary>
    /// Slash commands <c>admin-tabela-sezonu</c> / <c>admin-tabela-kolejki</c> (text) and <c>admin-tabela-sezonu-obraz</c> / <c>admin-tabela-kolejki-obraz</c> (PNG) are in <c>AdminModule</c> / here.
    /// Panel buttons here generate PNG images (ephemeral).
    /// </summary>
    [ComponentInteraction("admin_table_season")]
    public async Task HandleTableSeasonButtonAsync()
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

        try
        {
            using var imageStream = await CreateSeasonTablePngStreamAsync(season.Id);
            await FollowupWithFileAsync(imageStream, $"tabela_sezonu_{DateTime.Now:yyyyMMdd}.png", $"🏆 **Aktualna tabela sezonu: {season.Name}**", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating season table");
            await FollowupAsync($"❌ Wystąpił błąd: {ex.Message}", ephemeral: true);
        }
    }

    [ComponentInteraction("admin_table_round")]
    public async Task HandleTableRoundButtonAsync()
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
            await FollowupAsync("❌ Brak kolejek w tym sezonie.", ephemeral: true);
            return;
        }

        var selectMenu = new SelectMenuBuilder()
            .WithCustomId("admin_table_round_select")
            .WithPlaceholder("Wybierz kolejkę...");

        foreach (var round in rounds)
        {
            selectMenu.AddOption($"Kolejka {round.Number}", round.Id.ToString());
        }

        var component = new ComponentBuilder()
            .WithSelectMenu(selectMenu)
            .Build();

        await FollowupAsync("Wybierz kolejkę, dla której chcesz wygenerować tabelę:", components: component, ephemeral: true);
    }

    [ComponentInteraction("admin_table_round_select")]
    public async Task HandleTableRoundSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        if (selectedValues.Length == 0 || !int.TryParse(selectedValues[0], out var roundId))
        {
            await RespondAsync("❌ Nieprawidłowy wybór.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            using var imageStream = await CreateRoundTablePngStreamAsync(roundId);
            await FollowupWithFileAsync(imageStream, $"tabela_kolejki_{roundId}_{DateTime.Now:yyyyMMdd}.png", $"📊 **Tabela kolejki**", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating round table");
            await FollowupAsync($"❌ Wystąpił błąd: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("admin-tabela-sezonu-obraz", "Tabela sezonu (PNG): domyślnie w tym kanale; parametr = inny kanał/wątek")]
    public async Task AdminPostSeasonTablePngAsync(
        [Summary("kanal_lub_watek", "Opcjonalnie inny kanał/wątek. Puste = kanał, w którym wpisano komendę.")]
        [ChannelTypes(ChannelType.Text, ChannelType.News, ChannelType.PublicThread, ChannelType.PrivateThread, ChannelType.NewsThread)]
        ITextChannel? kanał = null)
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

        try
        {
            var (target, err) = AdminTableChannelHelper.Resolve(Context.Guild, kanał, Context.Channel);
            if (target == null)
            {
                await FollowupAsync($"❌ {err}", ephemeral: true);
                return;
            }

            using var imageStream = await CreateSeasonTablePngStreamAsync(season.Id);
            await target.SendFileAsync(
                imageStream,
                $"tabela_sezonu_{DateTime.Now:yyyyMMdd}.png",
                text: $"🏆 **Tabela sezonu: {season.Name}**");

            await FollowupAsync($"✅ PNG wysłany na {MentionUtils.MentionChannel(target.Id)}.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error posting season table PNG");
            await FollowupAsync($"❌ Wystąpił błąd: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("admin-tabela-kolejki-obraz", "Tabela kolejki (PNG): domyślnie w tym kanale; parametr = inny kanał/wątek")]
    public async Task AdminPostRoundTablePngAsync(
        [Summary(description: "Numer kolejki")] int round,
        [Summary("kanal_lub_watek", "Opcjonalnie inny kanał/wątek. Puste = kanał, w którym wpisano komendę.")]
        [ChannelTypes(ChannelType.Text, ChannelType.News, ChannelType.PublicThread, ChannelType.PrivateThread, ChannelType.NewsThread)]
        ITextChannel? kanał = null)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        if (!RoundHelper.IsValidRoundNumber(round))
        {
            await FollowupAsync($"❌ Numer kolejki musi być z zakresu 1–18 (podano: {round}).", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var roundEntity = season.FindRoundByNumber(round)
            ?? await _roundRepository.GetByNumberAsync(season.Id, round);
        if (roundEntity == null)
        {
            var available = season.Rounds.Count > 0
                ? string.Join(", ", season.Rounds.OrderBy(r => r.Number).Select(r => r.Number))
                : "brak";
            await FollowupAsync(
                $"❌ W **aktywnym** sezonie „{season.Name}” nie ma kolejki **{round}**.\nDostępne: {available}.",
                ephemeral: true);
            return;
        }

        try
        {
            var (target, err) = AdminTableChannelHelper.Resolve(Context.Guild, kanał, Context.Channel);
            if (target == null)
            {
                await FollowupAsync($"❌ {err}", ephemeral: true);
                return;
            }

            using var imageStream = await CreateRoundTablePngStreamAsync(roundEntity.Id);
            var roundLabel = RoundHelper.GetRoundLabel(round);
            await target.SendFileAsync(
                imageStream,
                $"tabela_kolejki_{round}_{DateTime.Now:yyyyMMdd}.png",
                text: $"📊 **Tabela {roundLabel} — {season.Name}**");

            await FollowupAsync($"✅ PNG wysłany na {MentionUtils.MentionChannel(target.Id)}.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error posting round table PNG");
            await FollowupAsync($"❌ Wystąpił błąd: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("wyniki-gracza", "Wyświetl wyniki konkretnego gracza (tylko dla adminów)")]
    public async Task ShowPlayerResultsAsync(IUser discordUser)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondWithErrorAsync("Nie masz uprawnień do użycia tej komendy.");
            return;
        }

        await DeferAsync(ephemeral: true);

        var player = await _playerRepository.GetByDiscordUserIdAsync(discordUser.Id);
        if (player == null)
        {
            await FollowupAsync("❌ Gracz nie został znaleziony w bazie danych.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        try
        {
            var playerScores = player.PlayerScores
                .Where(s => s.Prediction?.Match?.Round?.SeasonId == season.Id)
                .OrderBy(s => s.Prediction?.Match?.StartTime)
                .ToList();

            if (!playerScores.Any())
            {
                var dn = DiscordDisplayNameHelper.ForDisplay(discordUser);
                await FollowupAsync($"❌ Gracz {dn} nie ma jeszcze żadnych wyników w obecnym sezonie.", ephemeral: true);
                return;
            }

            var displayName = DiscordDisplayNameHelper.ForDisplay(discordUser);
            var embed = new EmbedBuilder()
                .WithTitle($"Wyniki gracza: {displayName}")
                .WithDescription($"**Sezon:** {season.Name}\n**Suma punktów:** {playerScores.Sum(s => s.Points)}")
                .WithColor(Color.Blue)
                .WithCurrentTimestamp();

            foreach (var score in playerScores)
            {
                var match = score.Prediction?.Match;
                if (match != null)
                {
                    embed.AddField(
                        $"{match.HomeTeam} vs {match.AwayTeam}",
                        $"Typ: {score.Prediction?.HomeTip}:{score.Prediction?.AwayTip}\nWynik: {match.HomeScore}:{match.AwayScore}\nPunkty: **{score.Points}**",
                        true);
                }
            }

            await FollowupAsync(embed: embed.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching player results");
            await FollowupAsync($"❌ Wystąpił błąd: {ex.Message}", ephemeral: true);
        }
    }

    private async Task<MemoryStream> CreateSeasonTablePngStreamAsync(int seasonId)
    {
        var season = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(seasonId)
            ?? throw new InvalidOperationException("Sezon nie został znaleziony.");
        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        var bytes = _tableGenerator.GenerateSeasonTable(season, players);
        return new MemoryStream(bytes, writable: false);
    }

    private async Task<MemoryStream> CreateRoundTablePngStreamAsync(int roundId)
    {
        var round = await _roundRepository.GetByIdAsync(roundId)
            ?? throw new InvalidOperationException("Kolejka nie została znaleziona.");
        var season = await _seasonRepository.GetByIdAsync(round.SeasonId)
            ?? throw new InvalidOperationException("Sezon nie został znaleziony.");
        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        var bytes = _tableGenerator.GenerateRoundTable(season, round, players);
        return new MemoryStream(bytes, writable: false);
    }

    [SlashCommand("admin-pkt-meczu", "PNG: punkty graczy w wybranym meczu i różnica względem poprzedniego zakończonego meczu w sezonie")]
    public async Task AdminMatchPointsDeltaAsync(
        [Summary(description: "Mecz z listy (tylko zakończony z wynikiem)")]
        [Autocomplete(typeof(AdminMatchChoiceAutocompleteHandler))]
        string mecz)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("Brak uprawnień.", ephemeral: true);
            return;
        }

        if (!int.TryParse(mecz, out var matchId))
        {
            await RespondAsync("Wybierz mecz z autouzupełniania.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var match = await _matchRepository.GetByIdAsync(matchId, includeRound: true);
        if (match?.Round == null)
        {
            await FollowupAsync("Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        var active = await _seasonRepository.GetActiveSeasonAsync();
        if (active == null || active.Id != match.Round.SeasonId)
        {
            await FollowupAsync("Mecz musi należeć do aktywnego sezonu.", ephemeral: true);
            return;
        }

        if (!StandingsAnalyticsGenerator.IsFinishedWithScore(match))
        {
            await FollowupAsync("Tabela jest dostępna tylko dla meczów zakończonych z wpisanym wynikiem.", ephemeral: true);
            return;
        }

        var seasonFull = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(active.Id);
        if (seasonFull == null)
        {
            await FollowupAsync("Nie udało się wczytać sezonu.", ephemeral: true);
            return;
        }

        var ordered = StandingsAnalyticsGenerator.OrderFinishedMatches(seasonFull);
        var prev = StandingsAnalyticsGenerator.GetPreviousFinishedMatch(ordered, match);
        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        var rows = _analyticsGenerator.BuildMatchDeltaRows(match, prev, players);
        var title = $"{match.HomeTeam} vs {match.AwayTeam}";
        var bytes = _analyticsGenerator.GenerateMatchDeltaTablePng(seasonFull.Name, title, rows,
            $"Poprzedni mecz w kolejności sezonu: {(prev == null ? "brak" : $"{prev.HomeTeam} vs {prev.AwayTeam}")}");
        using var stream = new MemoryStream(bytes, writable: false);
        await FollowupWithFileAsync(stream, $"pkt_meczu_{matchId}.png", text: $"Punkty w meczu — {title}", ephemeral: true);
    }

    [SlashCommand("admin-pkt-kolejki", "PNG: suma punktów graczy w kolejce i różnica względem poprzedniej kolejki")]
    public async Task AdminRoundPointsDeltaAsync([Summary(description: "Numer kolejki (1–18)")] int numer)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("Brak uprawnień.", ephemeral: true);
            return;
        }

        if (!RoundHelper.IsValidRoundNumber(numer))
        {
            await RespondAsync($"Numer kolejki musi być z zakresu 1–18 (podano: {numer}).", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var round = await _roundRepository.GetByNumberAsync(season.Id, numer);
        if (round == null)
        {
            await FollowupAsync($"W aktywnym sezonie nie ma kolejki {numer}.", ephemeral: true);
            return;
        }

        var seasonFull = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(season.Id) ?? season;
        var prevRound = StandingsAnalyticsGenerator.GetPreviousRound(seasonFull, round);
        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        var rows = _analyticsGenerator.BuildRoundDeltaRows(round, prevRound, players);
        var roundLabel = RoundHelper.GetRoundLabel(numer);
        var bytes = _analyticsGenerator.GenerateRoundDeltaTablePng(seasonFull.Name, roundLabel, rows,
            prevRound == null ? "Brak poprzedniej kolejki — kolumna Δ pokazuje „—”." : $"Porównanie z {RoundHelper.GetRoundLabel(prevRound.Number)}.");
        using var stream = new MemoryStream(bytes, writable: false);
        await FollowupWithFileAsync(stream, $"pkt_kolejki_{numer}.png", text: $"Punkty w kolejce — {roundLabel}", ephemeral: true);
    }

    [SlashCommand("admin-wykres-punktow", "PNG: skumulowane punkty — tylko cały aktywny sezon (wszystkie zakończone mecze), kolejki zaznaczone")]
    public async Task AdminSeasonPointsChartAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("Brak uprawnień.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var seasonFull = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(season.Id) ?? season;
        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        var bytes = _analyticsGenerator.GenerateSeasonCumulativeChartPng(seasonFull, players);
        using var stream = new MemoryStream(bytes, writable: false);
        await FollowupWithFileAsync(stream, $"wykres_punktow_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png",
            text: $"Wykres punktów (cały sezon) — {seasonFull.Name}", ephemeral: true);
    }

    [SlashCommand("admin-rozklad-punktow", "PNG: ile razy każdy gracz dostał daną liczbę punktów w meczu — cały aktywny sezon")]
    public async Task AdminSeasonPointsHistogramAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("Brak uprawnień.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var seasonFull = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(season.Id) ?? season;
        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        var bytes = _analyticsGenerator.GenerateSeasonPointsHistogramPng(seasonFull, players);
        using var stream = new MemoryStream(bytes, writable: false);
        await FollowupWithFileAsync(stream, $"rozklad_punktow_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png",
            text: $"Rozkład punktów (cały sezon) — {seasonFull.Name}", ephemeral: true);
    }
}
