using Discord;
using Discord.WebSocket;

namespace TyperBot.DiscordBot.Services;

public class NicknameRoastService
{
    public async Task<bool> TryChangeNicknameByUsernameAsync(
        SocketGuild guild,
        string targetDiscordUsername,
        string newNickname)
    {
        var targetUser = FindUser(guild.Users, targetDiscordUsername);

        if (targetUser == null)
        {
            await foreach (var userBatch in guild.GetUsersAsync())
            {
                targetUser = FindUser(userBatch, targetDiscordUsername);
                if (targetUser != null)
                    break;
            }
        }

        if (targetUser == null)
            return false;

        await targetUser.ModifyAsync(props => props.Nickname = newNickname);
        return true;
    }

    public async Task<bool> TryChangeNicknameByUsernameAsync(
        IEnumerable<IGuildUser> users,
        string targetDiscordUsername,
        string newNickname)
    {
        var targetUser = FindUser(users, targetDiscordUsername);

        if (targetUser == null)
            return false;

        await targetUser.ModifyAsync(props => props.Nickname = newNickname);
        return true;
    }

    private static IGuildUser? FindUser(IEnumerable<IGuildUser> users, string targetDiscordUsername)
    {
        return users.FirstOrDefault(u =>
            u.Username.Equals(targetDiscordUsername, StringComparison.OrdinalIgnoreCase)
            || (u.Nickname != null && u.Nickname.Equals(targetDiscordUsername, StringComparison.OrdinalIgnoreCase))
            || (u.GlobalName != null && u.GlobalName.Equals(targetDiscordUsername, StringComparison.OrdinalIgnoreCase)));
    }
}
