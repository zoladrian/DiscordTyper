using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.DiscordBot;
using TyperBot.DiscordBot.Constants;
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

    /// <summary>Krótki kod do logów — bez wartości typu (suma/liczby z modala).</summary>
    private static string ValidationRejectionCodeForLog(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage)) return "unknown";
        if (errorMessage.Contains("Suma punktów", StringComparison.Ordinal)) return "sum_not_90";
        if (errorMessage.Contains("większe lub równe 0", StringComparison.Ordinal)) return "scores_non_negative";
        if (errorMessage.Contains("nie znaleziony", StringComparison.Ordinal)) return "match_not_found";
        if (errorMessage.Contains("odwołany", StringComparison.Ordinal)) return "match_cancelled";
        if (errorMessage.Contains("zakończył", StringComparison.Ordinal)) return "match_finished";
        if (errorMessage.Contains("w trakcie rozgrywania", StringComparison.Ordinal)) return "match_in_progress";
        if (errorMessage.Contains("pierwotną godziną", StringComparison.Ordinal)) return "typing_closed_postponed";
        if (errorMessage.Contains("Czas na typowanie minął", StringComparison.Ordinal)) return "typing_closed_deadline";
        return "other";
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
            // Spójnie z walidacją typów: po zamknięciu okna typowania nie publikujemy już komunikatu w wątku
            if (match.Status == MatchStatus.Postponed)
            {
                if (DateTimeOffset.UtcNow >= match.StartTime)
                {
                    _logger.LogWarning("Skipped prediction thread message after postponed cutoff - Match ID: {MatchId}", match.Id);
                    return;
                }
            }
            else
            {
                var typingEnd = match.TypingDeadline ?? match.StartTime;
                if (DateTimeOffset.UtcNow >= typingEnd)
                {
                    _logger.LogWarning("Skipped prediction thread message after typing window - Match ID: {MatchId}", match.Id);
                    return;
                }
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
                "Prediction message posted in thread - User: {Username} (ID: {UserId}), Match ID: {MatchId}, Update: {IsUpdate}",
                user.Username, user.Id, match.Id, isUpdate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message in match thread");
        }
    }

    /// <summary>Posts the public "invalid input" thread message while the interaction scope is still active.</summary>
    private async Task TryPostInvalidPredictionShameAsync(int matchId, SocketGuildUser user)
    {
        try
        {
            var match = await _matchRepository.GetByIdAsync(matchId);
            if (match == null) return;

            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel == null) return;

            SocketThreadChannel? thread = null;
            if (match.ThreadId.HasValue)
                thread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
            if (thread == null)
            {
                var roundLabel = Application.Services.RoundHelper.GetRoundLabel(match.Round?.Number ?? 0);
                var threadName = $"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}";
                thread = predictionsChannel.Threads.FirstOrDefault(t => t.Name == threadName);
            }

            if (thread != null)
            {
                await thread.SendMessageAsync($"{user.Username} próbował zatypować jak skończony imbecyl");
                _logger.LogInformation(
                    "Typ odrzucony — nieprawidłowy format liczb (modal). User: {Username} (ID: {UserId}), MatchId: {MatchId}",
                    user.Username, user.Id, matchId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send public message");
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

        var match = await _matchRepository.GetByIdAsync(matchId, includeRound: false);
        if (match == null)
        {
            await RespondAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        // Validate match status and timing — mirrors PredictionService.ValidatePrediction
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

        if (match.Status == MatchStatus.InProgress)
        {
            await RespondAsync("❌ Ten mecz jest w trakcie rozgrywania.", ephemeral: true);
            return;
        }

        if (match.Status == MatchStatus.Postponed)
        {
            if (DateTimeOffset.UtcNow >= match.StartTime)
            {
                await RespondAsync("❌ Typowanie dla tego meczu zostało zamknięte (mecz przełożony, ale minęła pierwotna godzina rozpoczęcia).", ephemeral: true);
                return;
            }
        }
        else
        {
            var deadline = match.TypingDeadline ?? match.StartTime;
            if (DateTimeOffset.UtcNow >= deadline)
            {
                await RespondAsync("❌ Czas na typowanie minął.", ephemeral: true);
                return;
            }
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
            .AddTextInput(
                DiscordApiLimits.Truncate(match.HomeTeam, DiscordApiLimits.TextInputLabel),
                "home_points",
                TextInputStyle.Short,
                placeholder: DiscordApiLimits.Truncate(match.HomeTeam, DiscordApiLimits.TextInputPlaceholder),
                required: true)
            .AddTextInput(
                DiscordApiLimits.Truncate(match.AwayTeam, DiscordApiLimits.TextInputLabel),
                "away_points",
                TextInputStyle.Short,
                placeholder: DiscordApiLimits.Truncate(match.AwayTeam, DiscordApiLimits.TextInputPlaceholder),
                required: true)
            .Build();

        await RespondWithModalAsync(modal);
    }

    [ComponentInteraction(CustomIds.Prediction.MyMatchPredictionWildcard)]
    public async Task HandleMyMatchPredictionAsync(string matchIdStr)
    {
        var user = Context.User as SocketGuildUser;

        if (!HasPlayerRole(user))
        {
            await RespondAsync("❌ Musisz mieć rolę Typer, aby korzystać z typera.", ephemeral: true);
            return;
        }

        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Nieprawidłowy mecz.", ephemeral: true);
            return;
        }

        var match = await _matchRepository.GetByIdAsync(matchId, includeRound: false);
        if (match == null)
        {
            await RespondAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        var player = await _playerRepository.GetByDiscordUserIdAsync(user!.Id);
        if (player == null)
        {
            await RespondAsync("❌ Nie złożyłeś jeszcze żadnych typów.", ephemeral: true);
            return;
        }

        var prediction = await _predictionRepository.GetByMatchAndPlayerAsync(matchId, player.Id);
        if (prediction == null)
        {
            await RespondAsync(
                $"❌ Nie złożyłeś typu na mecz **{match.HomeTeam} vs {match.AwayTeam}**.",
                ephemeral: true);
            return;
        }

        await RespondAsync(
            $"Twój typ na **{match.HomeTeam} vs {match.AwayTeam}**: **`{prediction.HomeTip}:{prediction.AwayTip}`**",
            ephemeral: true);
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

        // Parse input before deferring — no DB needed for this check
        if (!int.TryParse(modal.HomePoints, out var homeTip) || !int.TryParse(modal.AwayPoints, out var awayTip))
        {
            await RespondAsync("❌ Wprowadź prawidłowe liczby dla obu wyników. Typ nie został zapisany.", ephemeral: true);
            // Must await: interaction scope (and DbContext) ends when handler returns — no fire-and-forget Task.Run.
            await TryPostInvalidPredictionShameAsync(matchId, user!);
            return;
        }

        await DeferAsync(ephemeral: true);

        var match2 = await _matchRepository.GetByIdAsync(matchId);
        if (match2 == null)
        {
            await FollowupAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        // Validate prediction using service (includes sum=90 validation)
        var (isValid, errorMessage) = await _predictionService.ValidatePrediction(user!.Id, matchId, homeTip, awayTip);
        if (!isValid)
        {
            _logger.LogInformation(
                "Typ odrzucony po walidacji — User: {DiscordUserId}, MatchId: {MatchId}, Powód: {ReasonCode}",
                user!.Id, matchId, ValidationRejectionCodeForLog(errorMessage));
            await FollowupAsync($"❌ {errorMessage}", ephemeral: true);
            return;
        }

        // Ensure player exists
        var player = await EnsurePlayerExistsAsync(user!.Id, user.Username);
        if (player == null)
        {
            await FollowupAsync("❌ Nie udało się utworzyć gracza. Spróbuj ponownie.", ephemeral: true);
            return;
        }

        // Create or update prediction (UpdatedAt != null means it was an existing prediction)
        var prediction = await _predictionService.CreateOrUpdatePredictionAsync(user.Id, matchId, homeTip, awayTip);
        var isUpdate = prediction?.UpdatedAt != null;
        
        if (prediction == null)
        {
            _logger.LogWarning("Zapis typu nieudany (serwis zwrócił null) — User: {DiscordUserId}, MatchId: {MatchId}", user.Id, matchId);
            await FollowupAsync("❌ Nie udało się zapisać typu. Spróbuj ponownie.", ephemeral: true);
            return;
        }

        await FollowupAsync($"✅ Typ zapisany: **{homeTip}:{awayTip}**\nPowodzenia! 🍀", ephemeral: true);
        _logger.LogInformation(
            "Typ zapisany poprawnie — User: {DiscordUserId}, MatchId: {MatchId}, Aktualizacja: {IsUpdate}",
            user.Id, matchId, isUpdate);

        // Post normal message in match thread
        await PostPredictionMessageInThreadAsync(match2, user, isUpdate);
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

