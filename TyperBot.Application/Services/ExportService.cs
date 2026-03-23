using System.Globalization;
using System.Text;
using TyperBot.Domain.Entities;

namespace TyperBot.Application.Services;

public class ExportService
{
    public byte[] ExportSeasonToCsv(Season season, List<Player> players)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"PGE Ekstraliga {season.Name} - Season Export");
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // Season Table
        sb.AppendLine("=== SEASON STANDINGS ===");
        sb.AppendLine("Rank,Player,Total Points,P50,P35,P20,P18,P16,P14,P12,P10,P8,P6,P4,P2,P0");

        var standings = CalculateSeasonStandings(players, season);
        for (int i = 0; i < standings.Count; i++)
        {
            var standing = standings[i];
            sb.Append($"{i + 1},{CsvEscape(standing.PlayerName)},{standing.TotalPoints}");
            foreach (var bucket in new[] { "P50", "P35", "P20", "P18", "P16", "P14", "P12", "P10", "P8", "P6", "P4", "P2", "P0" })
            {
                sb.Append($",{standing.BucketCounts.GetValueOrDefault(bucket, 0)}");
            }
            sb.AppendLine();
        }

        // Per-Round Details
        foreach (var round in season.Rounds.OrderBy(r => r.Number))
        {
            sb.AppendLine();
            sb.AppendLine($"=== ROUND {round.Number} ===");
            sb.AppendLine("Match,Player,Home Tip,Away Tip,Actual Score,Points,Bucket");

            var roundPredictions = players
                .SelectMany(p => p.Predictions.Where(pr => round.Matches.Any(m => m.Id == pr.MatchId)))
                .Where(p => p.IsValid)
                .ToList();

            var matchLookup = round.Matches.ToDictionary(m => m.Id);
            foreach (var prediction in roundPredictions.OrderBy(p => p.MatchId))
            {
                var match = matchLookup.GetValueOrDefault(prediction.MatchId) ?? prediction.Match;
                if (match == null) continue;
                var actualScore = match.Status == Domain.Enums.MatchStatus.Finished && match.HomeScore.HasValue && match.AwayScore.HasValue
                    ? $"{match.HomeScore}:{match.AwayScore}"
                    : "N/A";

                var points = prediction.PlayerScore?.Points ?? 0;
                var bucket = prediction.PlayerScore?.Bucket.ToString() ?? "N/A";
                var playerName = prediction.Player?.DiscordUsername ?? "?";

                sb.AppendLine($"{CsvEscape($"{match.HomeTeam} vs {match.AwayTeam}")},{CsvEscape(playerName)},{prediction.HomeTip},{prediction.AwayTip},{actualScore},{points},{bucket}");
            }
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] ExportRoundToCsv(Round round, List<Player> players)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"PGE Ekstraliga Round {round.Number} - Export");
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // Round Table
        sb.AppendLine("=== ROUND STANDINGS ===");
        sb.AppendLine("Rank,Player,Total Points,P50,P35,P20,P18,P16,P14,P12,P10,P8,P6,P4,P2,P0");

        var standings = CalculateRoundStandings(players, round);
        for (int i = 0; i < standings.Count; i++)
        {
            var standing = standings[i];
            sb.Append($"{i + 1},{standing.PlayerName},{standing.TotalPoints}");
            foreach (var bucket in new[] { "P50", "P35", "P20", "P18", "P16", "P14", "P12", "P10", "P8", "P6", "P4", "P2", "P0" })
            {
                sb.Append($",{standing.BucketCounts.GetValueOrDefault(bucket, 0)}");
            }
            sb.AppendLine();
        }

        // Match Details
        sb.AppendLine();
        sb.AppendLine("=== MATCH DETAILS ===");
        sb.AppendLine("Match,Player,Home Tip,Away Tip,Actual Score,Points,Bucket");

        var roundPredictions = players
            .SelectMany(p => p.Predictions.Where(pr => round.Matches.Any(m => m.Id == pr.MatchId)))
            .Where(p => p.IsValid)
            .ToList();

        var matchLookup = round.Matches.ToDictionary(m => m.Id);
        foreach (var prediction in roundPredictions.OrderBy(p => p.MatchId))
        {
            var match = matchLookup.GetValueOrDefault(prediction.MatchId) ?? prediction.Match;
            if (match == null) continue;
            var actualScore = match.Status == Domain.Enums.MatchStatus.Finished && match.HomeScore.HasValue && match.AwayScore.HasValue
                ? $"{match.HomeScore}:{match.AwayScore}"
                : "N/A";

            var points = prediction.PlayerScore?.Points ?? 0;
            var bucket = prediction.PlayerScore?.Bucket.ToString() ?? "N/A";
            var playerName = prediction.Player?.DiscordUsername ?? "?";

            sb.AppendLine($"{CsvEscape($"{match.HomeTeam} vs {match.AwayTeam}")},{CsvEscape(playerName)},{prediction.HomeTip},{prediction.AwayTip},{actualScore},{points},{bucket}");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private List<PlayerStanding> CalculateSeasonStandings(List<Player> players, Season season)
    {
        var seasonMatchIds = EnhancedTableGenerator.ResolveSeasonMatchIdsPublic(season);
        var filterBySeason = seasonMatchIds.Count > 0;
        var standings = new List<PlayerStanding>();

        foreach (var player in players.Where(p => p.IsActive))
        {
            var scores = player.PlayerScores
                .Where(ps => ps.Prediction != null && ps.Prediction.IsValid);
            if (filterBySeason)
                scores = scores.Where(ps => seasonMatchIds.Contains(ps.Prediction!.MatchId));
            var scoreList = scores.ToList();

            var standing = new PlayerStanding
            {
                PlayerName = player.DiscordUsername,
                TotalPoints = scoreList.Sum(ps => ps.Points),
                BucketCounts = new Dictionary<string, int>()
            };

            foreach (var score in scoreList)
            {
                string bucketKey = score.Bucket.ToString();
                if (!standing.BucketCounts.ContainsKey(bucketKey))
                {
                    standing.BucketCounts[bucketKey] = 0;
                }
                standing.BucketCounts[bucketKey]++;
            }

            standings.Add(standing);
        }

        // Sort by points, then by bucket counts
        standings.Sort((x, y) =>
        {
            int pointsCompare = y.TotalPoints.CompareTo(x.TotalPoints);
            if (pointsCompare != 0) return pointsCompare;

            // Tie-break by bucket counts
            var buckets = new[] { "P50", "P35", "P20", "P18", "P16", "P14", "P12", "P10", "P8", "P6", "P4", "P2" };
            foreach (var bucket in buckets)
            {
                int xCount = x.BucketCounts.GetValueOrDefault(bucket, 0);
                int yCount = y.BucketCounts.GetValueOrDefault(bucket, 0);
                int bucketCompare = yCount.CompareTo(xCount);
                if (bucketCompare != 0) return bucketCompare;
            }

            return 0;
        });

        return standings;
    }

    private List<PlayerStanding> CalculateRoundStandings(List<Player> players, Round round)
    {
        var standings = new List<PlayerStanding>();

        foreach (var player in players.Where(p => p.IsActive))
        {
            var roundPredictions = player.Predictions
                .Where(p => round.Matches.Any(m => m.Id == p.MatchId) && p.IsValid);

            var standing = new PlayerStanding
            {
                PlayerName = player.DiscordUsername,
                TotalPoints = roundPredictions
                    .Where(p => p.PlayerScore != null)
                    .Sum(p => p.PlayerScore!.Points),
                BucketCounts = new Dictionary<string, int>()
            };

            foreach (var prediction in roundPredictions.Where(p => p.PlayerScore != null))
            {
                string bucketKey = prediction.PlayerScore!.Bucket.ToString();
                if (!standing.BucketCounts.ContainsKey(bucketKey))
                {
                    standing.BucketCounts[bucketKey] = 0;
                }
                standing.BucketCounts[bucketKey]++;
            }

            standings.Add(standing);
        }

        // Sort by points, then by bucket counts
        standings.Sort((x, y) =>
        {
            int pointsCompare = y.TotalPoints.CompareTo(x.TotalPoints);
            if (pointsCompare != 0) return pointsCompare;

            // Tie-break by bucket counts
            var buckets = new[] { "P50", "P35", "P20", "P18", "P16", "P14", "P12", "P10", "P8", "P6", "P4", "P2" };
            foreach (var bucket in buckets)
            {
                int xCount = x.BucketCounts.GetValueOrDefault(bucket, 0);
                int yCount = y.BucketCounts.GetValueOrDefault(bucket, 0);
                int bucketCompare = yCount.CompareTo(xCount);
                if (bucketCompare != 0) return bucketCompare;
            }

            return 0;
        });

        return standings;
    }

    private class PlayerStanding
    {
        public string PlayerName { get; set; } = string.Empty;
        public int TotalPoints { get; set; }
        public Dictionary<string, int> BucketCounts { get; set; } = new();
    }
}

