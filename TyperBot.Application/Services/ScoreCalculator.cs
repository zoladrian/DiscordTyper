using TyperBot.Domain.Enums;

namespace TyperBot.Application.Services;

public class ScoreCalculator
{
    public (int points, Bucket bucket) CalculateScore(int realHome, int realAway, int tipHome, int tipAway)
    {
        // Calculate differences
        int realDiff = realHome - realAway;
        int tipDiff = tipHome - tipAway;

        // Check if winner predicted correctly
        bool winnerCorrect = false;
        if (realDiff > 0 && tipDiff > 0) winnerCorrect = true;  // Home wins both
        else if (realDiff < 0 && tipDiff < 0) winnerCorrect = true;  // Away wins both
        else if (realDiff == 0 && tipDiff == 0) winnerCorrect = true;  // Draw both

        if (!winnerCorrect)
        {
            return (0, Bucket.P0);
        }

        // Perfect draw (all values equal)
        if (realHome == realAway && tipHome == tipAway && realHome == tipHome)
        {
            return (50, Bucket.P50);
        }

        // Exact score match
        if (realHome == tipHome && realAway == tipAway)
        {
            return (35, Bucket.P35);
        }

        // Calculate margin and penalty
        int realMargin = Math.Abs(realDiff);
        int tipMargin = Math.Abs(tipDiff);
        int marginDiff = Math.Abs(realMargin - tipMargin);

        int sumTip = tipHome + tipAway;
        int penalty = Math.Abs(sumTip - 90);

        int totalDiff = marginDiff + penalty;

        // Map totalDiff to points
        int points = totalDiff switch
        {
            <= 2 => 20,
            <= 4 => 18,
            <= 6 => 16,
            <= 8 => 14,
            <= 10 => 12,
            <= 12 => 10,
            <= 14 => 8,
            <= 16 => 6,
            <= 18 => 4,
            _ => 2
        };

        Bucket bucket = (Bucket)points;

        return (points, bucket);
    }
}

