namespace TyperBot.Application.Services;

public static class TeamNameHelper
{
    /// <summary>
    /// Maps team names to cities (for generating abbreviations)
    /// </summary>
    private static readonly Dictionary<string, string> TeamCityMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Motor Lublin", "Lublin" },
        { "Sparta Wrocław", "Wrocław" },
        { "Betard Sparta Wrocław", "Wrocław" },
        { "Apator Toruń", "Toruń" },
        { "GKM Grudziądz", "Grudziądz" },
        { "Falubaz Zielona Góra", "Zielona Góra" },
        { "Stal Gorzów", "Gorzów" },
        { "Włókniarz Częstochowa", "Częstochowa" },
        { "Unia Leszno", "Leszno" },
        { "Fogo Unia Leszno", "Leszno" }
    };

    /// <summary>
    /// Generates a team abbreviation using the city name (e.g. "Stal Gorzów" -> "GOR", "Unia Leszno" -> "LES")
    /// </summary>
    public static string GetTeamShortcut(string teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName))
            return "???";

        // Check if we have a mapping for this team
        if (TeamCityMap.TryGetValue(teamName.Trim(), out var city))
        {
            return GetCityShortcut(city);
        }

        // Fallback: try to find the city in the name (last word, or second-to-last for "Zielona Góra")
        var words = teamName.Trim().Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (words.Length == 0)
            return "???";

        // Check if the last two words are "Zielona Góra"
        if (words.Length >= 2 && 
            words[words.Length - 2].Equals("Zielona", StringComparison.OrdinalIgnoreCase) &&
            words[words.Length - 1].Equals("Góra", StringComparison.OrdinalIgnoreCase))
        {
            return "ZIE";
        }

        // Otherwise use the last word as the city name
        var lastWord = words[words.Length - 1];
        return GetCityShortcut(lastWord);
    }

    /// <summary>
    /// Generates a city abbreviation (first 3 letters)
    /// </summary>
    private static string GetCityShortcut(string cityName)
    {
        var normalized = RemovePolishChars(cityName);
        if (normalized.Length >= 3)
        {
            return normalized.Substring(0, 3).ToUpper();
        }
        return normalized.ToUpper().PadRight(3, 'X');
    }

    /// <summary>
    /// Generates a match abbreviation (e.g. "WRO-TOR")
    /// </summary>
    public static string GetMatchShortcut(string homeTeam, string awayTeam)
    {
        return $"{GetTeamShortcut(homeTeam)}-{GetTeamShortcut(awayTeam)}";
    }

    private static string RemovePolishChars(string text)
    {
        var replacements = new Dictionary<char, char>
        {
            {'ą', 'a'}, {'ć', 'c'}, {'ę', 'e'}, {'ł', 'l'}, {'ń', 'n'},
            {'ó', 'o'}, {'ś', 's'}, {'ź', 'z'}, {'ż', 'z'},
            {'Ą', 'A'}, {'Ć', 'C'}, {'Ę', 'E'}, {'Ł', 'L'}, {'Ń', 'N'},
            {'Ó', 'O'}, {'Ś', 'S'}, {'Ź', 'Z'}, {'Ż', 'Z'}
        };

        return new string(text.Select(c => replacements.ContainsKey(c) ? replacements[c] : c).ToArray());
    }
}

