using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.DiscordBot.Models;

namespace TyperBot.DiscordBot.Services;

public class DiscordBotService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly DiscordSettings _settings;
    private readonly DiscordLookupService _lookupService;

    public DiscordBotService(
        DiscordSocketClient client,
        InteractionService interactionService,
        IServiceProvider serviceProvider,
        ILogger<DiscordBotService> logger,
        IOptions<DiscordSettings> settings,
        DiscordLookupService lookupService)
    {
        _client = client;
        _interactionService = interactionService;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;
        _lookupService = lookupService;

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.InteractionCreated += HandleInteractionAsync;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Discord Bot Service...");

        // Load interaction modules
        await _interactionService.AddModulesAsync(typeof(DiscordBotService).Assembly, _serviceProvider);

        // Login and start
        await _client.LoginAsync(TokenType.Bot, _settings.Token);
        await _client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Discord Bot Service...");
        await _client.StopAsync();
        await _client.LogoutAsync();
    }

    private Task LogAsync(LogMessage log)
    {
        var logLevel = log.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        _logger.Log(logLevel, log.Exception, "[{Source}] {Message}", log.Source, log.Message);
        return Task.CompletedTask;
    }

    private async Task ReadyAsync()
    {
        _logger.LogInformation("Bot connected as {Username}#{Discriminator}", 
            _client.CurrentUser.Username, 
            _client.CurrentUser.Discriminator);

        // Resolve and log guild, channels, and roles
        var guild = await _lookupService.GetGuildAsync();
        if (guild != null)
        {
            _logger.LogInformation("Connected to guild: {GuildName} (ID: {GuildId})", guild.Name, guild.Id);
        }

        var adminChannel = await _lookupService.GetAdminChannelAsync();
        if (adminChannel != null)
        {
            _logger.LogInformation("Admin channel found: #{ChannelName}", adminChannel.Name);
        }

        var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
        if (predictionsChannel != null)
        {
            _logger.LogInformation("Predictions channel found: #{ChannelName}", predictionsChannel.Name);
        }

        var resultsChannel = await _lookupService.GetResultsChannelAsync();
        if (resultsChannel != null)
        {
            _logger.LogInformation("Results channel found: #{ChannelName}", resultsChannel.Name);
        }

        if (guild != null)
        {
            var playerRole = _lookupService.GetPlayerRole(guild);
            if (playerRole != null)
            {
                _logger.LogInformation("Player role found: {RoleName}", playerRole.Name);
            }

            var adminRole = _lookupService.GetAdminRole(guild);
            if (adminRole != null)
            {
                _logger.LogInformation("Admin role found: {RoleName}", adminRole.Name);
            }
        }

        // Register commands to guild (not global)
        if (_settings.GuildId != 0)
        {
            await _interactionService.RegisterCommandsToGuildAsync(_settings.GuildId);
            _logger.LogInformation("Registered commands to guild {GuildId}", _settings.GuildId);
        }
        else
        {
            _logger.LogWarning("GuildId is 0, commands not registered");
        }
    }

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(_client, interaction);
            await _interactionService.ExecuteCommandAsync(context, _serviceProvider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling interaction of type {InteractionType}", interaction.Type);

            if (!interaction.HasResponded)
            {
                try
                {
                    var errorMessage = "❌ Wystąpił błąd. Spróbuj ponownie.";
                    switch (interaction)
                    {
                        case SocketModal modalInteraction:
                            errorMessage = "❌ Wystąpił błąd podczas przetwarzania formularza. Spróbuj ponownie.";
                            break;
                        case SocketMessageComponent:
                            errorMessage = "❌ Wystąpił błąd podczas przetwarzania przycisku. Spróbuj ponownie.";
                            break;
                        case IApplicationCommandInteraction:
                            errorMessage = "❌ Wystąpił błąd podczas przetwarzania komendy. Spróbuj ponownie.";
                            break;
                    }
                    await interaction.RespondAsync(errorMessage, ephemeral: true);
                }
                catch (Exception respondEx)
                {
                    _logger.LogError(respondEx, "Failed to send error response to user");
                }
            }
        }
    }
}

