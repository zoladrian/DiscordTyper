using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.Application.Services;

public class EnhancedTableGenerator
{
    private readonly TableGenerator _tableGenerator;
    private readonly ISeasonRepository _seasonRepository;

    public EnhancedTableGenerator(
        TableGenerator tableGenerator,
        ISeasonRepository seasonRepository)
    {
        _tableGenerator = tableGenerator;
        _seasonRepository = seasonRepository;
    }

    public Task<(TableFormat format, string? textTable, byte[]? imageBytes)> GenerateSeasonTableAsync(
        Season season,
        List<Player> players,
        Match? triggerMatch = null)
    {
        if (season.PreferredTableFormat == TableFormat.Image)
        {
            // Generate PNG image
            var imageBytes = _tableGenerator.GenerateSeasonTable(season, players);
            return Task.FromResult<(TableFormat, string?, byte[]?)>((TableFormat.Image, null, imageBytes));
        }
        else
        {
            // Generate text table
            var textTable = GenerateTextTable(season, players);
            return Task.FromResult<(TableFormat, string?, byte[]?)>((TableFormat.Text, textTable, null));
        }
    }

    public static HashSet<int> ResolveSeasonMatchIdsPublic(Season season) => ResolveSeasonMatchIds(season);

    private static HashSet<int> ResolveSeasonMatchIds(Season season)
    {
        var set = new HashSet<int>();
        if (season.Rounds == null) return set;
        foreach (var r in season.Rounds)
        {
            if (r.Matches == null) continue;
            foreach (var m in r.Matches)
                set.Add(m.Id);
        }
        return set;
    }

    private string GenerateTextTable(Season season, List<Player> players)
    {
        var seasonMatchIds = ResolveSeasonMatchIds(season);
        var filterBySeason = seasonMatchIds.Count > 0;

        // Calculate season scores
        var allScores = new List<(string PlayerName, int TotalPoints, int PredictionsCount, int ExactScores, int CorrectWinners)>();

        foreach (var player in players)
        {
            var q = player.PlayerScores.Where(s => s.Prediction != null && s.Prediction.IsValid);
            if (filterBySeason)
                q = q.Where(s => seasonMatchIds.Contains(s.Prediction!.MatchId));
            var playerScores = q.ToList();

            var totalPoints = playerScores.Sum(s => s.Points);
            // Exact scores = P35 (exact match) or P50 (perfect draw)
            var exactScores = playerScores.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50);
            // Correct winners = all scores > 0
            var correctWinners = playerScores.Count(s => s.Points > 0);
            var predCount = playerScores.Count;

            allScores.Add((player.DiscordUsername, totalPoints, predCount, exactScores, correctWinners));
        }

        var sortedScores = allScores.OrderByDescending(s => s.TotalPoints).ToList();

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
        return table;
    }
}

public class PlayerTableRow
{
    public int Position { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int TotalPoints { get; set; }
    public int PredictionsCount { get; set; }
    public int BullseyeCount { get; set; }
    public int WinsCount { get; set; }
}
