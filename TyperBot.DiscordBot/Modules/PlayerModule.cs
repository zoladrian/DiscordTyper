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
        var player = await _playerRepository.GetByDiscordUserIdAsync(Context.User.Id);
        if (player == null)
        {
            await RespondAsync("❌ Nie złożyłeś jeszcze żadnych typów.", ephemeral: true);
            return;
        }

        IEnumerable<Domain.Entities.Prediction> predictions;
        
        if (round.HasValue)
        {
            // Show all predictions for a specific round (any status)
            var season = await _seasonRepository.GetActiveSeasonAsync();
            if (season == null)
            {
                await RespondAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
                return;
            }
            var roundEntity = await _roundRepository.GetByNumberAsync(season.Id, round.Value);
            if (roundEntity == null)
            {
                await RespondAsync($"❌ Kolejka {round.Value} nie znaleziona.", ephemeral: true);
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
            await RespondAsync(message, ephemeral: true);
            return;
        }

        var roundNumber = round;
        var embed = new EmbedBuilder()
            .WithTitle(roundNumber.HasValue ? $"📝 Moje Typy - Kolejka {roundNumber.Value}" : "📝 Moje Typy - Nadchodzące")
            .WithColor(Color.Blue)
            .WithCurrentTimestamp();

        // Build detailed table with match info
        var matchesWithPreds = new List<(Domain.Entities.Match Match, Domain.Entities.Prediction Pred)>();
        foreach (var pred in predictionsList)
        {
            var match = await _matchRepository.GetByIdAsync(pred.MatchId);
            if (match != null && match.Round != null)
            {
                matchesWithPreds.Add((match, pred));
            }
        }

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

        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }

    [SlashCommand("tabela-kolejki", "Wyświetl tabelę dla konkretnej kolejki")]
    public async Task RoundTableAsync([Summary(description: "Numer kolejki")] int round)
    {
        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await RespondAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var roundEntity = await _roundRepository.GetByNumberAsync(season.Id, round);
        if (roundEntity == null)
        {
            await RespondAsync($"❌ Kolejka {round} nie znaleziona.", ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await RespondAsync("❌ Brak aktywnych graczy.", ephemeral: true);
            return;
        }

        try
        {
            // Calculate scores for the round
            var roundMatches = (await _matchRepository.GetByRoundIdAsync(roundEntity.Id)).Select(m => m.Id).ToList();
            var allScores = new List<(int PlayerId, string PlayerName, int TotalPoints, int PredictionsCount, int ExactScores, int CorrectWinners)>();
            
            foreach (var player in players)
            {
                var roundPredictions = player.Predictions
                    .Where(p => roundMatches.Contains(p.MatchId) && p.IsValid && p.PlayerScore != null)
                    .ToList();
                
                var playerScores = roundPredictions
                    .Select(p => p.PlayerScore!)
                    .ToList();
                
                var totalPoints = playerScores.Sum(s => s.Points);
                // Exact scores = P35 (exact match) or P50 (perfect draw)
                var exactScores = playerScores.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50);
                // Correct winners = all scores > 0
                var correctWinners = playerScores.Count(s => s.Points > 0);
                var predCount = roundPredictions.Count;
                
                allScores.Add((player.Id, player.DiscordUsername, totalPoints, predCount, exactScores, correctWinners));
            }
            
            var sortedScores = allScores.OrderByDescending(s => s.TotalPoints).ToList();

            var embed = new EmbedBuilder()
                .WithTitle($"📊 Tabela Kolejki {round}")
                .WithDescription($"**Sezon**: {season.Name}\n**Kolejka**: {roundEntity.Description ?? $"Kolejka {round}"}")
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
            
            embed.WithFooter($"Typ = Liczba typów | Cel = Celne wyniki | Wyg = Poprawne zwycięzców");

            await RespondAsync(embed: embed.Build(), ephemeral: true);
            _logger.LogInformation("Round {Round} table generated by {User}", round, Context.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate round table");
            await RespondAsync("❌ Nie udało się wygenerować tabeli kolejki.", ephemeral: true);
        }
    }

    [SlashCommand("tabela-sezonu", "Wyświetl ogólną tabelę sezonu")]
    public async Task SeasonTableAsync()
    {
        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await RespondAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await RespondAsync("❌ Brak aktywnych graczy.", ephemeral: true);
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
                var playerScores = player.Predictions
                    .Where(p => seasonMatchIds.Contains(p.MatchId) && p.IsValid && p.PlayerScore != null)
                    .Select(p => p.PlayerScore!)
                    .ToList();
                
                var totalPoints = playerScores.Sum(s => s.Points);
                // Exact scores = P35 (exact match) or P50 (perfect draw)
                var exactScores = playerScores.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50);
                // Correct winners = all scores > 0 (any points means correct winner)
                var correctWinners = playerScores.Count(s => s.Points > 0);
                var predCount = playerScores.Count;
                
                allScores.Add((player.DiscordUsername, totalPoints, predCount, exactScores, correctWinners));
            }

            var sortedScores = allScores.OrderByDescending(s => s.TotalPoints).ToList();

            var embed = new EmbedBuilder()
                .WithTitle($"🏆 Tabela Sezonu")
                .WithDescription($"**Sezon**: {season.Name}")
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
            
            embed.WithFooter($"Typ = Liczba typów | Cel = Celne wyniki | Wyg = Poprawne zwycięzców");

            await RespondAsync(embed: embed.Build(), ephemeral: true);
            _logger.LogInformation("Season table generated by {User}", Context.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate season table");
            await RespondAsync("❌ Nie udało się wygenerować tabeli sezonu.", ephemeral: true);
        }
    }
}

