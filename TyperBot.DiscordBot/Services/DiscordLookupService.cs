using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.DiscordBot.Models;

namespace TyperBot.DiscordBot.Services;

public class DiscordLookupService
{
    private readonly DiscordSocketClient _client;
    private readonly ILogger<DiscordLookupService> _logger;
    private readonly DiscordSettings _settings;

    public DiscordLookupService(
        DiscordSocketClient client,
        ILogger<DiscordLookupService> logger,
        IOptions<DiscordSettings> settings)
    {
        _client = client;
        _logger = logger;
        _settings = settings.Value;
    }

    public Task<SocketGuild?> GetGuildAsync()
    {
        var guild = _client.GetGuild(_settings.GuildId);
        
        if (guild == null)
        {
            _logger.LogWarning("Guild {GuildId} not found", _settings.GuildId);
        }
        
        return Task.FromResult(guild);
    }

    public async Task<SocketTextChannel?> GetAdminChannelAsync()
    {
        var guild = await GetGuildAsync();
        if (guild == null) return null;

        var channel = guild.TextChannels.FirstOrDefault(c => c.Name == _settings.Channels.AdminChannel);
        
        if (channel == null)
        {
            _logger.LogWarning("Admin channel '{ChannelName}' not found in guild", _settings.Channels.AdminChannel);
        }
        
        return channel;
    }

    public async Task<SocketTextChannel?> GetPredictionsChannelAsync()
    {
        var guild = await GetGuildAsync();
        if (guild == null) return null;

        var channel = guild.TextChannels.FirstOrDefault(c => c.Name == _settings.Channels.PredictionsChannel);
        
        if (channel == null)
        {
            _logger.LogWarning("Predictions channel '{ChannelName}' not found in guild", _settings.Channels.PredictionsChannel);
        }
        
        return channel;
    }

    public async Task<SocketTextChannel?> GetResultsChannelAsync()
    {
        var guild = await GetGuildAsync();
        if (guild == null) return null;

        var channel = guild.TextChannels.FirstOrDefault(c => c.Name == _settings.Channels.ResultsChannel);
        
        if (channel == null)
        {
            _logger.LogWarning("Results channel '{ChannelName}' not found in guild", _settings.Channels.ResultsChannel);
        }
        
        return channel;
    }

    public SocketRole? GetPlayerRole(SocketGuild guild)
    {
        if (guild == null) return null;

        var role = guild.Roles.FirstOrDefault(r => r.Name == _settings.PlayerRoleName);
        
        if (role == null)
        {
            _logger.LogWarning("Player role '{RoleName}' not found in guild", _settings.PlayerRoleName);
        }
        
        return role;
    }

    public SocketRole? GetAdminRole(SocketGuild guild)
    {
        if (guild == null) return null;

        var role = guild.Roles.FirstOrDefault(r => r.Name == _settings.AdminRoleName);
        
        if (role == null)
        {
            _logger.LogWarning("Admin role '{RoleName}' not found in guild", _settings.AdminRoleName);
        }
        
        return role;
    }
}

