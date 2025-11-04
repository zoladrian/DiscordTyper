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
// Find appsettings.json in multiple possible locations
var basePath = AppContext.BaseDirectory;

// Try to find project directory (go up from bin/Debug/net9.0 to project root)
var searchPaths = new[]
{
    basePath, // Output directory
    Path.GetFullPath(Path.Combine(basePath, "..", "..", "..")), // Project directory (from bin/Debug/net9.0)
    Directory.GetCurrentDirectory(), // Current working directory
    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "TyperBot.DiscordBot")) // Project directory from solution root
};

string? appsettingsPath = null;
foreach (var path in searchPaths)
{
    var testPath = Path.Combine(path, "appsettings.json");
    if (File.Exists(testPath))
    {
        appsettingsPath = path;
        break;
    }
}

if (appsettingsPath == null)
{
    throw new FileNotFoundException("appsettings.json not found in any of the expected locations.");
}

builder.Configuration
    .SetBasePath(appsettingsPath)
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
builder.Services.AddSingleton<AdminMatchCreationStateService>();

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
