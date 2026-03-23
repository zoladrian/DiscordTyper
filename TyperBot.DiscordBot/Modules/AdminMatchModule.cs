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

public class AdminMatchModule : BaseAdminModule
{
    private readonly ILogger<AdminMatchModule> _logger;
    private readonly IMatchRepository _matchRepository;
    private readonly IRoundRepository _roundRepository;
    private readonly MatchManagementService _matchService;
    private readonly MatchCreationService _matchCreationService;
    private readonly MatchResultHandler _matchResultHandler;
    private readonly AdminMatchCreationStateService _stateService;
    private readonly MatchCardService _matchCardService;
    private readonly DiscordLookupService _lookupService;

    public AdminMatchModule(
        ILogger<AdminMatchModule> logger,
        IOptions<DiscordSettings> settings,
        IMatchRepository matchRepository,
        IRoundRepository roundRepository,
        MatchManagementService matchService,
        MatchCreationService matchCreationService,
        MatchResultHandler matchResultHandler,
        AdminMatchCreationStateService stateService,
        MatchCardService matchCardService,
        DiscordLookupService lookupService) : base(settings.Value)
    {
        _logger = logger;
        _matchRepository = matchRepository;
        _roundRepository = roundRepository;
        _matchService = matchService;
        _matchCreationService = matchCreationService;
        _matchResultHandler = matchResultHandler;
        _stateService = stateService;
        _matchCardService = matchCardService;
        _lookupService = lookupService;
    }

    [ComponentInteraction("admin_add_match")]
    public async Task HandleAddMatchButtonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        await RespondWithModalAsync<AddMatchModalV2>("admin_add_match_modal_v2");
    }

    [ModalInteraction("admin_add_match_modal_v2", true)]
    public async Task HandleAddMatchModalV2Async(AddMatchModalV2 modal)
    {
        await _matchCreationService.HandleAddMatchModalAsync(Context, modal.RoundNumber, modal.HomeTeam, modal.AwayTeam, modal.MatchDate, modal.MatchTime);
    }

    [ComponentInteraction("admin_edit_match_*")]
    public async Task HandleEditMatchButtonAsync(string matchIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
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

        var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(match.StartTime.UtcDateTime, tz);
        var deadlineTime = match.TypingDeadline.HasValue 
            ? TimeZoneInfo.ConvertTimeFromUtc(match.TypingDeadline.Value.UtcDateTime, tz) 
            : (DateTime?)null;
        
        var modal = new EditMatchModal
        {
            HomeTeam = match.HomeTeam,
            AwayTeam = match.AwayTeam,
            Date = localTime.ToString("yyyy-MM-dd"),
            Time = localTime.ToString("HH:mm"),
            TypingDeadline = deadlineTime?.ToString("yyyy-MM-dd HH:mm") ?? ""
        };

        await RespondWithModalAsync($"admin_edit_match_modal_{matchId}", modal);
    }

    [ModalInteraction("admin_edit_match_modal_*", true)]
    public async Task HandleEditMatchModalAsync(string matchIdStr, EditMatchModal modal)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Nieprawidłowy mecz.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await FollowupAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        var oldHomeTeam = match.HomeTeam;
        var oldAwayTeam = match.AwayTeam;
        var oldStartTime = match.StartTime;
        var oldRound = match.Round;
        var oldRoundNum = oldRound?.Number ?? 0;

        IUserMessage? messageToUpdate = null;
        SocketThreadChannel? threadToUpdate = null;
        var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
        if (predictionsChannel != null && oldRound != null)
        {
            var oldRoundLabel = Application.Services.RoundHelper.GetRoundLabel(oldRoundNum);
            var oldThreadName = $"{oldRoundLabel}: {oldHomeTeam} vs {oldAwayTeam}";
            threadToUpdate = predictionsChannel.Threads.FirstOrDefault(t => t.Name == oldThreadName);
            if (threadToUpdate != null)
            {
                messageToUpdate = await _lookupService.FindMatchCardMessageAsync(threadToUpdate);
            }
        }

        _logger.LogInformation(
            "Edit match modal submitted - User: {Username}, Match ID: {MatchId}, Old: {OldHome} vs {OldAway}, New: {NewHome} vs {NewAway}",
            Context.User.Username, matchId, oldHomeTeam, oldAwayTeam, modal.HomeTeam, modal.AwayTeam);

        DateTimeOffset startTime;
        try
        {
            if (!DateTime.TryParse($"{modal.Date} {modal.Time}", out var localTime))
            {
                await FollowupAsync("❌ Nieprawidłowy format daty lub godziny. Użyj YYYY-MM-DD dla daty i HH:MM dla godziny.", ephemeral: true);
                return;
            }

            var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
            var localDateTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
            startTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception parsing date/time in edit - Match ID: {MatchId}", matchId);
            await FollowupAsync("❌ Błąd podczas parsowania daty/godziny.", ephemeral: true);
            return;
        }

        DateTimeOffset? typingDeadline = null;
        if (!string.IsNullOrWhiteSpace(modal.TypingDeadline))
        {
            try
            {
                if (DateTime.TryParse(modal.TypingDeadline, out var localDeadlineTime))
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
                    var localDeadlineDateTime = DateTime.SpecifyKind(localDeadlineTime, DateTimeKind.Unspecified);
                    typingDeadline = TimeZoneInfo.ConvertTimeToUtc(localDeadlineDateTime, tz);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse typing deadline, using default");
            }
        }

        match.HomeTeam = modal.HomeTeam;
        match.AwayTeam = modal.AwayTeam;
        if (match.StartTime != startTime)
        {
            match.PredictionsRevealed = false;
        }
        match.StartTime = startTime;
        match.TypingDeadline = typingDeadline;
        await _matchRepository.UpdateAsync(match);

        var round = match.Round;
        var roundNum = round?.Number ?? 0;
        var roundLabel = Application.Services.RoundHelper.GetRoundLabel(roundNum);
        var newThreadName = $"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}";

        IUserMessage? cardMessage = messageToUpdate;
        SocketThreadChannel? targetThread = threadToUpdate;

        if (cardMessage == null && startTime > DateTimeOffset.UtcNow && predictionsChannel != null)
        {
            if (match.ThreadId.HasValue)
            {
                targetThread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
                if (targetThread != null)
                    cardMessage = await _lookupService.FindMatchCardMessageAsync(targetThread);
            }

            if (targetThread == null)
            {
                var searchThreadName = newThreadName.Length > 100 ? newThreadName.Substring(0, 97) + "..." : newThreadName;
                var existingThread = predictionsChannel.Threads.FirstOrDefault(t => t.Name == searchThreadName);
                if (existingThread != null)
                {
                    targetThread = existingThread;
                    cardMessage = await _lookupService.FindMatchCardMessageAsync(existingThread);
                    if (!match.ThreadId.HasValue)
                    {
                        match.ThreadId = existingThread.Id;
                        await _matchRepository.UpdateAsync(match);
                    }
                }
            }
        }

        if (cardMessage != null)
            await _matchCardService.PostMatchCardAsync(match, roundNum, cardMessage);
        else if (startTime > DateTimeOffset.UtcNow)
            await _matchCardService.PostMatchCardAsync(match, roundNum, null);

        if (targetThread != null)
        {
            var validatedThreadName = newThreadName.Length > 100 ? newThreadName.Substring(0, 97) + "..." : newThreadName;
            if (targetThread.Name != validatedThreadName)
            {
                try
                {
                    await targetThread.ModifyAsync(props => props.Name = validatedThreadName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update thread name - Thread ID: {ThreadId}", targetThread.Id);
                }
            }
        }

        _logger.LogInformation(
            "Match updated - ID: {MatchId}, {NewHome} vs {NewAway}, StartTime: {NewTime}",
            matchId, match.HomeTeam, match.AwayTeam, startTime);

        await FollowupAsync("✅ Mecz został zaktualizowany.", ephemeral: true);
    }

    [ComponentInteraction("admin_cancel_match_*")]
    public async Task HandleCancelMatchButtonAsync(string matchIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Nieprawidłowy mecz.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await FollowupAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("🚫 Odwołaj mecz")
            .WithDescription($"Czy na pewno chcesz odwołać mecz **{match.HomeTeam} vs {match.AwayTeam}**?\n\n" +
                           "Mecz zostanie oznaczony jako odwołany. Typy graczy zostaną zachowane.")
            .WithColor(Color.Orange);

        var confirmButton = new ButtonBuilder()
            .WithCustomId($"admin_confirm_cancel_match_{match.Id}")
            .WithLabel("✅ Tak, odwołaj mecz")
            .WithStyle(ButtonStyle.Danger);

        var cancelButton = new ButtonBuilder()
            .WithCustomId($"admin_cancel_action_{match.Id}")
            .WithLabel("❌ Anuluj")
            .WithStyle(ButtonStyle.Secondary);

        var component = new ComponentBuilder()
            .WithButton(confirmButton, row: 0)
            .WithButton(cancelButton, row: 0)
            .Build();

        await FollowupAsync(embed: embed.Build(), components: component, ephemeral: true);
    }

    [ComponentInteraction("admin_add_match_to_round_*")]
    public async Task HandleAddMatchToRoundAsync(string roundIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(roundIdStr, out var roundId))
        {
            await RespondAsync("❌ Nieprawidłowy identyfikator kolejki.", ephemeral: true);
            return;
        }

        var round = await _roundRepository.GetByIdAsync(roundId);
        if (round == null)
        {
            await RespondAsync("❌ Kolejka nie znaleziona.", ephemeral: true);
            return;
        }

        var modal = new AddMatchModalV2
        {
            RoundNumber = round.Number.ToString(),
            MatchDate = DateTime.Now.ToString("yyyy-MM-dd"),
            MatchTime = "18:00",
            HomeTeam = "Motor Lublin",
            AwayTeam = "Włókniarz Częstochowa"
        };

        try
        {
            await RespondWithModalAsync("admin_add_match_modal_v2", modal);
        }
        catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Interaction expired before opening add-match-to-round modal");
        }
    }

    [ComponentInteraction("admin_delete_match_*")]
    public async Task HandleDeleteMatchButtonAsync(string matchIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
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

        var embed = new EmbedBuilder()
            .WithTitle("🗑️ Usuń mecz")
            .WithDescription($"Czy na pewno chcesz usunąć mecz **{match.HomeTeam} vs {match.AwayTeam}**?")
            .WithColor(Color.Red);

        var cancelMatchButton = new ButtonBuilder()
            .WithCustomId($"admin_confirm_cancel_match_{match.Id}")
            .WithLabel("🚫 Odwołaj (Status: Cancelled)")
            .WithStyle(ButtonStyle.Secondary);

        var hardDeleteButton = new ButtonBuilder()
            .WithCustomId($"admin_confirm_hard_delete_match_{match.Id}")
            .WithLabel("💥 Usuń trwale (z bazy)")
            .WithStyle(ButtonStyle.Danger);

        var component = new ComponentBuilder()
            .WithButton(cancelMatchButton)
            .WithButton(hardDeleteButton)
            .Build();

        await FollowupAsync(embed: embed.Build(), components: component, ephemeral: true);
    }

    [ComponentInteraction("admin_confirm_cancel_match_*")]
    public async Task HandleConfirmCancelMatchAsync(string matchIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }
        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Nieprawidłowy mecz.", ephemeral: true);
            return;
        }
        await _matchResultHandler.HandleCancelMatchAsync(Context, matchId);
    }

    [ComponentInteraction("admin_restore_match_*")]
    public async Task HandleRestoreMatchAsync(string matchIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }
        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Nieprawidłowy mecz.", ephemeral: true);
            return;
        }
        await _matchResultHandler.HandleRestoreMatchAsync(Context, matchId);
    }

    [ComponentInteraction("admin_set_cancelled_match_date_*")]
    public async Task HandleSetCancelledMatchDateButtonAsync(string matchIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }
        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Nieprawidłowy mecz.", ephemeral: true);
            return;
        }
        
        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null) return;

        var now = DateTimeOffset.UtcNow;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now.UtcDateTime, tz);
        var defaultDate = localNow.AddDays(1).Date.AddHours(localNow.Hour);

        var modal = new SetCancelledMatchDateModal
        {
            Date = defaultDate.ToString("yyyy-MM-dd"),
            Time = defaultDate.ToString("HH:mm")
        };

        await RespondWithModalAsync<SetCancelledMatchDateModal>($"admin_set_cancelled_match_date_modal_{matchId}", modal);
    }

    [ModalInteraction("admin_set_cancelled_match_date_modal_*", true)]
    public async Task HandleSetCancelledMatchDateModalAsync(string matchIdStr, SetCancelledMatchDateModal modal)
    {
        if (!int.TryParse(matchIdStr, out var matchId)) return;

        DateTimeOffset newStartTime;
        try
        {
            if (!DateTime.TryParse($"{modal.Date} {modal.Time}", out var localTime))
            {
                await RespondAsync("❌ Nieprawidłowy format daty lub godziny.", ephemeral: true);
                return;
            }

            var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
            var localDateTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
            newStartTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);
        }
        catch (Exception)
        {
            await RespondAsync("❌ Błąd podczas parsowania daty/godziny.", ephemeral: true);
            return;
        }

        await _matchResultHandler.HandleSetCancelledMatchDateAsync(Context, matchId, newStartTime);
    }

    [ComponentInteraction("admin_confirm_hard_delete_match_*")]
    public async Task HandleConfirmHardDeleteMatchAsync(string matchIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }
        if (!int.TryParse(matchIdStr, out var matchId))
        {
            await RespondAsync("❌ Nieprawidłowy mecz.", ephemeral: true);
            return;
        }
        await _matchResultHandler.HandleHardDeleteMatchAsync(Context, matchId);
    }
}
