namespace TyperBot.Application.Services;

/// <summary>
/// Helper service for round (kolejka) related functionality.
/// Provides Polish round labels and validation.
/// </summary>
public static class RoundHelper
{
    private const int TotalRounds = 18;
    private const int RegularRoundsCount = 14;
    private const int QuarterFinalsStartRound = 15;
    private const int SemiFinalsRound = 17;
    private const int FinalsRound = 18;

    /// <summary>
    /// Gets the Polish display label for a given round number.
    /// </summary>
    /// <param name="roundNumber">Round number (1-18)</param>
    /// <returns>Polish label for the round</returns>
    public static string GetRoundLabel(int roundNumber)
    {
        return roundNumber switch
        {
            >= 1 and <= RegularRoundsCount => $"Runda {roundNumber}",
            QuarterFinalsStartRound => "1/4 finału – 1",
            QuarterFinalsStartRound + 1 => "1/4 finału – 2",
            SemiFinalsRound => "1/2 finału",
            FinalsRound => "Finał",
            _ => $"Runda {roundNumber}"
        };
    }

    /// <summary>
    /// Gets a short label for a round (used in select menus).
    /// </summary>
    public static string GetRoundShortLabel(int roundNumber)
    {
        return GetRoundLabel(roundNumber);
    }

    /// <summary>
    /// Gets a description for a round (used in select menu descriptions).
    /// </summary>
    public static string GetRoundDescription(int roundNumber)
    {
        return roundNumber switch
        {
            >= 1 and <= RegularRoundsCount => $"Regularna kolejka {roundNumber}",
            QuarterFinalsStartRound => "Pierwsza kolejka ćwierćfinałów",
            QuarterFinalsStartRound + 1 => "Druga kolejka ćwierćfinałów",
            SemiFinalsRound => "Półfinały",
            FinalsRound => "Mecz finałowy",
            _ => $"Kolejka {roundNumber}"
        };
    }

    /// <summary>
    /// Validates if a round number is valid (1-18).
    /// </summary>
    public static bool IsValidRoundNumber(int roundNumber)
    {
        return roundNumber >= 1 && roundNumber <= TotalRounds;
    }

    /// <summary>
    /// Gets all valid round numbers.
    /// </summary>
    public static IEnumerable<int> GetAllRoundNumbers()
    {
        return Enumerable.Range(1, TotalRounds);
    }
}

