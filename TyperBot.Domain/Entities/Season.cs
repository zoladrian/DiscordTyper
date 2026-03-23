using TyperBot.Domain.Enums;

namespace TyperBot.Domain.Entities;

public class Season
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public TableFormat PreferredTableFormat { get; set; } = TableFormat.Image;

    // Navigation properties
    public ICollection<Round> Rounds { get; set; } = new List<Round>();

    /// <summary>
    /// Uses rounds already loaded on this instance (e.g. from GetActiveSeasonAsync). Avoids an extra DB round-trip and stays consistent with the same query.
    /// </summary>
    public Round? FindRoundByNumber(int roundNumber) =>
        Rounds.FirstOrDefault(r => r.Number == roundNumber);
}

