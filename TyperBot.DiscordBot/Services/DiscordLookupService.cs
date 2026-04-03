using Discord;
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
            _logger.LogWarning("Admin channel '{ChannelName}' not found in guild, creating it...", _settings.Channels.AdminChannel);
            
            try
            {
                var restChannel = await guild.CreateTextChannelAsync(_settings.Channels.AdminChannel);
                // Wait a moment for Socket client to update cache, then get SocketTextChannel
                await Task.Delay(100);
                channel = _client.GetChannel(restChannel.Id) as SocketTextChannel;
                
                if (channel == null)
                {
                    // Fallback: try to get from guild cache
                    channel = guild.TextChannels.FirstOrDefault(c => c.Id == restChannel.Id);
                }
                
                _logger.LogInformation("Admin channel '{ChannelName}' created successfully (ID: {ChannelId})", 
                    _settings.Channels.AdminChannel, restChannel.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create admin channel '{ChannelName}'", _settings.Channels.AdminChannel);
                return null;
            }
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
            _logger.LogWarning("Predictions channel '{ChannelName}' not found in guild, creating it...", _settings.Channels.PredictionsChannel);
            
            try
            {
                var restChannel = await guild.CreateTextChannelAsync(_settings.Channels.PredictionsChannel);
                // Wait a moment for Socket client to update cache, then get SocketTextChannel
                await Task.Delay(100);
                channel = _client.GetChannel(restChannel.Id) as SocketTextChannel;
                
                if (channel == null)
                {
                    // Fallback: try to get from guild cache
                    channel = guild.TextChannels.FirstOrDefault(c => c.Id == restChannel.Id);
                }
                
                _logger.LogInformation("Predictions channel '{ChannelName}' created successfully (ID: {ChannelId})", 
                    _settings.Channels.PredictionsChannel, restChannel.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create predictions channel '{ChannelName}'", _settings.Channels.PredictionsChannel);
                return null;
            }
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
            _logger.LogWarning("Results channel '{ChannelName}' not found in guild, creating it...", _settings.Channels.ResultsChannel);
            
            try
            {
                var restChannel = await guild.CreateTextChannelAsync(_settings.Channels.ResultsChannel);
                // Wait a moment for Socket client to update cache, then get SocketTextChannel
                await Task.Delay(100);
                channel = _client.GetChannel(restChannel.Id) as SocketTextChannel;
                
                if (channel == null)
                {
                    // Fallback: try to get from guild cache
                    channel = guild.TextChannels.FirstOrDefault(c => c.Id == restChannel.Id);
                }
                
                _logger.LogInformation("Results channel '{ChannelName}' created successfully (ID: {ChannelId})", 
                    _settings.Channels.ResultsChannel, restChannel.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create results channel '{ChannelName}'", _settings.Channels.ResultsChannel);
                return null;
            }
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

    /// <summary>
    /// Wątek meczu: cache klienta, potem wątki pod kanałem typowanie, potem próba REST (np. wątek zarchiwizowany).
    /// </summary>
    public async Task<SocketThreadChannel?> TryGetMatchThreadAsync(ulong threadId)
    {
        if (_client.GetChannel(threadId) is SocketThreadChannel direct)
            return direct;

        var predictions = await GetPredictionsChannelAsync();
        var fromParent = predictions?.Threads.FirstOrDefault(t => t.Id == threadId);
        if (fromParent != null)
            return fromParent;

        try
        {
            await _client.Rest.GetChannelAsync(threadId);
            return _client.GetChannel(threadId) as SocketThreadChannel;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TryGetMatchThreadAsync: REST GetChannel failed for thread {ThreadId}", threadId);
        }

        return null;
    }

    /// <summary>
    /// Finds the match card message in a thread. The card is the oldest bot embed that looks like
    /// a match card (round label + " vs "), not a reveal/result/admin notice. We page backward because
    /// the card is usually near the start and <see cref="GetMessagesAsync(int)"/> only returns recent messages.
    /// </summary>
    public async Task<Discord.IUserMessage?> FindMatchCardMessageAsync(SocketThreadChannel thread)
    {
        const int batchSize = 100;
        const int maxBatches = 40;

        IUserMessage? oldestCard = null;
        ulong? beforeId = null;

        for (var batch = 0; batch < maxBatches; batch++)
        {
            var page = beforeId == null
                ? await thread.GetMessagesAsync(batchSize).FlattenAsync()
                : await thread.GetMessagesAsync(beforeId.Value, Direction.Before, batchSize).FlattenAsync();

            var list = page.ToList();
            if (list.Count == 0)
                break;

            foreach (var msg in list)
            {
                if (msg.Author.Id != _client.CurrentUser.Id || msg is not IUserMessage userMsg || !userMsg.Embeds.Any())
                    continue;

                var embed = userMsg.Embeds.First();
                if (!IsMatchCardEmbed(embed))
                    continue;

                if (oldestCard == null || userMsg.CreatedAt < oldestCard.CreatedAt)
                    oldestCard = userMsg;
            }

            if (list.Count < batchSize)
                break;

            beforeId = list.Min(m => m.Id);
        }

        return oldestCard;
    }

    private static bool IsMatchCardEmbed(IEmbed embed)
    {
        var title = embed.Title ?? string.Empty;
        if (title.StartsWith("👁️", StringComparison.Ordinal)) return false;
        if (title.StartsWith("⚽", StringComparison.Ordinal)) return false;
        if (title.StartsWith("⚠️", StringComparison.Ordinal)) return false;
        if (title.StartsWith("🔧", StringComparison.Ordinal)) return false;
        return title.Contains(" vs ", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IEnumerable<SocketGuildUser>> GetPlayersWithRoleAsync()
    {
        var guild = await GetGuildAsync();
        if (guild == null) return Enumerable.Empty<SocketGuildUser>();

        var playerRole = GetPlayerRole(guild);
        if (playerRole == null) return Enumerable.Empty<SocketGuildUser>();

        // Download all users to cache if not already downloaded
        // This ensures we get all users, not just those in cache
        try
        {
            await guild.DownloadUsersAsync();
            _logger.LogDebug("Downloaded all users for guild {GuildId}", guild.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download all users for guild {GuildId}, using cached users only", guild.Id);
        }

        // Get all users with the player role
        var players = guild.Users.Where(u => u.Roles.Any(r => r.Id == playerRole.Id)).ToList();
        
        _logger.LogInformation("Found {Count} users with role '{RoleName}'", players.Count, _settings.PlayerRoleName);
        
        return players;
    }
}

