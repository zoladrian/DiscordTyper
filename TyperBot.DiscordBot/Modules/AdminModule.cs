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

/// <summary>
/// Handles match creation wizard (calendar/time UI), batch round creation,
/// and admin-only slash commands for tables and exports.
/// All other admin handlers live in focused modules (AdminMatchModule, AdminResultModule, etc.).
/// </summary>
public class AdminModule : BaseAdminModule
{
    private readonly ILogger<AdminModule> _logger;
    private readonly DiscordLookupService _lookupService;
    private readonly MatchManagementService _matchService;
    private readonly IMatchRepository _matchRepository;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly IPredictionRepository _predictionRepository;
    private readonly ExportService _exportService;
    private readonly IRoundRepository _roundRepository;
    private readonly AdminMatchCreationStateService _stateService;
    private readonly MatchCardService _matchCardService;

    public AdminModule(
        ILogger<AdminModule> logger,
        IOptions<DiscordSettings> settings,
        DiscordLookupService lookupService,
        MatchManagementService matchService,
        IMatchRepository matchRepository,
        ISeasonRepository seasonRepository,
        IPlayerRepository playerRepository,
        IPredictionRepository predictionRepository,
        ExportService exportService,
        IRoundRepository roundRepository,
        AdminMatchCreationStateService stateService,
        MatchCardService matchCardService) : base(settings.Value)
    {
        _logger = logger;
        _lookupService = lookupService;
        _matchService = matchService;
        _matchRepository = matchRepository;
        _seasonRepository = seasonRepository;
        _playerRepository = playerRepository;
        _predictionRepository = predictionRepository;
        _exportService = exportService;
        _roundRepository = roundRepository;
        _stateService = stateService;
        _matchCardService = matchCardService;
    }

    #region Match Creation Wizard (calendar/time UI)

    private async Task ShowAddMatchCalendarAsync()
    {
        if (Context.Guild == null) return;

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null) return;

        var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        int year = state.CurrentCalendarYear > 0 ? state.CurrentCalendarYear : now.Year;
        int month = state.CurrentCalendarMonth > 0 ? state.CurrentCalendarMonth : now.Month;

        var roundOptions = new List<SelectMenuOptionBuilder>();
        for (int i = 1; i <= 18; i++)
        {
            roundOptions.Add(new SelectMenuOptionBuilder()
                .WithLabel(RoundHelper.GetRoundLabel(i))
                .WithValue(i.ToString())
                .WithDescription(RoundHelper.GetRoundDescription(i)));
        }

        var dateOptions = BuildDateOptions(year, month, now);

        var selectedDateStr = !string.IsNullOrEmpty(state.SelectedDate) ? $"Wybrana data: {state.SelectedDate}" : "Nie wybrano daty";
        var selectedRoundStr = state.SelectedRound.HasValue ? $"Wybrana runda: {state.SelectedRound.Value}" : "Nie wybrano rundy";
        var timeStr = !string.IsNullOrEmpty(state.SelectedTime) ? state.SelectedTime : "18:00";
        var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy", new System.Globalization.CultureInfo("pl-PL"));

        var embed = new EmbedBuilder()
            .WithTitle("Dodaj mecz - Krok 1")
            .WithDescription($"**{selectedRoundStr}**\n**{selectedDateStr}**\n**Godzina: {timeStr}**\n\nWybierz rundę, datę i godzinę, a następnie kliknij Kontynuuj.")
            .AddField("Miesiąc", monthName, true)
            .WithColor(Color.Blue)
            .Build();

        var component = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("admin_add_match_round").WithPlaceholder("Wybierz rundę")
                .WithOptions(roundOptions).WithMinValues(1).WithMaxValues(1), row: 0)
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("admin_add_match_date").WithPlaceholder("Wybierz datę")
                .WithOptions(dateOptions).WithMinValues(1).WithMaxValues(1), row: 1)
            .WithButton("⏪ -15 min", "admin_time_minus_15", ButtonStyle.Secondary, row: 2)
            .WithButton("⏩ +15 min", "admin_time_plus_15", ButtonStyle.Secondary, row: 2)
            .WithButton("✏️ Ustaw godzinę ręcznie", "admin_time_manual", ButtonStyle.Secondary, row: 2)
            .WithButton("« Poprzedni miesiąc", "admin_calendar_prev", ButtonStyle.Secondary, row: 3)
            .WithButton("📅 Dziś", "admin_calendar_today", ButtonStyle.Secondary, row: 3)
            .WithButton("Następny miesiąc »", "admin_calendar_next", ButtonStyle.Secondary, row: 3)
            .WithButton("Kontynuuj", "admin_add_match_continue", ButtonStyle.Success, row: 4)
            .Build();

        if (Context.Interaction.HasResponded)
            await ModifyOriginalResponseAsync(prop => { prop.Embed = embed; prop.Components = component; });
        else
            await RespondAsync(embed: embed, components: component, ephemeral: true);
    }

    private List<SelectMenuOptionBuilder> BuildDateOptions(int year, int month, DateTime now)
    {
        var dateOptions = new List<SelectMenuOptionBuilder>();
        var startDate = (month == now.Month && year == now.Year) ? now.Date : new DateTime(year, month, 1);
        var endDate = new DateTime(year, month, 1).AddMonths(3);
        var currentDate = startDate;
        const int maxDays = 25;

        while (currentDate < endDate && dateOptions.Count < maxDays)
        {
            if (currentDate >= now.Date)
            {
                var dayName = GetPolishDayName(currentDate.DayOfWeek);
                var dateStr = currentDate.ToString("yyyy-MM-dd");
                dateOptions.Add(new SelectMenuOptionBuilder()
                    .WithLabel($"{dateStr} ({dayName})")
                    .WithValue(dateStr)
                    .WithDescription(currentDate.ToString("dddd, d MMMM yyyy", new System.Globalization.CultureInfo("pl-PL"))));
            }
            currentDate = currentDate.AddDays(1);
            if (currentDate.Day == 1 && dateOptions.Count >= 20) break;
        }
        return dateOptions;
    }

    private static string GetPolishDayName(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "pon",
        DayOfWeek.Tuesday => "wt",
        DayOfWeek.Wednesday => "śr",
        DayOfWeek.Thursday => "czw",
        DayOfWeek.Friday => "pt",
        DayOfWeek.Saturday => "sob",
        DayOfWeek.Sunday => "nie",
        _ => dayOfWeek.ToString()[..3]
    };

    [ComponentInteraction("admin_add_match_round")]
    public async Task HandleRoundSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        if (selectedValues.Length == 0 || !int.TryParse(selectedValues[0], out var round)) { await RespondAsync("❌ Nieprawidłowy wybór rundy.", ephemeral: true); return; }
        _stateService.UpdateRound(Context.Guild.Id, Context.User.Id, round);
        _logger.LogInformation("Round {Round} selected - User: {Username}", round, Context.User.Username);
        await DeferAsync();
        await ShowAddMatchCalendarAsync();
    }

    [ComponentInteraction("admin_add_match_date")]
    public async Task HandleDateSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        if (selectedValues.Length == 0) { await RespondAsync("❌ Nieprawidłowy wybór daty.", ephemeral: true); return; }
        _stateService.UpdateDate(Context.Guild.Id, Context.User.Id, selectedValues[0]);
        _logger.LogInformation("Date {Date} selected - User: {Username}", selectedValues[0], Context.User.Username);
        await DeferAsync();
        await ShowAddMatchCalendarAsync();
    }

    [ComponentInteraction("admin_calendar_prev")]
    public async Task HandleCalendarPrevAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null) return;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        int year = state.CurrentCalendarYear > 0 ? state.CurrentCalendarYear : now.Year;
        int month = state.CurrentCalendarMonth > 0 ? state.CurrentCalendarMonth : now.Month;
        var date = new DateTime(year, month, 1).AddMonths(-1);
        _stateService.UpdateCalendarMonth(Context.Guild.Id, Context.User.Id, date.Year, date.Month);
        await DeferAsync();
        await ShowAddMatchCalendarAsync();
    }

    [ComponentInteraction("admin_calendar_next")]
    public async Task HandleCalendarNextAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null) return;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        int year = state.CurrentCalendarYear > 0 ? state.CurrentCalendarYear : now.Year;
        int month = state.CurrentCalendarMonth > 0 ? state.CurrentCalendarMonth : now.Month;
        var date = new DateTime(year, month, 1).AddMonths(1);
        _stateService.UpdateCalendarMonth(Context.Guild.Id, Context.User.Id, date.Year, date.Month);
        await DeferAsync();
        await ShowAddMatchCalendarAsync();
    }

    [ComponentInteraction("admin_calendar_today")]
    public async Task HandleCalendarTodayAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        _stateService.UpdateCalendarMonth(Context.Guild.Id, Context.User.Id, now.Year, now.Month);
        await DeferAsync();
        await ShowAddMatchCalendarAsync();
    }

    [ComponentInteraction("admin_time_minus_15")]
    public async Task HandleTimeMinus15Async()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        AdjustTime(-15);
        await DeferAsync();
        await ShowAddMatchCalendarAsync();
    }

    [ComponentInteraction("admin_time_plus_15")]
    public async Task HandleTimePlus15Async()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        AdjustTime(15);
        await DeferAsync();
        await ShowAddMatchCalendarAsync();
    }

    private void AdjustTime(int minutes)
    {
        if (Context.Guild == null) return;
        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null) return;
        var timeStr = !string.IsNullOrEmpty(state.SelectedTime) ? state.SelectedTime : "18:00";
        if (TimeSpan.TryParse(timeStr, out var time))
        {
            var newTime = time.Add(TimeSpan.FromMinutes(minutes));
            if (newTime.TotalDays >= 1) newTime = newTime.Add(TimeSpan.FromDays(-1));
            if (newTime.TotalDays < 0) newTime = newTime.Add(TimeSpan.FromDays(1));
            _stateService.UpdateTime(Context.Guild.Id, Context.User.Id, $"{(int)newTime.TotalHours:D2}:{newTime.Minutes:D2}");
        }
    }

    [ComponentInteraction("admin_time_manual")]
    public async Task HandleTimeManualAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        var defaultTime = !string.IsNullOrEmpty(state?.SelectedTime) ? state.SelectedTime : "18:00";
        var modal = new ModalBuilder()
            .WithTitle("Ustaw godzinę")
            .WithCustomId("admin_time_modal")
            .AddTextInput("Godzina", "time", TextInputStyle.Short, placeholder: "18:30", value: defaultTime, required: true)
            .Build();
        await RespondWithModalAsync(modal);
    }

    [ModalInteraction("admin_time_modal")]
    public async Task HandleTimeModalAsync(string time)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        if (!TimeSpan.TryParse(time, out var parsedTime) || parsedTime.TotalHours >= 24)
        {
            await RespondAsync("❌ Nieprawidłowy format godziny. Użyj HH:mm, np. 18:30.", ephemeral: true);
            return;
        }
        _stateService.UpdateTime(Context.Guild.Id, Context.User.Id, $"{(int)parsedTime.TotalHours:D2}:{parsedTime.Minutes:D2}");
        await RespondAsync($"✅ Godzina ustawiona na: {time}", ephemeral: true);
    }

    [ComponentInteraction("admin_add_match_continue")]
    public async Task HandleContinueButtonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null || !state.SelectedRound.HasValue || string.IsNullOrEmpty(state.SelectedDate) || string.IsNullOrEmpty(state.SelectedTime))
        {
            await RespondAsync("❌ Wybierz rundę, datę i godzinę przed kontynuowaniem.", ephemeral: true);
            return;
        }
        try
        {
            var modal = new ModalBuilder()
                .WithTitle("Dodaj mecz")
                .WithCustomId("admin_add_match_modal")
                .AddTextInput("Drużyna domowa", "home_team", TextInputStyle.Short, placeholder: "Motor Lublin", required: true)
                .AddTextInput("Drużyna wyjazdowa", "away_team", TextInputStyle.Short, placeholder: "Włókniarz Częstochowa", required: true)
                .Build();
            await RespondWithModalAsync(modal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing modal - User: {Username}", Context.User.Username);
            if (!Context.Interaction.HasResponded)
                await RespondAsync("❌ Wystąpił błąd podczas wyświetlania formularza. Spróbuj ponownie.", ephemeral: true);
        }
    }

    [ModalInteraction("admin_add_match_modal")]
    public async Task HandleAddMatchModalAsync(string homeTeam, string awayTeam)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null || !state.SelectedRound.HasValue || string.IsNullOrEmpty(state.SelectedDate) || string.IsNullOrEmpty(state.SelectedTime))
        {
            await RespondAsync("❌ Twój formularz dodawania meczu wygasł, otwórz ponownie /panel-admina i spróbuj ponownie.", ephemeral: true);
            return;
        }

        var roundNum = state.SelectedRound.Value;
        var dateStr = state.SelectedDate;
        var timeStr = state.SelectedTime;

        DateTimeOffset startTime;
        try
        {
            if (!DateTime.TryParse($"{dateStr} {timeStr}", out var localTime))
            {
                await RespondAsync("❌ Nie udało się sparsować daty/godziny meczu. Spróbuj ponownie.", ephemeral: true);
                return;
            }
            var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
            startTime = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), tz);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception parsing date/time");
            await RespondAsync("❌ Nie udało się sparsować daty/godziny meczu. Spróbuj ponownie.", ephemeral: true);
            return;
        }

        if (startTime <= DateTimeOffset.UtcNow)
        {
            await RespondAsync("❌ Data rozpoczęcia meczu musi być w przyszłości.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            var (success, error, match) = await _matchService.CreateMatchAsync(roundNum, homeTeam, awayTeam, startTime);
            if (!success || match == null)
            {
                await FollowupAsync($"❌ Błąd podczas tworzenia meczu: {error ?? "Nieznany błąd"}", ephemeral: true);
                return;
            }

            if (Context.Guild != null) _stateService.ClearState(Context.Guild.Id, Context.User?.Id ?? 0);
            await _matchCardService.PostMatchCardAsync(match, match.Round?.Number ?? roundNum);

            var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
            var localStartTime = TimeZoneInfo.ConvertTimeFromUtc(match.StartTime.UtcDateTime, tz);
            await FollowupAsync($"✅ Mecz utworzony: Runda {roundNum}, {homeTeam} vs {awayTeam} o {localStartTime:yyyy-MM-dd HH:mm}.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception creating match");
            await FollowupAsync("❌ Wystąpił błąd podczas tworzenia meczu. Szczegóły zapisano w logach.", ephemeral: true);
        }
    }

    #endregion

    #region Batch Round Match Creation

    private async Task ShowBatchRoundMatchFormAsync()
    {
        if (Context.Guild == null) return;

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null || !state.IsBatchRoundCreation || !state.SelectedRound.HasValue)
        {
            await FollowupAsync("❌ Stan formularza wygasł. Rozpocznij ponownie z /panel-admina.", ephemeral: true);
            return;
        }

        var currentMatch = state.CurrentMatchIndex + 1;
        var totalMatches = state.TotalMatchesInBatch;
        var roundLabel = RoundHelper.GetRoundLabel(state.SelectedRound.Value);

        var openModalButton = new ButtonBuilder()
            .WithCustomId($"admin_kolejka_open_match_modal_{currentMatch}")
            .WithLabel($"📝 Dodaj mecz {currentMatch}/{totalMatches}")
            .WithStyle(ButtonStyle.Primary);

        var component = new ComponentBuilder().WithButton(openModalButton).Build();

        var followupMessage = await FollowupAsync(
            $"📝 **{roundLabel} - Mecz {currentMatch}/{totalMatches}**\nKliknij przycisk poniżej, aby dodać mecz.",
            components: component, ephemeral: true);

        if (followupMessage != null && state != null)
            state.FollowupMessageId = followupMessage.Id;
    }

    [ComponentInteraction("admin_kolejka_home_team")]
    public async Task HandleBatchRoundHomeTeamSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        if (selectedValues.Length > 0) _stateService.UpdateHomeTeam(Context.Guild.Id, Context.User.Id, selectedValues[0]);
        await DeferAsync();
    }

    [ComponentInteraction("admin_kolejka_away_team")]
    public async Task HandleBatchRoundAwayTeamSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        if (selectedValues.Length > 0) _stateService.UpdateAwayTeam(Context.Guild.Id, Context.User.Id, selectedValues[0]);
        await DeferAsync();
    }

    [ComponentInteraction("admin_kolejka_match_date")]
    public async Task HandleBatchRoundMatchDateSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        if (selectedValues.Length > 0) _stateService.UpdateDate(Context.Guild.Id, Context.User.Id, selectedValues[0]);
        await DeferAsync();
    }

    [ComponentInteraction("admin_kolejka_time_minus_15")]
    public async Task HandleBatchRoundTimeMinus15Async()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        AdjustTime(-15);
        await DeferAsync();
        await ShowBatchRoundMatchFormAsync();
    }

    [ComponentInteraction("admin_kolejka_time_plus_15")]
    public async Task HandleBatchRoundTimePlus15Async()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        AdjustTime(15);
        await DeferAsync();
        await ShowBatchRoundMatchFormAsync();
    }

    [ComponentInteraction("admin_kolejka_time_manual")]
    public async Task HandleBatchRoundTimeManualAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        var defaultTime = !string.IsNullOrEmpty(state?.SelectedTime) ? state.SelectedTime : "18:00";
        var modal = new ModalBuilder()
            .WithTitle("Ustaw godzinę")
            .WithCustomId("admin_kolejka_time_modal")
            .AddTextInput("Godzina", "time", TextInputStyle.Short, placeholder: "18:30", value: defaultTime, required: true)
            .Build();
        await RespondWithModalAsync(modal);
    }

    [ModalInteraction("admin_kolejka_time_modal")]
    public async Task HandleBatchRoundTimeModalAsync(string time)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }

        if (!DateTime.TryParseExact(time, "HH:mm", null, System.Globalization.DateTimeStyles.None, out var parsedTime))
        {
            if (!TimeSpan.TryParse(time, out var parsedSpan) || parsedSpan.TotalHours >= 24)
            {
                await RespondAsync("❌ Nieprawidłowy format godziny. Użyj HH:mm, np. 18:30.", ephemeral: true);
                return;
            }
            _stateService.UpdateTime(Context.Guild.Id, Context.User.Id, $"{(int)parsedSpan.TotalHours:D2}:{parsedSpan.Minutes:D2}");
        }
        else
        {
            _stateService.UpdateTime(Context.Guild.Id, Context.User.Id, parsedTime.ToString("HH:mm"));
        }
        await RespondAsync($"✅ Godzina ustawiona na: {time}", ephemeral: true);
    }

    [ComponentInteraction("admin_kolejka_calendar_prev")]
    public async Task HandleBatchRoundCalendarPrevAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null) { await RespondAsync("❌ Stan formularza wygasł.", ephemeral: true); return; }
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        int year = state.CurrentCalendarYear > 0 ? state.CurrentCalendarYear : now.Year;
        int month = state.CurrentCalendarMonth > 0 ? state.CurrentCalendarMonth : now.Month;
        var date = new DateTime(year, month, 1).AddMonths(-1);
        _stateService.UpdateCalendarMonth(Context.Guild.Id, Context.User.Id, date.Year, date.Month);
        await DeferAsync();
        await ShowBatchRoundMatchFormAsync();
    }

    [ComponentInteraction("admin_kolejka_calendar_next")]
    public async Task HandleBatchRoundCalendarNextAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null) { await RespondAsync("❌ Stan formularza wygasł.", ephemeral: true); return; }
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        int year = state.CurrentCalendarYear > 0 ? state.CurrentCalendarYear : now.Year;
        int month = state.CurrentCalendarMonth > 0 ? state.CurrentCalendarMonth : now.Month;
        var date = new DateTime(year, month, 1).AddMonths(1);
        _stateService.UpdateCalendarMonth(Context.Guild.Id, Context.User.Id, date.Year, date.Month);
        await DeferAsync();
        await ShowBatchRoundMatchFormAsync();
    }

    [ComponentInteraction("admin_kolejka_calendar_today")]
    public async Task HandleBatchRoundCalendarTodayAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        _stateService.UpdateCalendarMonth(Context.Guild.Id, Context.User.Id, now.Year, now.Month);
        await DeferAsync();
        await ShowBatchRoundMatchFormAsync();
    }

    [ComponentInteraction("admin_kolejka_submit_match")]
    public async Task HandleBatchRoundSubmitMatchAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null || !state.IsBatchRoundCreation || !state.SelectedRound.HasValue)
        {
            await RespondAsync("❌ Stan formularza wygasł. Rozpocznij ponownie.", ephemeral: true);
            return;
        }

        if (string.IsNullOrEmpty(state.SelectedHomeTeam) || string.IsNullOrEmpty(state.SelectedAwayTeam) ||
            string.IsNullOrEmpty(state.SelectedDate) || string.IsNullOrEmpty(state.SelectedTime))
        {
            await RespondAsync("❌ Wybierz wszystkie pola (drużyny, datę, godzinę) przed zatwierdzeniem.", ephemeral: true);
            return;
        }

        if (state.SelectedHomeTeam == state.SelectedAwayTeam)
        {
            await RespondAsync("❌ Drużyna domowa i wyjazdowa muszą być różne.", ephemeral: true);
            return;
        }

        _stateService.AddMatchToBatch(Context.Guild.Id, Context.User.Id,
            state.SelectedHomeTeam, state.SelectedAwayTeam, state.SelectedDate, state.SelectedTime);

        _stateService.UpdateHomeTeam(Context.Guild.Id, Context.User.Id, "");
        _stateService.UpdateAwayTeam(Context.Guild.Id, Context.User.Id, "");

        var updatedState = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (updatedState != null && updatedState.CurrentMatchIndex >= updatedState.TotalMatchesInBatch)
        {
            await DeferAsync();
            await CreateBatchRoundMatchesAsync();
        }
        else
        {
            await DeferAsync();
            await ShowBatchRoundMatchFormAsync();
        }
    }

    [ComponentInteraction("admin_kolejka_open_match_modal_*")]
    public async Task HandleOpenMatchModalForBatchRoundAsync(string matchIndexStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null || !state.IsBatchRoundCreation || !state.SelectedRound.HasValue)
        {
            await RespondAsync("❌ Stan formularza wygasł. Rozpocznij ponownie.", ephemeral: true);
            return;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var modal = new AddMatchModalV2
        {
            RoundNumber = state.SelectedRound.Value.ToString(),
            MatchDate = !string.IsNullOrEmpty(state.SelectedDate) ? state.SelectedDate : now.Date.AddDays(1).ToString("yyyy-MM-dd"),
            MatchTime = !string.IsNullOrEmpty(state.SelectedTime) ? state.SelectedTime : "18:00",
            HomeTeam = !string.IsNullOrEmpty(state.SelectedHomeTeam) ? state.SelectedHomeTeam : "Motor Lublin",
            AwayTeam = !string.IsNullOrEmpty(state.SelectedAwayTeam) ? state.SelectedAwayTeam : "Włókniarz Częstochowa"
        };
        await RespondWithModalAsync("admin_add_match_modal_kolejka", modal);
    }

    [ComponentInteraction("admin_kolejka_finish")]
    public async Task HandleFinishBatchRoundAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null || !state.IsBatchRoundCreation || !state.SelectedRound.HasValue)
        {
            await RespondAsync("❌ Stan formularza wygasł. Rozpocznij ponownie.", ephemeral: true);
            return;
        }

        var missingMatches = state.TotalMatchesInBatch - state.CollectedMatches.Count;
        if (missingMatches > 0)
        {
            await DeferAsync();
            state.CurrentMatchIndex = state.CollectedMatches.Count + 1;
            await ShowBatchRoundMatchFormAsync();
            await FollowupAsync(
                $"⚠️ **Uwaga:** Brakuje jeszcze {missingMatches} mecz(ów) w kolejce. " +
                $"Możesz je dodać teraz lub zatwierdzić kolejkę bez nich używając przycisku '✅ Zatwierdź kolejkę'.",
                ephemeral: true);
            return;
        }

        await DeferAsync();
        await CreateBatchRoundMatchesAsync();
    }

    [ModalInteraction("admin_add_match_modal_kolejka", true)]
    public async Task HandleAddMatchModalBatchRoundAsync(AddMatchModalV2 modal)
    {
        await HandleBatchRoundModalV2Async(modal);
    }

    private async Task HandleBatchRoundModalV2Async(AddMatchModalV2 modal)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondWithErrorAsync("Nie masz uprawnień."); return; }

        if (!int.TryParse(modal.RoundNumber, out var roundNum) || roundNum < 1 || roundNum > 18) { await RespondWithErrorAsync("Nieprawidłowy numer kolejki."); return; }
        if (!DateTime.TryParse(modal.MatchDate, out _)) { await RespondWithErrorAsync("Nieprawidłowy format daty."); return; }
        if (!TimeSpan.TryParse(modal.MatchTime, out var parsedTime) || parsedTime.TotalHours >= 24) { await RespondWithErrorAsync("Nieprawidłowy format godziny."); return; }
        if (string.IsNullOrWhiteSpace(modal.HomeTeam) || string.IsNullOrWhiteSpace(modal.AwayTeam)) { await RespondWithErrorAsync("Nazwy drużyn nie mogą być puste."); return; }
        if (modal.HomeTeam.Equals(modal.AwayTeam, StringComparison.OrdinalIgnoreCase)) { await RespondWithErrorAsync("Drużyna domowa i wyjazdowa muszą być różne."); return; }

        DateTimeOffset startTime;
        try
        {
            if (!DateTime.TryParse($"{modal.MatchDate} {modal.MatchTime}", out var localTime))
            {
                await RespondWithErrorAsync("Nie udało się sparsować daty/godziny meczu.");
                return;
            }
            var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);
            startTime = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), tz);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception parsing date/time in batch round");
            await RespondWithErrorAsync("Nie udało się sparsować daty/godziny meczu.");
            return;
        }

        if (startTime <= DateTimeOffset.UtcNow) { await RespondWithErrorAsync("Data rozpoczęcia meczu musi być w przyszłości."); return; }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null || !state.IsBatchRoundCreation)
        {
            await RespondWithErrorAsync("Błąd: stan formularza wygasł.");
            return;
        }

        _stateService.AddMatchToBatch(Context.Guild.Id, Context.User.Id, modal.HomeTeam, modal.AwayTeam, modal.MatchDate, modal.MatchTime);

        var updatedState = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (updatedState == null) { await RespondWithErrorAsync("Błąd: stan formularza wygasł."); return; }

        var currentMatch = updatedState.CurrentMatchIndex;
        var totalMatches = updatedState.TotalMatchesInBatch;
        var roundLabel = RoundHelper.GetRoundLabel(roundNum);

        if (currentMatch >= totalMatches)
        {
            await DeferAsync();
            await CreateBatchRoundMatchesAsync();
            return;
        }

        try
        {
            await RespondAsync($"✅ Mecz {currentMatch}/{totalMatches} dodany: {modal.HomeTeam} vs {modal.AwayTeam}", ephemeral: true);

            var nextMatch = currentMatch + 1;
            var updatedButton = new ButtonBuilder()
                .WithCustomId($"admin_kolejka_open_match_modal_{nextMatch}")
                .WithLabel($"📝 Dodaj mecz {nextMatch}/{totalMatches}")
                .WithStyle(ButtonStyle.Primary);
            var updatedComponent = new ComponentBuilder().WithButton(updatedButton).Build();

            var newFollowupMessage = await FollowupAsync(
                $"📝 **{roundLabel} - Mecz {nextMatch}/{totalMatches}**\nKliknij przycisk poniżej, aby dodać mecz.",
                components: updatedComponent, ephemeral: true);

            if (newFollowupMessage != null)
                updatedState.FollowupMessageId = newFollowupMessage.Id;
        }
        catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Interaction expired while adding match to batch round");
        }
    }

    private async Task CreateBatchRoundMatchesAsync()
    {
        if (Context.Guild == null) return;

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null || !state.IsBatchRoundCreation || !state.SelectedRound.HasValue)
        {
            try { await FollowupAsync("❌ Błąd: stan formularza wygasł.", ephemeral: true); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send response - interaction expired"); }
            return;
        }

        var roundNumber = state.SelectedRound.Value;
        var roundLabel = RoundHelper.GetRoundLabel(roundNumber);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.Timezone);

        try
        {
            var createdMatches = new List<Domain.Entities.Match>();
            var errors = new List<string>();

            foreach (var (homeTeam, awayTeam, dateStr, timeStr) in state.CollectedMatches)
            {
                if (!DateTime.TryParse($"{dateStr} {timeStr}", out var localTime))
                {
                    errors.Add($"❌ Błąd parsowania daty/godziny dla meczu {homeTeam} vs {awayTeam}.");
                    continue;
                }
                var startTime = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), tz);
                var (success, error, match) = await _matchService.CreateMatchAsync(roundNumber, homeTeam, awayTeam, startTime);
                if (!success || match == null) { errors.Add($"❌ Błąd tworzenia meczu {homeTeam} vs {awayTeam}: {error}"); continue; }
                createdMatches.Add(match);
                await _matchCardService.PostMatchCardAsync(match, roundNumber);
            }

            var totalMatchesExpected = state.TotalMatchesInBatch;
            var missingMatches = totalMatchesExpected - createdMatches.Count;
            _stateService.ClearState(Context.Guild.Id, Context.User.Id);

            _logger.LogInformation("Batch round created - {Label}, Matches: {Count}/{Total}", roundLabel, createdMatches.Count, totalMatchesExpected);

            var responseMessage = $"✅ Kolejka {roundLabel} została dodana z {createdMatches.Count} meczami.";
            if (missingMatches > 0)
                responseMessage += $"\n\n⚠️ **Brakuje {missingMatches} mecz(ów).** Możesz je dodać później w panelu zarządzania kolejką.";
            if (errors.Any())
                responseMessage += $"\n\n⚠️ Wystąpiły błędy:\n{string.Join("\n", errors)}";

            try { await FollowupAsync(responseMessage, ephemeral: true); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send response - interaction expired, but matches were created"); }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception creating batch round matches for round {Round}", roundNumber);
            try { await FollowupAsync("❌ Wystąpił błąd podczas tworzenia kolejki. Szczegóły w logach.", ephemeral: true); }
            catch { _logger.LogError("Failed to send error response - interaction expired"); }
        }
    }

    #endregion

    #region Slash Commands - Tables & Export

    [SlashCommand("admin-tabela-sezonu", "Wyślij tabelę sezonu do kanału wyników (tylko dla adminów)")]
    public async Task AdminPostSeasonTableAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        await DeferAsync(ephemeral: true);
        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null) { await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true); return; }
        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any()) { await FollowupAsync("❌ Brak aktywnych graczy.", ephemeral: true); return; }
        try
        {
            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel == null) { await FollowupAsync("❌ Nie znaleziono kanału typowanie.", ephemeral: true); return; }
            await PostSeasonTableEmbedAsync(season, players, predictionsChannel);
            await FollowupAsync("✅ Tabela sezonu została opublikowana w kanale typowanie.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating season table");
            await FollowupAsync("❌ Wystąpił błąd podczas generowania tabeli.", ephemeral: true);
        }
    }

    [SlashCommand("admin-tabela-kolejki", "Wyślij tabelę kolejki do kanału wyników (tylko dla adminów)")]
    public async Task AdminPostRoundTableAsync([Summary(description: "Numer kolejki")] int round)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        await DeferAsync(ephemeral: true);
        if (!RoundHelper.IsValidRoundNumber(round))
        {
            await FollowupAsync($"❌ Numer kolejki musi być z zakresu 1–18 (podano: {round}).", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null) { await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true); return; }
        var roundEntity = season.FindRoundByNumber(round)
            ?? await _roundRepository.GetByNumberAsync(season.Id, round);
        if (roundEntity == null)
        {
            var available = season.Rounds.Count > 0
                ? string.Join(", ", season.Rounds.OrderBy(r => r.Number).Select(r => r.Number))
                : "brak";
            await FollowupAsync(
                $"❌ W **aktywnym** sezonie „{season.Name}” nie ma kolejki **{round}**.\n" +
                $"Dostępne numery kolejek: {available}.",
                ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any()) { await FollowupAsync("❌ Brak aktywnych graczy.", ephemeral: true); return; }
        try
        {
            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
            if (predictionsChannel == null) { await FollowupAsync("❌ Nie znaleziono kanału typowanie.", ephemeral: true); return; }
            await PostRoundTableEmbedAsync(season, roundEntity, players, predictionsChannel);
            await FollowupAsync($"✅ Tabela kolejki {RoundHelper.GetRoundLabel(round)} została opublikowana.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating round table");
            await FollowupAsync("❌ Wystąpił błąd podczas generowania tabeli.", ephemeral: true);
        }
    }

    [SlashCommand("admin-eksport-sezonu", "Eksportuj pełne dane sezonu do CSV (tylko dla adminów)")]
    public async Task ExportSeasonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        var channel = Context.Channel as SocketTextChannel;
        if (!IsAllowedChannel(channel)) { await RespondWithErrorAsync($"Ta komenda może być używana tylko w kanałach: #{Settings.Channels.AdminChannel} lub #{Settings.Channels.PredictionsChannel}"); return; }

        await DeferAsync(ephemeral: true);

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null) { await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true); return; }
        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any()) { await FollowupAsync("❌ Brak aktywnych graczy.", ephemeral: true); return; }
        try
        {
            var csv = _exportService.ExportSeasonToCsv(season, players);
            await FollowupWithFileAsync(new Discord.FileAttachment(new MemoryStream(csv), $"eksport-sezonu-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv"));
            _logger.LogInformation("Season export generated - User: {Username}", Context.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate season export");
            await FollowupAsync("❌ Nie udało się wygenerować eksportu sezonu.", ephemeral: true);
        }
    }

    [SlashCommand("admin-eksport-kolejki", "Eksportuj dane kolejki do CSV (tylko dla adminów)")]
    public async Task ExportRoundAsync([Summary(description: "Numer kolejki")] int round)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null) { await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true); return; }
        var channel = Context.Channel as SocketTextChannel;
        if (!IsAllowedChannel(channel)) { await RespondWithErrorAsync($"Ta komenda może być używana tylko w kanałach: #{Settings.Channels.AdminChannel} lub #{Settings.Channels.PredictionsChannel}"); return; }

        await DeferAsync(ephemeral: true);

        if (!RoundHelper.IsValidRoundNumber(round))
        {
            await FollowupAsync($"❌ Numer kolejki musi być z zakresu 1–18 (podano: {round}).", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null) { await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true); return; }
        var roundEntity = season.FindRoundByNumber(round)
            ?? await _roundRepository.GetByNumberAsync(season.Id, round);
        if (roundEntity == null)
        {
            var available = season.Rounds.Count > 0
                ? string.Join(", ", season.Rounds.OrderBy(r => r.Number).Select(r => r.Number))
                : "brak";
            await FollowupAsync(
                $"❌ W **aktywnym** sezonie „{season.Name}” nie ma kolejki **{round}**.\n" +
                $"Dostępne numery kolejek: {available}.",
                ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any()) { await FollowupAsync("❌ Brak aktywnych graczy.", ephemeral: true); return; }
        try
        {
            var csv = _exportService.ExportRoundToCsv(roundEntity, players);
            await FollowupWithFileAsync(new Discord.FileAttachment(new MemoryStream(csv), $"eksport-kolejki-{round}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv"));
            _logger.LogInformation("Round {Round} export generated - User: {Username}", round, Context.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate round {Round} export", round);
            await FollowupAsync("❌ Nie udało się wygenerować eksportu kolejki.", ephemeral: true);
        }
    }

    #endregion

    #region Table Helpers (used by slash commands and MatchResultHandler)

    public async Task PostRoundTableEmbedAsync(Domain.Entities.Season season, Domain.Entities.Round round, List<Domain.Entities.Player> players, ITextChannel channel, Domain.Entities.Match? triggerMatch = null)
    {
        var roundMatches = (await _matchRepository.GetByRoundIdAsync(round.Id)).Select(m => m.Id).ToList();
        var allScores = new List<(int PlayerId, string PlayerName, int TotalPoints, int PredictionsCount, int ExactScores, int CorrectWinners)>();

        foreach (var player in players)
        {
            var predsInRound = player.Predictions
                .Where(p => roundMatches.Contains(p.MatchId) && p.IsValid)
                .ToList();
            var scored = predsInRound.Where(p => p.PlayerScore != null).Select(p => p.PlayerScore!).ToList();
            var totalPoints = scored.Sum(s => s.Points);
            var exactScores = scored.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50);
            var correctWinners = scored.Count(s => s.Points > 0);
            var typCount = predsInRound.Count;
            allScores.Add((player.Id, player.DiscordUsername, totalPoints, typCount, exactScores, correctWinners));
        }

        var sortedScores = allScores
            .OrderByDescending(s => s.TotalPoints)
            .ThenByDescending(s => s.PredictionsCount)
            .ToList();
        var roundLabel = RoundHelper.GetRoundLabel(round.Number);

        var anyTipInRound = sortedScores.Sum(s => s.PredictionsCount) > 0;
        var baseDesc = triggerMatch == null || string.IsNullOrEmpty(triggerMatch.HomeTeam)
            ? $"**Sezon**: {season.Name}\n**Kolejka**: {round.Description ?? roundLabel}"
            : $"**Sezon**: {season.Name}\n**Kolejka**: {round.Description ?? roundLabel}\n**Po meczu**: {triggerMatch.HomeTeam} vs {triggerMatch.AwayTeam} ({triggerMatch.HomeScore}:{triggerMatch.AwayScore})";
        var descriptionText = baseDesc + "\n\n_Pkt — mecze z wynikiem. Typ — wszystkie typy w tej kolejce._"
            + (anyTipInRound ? "" : "\n\n⚠️ _Brak typów dla tej kolejki — sprawdź numer kolejki._");

        var embed = new EmbedBuilder()
            .WithTitle($"📊 Tabela Kolejki {round.Number}")
            .WithDescription(descriptionText)
            .WithColor(Color.Blue)
            .WithCurrentTimestamp();

        if (sortedScores.Any())
        {
            embed.AddField("Tabela punktowa", BuildStandingsTable(sortedScores), false);
        }
        else
        {
            embed.WithDescription(descriptionText + "\n\n*Brak graczy do wyświetlenia.*");
        }

        embed.WithFooter("Typ = typy w tej kolejce | Pkt/Cel/Wyg = tylko mecze z wynikiem");
        await channel.SendMessageAsync(embed: embed.Build());
        _logger.LogInformation("Round {Round} table published", round.Number);
    }

    public async Task PostSeasonTableEmbedAsync(Domain.Entities.Season season, List<Domain.Entities.Player> players, ITextChannel channel, Domain.Entities.Match? triggerMatch = null)
    {
        var seasonMatchIds = EnhancedTableGenerator.ResolveSeasonMatchIdsPublic(season);
        var filterBySeason = seasonMatchIds.Count > 0;

        var allScores = new List<(string PlayerName, int TotalPoints, int PredictionsCount, int ExactScores, int CorrectWinners)>();
        foreach (var player in players)
        {
            var predsInSeason = player.Predictions
                .Where(p => p.IsValid && (!filterBySeason || seasonMatchIds.Contains(p.MatchId)))
                .ToList();
            var scored = predsInSeason.Where(p => p.PlayerScore != null).Select(p => p.PlayerScore!).ToList();
            allScores.Add((
                player.DiscordUsername,
                scored.Sum(s => s.Points),
                predsInSeason.Count,
                scored.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50),
                scored.Count(s => s.Points > 0)));
        }

        var sortedScores = allScores
            .OrderByDescending(s => s.TotalPoints)
            .ThenByDescending(s => s.PredictionsCount)
            .ToList();
        var descriptionText = (triggerMatch == null || string.IsNullOrEmpty(triggerMatch.HomeTeam)
            ? $"**Sezon**: {season.Name}"
            : $"**Sezon**: {season.Name}\n**Po meczu**: {triggerMatch.HomeTeam} vs {triggerMatch.AwayTeam} ({triggerMatch.HomeScore}:{triggerMatch.AwayScore})")
            + "\n\n_Pkt — mecze z wynikiem. Typ — wszystkie typy w sezonie._";

        var embed = new EmbedBuilder()
            .WithTitle("🏆 Tabela Sezonu")
            .WithDescription(descriptionText)
            .WithColor(Color.Gold)
            .WithCurrentTimestamp();

        embed.AddField("Tabela punktowa - Sezon", BuildStandingsTable(
            sortedScores.Select(s => (0, s.PlayerName, s.TotalPoints, s.PredictionsCount, s.ExactScores, s.CorrectWinners)).ToList()), false);
        embed.WithFooter("Typ = typy w sezonie | Pkt/Cel/Wyg = tylko mecze z wynikiem");

        await channel.SendMessageAsync(embed: embed.Build());
        _logger.LogInformation("Season table published");
    }

    private static string BuildStandingsTable(List<(int PlayerId, string PlayerName, int TotalPoints, int PredictionsCount, int ExactScores, int CorrectWinners)> sortedScores)
    {
        var table = "```\n";
        table += "Poz  Gracz                    Pkt   Typ   Cel   Wyg\n";
        table += "═══════════════════════════════════════════════════\n";
        for (int i = 0; i < sortedScores.Count; i++)
        {
            var score = sortedScores[i];
            var playerName = score.PlayerName;
            if (playerName.Length > 22) playerName = playerName[..19] + "...";
            var medal = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ when i == sortedScores.Count - 1 => "💩", _ => "  " };
            table += $"{medal} {i + 1,2}  {playerName,-22}  {score.TotalPoints,3}  {score.PredictionsCount,4}  {score.ExactScores,4}  {score.CorrectWinners,4}\n";
        }
        table += "```";
        return table;
    }

    #endregion
}
