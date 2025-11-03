namespace TyperBot.Domain.Entities;

public class Round
{
    public int Id { get; set; }
    public int SeasonId { get; set; }
    public int Number { get; set; }
    public string Description { get; set; } = string.Empty;

    // Navigation properties
    public Season Season { get; set; } = null!;
    public ICollection<Match> Matches { get; set; } = new List<Match>();
}

