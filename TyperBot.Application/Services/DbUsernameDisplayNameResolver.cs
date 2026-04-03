using TyperBot.Domain.Entities;

namespace TyperBot.Application.Services;

/// <summary>Fallback: wyłącznie login zapisany w <see cref="Player.DiscordUsername"/>.</summary>
public sealed class DbUsernameDisplayNameResolver : IPlayerDisplayNameResolver
{
    public string GetDisplayName(Player player) =>
        player?.DiscordUsername ?? string.Empty;
}
