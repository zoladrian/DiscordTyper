using TyperBot.Domain.Enums;

namespace TyperBot.Domain.Entities;

public class PlayerScore
{
    public int Id { get; set; }
    public int PredictionId { get; set; }
    public int Points { get; set; }
    public Bucket Bucket { get; set; }

    // Navigation property
    public Prediction Prediction { get; set; } = null!;
}

