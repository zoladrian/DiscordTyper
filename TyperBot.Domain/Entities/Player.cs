namespace TyperBot.Domain.Entities;

public class Player
{
    public int Id { get; set; }
    public ulong DiscordUserId { get; set; }
    public string DiscordUsername { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    // Navigation properties
    public ICollection<Prediction> Predictions { get; set; } = new List<Prediction>();
    public ICollection<PlayerScore> PlayerScores { get; set; } = new List<PlayerScore>();
}

