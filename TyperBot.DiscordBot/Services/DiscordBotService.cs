using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
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
    private readonly WelcomeMessageService _welcomeMessageService;

    public DiscordBotService(
        DiscordSocketClient client,
        InteractionService interactionService,
        IServiceProvider serviceProvider,
        ILogger<DiscordBotService> logger,
        IOptions<DiscordSettings> settings,
        DiscordLookupService lookupService,
        WelcomeMessageService welcomeMessageService)
    {
        _client = client;
        _interactionService = interactionService;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;
        _lookupService = lookupService;
        _welcomeMessageService = welcomeMessageService;

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
        var moduleName = commandInfo.Module?.Name ?? "?";
        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "InteractionExecuted OK — handler {HandlerName} in {ModuleName}",
                commandInfo.Name,
                moduleName);
        }
        else
        {
            _logger.LogError(
                "InteractionExecuted FAILED — handler {HandlerName} in {ModuleName}: {Error} — {ErrorReason}",
                commandInfo.Name,
                moduleName,
                result.Error,
                result.ErrorReason);
            if (result is ExecuteResult executeResult && executeResult.Exception != null)
            {
                _logger.LogError(executeResult.Exception, "Exception in handler {HandlerName} ({ModuleName})", commandInfo.Name, moduleName);
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

        // Send welcome messages when bot connects to server
        try
        {
            _logger.LogInformation("Checking and sending welcome messages...");
            await _welcomeMessageService.SendWelcomeMessagesIfNeededAsync();
            _logger.LogInformation("Welcome messages check completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome messages during startup");
        }
    }

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        var guildId = interaction switch
        {
            SocketSlashCommand s => s.GuildId,
            SocketMessageComponent c => c.GuildId,
            SocketModal m => m.GuildId,
            _ => null
        };
        var channelId = interaction switch
        {
            SocketSlashCommand s => s.ChannelId,
            SocketMessageComponent c => c.ChannelId,
            SocketModal m => m.ChannelId,
            _ => null
        };

        _logger.LogInformation(
            "Interaction received — Id: {InteractionId}, Type: {Type}, User: {Username} ({UserId}), Guild: {GuildId}, Channel: {ChannelId}, Summary: {Summary}",
            interaction.Id,
            interaction.Type,
            interaction.User.Username,
            interaction.User.Id,
            guildId ?? 0,
            channelId ?? 0,
            DescribeInteraction(interaction));

        if (interaction is SocketModal modalForComponents)
        {
            foreach (var component in modalForComponents.Data.Components)
            {
                _logger.LogDebug(
                    "Modal field — CustomId: '{CustomId}', Value: '{Value}'",
                    component.CustomId,
                    TruncateForLog(component.Value, 200));
            }
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = new SocketInteractionContext(_client, interaction);

            _logger.LogDebug("Dispatching ExecuteCommandAsync for interaction {InteractionId}", interaction.Id);

            var sw = Stopwatch.StartNew();
            var result = await _interactionService.ExecuteCommandAsync(context, scope.ServiceProvider);
            sw.Stop();

            if (!result.IsSuccess)
            {
                _logger.LogError(
                    "ExecuteCommandAsync FAILED — InteractionId: {InteractionId}, ElapsedMs: {ElapsedMs}, Summary: {Summary}",
                    interaction.Id,
                    sw.ElapsedMilliseconds,
                    DescribeInteraction(interaction));
                _logger.LogError("   Error: {Error}", result.Error);
                _logger.LogError("   ErrorReason: {Reason}", result.ErrorReason);
                _logger.LogError("   HasResponded: {HasResponded}", interaction.HasResponded);

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
                _logger.LogInformation(
                    "ExecuteCommandAsync SUCCESS — InteractionId: {InteractionId}, ElapsedMs: {ElapsedMs}, HasResponded: {HasResponded}, Summary: {Summary}",
                    interaction.Id,
                    sw.ElapsedMilliseconds,
                    interaction.HasResponded,
                    DescribeInteraction(interaction));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌❌❌ CRITICAL ERROR handling interaction");
            _logger.LogError("═══════════════════════════════════════════════════════════");
            _logger.LogError("Exception Type: {Type}", ex.GetType().FullName);
            _logger.LogError("Exception Message: {Message}", ex.Message);
            _logger.LogError("Interaction Type: {InteractionType}", interaction.Type);
            _logger.LogError("Interaction Id: {InteractionId}", interaction.Id);
            _logger.LogError("Summary: {Summary}", DescribeInteraction(interaction));
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

    private static string DescribeInteraction(SocketInteraction interaction)
    {
        return interaction switch
        {
            SocketSlashCommand sc => $"Slash /{sc.Data.Name}{FormatSlashOptions(sc.Data.Options)}",
            SocketMessageComponent mc =>
                $"Component {mc.Data.Type} customId={mc.Data.CustomId} messageId={mc.Message?.Id}",
            SocketModal m => $"Modal customId={m.Data.CustomId}",
            SocketUserCommand uc =>
                $"UserCommand {uc.Data.Name} targetUserId={uc.User?.Id} targetUsername={uc.User?.Username}",
            SocketMessageCommand msgc =>
                $"MessageCommand {msgc.Data.Name} messageId={msgc.Data.Message?.Id}",
            _ => interaction.Type.ToString()
        };
    }

    private static string FormatSlashOptions(IReadOnlyCollection<SocketSlashCommandDataOption>? options, int depth = 0)
    {
        if (options == null || options.Count == 0)
            return "";

        if (depth > 4)
            return " (...)";

        var parts = new List<string>();
        foreach (var o in options)
        {
            if (o.Type is ApplicationCommandOptionType.SubCommand or ApplicationCommandOptionType.SubCommandGroup)
            {
                var inner = FormatSlashOptions(o.Options, depth + 1);
                parts.Add(string.IsNullOrEmpty(inner) ? o.Name : $"{o.Name}{inner}");
            }
            else
            {
                parts.Add($"{o.Name}={TruncateForLog(o.Value?.ToString(), 120)}");
            }
        }

        return " " + string.Join(", ", parts);
    }

    private static string TruncateForLog(string? value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Length <= maxLen ? value : value[..maxLen] + "…";
    }
}

