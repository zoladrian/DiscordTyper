namespace TyperBot.DiscordBot.Models;

public class DiscordSettings
{
    public string Token { get; set; } = string.Empty;
    public ulong GuildId { get; set; }
    public string PlayerRoleName { get; set; } = "Typer";
    public string AdminRoleName { get; set; } = "TyperAdmin";
    /// <summary>
    /// Gdy true, bot wysyła publiczne wiadomości w wątku meczu po złożeniu/zmianie typu oraz komunikaty o odrzuconym typie (format/suma).
    /// </summary>
    public bool EnablePredictionThreadMessages { get; set; } = true;
    public ChannelSettings Channels { get; set; } = new();
    public string Timezone { get; set; } = "Europe/Warsaw";
}

public class ChannelSettings
{
    public string PredictionsChannel { get; set; } = "typowanie";
    public string ResultsChannel { get; set; } = "wyniki-typera";
    public string AdminChannel { get; set; } = "typer-admin";
}

