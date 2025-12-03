using TyperBot.Domain.Enums;

namespace TyperBot.Application.Services;

public class ScoreCalculator
{
    public (int points, Bucket bucket) CalculateScore(int realHome, int realAway, int tipHome, int tipAway)
    {
        // Check if winner predicted correctly
        int realDiff = realHome - realAway;
        int tipDiff = tipHome - tipAway;
        
        bool winnerCorrect = false;
        if (realDiff > 0 && tipDiff > 0) winnerCorrect = true;  // Home wins both
        else if (realDiff < 0 && tipDiff < 0) winnerCorrect = true;  // Away wins both
        else if (realDiff == 0 && tipDiff == 0) winnerCorrect = true;  // Draw both

        // 50 points: Perfect draw (exactly 45:45)
        if (realHome == 45 && realAway == 45 && tipHome == 45 && tipAway == 45)
        {
            return (50, Bucket.P50);
        }

        // 35 points: Exact score match (but not a draw - draws are only 45:45)
        if (realHome == tipHome && realAway == tipAway && realHome != realAway)
        {
            return (35, Bucket.P35);
        }

        // If winner is not correct, no points
        if (!winnerCorrect)
        {
            return (0, Bucket.P0);
        }

        // Calculate margin difference (difference in score differences)
        int realMargin = Math.Abs(realDiff);
        int tipMargin = Math.Abs(tipDiff);
        int marginDiff = Math.Abs(realMargin - tipMargin);

        // Calculate penalty for sum not equal to 90
        int sumTip = tipHome + tipAway;
        int penalty = Math.Abs(sumTip - 90);

        int totalDiff = marginDiff + penalty;

        // Map totalDiff to points (only if winner is correct AND margin is close enough)
        // According to rules: 19+ difference = 2 points (not 0)
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
            _ => 2  // 19+ difference = 2 points (correct winner but large difference)
        };

        Bucket bucket = (Bucket)points;

        return (points, bucket);
    }
}

