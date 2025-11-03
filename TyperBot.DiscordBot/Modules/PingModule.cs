using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace TyperBot.DiscordBot.Modules;

public class PingModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger<PingModule> _logger;

    public PingModule(ILogger<PingModule> logger)
    {
        _logger = logger;
    }

    [SlashCommand("ping", "Responds with pong")]
    public async Task PingAsync()
    {
        _logger.LogInformation("Ping command received from {User}", Context.User.Username);
        await RespondAsync("pong", ephemeral: true);
    }
}

