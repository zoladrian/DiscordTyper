using SkiaSharp;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;

namespace TyperBot.Application.Services;

public class TableGenerator
{
    private const int TableWidth = 900;
    private const int RowHeight = 40;
    private const int HeaderHeight = 60;
    private const int FooterHeight = 40;
    private const int Padding = 20;

    public byte[] GenerateSeasonTable(Season season, List<Player> players)
    {
        var standings = CalculateSeasonStandings(players, season);

        // Calculate table height
        int rows = Math.Max(standings.Count, 1);
        int totalHeight = HeaderHeight + (rows * RowHeight) + FooterHeight;

        using var surface = SKSurface.Create(new SKImageInfo(TableWidth, totalHeight));
        var canvas = surface.Canvas;

        // Draw background
        canvas.Clear(new SKColor(0x1E, 0x1E, 0x1E));

        // Draw header with gradient
        DrawHeader(canvas, $"🏁 {season.Name} - Season Standings", TableWidth, HeaderHeight);

        // Draw table content
        int yPos = HeaderHeight;
        for (int i = 0; i < standings.Count; i++)
        {
            var standing = standings[i];
            bool isAlternate = i % 2 == 1;
            DrawRow(canvas, yPos, standing, i + 1, isAlternate);
            yPos += RowHeight;
        }

        // Draw footer
        DrawFooter(canvas, totalHeight, $"Participants: {players.Count}");

        // Export to bytes
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public byte[] GenerateRoundTable(Season season, Round round, List<Player> players)
    {
        var standings = CalculateRoundStandings(players, round);

        // Calculate table height
        int rows = Math.Max(standings.Count, 1);
        int totalHeight = HeaderHeight + (rows * RowHeight) + FooterHeight;

        using var surface = SKSurface.Create(new SKImageInfo(TableWidth, totalHeight));
        var canvas = surface.Canvas;

        // Draw background
        canvas.Clear(new SKColor(0x1E, 0x1E, 0x1E));

        // Draw header with gradient
        DrawHeader(canvas, $"🏁 {season.Name} - Round {round.Number}", TableWidth, HeaderHeight);

        // Draw table content
        int yPos = HeaderHeight;
        for (int i = 0; i < standings.Count; i++)
        {
            var standing = standings[i];
            bool isAlternate = i % 2 == 1;
            DrawRow(canvas, yPos, standing, i + 1, isAlternate);
            yPos += RowHeight;
        }

        // Draw footer
        var completedMatches = round.Matches.Count(m => m.Status == MatchStatus.Finished || m.Status == MatchStatus.Cancelled);
        DrawFooter(canvas, totalHeight, $"Participants: {players.Count} • Matches: {completedMatches}/{round.Matches.Count}");

        // Export to bytes
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private void DrawHeader(SKCanvas canvas, string title, int width, int height)
    {
        // Gradient background
        var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            new[] { new SKColor(0xFF, 0xD7, 0x00), new SKColor(0x00, 0x57, 0xB8) },
            SKShaderTileMode.Clamp
        );

        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, width, height, paint);

        // Draw title
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 24,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Roboto Mono", SKFontStyle.Bold),
            TextAlign = SKTextAlign.Center
        };

        canvas.DrawText(title, width / 2, height / 2 + 10, textPaint);
    }

    private void DrawRow(SKCanvas canvas, int yPos, PlayerStanding standing, int rank, bool isAlternate)
    {
        // Row background
        var bgColor = isAlternate ? new SKColor(0x2A, 0x2A, 0x2A) : new SKColor(0x1E, 0x1E, 0x1E);
        using var bgPaint = new SKPaint { Color = bgColor };
        canvas.DrawRect(0, yPos, TableWidth, RowHeight, bgPaint);

        // Medal emoji for top 3
        string rankDisplay = rank switch
        {
            1 => "🥇",
            2 => "🥈",
            3 => "🥉",
            _ => rank.ToString()
        };

        // Text paint
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 16,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Roboto Mono", SKFontStyle.Normal)
        };

        // Draw rank
        textPaint.TextAlign = SKTextAlign.Left;
        canvas.DrawText(rankDisplay, Padding, yPos + RowHeight / 2 + 5, textPaint);

        // Draw player name (truncate if too long)
        string playerName = standing.PlayerName.Length > 20 ? standing.PlayerName[..20] : standing.PlayerName;
        canvas.DrawText($"@{playerName}", Padding + 60, yPos + RowHeight / 2 + 5, textPaint);

        // Draw points
        textPaint.TextAlign = SKTextAlign.Right;
        canvas.DrawText($"{standing.TotalPoints} pts", TableWidth - Padding - 200, yPos + RowHeight / 2 + 5, textPaint);

        // Draw bucket counts
        string bucketStr = string.Join("  ", standing.BucketCounts.Where(kvp => kvp.Value > 0)
            .Select(kvp => $"{kvp.Key}{kvp.Value}x"));
        
        if (bucketStr.Length > 40)
        {
            bucketStr = bucketStr[..40] + "...";
        }

        canvas.DrawText(bucketStr, TableWidth - Padding, yPos + RowHeight / 2 + 5, textPaint);
    }

    private void DrawFooter(SKCanvas canvas, int totalHeight, string footerText)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(0x40, 0x40, 0x40),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(0, totalHeight - FooterHeight, TableWidth, FooterHeight, paint);

        using var textPaint = new SKPaint
        {
            Color = SKColors.LightGray,
            TextSize = 14,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Roboto Mono", SKFontStyle.Normal),
            TextAlign = SKTextAlign.Center
        };

        canvas.DrawText(footerText, TableWidth / 2, totalHeight - FooterHeight / 2 + 5, textPaint);
    }

    private List<PlayerStanding> CalculateSeasonStandings(List<Player> players, Season season)
    {
        var standings = new List<PlayerStanding>();

        foreach (var player in players.Where(p => p.IsActive))
        {
            var standing = new PlayerStanding
            {
                PlayerName = player.DiscordUsername,
                TotalPoints = player.PlayerScores.Sum(ps => ps.Points),
                BucketCounts = new Dictionary<string, int>()
            };

            foreach (var score in player.PlayerScores)
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

