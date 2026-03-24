using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;

namespace TyperBot.Application.Services;

/// <summary>
/// Remisy w tabelach: suma punktów, potem liczba lepszych „kubełków” w ustalonej kolejności (P50 → P35 → … → P0), na końcu nick.
/// P50 = jedyny „dokładny remis” w regulaminie (45:45); P35 = dokładny wynik przy nieremisie.
/// </summary>
public static class StandingsTieBreak
{
    private static readonly Bucket[] BucketOrderDescending =
    {
        Bucket.P50, Bucket.P35, Bucket.P20, Bucket.P18, Bucket.P16, Bucket.P14,
        Bucket.P12, Bucket.P10, Bucket.P8, Bucket.P6, Bucket.P4, Bucket.P2, Bucket.P0
    };

    public static int ComparePlayerScores(IReadOnlyCollection<PlayerScore> a, IReadOnlyCollection<PlayerScore> b)
    {
        int pa = a.Sum(x => x.Points);
        int pb = b.Sum(x => x.Points);
        int c = pb.CompareTo(pa);
        if (c != 0) return c;

        foreach (var bucket in BucketOrderDescending)
        {
            int ca = a.Count(x => x.Bucket == bucket);
            int cb = b.Count(x => x.Bucket == bucket);
            c = cb.CompareTo(ca);
            if (c != 0) return c;
        }

        return 0;
    }

    /// <summary>Zakłada ważne typy z niepustym <see cref="Prediction.PlayerScore"/>; filtruje po <paramref name="matchIds"/>.</summary>
    public static int ComparePlayersByPredictions(Player a, Player b, HashSet<int> matchIds)
    {
        var sa = CollectScoresFromPredictions(a, matchIds);
        var sb = CollectScoresFromPredictions(b, matchIds);
        int c = ComparePlayerScores(sa, sb);
        return c != 0
            ? c
            : string.Compare(a.DiscordUsername, b.DiscordUsername, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Jak <see cref="ComparePlayersByPredictions"/> ale na kolekcji <see cref="Player.PlayerScores"/> (np. tekstowa tabela).</summary>
    public static int ComparePlayersByPlayerScores(Player a, Player b, HashSet<int> matchIds)
    {
        var sa = CollectScoresFromPlayerScores(a, matchIds);
        var sb = CollectScoresFromPlayerScores(b, matchIds);
        int c = ComparePlayerScores(sa, sb);
        return c != 0
            ? c
            : string.Compare(a.DiscordUsername, b.DiscordUsername, StringComparison.OrdinalIgnoreCase);
    }

    public static List<PlayerScore> CollectScoresFromPredictions(Player player, HashSet<int> matchIds)
    {
        var list = new List<PlayerScore>();
        bool filter = matchIds.Count > 0;
        foreach (var pr in player.Predictions ?? Enumerable.Empty<Prediction>())
        {
            if (!pr.IsValid || pr.PlayerScore == null) continue;
            if (filter && !matchIds.Contains(pr.MatchId)) continue;
            list.Add(pr.PlayerScore);
        }

        return list;
    }

    public static List<PlayerScore> CollectScoresFromPlayerScores(Player player, HashSet<int> matchIds)
    {
        var list = new List<PlayerScore>();
        bool filter = matchIds.Count > 0;
        foreach (var s in player.PlayerScores ?? Enumerable.Empty<PlayerScore>())
        {
            if (s.Prediction == null || !s.Prediction.IsValid) continue;
            if (filter && !matchIds.Contains(s.Prediction.MatchId)) continue;
            list.Add(s);
        }

        return list;
    }
}
