using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.DiscordBot.Autocomplete;
using TyperBot.DiscordBot.Models;
using TyperBot.DiscordBot.Services;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Modules;

public class PlayerModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger<PlayerModule> _logger;
    private readonly DiscordSettings _settings;
    private readonly IPlayerRepository _playerRepository;
    private readonly IMatchRepository _matchRepository;
    private readonly IPredictionRepository _predictionRepository;
    private readonly RoundManager _roundManager;
    private readonly TableGenerator _tableGenerator;
    private readonly StandingsAnalyticsGenerator _analyticsGenerator;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRoundRepository _roundRepository;

    public PlayerModule(
        ILogger<PlayerModule> logger,
        IOptions<DiscordSettings> settings,
        IPlayerRepository playerRepository,
        IMatchRepository matchRepository,
        IPredictionRepository predictionRepository,
        RoundManager roundManager,
        TableGenerator tableGenerator,
        StandingsAnalyticsGenerator analyticsGenerator,
        ISeasonRepository seasonRepository,
        IRoundRepository roundRepository)
    {
        _logger = logger;
        _settings = settings.Value;
        _playerRepository = playerRepository;
        _matchRepository = matchRepository;
        _predictionRepository = predictionRepository;
        _roundManager = roundManager;
        _tableGenerator = tableGenerator;
        _analyticsGenerator = analyticsGenerator;
        _seasonRepository = seasonRepository;
        _roundRepository = roundRepository;
    }

    [SlashCommand("moje-typy", "Wyświetl swoje typy dla nadchodzących meczów lub konkretnej kolejki")]
    public async Task MyPredictionsAsync(
        [Summary(description: "Numer kolejki (opcjonalne, pokazuje wszystkie nadchodzące jeśli nie podano)")] int? round = null)
    {
        await DeferAsync(ephemeral: true);

        var player = await _playerRepository.GetByDiscordUserIdAsync(Context.User.Id);
        if (player == null)
        {
            await FollowupAsync("❌ Nie złożyłeś jeszcze żadnych typów.", ephemeral: true);
            return;
        }

        IEnumerable<Domain.Entities.Prediction> predictions;
        
        if (round.HasValue)
        {
            // Show all predictions for a specific round (any status)
            var season = await _seasonRepository.GetActiveSeasonAsync();
            if (season == null)
            {
                await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
                return;
            }
            var roundEntity = season.FindRoundByNumber(round.Value)
                ?? await _roundRepository.GetByNumberAsync(season.Id, round.Value);
            if (roundEntity == null)
            {
                var available = season.Rounds.Count > 0
                    ? string.Join(", ", season.Rounds.OrderBy(r => r.Number).Select(r => r.Number))
                    : "brak";
                await FollowupAsync(
                    $"❌ W aktywnym sezonie nie ma kolejki **{round.Value}**. Dostępne: {available}.",
                    ephemeral: true);
                return;
            }
            var roundMatches = await _matchRepository.GetByRoundIdAsync(roundEntity.Id);
            var matchIds = roundMatches.Select(m => m.Id).ToList();
            predictions = await _predictionRepository.GetByPlayerIdAndMatchIdsAsync(player.Id, matchIds);
        }
        else
        {
            // Show upcoming predictions
            var upcomingMatches = await _matchRepository.GetUpcomingMatchesAsync();
            var matchIds = upcomingMatches.Select(m => m.Id).ToList();
            predictions = await _predictionRepository.GetByPlayerIdAndMatchIdsAsync(player.Id, matchIds);
        }
        
        var predictionsList = predictions.ToList();

        if (!predictionsList.Any())
        {
            var message = round.HasValue 
                ? $"❌ Nie masz typów dla kolejki {round.Value}."
                : "❌ Nie masz typów dla nadchodzących meczów.";
            await FollowupAsync(message, ephemeral: true);
            return;
        }

        var roundNumber = round;
        var embed = new EmbedBuilder()
            .WithTitle(roundNumber.HasValue ? $"📝 Moje Typy - Kolejka {roundNumber.Value}" : "📝 Moje Typy - Nadchodzące")
            .WithColor(Color.Blue)
            .WithCurrentTimestamp();

        // Match + Round already loaded via GetByPlayerIdAndMatchIdsAsync (no per-prediction DB round-trips)
        var matchesWithPreds = predictionsList
            .Where(p => p.Match?.Round != null)
            .Select(p => (Match: p.Match!, Pred: p))
            .ToList();

        // Group by round for better organization
        var grouped = matchesWithPreds
            .GroupBy(m => m.Match.RoundId)
            .OrderBy(g => g.First().Match.Round?.Number ?? 0);

        foreach (var roundGroup in grouped)
        {
            var roundEntity = roundGroup.First().Match.Round;
            var rndNum = roundEntity?.Number ?? 0;
            var roundDesc = roundEntity?.Description ?? $"Kolejka {rndNum}";
            
            var matchList = string.Join("\n\n", roundGroup
                .OrderBy(m => m.Match.StartTime)
                .Select(m =>
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
                    var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.Match.StartTime.UtcDateTime, tz);
                    
                    var statusIcon = m.Match.Status switch
                    {
                        Domain.Enums.MatchStatus.Finished => "✅",
                        Domain.Enums.MatchStatus.InProgress => "▶️",
                        Domain.Enums.MatchStatus.Cancelled => "❌",
                        _ => "⏰"
                    };
                    
                    var result = "";
                    if (m.Match.Status == Domain.Enums.MatchStatus.Finished)
                    {
                        if (m.Match.HomeScore.HasValue && m.Match.AwayScore.HasValue)
                        {
                            result = $"\n**Wynik:** `{m.Match.HomeScore.Value}:{m.Match.AwayScore.Value}`";
                        }
                        else
                        {
                            result = "\n**Wynik:** *Brak*";
                        }
                        
                        // Show points earned
                        var score = m.Pred.PlayerScore;
                        if (score != null)
                        {
                            var pointsDesc = score.Bucket switch
                            {
                                Bucket.P35 or Bucket.P50 => "🎯 Celny wynik",
                                _ when score.Points > 0 => "✓ Poprawny zwycięzca",
                                _ => "✗ Brak punktów"
                            };
                            result += $" → {pointsDesc} **+{score.Points}pkt**";
                        }
                    }
                    else if (m.Match.Status == Domain.Enums.MatchStatus.Cancelled)
                    {
                        result = "\n*Mecz odwołany*";
                    }
                    
                    return $"{statusIcon} **{m.Match.HomeTeam} vs {m.Match.AwayTeam}**\n" +
                           $"`{localTime:yyyy-MM-dd HH:mm}` | Twój typ: **`{m.Pred.HomeTip}:{m.Pred.AwayTip}`**{result}";
                }));
            
            // Discord limit: 1024 characters per field value
            if (matchList.Length > 1024)
            {
                matchList = matchList.Substring(0, 1020) + "...";
            }
            
            embed.AddField($"📋 {roundDesc}", matchList, inline: false);
        }
        
        var totalFinished = matchesWithPreds.Count(m => m.Match.Status == Domain.Enums.MatchStatus.Finished);
        var totalPoints = predictionsList
            .Where(p => p.PlayerScore != null)
            .Sum(p => p.PlayerScore!.Points);
        
        if (totalFinished > 0)
        {
            embed.WithFooter($"Zdobyte punkty: {totalPoints} | Zakończonych meczów: {totalFinished}/{predictionsList.Count}");
        }
        else
        {
            embed.WithFooter($"Liczba typów: {predictionsList.Count}");
        }

        await FollowupAsync(embed: embed.Build(), ephemeral: true);
    }

    [SlashCommand("tabela-kolejki", "Wyświetl tabelę dla konkretnej kolejki")]
    public async Task RoundTableAsync([Summary(description: "Numer kolejki")] int round)
    {
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
                $"❌ W aktywnym sezonie nie ma kolejki **{round}**. Dostępne: {available}.",
                ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await FollowupAsync("❌ Brak aktywnych graczy.", ephemeral: true);
            return;
        }

        try
        {
            var roundFull = await _roundRepository.GetByIdAsync(roundEntity.Id) ?? roundEntity;
            var seasonForTable = await _seasonRepository.GetByIdAsync(season.Id) ?? season;
            var bytes = _tableGenerator.GenerateRoundTable(seasonForTable, roundFull, players);
            using var stream = new MemoryStream(bytes, writable: false);
            var label = Application.Services.RoundHelper.GetRoundLabel(round);
            await FollowupWithFileAsync(
                stream,
                $"tabela_kolejki_{round}.png",
                text: $"**{season.Name}** — {label}",
                ephemeral: true);
            _logger.LogInformation("Round {Round} table PNG generated by {User}", round, Context.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate round table");
            await FollowupAsync("❌ Nie udało się wygenerować tabeli kolejki.", ephemeral: true);
        }
    }

    [SlashCommand("tabela-sezonu", "Wyświetl ogólną tabelę sezonu")]
    public async Task SeasonTableAsync()
    {
        await DeferAsync(ephemeral: true);

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await FollowupAsync("❌ Brak aktywnych graczy.", ephemeral: true);
            return;
        }

        try
        {
            var seasonFull = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(season.Id) ?? season;
            var bytes = _tableGenerator.GenerateSeasonTable(seasonFull, players);
            using var stream = new MemoryStream(bytes, writable: false);
            await FollowupWithFileAsync(stream, "tabela_sezonu.png", text: $"**Sezon:** {season.Name}", ephemeral: true);
            _logger.LogInformation("Season table PNG generated by {User}", Context.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate season table");
            await FollowupAsync("❌ Nie udało się wygenerować tabeli sezonu.", ephemeral: true);
        }
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

        var match = await _matchRepository.GetByIdAsync(matchId, includeRound: true);
        if (match?.Round == null)
        {
            await FollowupAsync("Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        var active = await _seasonRepository.GetActiveSeasonAsync();
        if (active == null || active.Id != match.Round.SeasonId)
        {
            await FollowupAsync("Mecz musi być z aktywnego sezonu.", ephemeral: true);
            return;
        }

        if (!StandingsAnalyticsGenerator.IsFinishedWithScore(match))
        {
            await FollowupAsync("Dostępne tylko dla meczów zakończonych z wynikiem.", ephemeral: true);
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
        var rows = StandingsAnalyticsGenerator.BuildMatchDeltaRows(match, prev, players);
        var title = $"{match.HomeTeam} vs {match.AwayTeam}";
        var bytes = _analyticsGenerator.GenerateMatchDeltaTablePng(seasonFull.Name, title, rows,
            $"Poprzedni mecz: {(prev == null ? "brak" : $"{prev.HomeTeam} vs {prev.AwayTeam}")}");
        using var stream = new MemoryStream(bytes, writable: false);
        await FollowupWithFileAsync(stream, $"pkt_meczu_{matchId}.png", text: $"Punkty w meczu — {title}", ephemeral: true);
    }

    [SlashCommand("pkt-kolejki", "PNG: punkty w kolejce i różnica względem poprzedniej kolejki")]
    public async Task PlayerRoundPointsDeltaAsync([Summary(description: "Numer kolejki 1–18")] int numer)
    {
        await DeferAsync(ephemeral: true);

        if (!RoundHelper.IsValidRoundNumber(numer))
        {
            await FollowupAsync($"Numer kolejki 1–18 (podano: {numer}).", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var round = await _roundRepository.GetByNumberAsync(season.Id, numer);
        if (round == null)
        {
            await FollowupAsync($"Brak kolejki {numer} w aktywnym sezonie.", ephemeral: true);
            return;
        }

        var seasonFull = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(season.Id) ?? season;
        var prevRound = StandingsAnalyticsGenerator.GetPreviousRound(seasonFull, round);
        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        var rows = StandingsAnalyticsGenerator.BuildRoundDeltaRows(round, prevRound, players);
        var roundLabel = RoundHelper.GetRoundLabel(numer);
        var bytes = _analyticsGenerator.GenerateRoundDeltaTablePng(seasonFull.Name, roundLabel, rows,
            prevRound == null ? "Brak poprzedniej kolejki." : $"vs {RoundHelper.GetRoundLabel(prevRound.Number)}.");
        using var stream = new MemoryStream(bytes, writable: false);
        await FollowupWithFileAsync(stream, $"pkt_kolejki_{numer}.png", text: $"Punkty w kolejce — {roundLabel}", ephemeral: true);
    }

    [SlashCommand("wykres-punktow", "PNG: skumulowane punkty — tylko cały aktywny sezon (linie wg graczy, kolejki)")]
    public async Task PlayerSeasonChartAsync()
    {
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

    [SlashCommand("rozklad-punktow", "PNG: ile razy dostałeś daną liczbę punktów w meczu — cały aktywny sezon (wszyscy gracze)")]
    public async Task PlayerSeasonPointsHistogramAsync()
    {
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

    [SlashCommand("kolowy-rozklad-punktow",
        "PNG: Twój wykres kołowy — ile razy która liczba punktów w meczu (tylko Ty, cały aktywny sezon)")]
    public async Task PlayerSeasonPointsPieAsync()
    {
        await DeferAsync(ephemeral: true);

        var player = await _playerRepository.GetByDiscordUserIdAsync(Context.User.Id);
        if (player == null)
        {
            await FollowupAsync("❌ Nie masz jeszcze konta w typerze.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var seasonFull = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(season.Id) ?? season;
        var bytes = _analyticsGenerator.GeneratePlayerSeasonPointsPiePng(seasonFull, player);
        using var stream = new MemoryStream(bytes, writable: false);
        await FollowupWithFileAsync(stream, $"kolowy_rozklad_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png",
            text: $"Twój rozkład punktów (kołowo, cały sezon) — {seasonFull.Name}", ephemeral: true);
    }
}

