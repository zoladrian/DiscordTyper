using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TyperBot.Application;
using TyperBot.DiscordBot.Models;
using TyperBot.DiscordBot.Services;
using TyperBot.Infrastructure;
using TyperBot.Infrastructure.Data;

var builder = Host.CreateApplicationBuilder(args);

// Load configuration first
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

// Configure Serilog
builder.Services.AddSerilog((services, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/typerbot-.txt", rollingInterval: RollingInterval.Day));

// Register settings
builder.Services.Configure<DiscordSettings>(
    builder.Configuration.GetSection("Discord"));

// Register infrastructure
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=typerbot.db";
builder.Services.AddInfrastructure(connectionString);

// Register application services
builder.Services.AddApplication();

// Configure Discord client
var discordConfig = new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds,
    AlwaysDownloadUsers = false,
    MessageCacheSize = 100,
    LogLevel = LogSeverity.Info
};

builder.Services.AddSingleton(discordConfig);
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));

// Register Discord services
builder.Services.AddSingleton<DiscordLookupService>();

// Add Discord bot service
builder.Services.AddHostedService<DiscordBotService>();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<TyperContext>();
    context.Database.Migrate();
    Log.Information("Database migrated successfully");
}

Log.Information("Starting TyperBot application");

app.Run();

public partial class Program { }
