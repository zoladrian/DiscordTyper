using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq;
using System.Reflection;
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
        
        // Hook up InteractionService command executed event for better error handling
        _interactionService.InteractionExecuted += InteractionExecutedAsync;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Discord Bot Service...");

        // Load interaction modules
        var modules = await _interactionService.AddModulesAsync(typeof(DiscordBotService).Assembly, _serviceProvider);
        var moduleList = modules.ToList();
        _logger.LogInformation("═══════════════════════════════════════════════════════════");
        _logger.LogInformation("Loaded {Count} interaction module(s):", moduleList.Count);
        foreach (var module in moduleList)
        {
            _logger.LogInformation("   - {ModuleName} ({CommandCount} commands, {ModalCount} modals, {ComponentCount} components)",
                module.Name,
                module.SlashCommands.Count,
                module.ModalCommands.Count,
                module.ComponentCommands.Count
            );
        }
        
        // Log all registered modal handlers
        var modalCommands = _interactionService.Modules
            .SelectMany(m => m.ModalCommands)
            .ToList();
        
        _logger.LogInformation("═══════════════════════════════════════════════════════════");
        _logger.LogInformation("📋 Registered {Count} modal handler(s):", modalCommands.Count);
        foreach (var modalCmd in modalCommands)
        {
            // Get CustomId from ModalInteraction attribute
            var modalAttr = modalCmd.Attributes.OfType<ModalInteractionAttribute>().FirstOrDefault();
            var customId = modalAttr?.CustomId ?? "N/A";
            // Get method name - try to get it from the command info's underlying method via reflection
            var methodName = "Unknown";
            try
            {
                // ModalCommandInfo has a CommandInfo property that might have the method
                var commandInfoType = modalCmd.GetType();
                var methodProperty = commandInfoType.GetProperty("Method", BindingFlags.Public | BindingFlags.Instance);
                if (methodProperty != null)
                {
                    var method = methodProperty.GetValue(modalCmd) as MethodInfo;
                    methodName = method?.Name ?? "Unknown";
                }
            }
            catch
            {
                // Fallback to module name if we can't get method name
                methodName = modalCmd.Module.Name;
            }
            
            _logger.LogInformation("   ✅ Modal: '{CustomId}' → {MethodName} in {ModuleName}", 
                customId, methodName, modalCmd.Module.Name);
        }
        _logger.LogInformation("═══════════════════════════════════════════════════════════");

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

    private Task InteractionExecutedAsync(ICommandInfo commandInfo, IInteractionContext context, IResult result)
    {
        if (!result.IsSuccess)
        {
            _logger.LogError("InteractionExecuted failed: {Error} - {ErrorReason}", result.Error, result.ErrorReason);
            if (result is ExecuteResult executeResult && executeResult.Exception != null)
            {
                _logger.LogError(executeResult.Exception, "Exception details for command: {CommandName}", commandInfo.Name);
            }
        }
        return Task.CompletedTask;
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
        // Log all interactions for debugging
        if (interaction is SocketModal modal)
        {
            _logger.LogInformation(
                "Modal interaction received - CustomId: '{CustomId}', User: {Username} (ID: {UserId}), Guild: {GuildId}, Channel: {ChannelId}",
                modal.Data.CustomId,
                interaction.User.Username,
                interaction.User.Id,
                modal.GuildId ?? 0,
                modal.ChannelId ?? 0);
            
            // Log all modal components
            foreach (var component in modal.Data.Components)
            {
                _logger.LogInformation(
                    "Modal component - CustomId: '{CustomId}', Value: '{Value}'",
                    component.CustomId,
                    component.Value);
            }
        }
        else
        {
            _logger.LogInformation(
                "Interaction received - Type: {Type}, User: {Username} (ID: {UserId}), CustomId: {CustomId}",
                interaction.Type,
                interaction.User.Username,
                interaction.User.Id,
                (interaction as SocketMessageComponent)?.Data?.CustomId ?? (interaction as SocketModal)?.Data?.CustomId ?? "N/A");
        }

        try
        {
            var context = new SocketInteractionContext(_client, interaction);
            
            if (interaction is SocketModal modalInteraction)
            {
                _logger.LogInformation(
                    "🔄 Executing ExecuteCommandAsync for modal '{CustomId}'...",
                    modalInteraction.Data.CustomId);
            }
            
            var result = await _interactionService.ExecuteCommandAsync(context, _serviceProvider);
            
            if (!result.IsSuccess)
            {
                _logger.LogError(
                    "❌❌❌ ExecuteCommandAsync FAILED!");
                _logger.LogError("   Error: {Error}", result.Error);
                _logger.LogError("   ErrorReason: {Reason}", result.ErrorReason);
                
                if (result.Error == InteractionCommandError.UnmetPrecondition)
                {
                    _logger.LogError("   → UnmetPrecondition - Handler may not be found or parameter binding failed!");
                }
                else if (result.Error == InteractionCommandError.Exception)
                {
                    _logger.LogError("   → Exception in handler!");
                    if (result.ErrorReason != null)
                    {
                        _logger.LogError("   → Exception details: {Details}", result.ErrorReason);
                    }
                }
            }
            else
            {
                _logger.LogInformation("✅ ExecuteCommandAsync SUCCESS");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌❌❌ CRITICAL ERROR handling interaction");
            _logger.LogError("═══════════════════════════════════════════════════════════");
            _logger.LogError("Exception Type: {Type}", ex.GetType().FullName);
            _logger.LogError("Exception Message: {Message}", ex.Message);
            _logger.LogError("Interaction Type: {InteractionType}", interaction.Type);
            _logger.LogError("User: {Username} (ID: {UserId})", interaction.User.Username, interaction.User.Id);
            _logger.LogError("Guild: {GuildId}, Channel: {ChannelId}",
                (interaction as SocketSlashCommand)?.GuildId ?? (interaction as SocketMessageComponent)?.GuildId ?? (interaction as SocketModal)?.GuildId ?? 0,
                (interaction as SocketSlashCommand)?.ChannelId ?? (interaction as SocketMessageComponent)?.ChannelId ?? (interaction as SocketModal)?.ChannelId ?? 0);
            _logger.LogError("CustomId: {CustomId}",
                (interaction as SocketMessageComponent)?.Data?.CustomId ?? (interaction as SocketModal)?.Data?.CustomId ?? "N/A");
            _logger.LogError("HasResponded: {HasResponded}", interaction.HasResponded);
            _logger.LogError("═══════════════════════════════════════════════════════════");
            _logger.LogError("Stack Trace:");
            _logger.LogError("{StackTrace}", ex.StackTrace);
            _logger.LogError("═══════════════════════════════════════════════════════════");

            if (!interaction.HasResponded)
            {
                try
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ Błąd")
                        .WithColor(Color.Red);

                    string errorMessage = "Wystąpił nieoczekiwany błąd podczas przetwarzania żądania.";
                    string? details = null;

                    switch (interaction)
                    {
                        case SocketModal modalInteraction:
                            errorMessage = "Wystąpił błąd podczas przetwarzania formularza.";
                            details = "Sprawdź czy wszystkie pola zostały wypełnione poprawnie i spróbuj ponownie.";
                            break;
                        case SocketMessageComponent componentInteraction:
                            errorMessage = "Wystąpił błąd podczas przetwarzania akcji.";
                            details = "Spróbuj ponownie lub użyj komendy /panel-admina.";
                            break;
                        case IApplicationCommandInteraction commandInteraction:
                            errorMessage = "Wystąpił błąd podczas wykonywania komendy.";
                            details = "Sprawdź logi lub skontaktuj się z administratorem.";
                            break;
                    }

                    // Add exception message if it's user-friendly (not stack trace)
                    if (!string.IsNullOrEmpty(ex.Message) && !ex.Message.Contains("at ") && ex.Message.Length < 200)
                    {
                        details = details != null ? $"{details}\n\nSzczegóły: {ex.Message}" : ex.Message;
                    }

                    errorEmbed.WithDescription(errorMessage);
                    if (details != null)
                    {
                        errorEmbed.AddField("Szczegóły", details, false);
                    }

                    await interaction.RespondAsync(embed: errorEmbed.Build(), ephemeral: true);
                }
                catch (Exception respondEx)
                {
                    _logger.LogError(respondEx, 
                        "Failed to send error response to user - InteractionType: {Type}, UserId: {UserId}",
                        interaction.Type, interaction.User.Id);
                }
            }
            else
            {
                // Try to send followup if already responded
                try
                {
                    if (interaction is SocketInteraction socketInteraction)
                    {
                        var errorEmbed = new EmbedBuilder()
                            .WithTitle("❌ Błąd")
                            .WithDescription("Wystąpił błąd podczas przetwarzania żądania.")
                            .WithColor(Color.Red)
                            .Build();
                        await socketInteraction.FollowupAsync(embed: errorEmbed, ephemeral: true);
                    }
                }
                catch (Exception followupEx)
                {
                    _logger.LogError(followupEx, "Failed to send error followup");
                }
            }
        }
    }
}

