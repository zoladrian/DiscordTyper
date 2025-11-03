namespace TyperBot.Domain.Entities;

public class Prediction
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public int PlayerId { get; set; }
    public int HomeTip { get; set; }
    public int AwayTip { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsValid { get; set; } = true;

    // Navigation properties
    public Match Match { get; set; } = null!;
    public Player Player { get; set; } = null!;
    public PlayerScore? PlayerScore { get; set; }
}

