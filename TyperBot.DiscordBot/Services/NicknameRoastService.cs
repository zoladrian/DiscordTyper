using Discord;
using Discord.WebSocket;

namespace TyperBot.DiscordBot.Services;

public class NicknameRoastService
{
    public async Task<bool> EnsureRoleByUsernameAsync(
        SocketGuild guild,
        string targetDiscordUsername,
        ulong roleId)
    {
        var role = FindRoleById(guild.Roles, roleId);
        if (role == null)
            return false;

        var targetUser = await FindUserInGuildAsync(guild, targetDiscordUsername);
        if (targetUser == null)
            return false;

        await EnsureRoleAssignedAsync(targetUser, role);
        return true;
    }

    public async Task<bool> EnsureRoleByUsernameAsync(
        SocketGuild guild,
        string targetDiscordUsername,
        string roleName)
    {
        var role = FindRole(guild.Roles, roleName);
        if (role == null)
            return false;

        var targetUser = await FindUserInGuildAsync(guild, targetDiscordUsername);
        if (targetUser == null)
            return false;

        await EnsureRoleAssignedAsync(targetUser, role);
        return true;
    }

    public async Task<bool> EnsureRoleByUsernameAsync(
        IEnumerable<IGuildUser> users,
        IEnumerable<IRole> roles,
        string targetDiscordUsername,
        ulong roleId)
    {
        var role = FindRoleById(roles, roleId);
        if (role == null)
            return false;

        var targetUser = FindUser(users, targetDiscordUsername);
        if (targetUser == null)
            return false;

        await EnsureRoleAssignedAsync(targetUser, role);
        return true;
    }

    public async Task<bool> EnsureRoleByUsernameAsync(
        IEnumerable<IGuildUser> users,
        IEnumerable<IRole> roles,
        string targetDiscordUsername,
        string roleName)
    {
        var role = FindRole(roles, roleName);
        if (role == null)
            return false;

        var targetUser = FindUser(users, targetDiscordUsername);
        if (targetUser == null)
            return false;

        await EnsureRoleAssignedAsync(targetUser, role);
        return true;
    }

    public async Task<bool> TryChangeNicknameByUsernameAsync(
        SocketGuild guild,
        string targetDiscordUsername,
        string newNickname)
    {
        var targetUser = await FindUserInGuildAsync(guild, targetDiscordUsername);

        if (targetUser == null)
            return false;

        await targetUser.ModifyAsync(props => props.Nickname = newNickname);
        return true;
    }

    private static async Task<IGuildUser?> FindUserInGuildAsync(SocketGuild guild, string targetDiscordUsername)
    {
        var targetUser = FindUser(guild.Users, targetDiscordUsername);
        if (targetUser != null)
            return targetUser;

        await foreach (var userBatch in guild.GetUsersAsync())
        {
            targetUser = FindUser(userBatch, targetDiscordUsername);
            if (targetUser != null)
                return targetUser;
        }

        return null;
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

    private static IRole? FindRole(IEnumerable<IRole> roles, string roleName)
    {
        return roles.FirstOrDefault(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
    }

    private static IRole? FindRoleById(IEnumerable<IRole> roles, ulong roleId)
    {
        return roles.FirstOrDefault(r => r.Id == roleId);
    }

    private static async Task EnsureRoleAssignedAsync(IGuildUser user, IRole role)
    {
        if (user.RoleIds.Contains(role.Id))
            return;

        await user.AddRoleAsync(role);
    }
}
