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

public class PlayerModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger<PlayerModule> _logger;
    private readonly DiscordSettings _settings;
    private readonly IPlayerRepository _playerRepository;
    private readonly IMatchRepository _matchRepository;
    private readonly IPredictionRepository _predictionRepository;
    private readonly RoundManager _roundManager;
    private readonly TableGenerator _tableGenerator;
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
            // Calculate scores for the round
            var roundMatches = (await _matchRepository.GetByRoundIdAsync(roundEntity.Id)).Select(m => m.Id).ToList();
            var allScores = new List<(int PlayerId, string PlayerName, int TotalPoints, int PredictionsCount, int ExactScores, int CorrectWinners)>();
            
            foreach (var player in players)
            {
                // "Typ" = wszystkie ważne typy w meczach tej kolejki (nawet przed wynikiem).
                // Pkt / Cel / Wyg tylko z meczów, które mają już rozliczenie (PlayerScore).
                var predsInRound = player.Predictions
                    .Where(p => roundMatches.Contains(p.MatchId) && p.IsValid)
                    .ToList();
                var scored = predsInRound.Where(p => p.PlayerScore != null).Select(p => p.PlayerScore!).ToList();

                var totalPoints = scored.Sum(s => s.Points);
                var exactScores = scored.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50);
                var correctWinners = scored.Count(s => s.Points > 0);
                var typCount = predsInRound.Count;

                allScores.Add((player.Id, player.DiscordUsername, totalPoints, typCount, exactScores, correctWinners));
            }

            var sortedScores = allScores
                .OrderByDescending(s => s.TotalPoints)
                .ThenByDescending(s => s.PredictionsCount)
                .ToList();

            var anyTipInRound = sortedScores.Sum(s => s.PredictionsCount) > 0;
            var desc = $"**Sezon**: {season.Name}\n**Kolejka**: {roundEntity.Description ?? $"Kolejka {round}"}\n\n" +
                       "_Pkt — suma po meczach z wpisanym wynikiem. Typ — wszystkie typy w **tej** kolejce._";
            if (!anyTipInRound)
                desc += "\n\n⚠️ _Brak typów dla tej kolejki w bazie. Jeśli typowałeś inny numer kolejki, podaj go w komendzie (`round`)._";

            var embed = new EmbedBuilder()
                .WithTitle($"📊 Tabela Kolejki {round}")
                .WithDescription(desc)
                .WithColor(Color.Blue)
                .WithCurrentTimestamp();

            // Build table using code block for monospace alignment
            var table = "```\n";
            table += "Poz  Gracz                    Pkt   Typ   Cel   Wyg\n";
            table += "═══════════════════════════════════════════════════\n";
            
            for (int i = 0; i < sortedScores.Count; i++)
            {
                var score = sortedScores[i];
                var player = players.FirstOrDefault(p => p.Id == score.PlayerId);
                var playerName = player?.DiscordUsername ?? "Unknown";
                
                // Truncate long names
                if (playerName.Length > 22)
                    playerName = playerName.Substring(0, 19) + "...";
                
                table += $"{i + 1,3}  {playerName,-22}  {score.TotalPoints,3}  {score.PredictionsCount,4}  {score.ExactScores,4}  {score.CorrectWinners,4}\n";
            }
            
            table += "```";
            embed.AddField("Tabela punktowa", table, false);

            embed.WithFooter("Typ = typy w tej kolejce | Pkt/Cel/Wyg = tylko mecze z wynikiem");

            await FollowupAsync(embed: embed.Build(), ephemeral: true);
            _logger.LogInformation("Round {Round} table generated by {User}", round, Context.User.Username);
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
            var rounds = await _roundRepository.GetBySeasonIdAsync(season.Id);
            var seasonMatchIds = new HashSet<int>();
            foreach (var r in rounds)
            {
                foreach (var m in await _matchRepository.GetByRoundIdAsync(r.Id))
                    seasonMatchIds.Add(m.Id);
            }

            var allScores = new List<(string PlayerName, int TotalPoints, int PredictionsCount, int ExactScores, int CorrectWinners)>();
            
            foreach (var player in players)
            {
                var predsInSeason = player.Predictions
                    .Where(p => seasonMatchIds.Contains(p.MatchId) && p.IsValid)
                    .ToList();
                var scored = predsInSeason.Where(p => p.PlayerScore != null).Select(p => p.PlayerScore!).ToList();

                var totalPoints = scored.Sum(s => s.Points);
                var exactScores = scored.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50);
                var correctWinners = scored.Count(s => s.Points > 0);
                var typCount = predsInSeason.Count;

                allScores.Add((player.DiscordUsername, totalPoints, typCount, exactScores, correctWinners));
            }

            var sortedScores = allScores
                .OrderByDescending(s => s.TotalPoints)
                .ThenByDescending(s => s.PredictionsCount)
                .ToList();

            var embed = new EmbedBuilder()
                .WithTitle($"🏆 Tabela Sezonu")
                .WithDescription($"**Sezon**: {season.Name}\n\n_Pkt — mecze z wynikiem. Typ — wszystkie typy w sezonie._")
                .WithColor(Color.Gold)
                .WithCurrentTimestamp();

            // Build table using code block for monospace alignment
            var table = "```\n";
            table += "Poz  Gracz                    Pkt   Typ   Cel   Wyg\n";
            table += "═══════════════════════════════════════════════════\n";
            
            for (int i = 0; i < sortedScores.Count; i++)
            {
                var score = sortedScores[i];
                var playerName = score.PlayerName;
                
                // Truncate long names
                if (playerName.Length > 22)
                    playerName = playerName.Substring(0, 19) + "...";
                
                // Add medal emojis for top 3
                var medal = i switch
                {
                    0 => "🥇",
                    1 => "🥈",
                    2 => "🥉",
                    _ => "  "
                };
                
                table += $"{medal} {i + 1,2}  {playerName,-22}  {score.TotalPoints,3}  {score.PredictionsCount,4}  {score.ExactScores,4}  {score.CorrectWinners,4}\n";
            }
            
            table += "```";
            embed.AddField("Tabela punktowa - Sezon", table, false);
            
            embed.WithFooter("Typ = typy w sezonie | Pkt/Cel/Wyg = tylko mecze z wynikiem");

            await FollowupAsync(embed: embed.Build(), ephemeral: true);
            _logger.LogInformation("Season table generated by {User}", Context.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate season table");
            await FollowupAsync("❌ Nie udało się wygenerować tabeli sezonu.", ephemeral: true);
        }
    }
}

