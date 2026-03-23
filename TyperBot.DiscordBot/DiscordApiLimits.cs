namespace TyperBot.DiscordBot;

/// <summary>
/// String limits for Discord REST API (components, modals, embeds).
/// See https://discord.com/developers/docs/components/reference and embed limits in the docs.
/// </summary>
public static class DiscordApiLimits
{
    public const int ModalTitle = 45;
    public const int TextInputLabel = 45;
    public const int TextInputPlaceholder = 100;
    public const int ButtonLabel = 80;
    public const int SelectPlaceholder = 150;
    public const int SelectOptionLabel = 100;
    public const int SelectOptionDescription = 100;
    public const int ComponentCustomId = 100;
    public const int EmbedTitle = 256;
    public const int EmbedDescription = 4096;

    /// <summary>
    /// Truncates to <paramref name="maxLength"/> code units (Discord uses UTF-16 counts like C# <see cref="string.Length"/>).
    /// </summary>
    public static string Truncate(string? text, int maxLength, string ellipsis = "…")
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        if (text.Length <= maxLength) return text;
        if (maxLength <= ellipsis.Length) return text[..maxLength];
        return text[..(maxLength - ellipsis.Length)] + ellipsis;
    }
}
