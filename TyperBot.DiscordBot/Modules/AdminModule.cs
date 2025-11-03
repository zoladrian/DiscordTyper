using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.DiscordBot.Models;
using TyperBot.DiscordBot.Services;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Modules;

public class AdminModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger<AdminModule> _logger;
    private readonly DiscordSettings _settings;
    private readonly DiscordLookupService _lookupService;
    private readonly MatchManagementService _matchService;
    private readonly IMatchRepository _matchRepository;
    private readonly PredictionService _predictionService;
    private readonly TableGenerator _tableGenerator;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly ExportService _exportService;
    private readonly IRoundRepository _roundRepository;

    public AdminModule(
        ILogger<AdminModule> logger,
        IOptions<DiscordSettings> settings,
        DiscordLookupService lookupService,
        MatchManagementService matchService,
        IMatchRepository matchRepository,
        PredictionService predictionService,
        TableGenerator tableGenerator,
        ISeasonRepository seasonRepository,
        IPlayerRepository playerRepository,
        ExportService exportService,
        IRoundRepository roundRepository)
    {
        _logger = logger;
        _settings = settings.Value;
        _lookupService = lookupService;
        _matchService = matchService;
        _matchRepository = matchRepository;
        _predictionService = predictionService;
        _tableGenerator = tableGenerator;
        _seasonRepository = seasonRepository;
        _playerRepository = playerRepository;
        _exportService = exportService;
        _roundRepository = roundRepository;
    }

    private bool IsAdmin(SocketGuildUser? user)
    {
        if (user == null) return false;
        
        // Check for admin role
        if (user.Roles.Any(r => r.Name == _settings.AdminRoleName))
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

    [SlashCommand("admin-panel", "Open the Typer admin panel.")]
    public async Task AdminPanelAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondAsync("❌ You don't have permission to use this command.", ephemeral: true);
            return;
        }
        
        _logger.LogInformation("Admin panel opened by {User}", Context.User.Username);

        var embed = new EmbedBuilder()
            .WithTitle("Typer Admin Panel")
            .WithDescription("Use the buttons below to manage matches and view statistics.")
            .WithColor(Color.Gold)
            .Build();

        var button = new ButtonBuilder()
            .WithCustomId("admin_add_match")
            .WithLabel("➕ Add Match")
            .WithStyle(ButtonStyle.Primary);

        var component = new ComponentBuilder()
            .WithButton(button)
            .Build();

        await RespondAsync(embed: embed, components: component);
    }

    [ComponentInteraction("admin_add_match")]
    public async Task HandleAddMatchButtonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondAsync("❌ You don't have permission to use this command.", ephemeral: true);
            return;
        }
        
        _logger.LogInformation("Add match button clicked by {User}", Context.User.Username);

        var modal = new ModalBuilder()
            .WithTitle("Add Match")
            .WithCustomId("admin_add_match_modal")
            .AddTextInput("Round", "round", TextInputStyle.Short, placeholder: "1", required: true)
            .AddTextInput("Home Team", "home_team", TextInputStyle.Short, placeholder: "Motor Lublin", required: true)
            .AddTextInput("Away Team", "away_team", TextInputStyle.Short, placeholder: "Włókniarz Częstochowa", required: true)
            .AddTextInput("Date", "date", TextInputStyle.Short, placeholder: "2025-12-15", required: true)
            .AddTextInput("Time", "time", TextInputStyle.Short, placeholder: "17:00", required: true)
            .Build();

        await RespondWithModalAsync(modal);
    }

    [ModalInteraction("admin_add_match_modal")]
    public async Task HandleAddMatchModalAsync(string round, string homeTeam, string awayTeam, string date, string time)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondAsync("❌ You don't have permission to use this command.", ephemeral: true);
            return;
        }
        
        _logger.LogInformation("Add match modal submitted by {User}", Context.User.Username);

        // Parse and validate round
        if (!int.TryParse(round, out var roundNum))
        {
            await RespondAsync("❌ Invalid round number. Please enter a valid number.", ephemeral: true);
            return;
        }

        // Parse date/time
        DateTimeOffset startTime;
        try
        {
            if (!DateTime.TryParse($"{date} {time}", out var localTime))
            {
                await RespondAsync("❌ Invalid date or time format. Use YYYY-MM-DD for date and HH:MM for time.", ephemeral: true);
                return;
            }

            // Convert to Europe/Warsaw timezone, then to UTC for storage
            var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
            var localDateTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
            startTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing date/time");
            await RespondAsync("❌ Error parsing date/time. Please check the format.", ephemeral: true);
            return;
        }

        // Validate start time is in the future
        if (startTime <= DateTimeOffset.UtcNow)
        {
            await RespondAsync("❌ Match start time must be in the future.", ephemeral: true);
            return;
        }

        // Create match
        var (success, error, match) = await _matchService.CreateMatchAsync(roundNum, homeTeam, awayTeam, startTime);
        
        if (!success)
        {
            await RespondAsync($"❌ Error creating match: {error}", ephemeral: true);
            return;
        }

        // Post match card to predictions channel
        await PostMatchCardAsync(match, roundNum);

        await RespondAsync($"✅ Match created and posted to predictions channel!", ephemeral: true);
        _logger.LogInformation("Match created: ID {MatchId}, Round {Round}, {Home} vs {Away}, {StartTime}", 
            match!.Id, roundNum, homeTeam, awayTeam, startTime);
    }

    private async Task PostMatchCardAsync(Domain.Entities.Match match, int roundNum)
    {
        var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
        if (predictionsChannel == null)
        {
            _logger.LogError("Predictions channel not found, cannot post match card");
            return;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(match.StartTime.UtcDateTime, tz);

        var timestamp = ((DateTimeOffset)localTime).ToUnixTimeSeconds();
        var embed = new EmbedBuilder()
            .WithTitle($"Round {roundNum}: {match.HomeTeam} vs {match.AwayTeam}")
            .WithDescription(
                "📋 **Prediction Rules:**\n" +
                "• Predictions are secret (only you see them)\n" +
                "• Sum must equal 90 points (e.g., 50:40, 46:44, 45:45)\n" +
                "• Deadline: Match start time"
            )
            .AddField("🏁 Start Time", $"<t:{timestamp}:F>", inline: true)
            .WithColor(Color.Blue)
            .Build();

        var predictButton = new ButtonBuilder()
            .WithCustomId($"predict_match_{match.Id}")
            .WithLabel("🔢 Predict Result")
            .WithStyle(ButtonStyle.Primary);

        var setResultButton = new ButtonBuilder()
            .WithCustomId($"admin_set_result_{match.Id}")
            .WithLabel("✅ Set Result")
            .WithStyle(ButtonStyle.Success);

        var component = new ComponentBuilder()
            .WithButton(predictButton, row: 0)
            .WithButton(setResultButton, row: 1)
            .Build();

        var thread = await predictionsChannel.CreateThreadAsync(
            name: $"Round {roundNum}: {match.HomeTeam} vs {match.AwayTeam}",
            type: ThreadType.PublicThread
        );

        await thread.SendMessageAsync(embed: embed, components: component);
        _logger.LogInformation("Match card posted to predictions channel: Match ID {MatchId}", match.Id);
    }

    [ComponentInteraction("admin_set_result_*")]
    public async Task HandleSetResultButtonAsync(string matchIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondAsync("❌ You don't have permission to use this command.", ephemeral: true);
            return;
        }

        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Invalid match.", ephemeral: true);
            return;
        }

        var modal = new ModalBuilder()
            .WithTitle("Set Match Result")
            .WithCustomId($"admin_set_result_modal_{matchId}")
            .AddTextInput("Home Score", "home_score", TextInputStyle.Short, placeholder: "50", required: true)
            .AddTextInput("Away Score", "away_score", TextInputStyle.Short, placeholder: "40", required: true)
            .Build();

        await RespondWithModalAsync(modal);
    }

    [ModalInteraction("admin_set_result_modal_*")]
    public async Task HandleSetResultModalAsync(string matchIdStr, string homeScore, string awayScore)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondAsync("❌ You don't have permission to use this command.", ephemeral: true);
            return;
        }

        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Invalid match.", ephemeral: true);
            return;
        }

        if (!int.TryParse(homeScore, out var home) || !int.TryParse(awayScore, out var away))
        {
            await RespondAsync("❌ Please enter valid numbers for both scores.", ephemeral: true);
            return;
        }

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await RespondAsync("❌ Match not found.", ephemeral: true);
            return;
        }

        // Update match result
        match.HomeScore = home;
        match.AwayScore = away;
        match.Status = MatchStatus.Finished;
        await _matchRepository.UpdateAsync(match);

        // Calculate scores for all predictions
        await _predictionService.RecalculateMatchScoresAsync(matchId);

        await RespondAsync($"✅ Result set: **{home}:{away}**\nScores calculated!", ephemeral: true);
        _logger.LogInformation("Match result set: Match {MatchId}, {Home}:{Away}. Scores calculated.", matchId, home, away);

        // Post standings tables
        await PostStandingsAfterResultAsync(match);
    }

    private async Task PostStandingsAfterResultAsync(Domain.Entities.Match match)
    {
        var resultsChannel = await _lookupService.GetResultsChannelAsync();
        if (resultsChannel == null)
        {
            _logger.LogError("Results channel not found, cannot post standings");
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            _logger.LogWarning("No active season found");
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            _logger.LogInformation("No active players, skipping table generation");
            return;
        }

        // Generate round table
        var round = match.Round;
        if (round != null)
        {
            try
            {
                var roundPng = _tableGenerator.GenerateRoundTable(season, round, players);
                await resultsChannel.SendFileAsync(
                    new Discord.FileAttachment(new MemoryStream(roundPng), $"round-{round.Number}-standings.png")
                );
                _logger.LogInformation("Round {Round} standings posted", round.Number);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate round table");
            }
        }

        // Generate season table
        try
        {
            var seasonPng = _tableGenerator.GenerateSeasonTable(season, players);
            await resultsChannel.SendFileAsync(
                new Discord.FileAttachment(new MemoryStream(seasonPng), $"season-standings.png")
            );
            _logger.LogInformation("Season standings posted");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate season table");
        }
    }

    [SlashCommand("admin-export-season", "Export full season data to CSV (admin only)")]
    public async Task ExportSeasonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondAsync("❌ You don't have permission to use this command.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await RespondAsync("❌ No active season found.", ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await RespondAsync("❌ No active players found.", ephemeral: true);
            return;
        }

        try
        {
            var csv = _exportService.ExportSeasonToCsv(season, players);
            await RespondWithFileAsync(
                new Discord.FileAttachment(new MemoryStream(csv), $"season-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv")
            );
            _logger.LogInformation("Season export generated by {User}", Context.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate season export");
            await RespondAsync("❌ Failed to generate season export.", ephemeral: true);
        }
    }

    [SlashCommand("admin-export-round", "Export round data to CSV (admin only)")]
    public async Task ExportRoundAsync([Summary(description: "Round number")] int round)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondAsync("❌ You don't have permission to use this command.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await RespondAsync("❌ No active season found.", ephemeral: true);
            return;
        }

        var roundEntity = await _roundRepository.GetByNumberAsync(season.Id, round);
        if (roundEntity == null)
        {
            await RespondAsync($"❌ Round {round} not found.", ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await RespondAsync("❌ No active players found.", ephemeral: true);
            return;
        }

        try
        {
            var csv = _exportService.ExportRoundToCsv(roundEntity, players);
            await RespondWithFileAsync(
                new Discord.FileAttachment(new MemoryStream(csv), $"round-{round}-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv")
            );
            _logger.LogInformation("Round {Round} export generated by {User}", round, Context.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate round export");
            await RespondAsync("❌ Failed to generate round export.", ephemeral: true);
        }
    }
}

