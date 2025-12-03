using TyperBot.Domain.Enums;

namespace TyperBot.Domain.Entities;

public class Match
{
    public int Id { get; set; }
    public int RoundId { get; set; }
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? ActualStartTime { get; set; }
    public DateTimeOffset? ThreadCreationTime { get; set; }
    public ulong? ThreadId { get; set; }
    public MatchStatus Status { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    // Navigation properties
    public Round Round { get; set; } = null!;
    public ICollection<Prediction> Predictions { get; set; } = new List<Prediction>();
}

