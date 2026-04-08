using Discord;
using Discord.WebSocket;
using TyperBot.Domain.Entities;

namespace TyperBot.DiscordBot;

/// <summary>
/// Wyświetlane nazwy użytkowników (nie zapisujemy w bazie — <see cref="Player.DiscordUsername"/> pozostaje loginem Discord).
/// </summary>
public static class DiscordDisplayNameHelper
{
    /// <summary>Pseudonim serwera → globalna nazwa → login (np. tabele, stopka admina — rozpoznanie „jak na tym serwerze").</summary>
    public static string ForDisplay(IUser user)
    {
        if (user is SocketGuildUser gu)
            return gu.Nickname ?? gu.GlobalName ?? gu.Username;
        return user.GlobalName ?? user.Username;
    }

    /// <summary>
    /// Klucz do porównań „easter egg" — <see cref="IUser.Username"/> (login / @handle, np. <c>agness88</c>).
    /// Nie nick serwera, nie GlobalName — easter eggi są dodawane po loginie.
    /// </summary>
    public static string ForGimmickMatch(SocketGuildUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Username;
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
