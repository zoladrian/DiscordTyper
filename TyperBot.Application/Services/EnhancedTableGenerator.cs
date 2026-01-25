using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.Application.Services;

public class EnhancedTableGenerator
{
    private readonly TableGenerator _textTableGenerator;
    private readonly ISeasonRepository _seasonRepository;

    public EnhancedTableGenerator(
        TableGenerator textTableGenerator,
        ISeasonRepository seasonRepository)
    {
        _textTableGenerator = textTableGenerator;
        _seasonRepository = seasonRepository;
    }

    public async Task<(TableFormat format, string? textTable, List<PlayerTableRow>? imageData)> GenerateSeasonTableAsync(
        Season season,
        List<Player> players,
        Match? triggerMatch = null)
    {
        // Get player table rows
        var tableRows = new List<PlayerTableRow>();
        
        // ... (tu będzie logika generowania danych tabeli)
        
        if (season.PreferredTableFormat == TableFormat.Image)
        {
            return (TableFormat.Image, null, tableRows);
        }
        else
        {
            var textTable = _textTableGenerator.GenerateSeasonTable(season, players, triggerMatch);
            return (TableFormat.Text, textTable, null);
        }
    }
}

public class PlayerTableRow
{
    public int Position { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int TotalPoints { get; set; }
    public int PredictionsCount { get; set; }
    public int BullseyeCount { get; set; }
    public int WinsCount { get; set; }
}
