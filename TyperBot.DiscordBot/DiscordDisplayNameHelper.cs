using Discord;
using Discord.WebSocket;
using TyperBot.Domain.Entities;

namespace TyperBot.DiscordBot;

/// <summary>
/// Wyświetlane nazwy użytkowników (nie zapisujemy w bazie — <see cref="Player.DiscordUsername"/> pozostaje loginem Discord).
/// </summary>
public static class DiscordDisplayNameHelper
{
    /// <summary>Pseudonim serwera → globalna nazwa wyświetlana → login.</summary>
    public static string ForDisplay(IUser user)
    {
        if (user is SocketGuildUser gu)
            return gu.Nickname ?? gu.GlobalName ?? gu.Username;
        return user.GlobalName ?? user.Username;
    }

    /// <summary>Imię z cache serwera albo <see cref="Player.DiscordUsername"/> (np. gracz poza serwerem).</summary>
    public static string ForPlayerInGuild(Player player, SocketGuild? guild)
    {
        if (guild == null)
            return player.DiscordUsername;
        var gu = guild.GetUser(player.DiscordUserId);
        return gu != null ? ForDisplay(gu) : player.DiscordUsername;
    }
}
