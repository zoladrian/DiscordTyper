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
            await RespondAsync("❌ Musisz mieć rolę Typer, aby składać typy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Nieprawidłowy mecz.", ephemeral: true);
            return;
        }

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await RespondAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        // Validate match status and timing
        if (match.Status == MatchStatus.Finished)
        {
            await RespondAsync("❌ Ten mecz już się zakończył.", ephemeral: true);
            return;
        }

        if (match.Status == MatchStatus.Cancelled)
        {
            await RespondAsync("❌ Ten mecz został odwołany.", ephemeral: true);
            return;
        }

        if (DateTimeOffset.UtcNow >= match.StartTime)
        {
            await RespondAsync("❌ Typowanie dla tego meczu zostało zamknięte.", ephemeral: true);
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
            .WithTitle("Złóż swój typ")
            .WithCustomId($"predict_match_modal_{matchId}")
            .AddTextInput("Punkty drużyny domowej", "home_points", TextInputStyle.Short, placeholder: "50", required: true)
            .AddTextInput("Punkty drużyny wyjazdowej", "away_points", TextInputStyle.Short, placeholder: "40", required: true)
            .Build();

        await RespondWithModalAsync(modal);
    }

    [ModalInteraction("predict_match_modal_*")]
    public async Task HandlePredictModalAsync(string matchIdStr, string homePoints, string awayPoints)
    {
        var user = Context.User as SocketGuildUser;
        
        if (!HasPlayerRole(user))
        {
            await RespondAsync("❌ Musisz mieć rolę Typer, aby składać typy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Nieprawidłowy mecz.", ephemeral: true);
            return;
        }

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await RespondAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        // Check for invalid input (non-numeric or invalid sum)
        bool hasInvalidInput = false;
        string? invalidInputError = null;
        int homeTip = 0;
        int awayTip = 0;

        if (!int.TryParse(homePoints, out homeTip) || !int.TryParse(awayPoints, out awayTip))
        {
            hasInvalidInput = true;
            invalidInputError = "Wprowadź prawidłowe liczby dla obu wyników.";
        }
        else if (homeTip + awayTip != 90)
        {
            hasInvalidInput = true;
            invalidInputError = $"Suma musi wynosić 90, a nie {homeTip + awayTip}.";
        }

        if (hasInvalidInput)
        {
            // Post public message in match thread
            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel != null)
            {
                var roundLabel = Application.Services.RoundHelper.GetRoundLabel(match.Round?.Number ?? 0);
                var threadName = $"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}";
                var thread = predictionsChannel.Threads.FirstOrDefault(t => t.Name == threadName);
                
                if (thread != null)
                {
                    var genderSuffix = user!.Username.EndsWith("a", StringComparison.OrdinalIgnoreCase) ? "a" : "";
                    await thread.SendMessageAsync($"@{user.Username} zatypował{genderSuffix} jak imbecyl 😂");
                    _logger.LogInformation(
                        "Publiczne oznaczenie użytkownika przy błędzie - Użytkownik: {Username} (ID: {UserId}), Mecz ID: {MatchId}, Typ: {Home}:{Away}",
                        user.Username, user.Id, matchId, homePoints, awayPoints);
                }
            }

            await RespondAsync($"❌ {invalidInputError}", ephemeral: true);
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
            await RespondAsync("❌ Nie udało się zapisać typu. Spróbuj ponownie.", ephemeral: true);
            return;
        }

        await RespondAsync($"✅ Typ zapisany: **{homeTip}:{awayTip}**\nPowodzenia! 🍀", ephemeral: true);
        _logger.LogInformation("Prediction saved: User {DiscordUserId}, Match {MatchId}, {Home}:{Away}", 
            user.Id, matchId, homeTip, awayTip);
    }
}

