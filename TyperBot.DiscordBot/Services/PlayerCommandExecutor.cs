using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.DiscordBot.Models;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Services;

/// <summary>
/// Wspólna logika komend gracza — slash i panel na kanale typowanie (po <see cref="SocketInteractionContext.DeferAsync"/>).
/// </summary>
public sealed class PlayerCommandExecutor
{
    private readonly ILogger<PlayerCommandExecutor> _logger;
    private readonly DiscordSettings _settings;
    private readonly IPlayerRepository _playerRepository;
    private readonly IMatchRepository _matchRepository;
    private readonly IPredictionRepository _predictionRepository;
    private readonly TableGenerator _tableGenerator;
    private readonly StandingsAnalyticsGenerator _analyticsGenerator;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRoundRepository _roundRepository;

    public PlayerCommandExecutor(
        ILogger<PlayerCommandExecutor> logger,
        IOptions<DiscordSettings> settings,
        IPlayerRepository playerRepository,
        IMatchRepository matchRepository,
        IPredictionRepository predictionRepository,
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
        _tableGenerator = tableGenerator;
        _analyticsGenerator = analyticsGenerator;
        _seasonRepository = seasonRepository;
        _roundRepository = roundRepository;
    }

    public async Task ExecuteMyPredictionsAsync(SocketInteractionContext ctx, int? round)
    {
        var player = await _playerRepository.GetByDiscordUserIdAsync(ctx.User.Id);
        if (player == null)
        {
            await ctx.Interaction.FollowupAsync("❌ Nie złożyłeś jeszcze żadnych typów.", ephemeral: true);
            return;
        }

        IEnumerable<Prediction> predictions;

        if (round.HasValue)
        {
            var season = await _seasonRepository.GetActiveSeasonAsync();
            if (season == null)
            {
                await ctx.Interaction.FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
                return;
            }

            var roundEntity = season.FindRoundByNumber(round.Value)
                ?? await _roundRepository.GetByNumberAsync(season.Id, round.Value);
            if (roundEntity == null)
            {
                var available = season.Rounds.Count > 0
                    ? string.Join(", ", season.Rounds.OrderBy(r => r.Number).Select(r => r.Number))
                    : "brak";
                await ctx.Interaction.FollowupAsync(
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
            var season = await _seasonRepository.GetActiveSeasonAsync();
            if (season == null)
            {
                await ctx.Interaction.FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
                return;
            }

            var matchIds = season.Rounds
                .SelectMany(r => r.Matches)
                .Select(m => m.Id)
                .Distinct()
                .ToList();
            predictions = await _predictionRepository.GetByPlayerIdAndMatchIdsAsync(player.Id, matchIds);
        }

        var predictionsList = predictions.ToList();

        if (!predictionsList.Any())
        {
            var message = round.HasValue
                ? $"❌ Nie masz typów dla kolejki {round.Value}."
                : "❌ Nie masz typów w aktywnym sezonie.";
            await ctx.Interaction.FollowupAsync(message, ephemeral: true);
            return;
        }

        var roundNumber = round;
        var embed = new EmbedBuilder()
            .WithTitle(roundNumber.HasValue ? $"📝 Moje typy — kolejka {roundNumber.Value}" : "📝 Moje typy — aktywny sezon")
            .WithColor(Color.Blue)
            .WithCurrentTimestamp();

        var matchesWithPreds = predictionsList
            .Where(p => p.Match?.Round != null)
            .Select(p => (Match: p.Match!, Pred: p))
            .ToList();

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
                        MatchStatus.Finished => "✅",
                        MatchStatus.InProgress => "▶️",
                        MatchStatus.Cancelled => "❌",
                        _ => "⏰"
                    };

                    var result = "";
                    if (m.Match.Status == MatchStatus.Finished)
                    {
                        if (m.Match.HomeScore.HasValue && m.Match.AwayScore.HasValue)
                            result = $"\n**Wynik:** `{m.Match.HomeScore.Value}:{m.Match.AwayScore.Value}`";
                        else
                            result = "\n**Wynik:** *Brak*";

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
                    else if (m.Match.Status == MatchStatus.Cancelled)
                    {
                        result = "\n*Mecz odwołany*";
                    }

                    return $"{statusIcon} **{m.Match.HomeTeam} vs {m.Match.AwayTeam}**\n" +
                           $"`{localTime:yyyy-MM-dd HH:mm}` | Twój typ: **`{m.Pred.HomeTip}:{m.Pred.AwayTip}`**{result}";
                }));

            if (matchList.Length > 1024)
                matchList = matchList[..1020] + "...";

            embed.AddField($"📋 {roundDesc}", matchList, inline: false);
        }

        var totalFinished = matchesWithPreds.Count(m => m.Match.Status == MatchStatus.Finished);
        var totalPoints = predictionsList
            .Where(p => p.PlayerScore != null)
            .Sum(p => p.PlayerScore!.Points);

        if (totalFinished > 0)
            embed.WithFooter($"Zdobyte punkty: {totalPoints} | Zakończonych meczów: {totalFinished}/{predictionsList.Count}");
        else
            embed.WithFooter($"Liczba typów: {predictionsList.Count}");

        await ctx.Interaction.FollowupAsync(embed: embed.Build(), ephemeral: true);
    }

    public async Task ExecuteRoundTableAsync(SocketInteractionContext ctx, int round, string userName)
    {
        if (!RoundHelper.IsValidRoundNumber(round))
        {
            await ctx.Interaction.FollowupAsync($"❌ Numer kolejki musi być z zakresu 1–18 (podano: {round}).", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await ctx.Interaction.FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var roundEntity = season.FindRoundByNumber(round)
            ?? await _roundRepository.GetByNumberAsync(season.Id, round);
        if (roundEntity == null)
        {
            var available = season.Rounds.Count > 0
                ? string.Join(", ", season.Rounds.OrderBy(r => r.Number).Select(r => r.Number))
                : "brak";
            await ctx.Interaction.FollowupAsync(
                $"❌ W aktywnym sezonie nie ma kolejki **{round}**. Dostępne: {available}.",
                ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await ctx.Interaction.FollowupAsync("❌ Brak aktywnych graczy.", ephemeral: true);
            return;
        }

        try
        {
            var roundFull = await _roundRepository.GetByIdAsync(roundEntity.Id) ?? roundEntity;
            var seasonForTable = await _seasonRepository.GetByIdAsync(season.Id) ?? season;
            var bytes = _tableGenerator.GenerateRoundTable(seasonForTable, roundFull, players);
            await using var stream = new MemoryStream(bytes, writable: false);
            var label = RoundHelper.GetRoundLabel(round);
            await ctx.Interaction.FollowupWithFileAsync(stream, $"tabela_kolejki_{round}.png", text: $"**{season.Name}** — {label}", ephemeral: true);
            _logger.LogInformation("Round {Round} table PNG generated by {User}", round, userName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate round table");
            await ctx.Interaction.FollowupAsync("❌ Nie udało się wygenerować tabeli kolejki.", ephemeral: true);
        }
    }

    public async Task ExecuteSeasonTableAsync(SocketInteractionContext ctx, string userName)
    {
        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await ctx.Interaction.FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await ctx.Interaction.FollowupAsync("❌ Brak aktywnych graczy.", ephemeral: true);
            return;
        }

        try
        {
            var seasonFull = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(season.Id) ?? season;
            var bytes = _tableGenerator.GenerateSeasonTable(seasonFull, players);
            await using var stream = new MemoryStream(bytes, writable: false);
            await ctx.Interaction.FollowupWithFileAsync(stream, "tabela_sezonu.png", text: $"**Sezon:** {season.Name}", ephemeral: true);
            _logger.LogInformation("Season table PNG generated by {User}", userName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate season table");
            await ctx.Interaction.FollowupAsync("❌ Nie udało się wygenerować tabeli sezonu.", ephemeral: true);
        }
    }

    public async Task ExecutePlayerMatchPointsDeltaAsync(SocketInteractionContext ctx, int matchId)
    {
        var match = await _matchRepository.GetByIdAsync(matchId, includeRound: true);
        if (match?.Round == null)
        {
            await ctx.Interaction.FollowupAsync("Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        var active = await _seasonRepository.GetActiveSeasonAsync();
        if (active == null || active.Id != match.Round.SeasonId)
        {
            await ctx.Interaction.FollowupAsync("Mecz musi być z aktywnego sezonu.", ephemeral: true);
            return;
        }

        if (!StandingsAnalyticsGenerator.IsFinishedWithScore(match))
        {
            await ctx.Interaction.FollowupAsync("Dostępne tylko dla meczów zakończonych z wynikiem.", ephemeral: true);
            return;
        }

        var seasonFull = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(active.Id);
        if (seasonFull == null)
        {
            await ctx.Interaction.FollowupAsync("Nie udało się wczytać sezonu.", ephemeral: true);
            return;
        }

        var ordered = StandingsAnalyticsGenerator.OrderFinishedMatches(seasonFull);
        var prev = StandingsAnalyticsGenerator.GetPreviousFinishedMatch(ordered, match);
        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        var rows = _analyticsGenerator.BuildMatchDeltaRows(match, prev, players);
        var title = $"{match.HomeTeam} vs {match.AwayTeam}";
        var bytes = _analyticsGenerator.GenerateMatchDeltaTablePng(seasonFull.Name, title, rows,
            $"Poprzedni mecz: {(prev == null ? "brak" : $"{prev.HomeTeam} vs {prev.AwayTeam}")}");
        await using var stream = new MemoryStream(bytes, writable: false);
        await ctx.Interaction.FollowupWithFileAsync(stream, $"pkt_meczu_{matchId}.png", text: $"Punkty w meczu — {title}", ephemeral: true);
    }

    public async Task ExecutePlayerRoundPointsDeltaAsync(SocketInteractionContext ctx, int numer)
    {
        if (!RoundHelper.IsValidRoundNumber(numer))
        {
            await ctx.Interaction.FollowupAsync($"Numer kolejki 1–18 (podano: {numer}).", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await ctx.Interaction.FollowupAsync("Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var round = await _roundRepository.GetByNumberAsync(season.Id, numer);
        if (round == null)
        {
            await ctx.Interaction.FollowupAsync($"Brak kolejki {numer} w aktywnym sezonie.", ephemeral: true);
            return;
        }

        var seasonFull = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(season.Id) ?? season;
        var prevRound = StandingsAnalyticsGenerator.GetPreviousRound(seasonFull, round);
        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        var rows = _analyticsGenerator.BuildRoundDeltaRows(round, prevRound, players);
        var roundLabel = RoundHelper.GetRoundLabel(numer);
        var bytes = _analyticsGenerator.GenerateRoundDeltaTablePng(seasonFull.Name, roundLabel, rows,
            prevRound == null ? "Brak poprzedniej kolejki." : $"vs {RoundHelper.GetRoundLabel(prevRound.Number)}.");
        await using var stream = new MemoryStream(bytes, writable: false);
        await ctx.Interaction.FollowupWithFileAsync(stream, $"pkt_kolejki_{numer}.png", text: $"Punkty w kolejce — {roundLabel}", ephemeral: true);
    }

    public async Task ExecuteSeasonChartAsync(SocketInteractionContext ctx)
    {
        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await ctx.Interaction.FollowupAsync("Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var seasonFull = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(season.Id) ?? season;
        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        var bytes = _analyticsGenerator.GenerateSeasonCumulativeChartPng(seasonFull, players);
        await using var stream = new MemoryStream(bytes, writable: false);
        await ctx.Interaction.FollowupWithFileAsync(stream, $"wykres_punktow_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png",
            text: $"Wykres punktów (cały sezon) — {seasonFull.Name}", ephemeral: true);
    }

    public async Task ExecuteSeasonPointsHistogramAsync(SocketInteractionContext ctx)
    {
        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await ctx.Interaction.FollowupAsync("Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var seasonFull = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(season.Id) ?? season;
        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        var bytes = _analyticsGenerator.GenerateSeasonPointsHistogramPng(seasonFull, players);
        await using var stream = new MemoryStream(bytes, writable: false);
        await ctx.Interaction.FollowupWithFileAsync(stream, $"rozklad_punktow_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png",
            text: $"Rozkład punktów (cały sezon) — {seasonFull.Name}", ephemeral: true);
    }

    public async Task ExecutePlayerSeasonPointsPieAsync(SocketInteractionContext ctx)
    {
        var player = await _playerRepository.GetByDiscordUserIdAsync(ctx.User.Id);
        if (player == null)
        {
            await ctx.Interaction.FollowupAsync("❌ Nie masz jeszcze konta w typerze.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await ctx.Interaction.FollowupAsync("Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var seasonFull = await _seasonRepository.GetByIdWithRoundsAndMatchesAsync(season.Id) ?? season;
        var bytes = _analyticsGenerator.GeneratePlayerSeasonPointsPiePng(seasonFull, player);
        await using var stream = new MemoryStream(bytes, writable: false);
        await ctx.Interaction.FollowupWithFileAsync(stream, $"kolowy_rozklad_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png",
            text: $"Twój rozkład punktów (kołowo, cały sezon) — {seasonFull.Name}", ephemeral: true);
    }
}
