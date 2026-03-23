using Discord;
using Discord.WebSocket;

namespace TyperBot.DiscordBot.Services;

/// <summary>
/// Resolves target for admin-posted tables: explicit text channel or thread, or configured predictions channel.
/// </summary>
public static class AdminTableChannelHelper
{
    public static async Task<(ITextChannel? Channel, string? Error)> ResolveAsync(
        SocketGuild guild,
        ITextChannel? chosen,
        DiscordLookupService lookup)
    {
        if (chosen != null)
        {
            if (chosen is not IGuildChannel guildChannel || guildChannel.GuildId != guild.Id)
                return (null, "Kanał lub wątek musi należeć do tego serwera.");

            var perms = guild.CurrentUser.GetPermissions(guildChannel);
            if (!perms.ViewChannel || !perms.SendMessages)
                return (null, "Bot potrzebuje uprawnień: **Wyświetl kanał** i **Wysyłaj wiadomości** (także w wątku).");

            return (chosen, null);
        }

        var predictions = await lookup.GetPredictionsChannelAsync();
        return predictions == null
            ? (null, "Nie znaleziono kanału typowania — ustaw go w konfiguracji lub wybierz kanał/wątek w komendzie.")
            : (predictions, null);
    }
}
