namespace TyperBot.Application.Services;

public static class TeamNameHelper
{
    /// <summary>
    /// Mapowanie nazw drużyn do miast (dla generowania skrótów)
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
    /// Generuje skrót z nazwy drużyny używając miasta (np. "Stal Gorzów" -> "GOR", "Unia Leszno" -> "LES")
    /// </summary>
    public static string GetTeamShortcut(string teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName))
            return "???";

        // Sprawdź czy mamy mapowanie dla tej drużyny
        if (TeamCityMap.TryGetValue(teamName.Trim(), out var city))
        {
            return GetCityShortcut(city);
        }

        // Fallback: spróbuj znaleźć miasto w nazwie (ostatnie słowo lub przedostatnie jeśli jest "Zielona Góra")
        var words = teamName.Trim().Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (words.Length == 0)
            return "???";

        // Sprawdź czy ostatnie dwa słowa to "Zielona Góra"
        if (words.Length >= 2 && 
            words[words.Length - 2].Equals("Zielona", StringComparison.OrdinalIgnoreCase) &&
            words[words.Length - 1].Equals("Góra", StringComparison.OrdinalIgnoreCase))
        {
            return "ZIE";
        }

        // W przeciwnym razie użyj ostatniego słowa jako miasta
        var lastWord = words[words.Length - 1];
        return GetCityShortcut(lastWord);
    }

    /// <summary>
    /// Generuje skrót z nazwy miasta (pierwsze 3 litery)
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
    /// Generuje skrót meczu (np. "WRO-TOR")
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

