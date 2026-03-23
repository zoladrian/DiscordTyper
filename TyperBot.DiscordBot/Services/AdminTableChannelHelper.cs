using Discord;
using Discord.WebSocket;

namespace TyperBot.DiscordBot.Services;

/// <summary>
/// Resolves target text channel for admin-posted tables: explicit choice or configured predictions channel.
/// </summary>
public static class AdminTableChannelHelper
{
    public static async Task<(SocketTextChannel? Channel, string? Error)> ResolveAsync(
        SocketGuild guild,
        SocketTextChannel? chosen,
        DiscordLookupService lookup)
    {
        if (chosen != null)
        {
            if (chosen.Guild.Id != guild.Id)
                return (null, "Kanał musi należeć do tego serwera.");

            var perms = guild.CurrentUser.GetPermissions(chosen);
            if (!perms.ViewChannel || !perms.SendMessages)
                return (null, "Bot potrzebuje na tym kanale uprawnień: **Wyświetl kanał** i **Wysyłaj wiadomości**.");

            return (chosen, null);
        }

        var predictions = await lookup.GetPredictionsChannelAsync();
        return predictions == null
            ? (null, "Nie znaleziono kanału typowania — ustaw go w konfiguracji lub wybierz kanał w komendzie.")
            : (predictions, null);
    }
}
