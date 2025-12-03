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
    private readonly IPredictionRepository _predictionRepository;

    public PredictionModule(
        ILogger<PredictionModule> logger,
        IOptions<DiscordSettings> settings,
        DiscordLookupService lookupService,
        PredictionService predictionService,
        IPlayerRepository playerRepository,
        IMatchRepository matchRepository,
        IPredictionRepository predictionRepository)
    {
        _logger = logger;
        _settings = settings.Value;
        _lookupService = lookupService;
        _predictionService = predictionService;
        _playerRepository = playerRepository;
        _matchRepository = matchRepository;
        _predictionRepository = predictionRepository;
    }

    private bool HasPlayerRole(SocketGuildUser? user)
    {
        if (user == null) return false;
        return user.Roles.Any(r => r.Name == _settings.PlayerRoleName);
    }

    private async Task<Domain.Entities.Player?> EnsurePlayerExistsAsync(ulong discordUserId, string discordUsername)
    {
        var player = await _playerRepository.GetByDiscordUserIdAsync(discordUserId);
        if (player == null)
        {
            // Create player
            player = new Domain.Entities.Player
            {
                DiscordUserId = discordUserId,
                DiscordUsername = discordUsername,
                IsActive = true
            };
            player = await _playerRepository.AddAsync(player);
            _logger.LogInformation("Created new player: {DiscordUserId} ({DiscordUsername})", player.DiscordUserId, player.DiscordUsername);
        }
        return player;
    }

    private async Task PostPredictionMessageInThreadAsync(Domain.Entities.Match match, SocketGuildUser user, bool isUpdate)
    {
        try
        {
            // Validate match hasn't started yet
            if (DateTimeOffset.UtcNow >= match.StartTime)
            {
                _logger.LogWarning("Próba wysłania wiadomości o typie po rozpoczęciu meczu - Mecz ID: {MatchId}", match.Id);
                return;
            }

            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel == null) return;

            // Use ThreadId if available, otherwise fall back to name search
            SocketThreadChannel? thread = null;
            if (match.ThreadId.HasValue)
            {
                thread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
            }
            
            // Fallback to name search if ThreadId not found or not set
            if (thread == null)
            {
                var roundLabel = Application.Services.RoundHelper.GetRoundLabel(match.Round?.Number ?? 0);
                var threadName = $"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}";
                thread = predictionsChannel.Threads.FirstOrDefault(t => t.Name == threadName);
            }
            
            if (thread == null) return;

            string message;
            if (isUpdate)
            {
                // Random message for update
                var updateMessages = new[]
                {
                    $"{user.Username} wydygał i zmienił wynik",
                    $"{user.Username} obsrał się i zmienił wynik",
                    $"{user.Username} obsmarkał się i zmienił wynik",
                    $"{user.Username} obsrał zbroje i zmienił wynik"
                };
                message = updateMessages[Random.Shared.Next(updateMessages.Length)];
            }
            else
            {
                // Random message for new prediction
                var newMessages = new[]
                {
                    $"{user.Username} obstawił",
                    $"{user.Username} zatypował",
                    $"{user.Username} wpisał wynik",
                    $"{user.Username} postawil",
                    $"{user.Username} zatypował wynik"
                };
                message = newMessages[Random.Shared.Next(newMessages.Length)];
            }

            await thread.SendMessageAsync(message);
            _logger.LogInformation(
                "Wiadomość o typie opublikowana w wątku - Użytkownik: {Username} (ID: {UserId}), Mecz ID: {MatchId}, Update: {IsUpdate}",
                user.Username, user.Id, match.Id, isUpdate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się opublikować wiadomości w wątku meczu");
        }
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

        // Allow postponed matches to be predicted before original start time
        if (match.Status == MatchStatus.Postponed)
        {
            if (DateTimeOffset.UtcNow >= match.StartTime)
            {
                await RespondAsync("❌ Typowanie dla tego meczu zostało zamknięte (mecz przełożony, ale minęła pierwotna godzina rozpoczęcia).", ephemeral: true);
                return;
            }
            // Allow prediction for postponed matches before original start time
        }
        else if (DateTimeOffset.UtcNow >= match.StartTime)
        {
            await RespondAsync("❌ Typowanie dla tego meczu zostało zamknięte.", ephemeral: true);
            return;
        }

        // Ensure player exists
        var player = await EnsurePlayerExistsAsync(user!.Id, user.Username);
        if (player == null)
        {
            await RespondAsync("❌ Nie udało się utworzyć gracza. Spróbuj ponownie.", ephemeral: true);
            return;
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

    [ModalInteraction("predict_match_modal_*", true)]
    public async Task HandlePredictModalAsync(string matchIdStr, PredictionModal modal)
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

        if (!int.TryParse(modal.HomePoints, out homeTip) || !int.TryParse(modal.AwayPoints, out awayTip))
        {
            hasInvalidInput = true;
            invalidInputError = "Wprowadź prawidłowe liczby dla obu wyników.";
        }
        else if (homeTip + awayTip != 90)
        {
            hasInvalidInput = true;
            invalidInputError = $"Suma wyniku musi wynosić 90, a nie {homeTip + awayTip}. Oglądałeś kiedyś żużel?";
        }

        if (hasInvalidInput)
        {
            // Post public message in match thread when sum != 90
            if (homeTip + awayTip != 90)
            {
                try
                {
                    var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
                    if (predictionsChannel != null)
                    {
                        SocketThreadChannel? thread = null;
                        
                        // Use ThreadId if available, otherwise fall back to name search
                        if (match.ThreadId.HasValue)
                        {
                            thread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
                        }
                        
                        // Fallback to name search if ThreadId not found or not set
                        if (thread == null)
                        {
                            var roundLabel = Application.Services.RoundHelper.GetRoundLabel(match.Round?.Number ?? 0);
                            var threadName = $"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}";
                            thread = predictionsChannel.Threads.FirstOrDefault(t => t.Name == threadName);
                        }
                        
                        if (thread != null)
                        {
                            await thread.SendMessageAsync($"{user!.Username} próbował zatypować jak skończony imbecyl");
                            _logger.LogInformation(
                                "Publiczne oznaczenie użytkownika przy błędzie sumy - Użytkownik: {Username} (ID: {UserId}), Mecz ID: {MatchId}, Typ: {Home}:{Away}, Suma: {Sum}",
                                user.Username, user.Id, matchId, modal.HomePoints, modal.AwayPoints, homeTip + awayTip);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Nie udało się wysłać publicznej wiadomości o błędzie sumy");
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

        // Ensure player exists
        var player = await EnsurePlayerExistsAsync(user!.Id, user.Username);
        if (player == null)
        {
            await RespondAsync("❌ Nie udało się utworzyć gracza. Spróbuj ponownie.", ephemeral: true);
            return;
        }

        // Check if this is an update (prediction already exists)
        var existingPrediction = await _predictionRepository.GetByMatchAndPlayerAsync(matchId, player.Id);
        var isUpdate = existingPrediction != null;

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

        // Post message in match thread
        await PostPredictionMessageInThreadAsync(match, user, isUpdate);
    }
}

public class PredictionModal : IModal
{
    public string Title => "Złóż swój typ";

    [InputLabel("Punkty drużyny domowej")]
    [ModalTextInput("home_points", TextInputStyle.Short, placeholder: "50", minLength: 1, maxLength: 3)]
    [RequiredInput(true)]
    public string HomePoints { get; set; } = string.Empty;

    [InputLabel("Punkty drużyny wyjazdowej")]
    [ModalTextInput("away_points", TextInputStyle.Short, placeholder: "40", minLength: 1, maxLength: 3)]
    [RequiredInput(true)]
    public string AwayPoints { get; set; } = string.Empty;
}

