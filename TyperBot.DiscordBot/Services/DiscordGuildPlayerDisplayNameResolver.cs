using Discord.WebSocket;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.Domain.Entities;
using TyperBot.DiscordBot.Models;

namespace TyperBot.DiscordBot.Services;

/// <summary>
/// Jak <see cref="DiscordDisplayNameHelper.ForPlayerInGuild"/> — nick serwera, potem globalna nazwa, potem login.
/// </summary>
public sealed class DiscordGuildPlayerDisplayNameResolver : IPlayerDisplayNameResolver
{
    private readonly DiscordSocketClient _client;
    private readonly DiscordSettings _settings;

    public DiscordGuildPlayerDisplayNameResolver(
        DiscordSocketClient client,
        IOptions<DiscordSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public string GetDisplayName(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var guild = _client.GetGuild(_settings.GuildId);
        return DiscordDisplayNameHelper.ForPlayerInGuild(player, guild);
    }
}
