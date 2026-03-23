using Discord;
using Discord.WebSocket;

namespace TyperBot.DiscordBot.Services;

/// <summary>
/// Resolves target for admin-posted tables: optional explicit channel/thread, otherwise the channel where the slash was used.
/// </summary>
public static class AdminTableChannelHelper
{
    /// <summary>
    /// <paramref name="explicitTarget"/> — z parametru komendy; jeśli null, używany jest kanał/wątek wywołania (<paramref name="invocationChannel"/>).
    /// </summary>
    public static (ITextChannel? Channel, string? Error) Resolve(
        SocketGuild guild,
        ITextChannel? explicitTarget,
        IChannel? invocationChannel)
    {
        var target = explicitTarget ?? (invocationChannel as ITextChannel);
        if (target == null)
        {
            return (null,
                "Użyj komendy na **kanale tekstowym** lub w **wątku**, albo wskaż docelowy kanał w parametrze.");
        }

        if (target is not IGuildChannel guildChannel || guildChannel.GuildId != guild.Id)
            return (null, "Kanał lub wątek musi należeć do tego serwera.");

        var perms = guild.CurrentUser.GetPermissions(guildChannel);
        if (!perms.ViewChannel || !perms.SendMessages)
            return (null, "Bot potrzebuje uprawnień: **Wyświetl kanał** i **Wysyłaj wiadomości** (także w wątku).");

        return (target, null);
    }
}
