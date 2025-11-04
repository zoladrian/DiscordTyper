namespace TyperBot.Application.Services;

/// <summary>
/// Contains constants for PGE Ekstraliga teams.
/// </summary>
public static class TeamConstants
{
    /// <summary>
    /// Fixed list of PGE Ekstraliga teams for the 2025 season.
    /// </summary>
    public static readonly string[] PgeEkstraligaTeams = new[]
    {
        "Motor Lublin",
        "Sparta Wrocław",
        "Apator Toruń",
        "GKM Grudziądz",
        "Falubaz Zielona Góra",
        "Stal Gorzów",
        "Włókniarz Częstochowa",
        "Unia Leszno"
    };

    /// <summary>
    /// Validates if a team name is in the official team list.
    /// </summary>
    public static bool IsValidTeam(string teamName)
    {
        return PgeEkstraligaTeams.Contains(teamName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets a normalized team name (proper casing).
    /// Returns null if team is not found.
    /// </summary>
    public static string? GetNormalizedTeamName(string teamName)
    {
        return PgeEkstraligaTeams.FirstOrDefault(t => 
            string.Equals(t, teamName, StringComparison.OrdinalIgnoreCase));
    }
}

