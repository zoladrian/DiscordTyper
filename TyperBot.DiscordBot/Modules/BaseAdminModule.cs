using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TyperBot.DiscordBot.Models;

namespace TyperBot.DiscordBot.Modules;

public abstract class BaseAdminModule : InteractionModuleBase<SocketInteractionContext>
{
    protected readonly DiscordSettings Settings;

    protected BaseAdminModule(DiscordSettings settings)
    {
        Settings = settings;
    }

    protected bool IsAdmin(SocketGuildUser? user)
    {
        if (user == null) return false;
        
        // Check for admin role
        if (user.Roles.Any(r => r.Name == Settings.AdminRoleName))
        {
            return true;
        }
        
        // Check for Discord Administrator permission
        if (user.GuildPermissions.Administrator)
        {
            return true;
        }
        
        return false;
    }

    protected bool IsAllowedChannel(SocketTextChannel? channel)
    {
        if (channel == null) return true; // Allow in DMs for testing
        
        var user = Context.User as SocketGuildUser;
        // Administrators can use commands anywhere
        if (IsAdmin(user))
        {
            return true;
        }
        
        var allowedChannels = new[] 
        { 
            Settings.Channels.AdminChannel,
            Settings.Channels.PredictionsChannel 
        };
        
        return allowedChannels.Contains(channel.Name);
    }

    protected async Task RespondWithErrorAsync(string message, string? details = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle("❌ Błąd")
            .WithDescription(message)
            .WithColor(Color.Red);
        
        if (!string.IsNullOrEmpty(details))
        {
            embed.AddField("Szczegóły", details, false);
        }
        
        if (Context.Interaction.HasResponded)
        {
            await FollowupAsync(embed: embed.Build(), ephemeral: true);
        }
        else
        {
            await RespondAsync(embed: embed.Build(), ephemeral: true);
        }
    }

    protected async Task RespondWithSuccessAsync(string message)
    {
        var embed = new EmbedBuilder()
            .WithTitle("✅ Sukces")
            .WithDescription(message)
            .WithColor(Color.Green);
        
        if (Context.Interaction.HasResponded)
        {
            await FollowupAsync(embed: embed.Build(), ephemeral: true);
        }
        else
        {
            await RespondAsync(embed: embed.Build(), ephemeral: true);
        }
    }
}
