using Discord;

namespace TyperBot.DiscordBot.Services;

public class NicknameRoastService
{
    public async Task<bool> TryChangeNicknameByUsernameAsync(
        IEnumerable<IGuildUser> users,
        string targetDiscordUsername,
        string newNickname)
    {
        var targetUser = users.FirstOrDefault(u =>
            u.Username.Equals(targetDiscordUsername, StringComparison.OrdinalIgnoreCase));

        if (targetUser == null)
            return false;

        await targetUser.ModifyAsync(props => props.Nickname = newNickname);
        return true;
    }
}
