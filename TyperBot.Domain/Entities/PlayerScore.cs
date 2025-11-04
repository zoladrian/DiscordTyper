using TyperBot.Domain.Enums;

namespace TyperBot.Domain.Entities;

public class PlayerScore
{
    public int Id { get; set; }
    public int PredictionId { get; set; }
    public int PlayerId { get; set; } // ← CRITICAL FIX: Direct link to Player for season standings
    public int Points { get; set; }
    public Bucket Bucket { get; set; }

    // Navigation properties
    public Prediction Prediction { get; set; } = null!;
    public Player Player { get; set; } = null!; // ← CRITICAL FIX: Enables player.PlayerScores.Sum()
}

