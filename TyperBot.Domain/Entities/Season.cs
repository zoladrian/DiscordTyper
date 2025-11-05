namespace TyperBot.Domain.Entities;

public class Season
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    // Navigation properties
    public ICollection<Round> Rounds { get; set; } = new List<Round>();
}

