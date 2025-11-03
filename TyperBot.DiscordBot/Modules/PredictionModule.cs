using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.DiscordBot.Models;
using TyperBot.DiscordBot.Services;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Modules;

public class PredictionModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger<PredictionModule> _logger;
    private readonly DiscordSettings _settings;
    private readonly DiscordLookupService _lookupService;
    private readonly PredictionService _predictionService;
    private readonly IPlayerRepository _playerRepository;
    private readonly IMatchRepository _matchRepository;

    public PredictionModule(
        ILogger<PredictionModule> logger,
        IOptions<DiscordSettings> settings,
        DiscordLookupService lookupService,
        PredictionService predictionService,
        IPlayerRepository playerRepository,
        IMatchRepository matchRepository)
    {
        _logger = logger;
        _settings = settings.Value;
        _lookupService = lookupService;
        _predictionService = predictionService;
        _playerRepository = playerRepository;
        _matchRepository = matchRepository;
    }

    private bool HasPlayerRole(SocketGuildUser? user)
    {
        if (user == null) return false;
        return user.Roles.Any(r => r.Name == _settings.PlayerRoleName);
    }

    [ComponentInteraction("predict_match_*")]
    public async Task HandlePredictButtonAsync(string matchIdStr)
    {
        var user = Context.User as SocketGuildUser;
        
        if (!HasPlayerRole(user))
        {
            await RespondAsync("❌ You need the Typer role to submit predictions.", ephemeral: true);
            return;
        }

        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Invalid match.", ephemeral: true);
            return;
        }

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await RespondAsync("❌ Match not found.", ephemeral: true);
            return;
        }

        // Validate match status and timing
        if (match.Status == MatchStatus.Finished)
        {
            await RespondAsync("❌ This match has already finished.", ephemeral: true);
            return;
        }

        if (match.Status == MatchStatus.Cancelled)
        {
            await RespondAsync("❌ This match has been cancelled.", ephemeral: true);
            return;
        }

        if (DateTimeOffset.UtcNow >= match.StartTime)
        {
            await RespondAsync("❌ Predictions are now closed for this match.", ephemeral: true);
            return;
        }

        // Ensure player exists
        var player = await _playerRepository.GetByDiscordUserIdAsync(user!.Id);
        if (player == null)
        {
            // Create player
            player = new Player
            {
                DiscordUserId = user.Id,
                DiscordUsername = user.Username,
                IsActive = true
            };
            player = await _playerRepository.AddAsync(player);
            _logger.LogInformation("Created new player: {DiscordUserId} ({DiscordUsername})", player.DiscordUserId, player.DiscordUsername);
        }

        // Show prediction modal
        var modal = new ModalBuilder()
            .WithTitle("Submit your prediction")
            .WithCustomId($"predict_match_modal_{matchId}")
            .AddTextInput("Home Points", "home_points", TextInputStyle.Short, placeholder: "50", required: true)
            .AddTextInput("Away Points", "away_points", TextInputStyle.Short, placeholder: "40", required: true)
            .Build();

        await RespondWithModalAsync(modal);
    }

    [ModalInteraction("predict_match_modal_*")]
    public async Task HandlePredictModalAsync(string matchIdStr, string homePoints, string awayPoints)
    {
        var user = Context.User as SocketGuildUser;
        
        if (!HasPlayerRole(user))
        {
            await RespondAsync("❌ You need the Typer role to submit predictions.", ephemeral: true);
            return;
        }

        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Invalid match.", ephemeral: true);
            return;
        }

        if (!int.TryParse(homePoints, out var homeTip) || !int.TryParse(awayPoints, out var awayTip))
        {
            await RespondAsync("❌ Please enter valid numbers for both scores.", ephemeral: true);
            return;
        }

        // Validate prediction using service
        var (isValid, errorMessage) = await _predictionService.ValidatePrediction(user!.Id, matchId, homeTip, awayTip);
        if (!isValid)
        {
            await RespondAsync($"❌ {errorMessage}", ephemeral: true);
            return;
        }

        // Create or update prediction
        var prediction = await _predictionService.CreateOrUpdatePredictionAsync(user.Id, matchId, homeTip, awayTip);
        
        if (prediction == null)
        {
            await RespondAsync("❌ Failed to save prediction. Please try again.", ephemeral: true);
            return;
        }

        await RespondAsync($"✅ Prediction saved: **{homeTip}:{awayTip}**\nGood luck! 🍀", ephemeral: true);
        _logger.LogInformation("Prediction saved: User {DiscordUserId}, Match {MatchId}, {Home}:{Away}", 
            user.Id, matchId, homeTip, awayTip);
    }
}

