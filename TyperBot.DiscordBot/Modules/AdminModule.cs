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
    private readonly IPredictionRepository _predictionRepository;
    private readonly ExportService _exportService;
    private readonly IRoundRepository _roundRepository;
    private readonly AdminMatchCreationStateService _stateService;
    private readonly DemoDataSeeder _demoDataSeeder;

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
        IPredictionRepository predictionRepository,
        ExportService exportService,
        IRoundRepository roundRepository,
        AdminMatchCreationStateService stateService,
        DemoDataSeeder demoDataSeeder)
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
        _predictionRepository = predictionRepository;
        _exportService = exportService;
        _roundRepository = roundRepository;
        _stateService = stateService;
        _demoDataSeeder = demoDataSeeder;
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

    [SlashCommand("panel-admina", "Otwórz panel administracyjny Typera.")]
    public async Task AdminPanelAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }
        
        _logger.LogInformation(
            "Panel admina otwarty - Komenda: panel-admina, Użytkownik: {Username} (ID: {UserId}), Kanał: {ChannelName} (ID: {ChannelId}), Serwer: {GuildId}",
            Context.User.Username,
            Context.User.Id,
            Context.Channel.Name,
            Context.Channel.Id,
            Context.Guild?.Id);

        var embed = new EmbedBuilder()
            .WithTitle("Panel Administracyjny Typera")
            .WithDescription("Użyj przycisków poniżej, aby zarządzać meczami i przeglądać statystyki.")
            .WithColor(Color.Gold)
            .Build();

        var button = new ButtonBuilder()
            .WithCustomId("admin_add_match")
            .WithLabel("➕ Dodaj mecz")
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
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }
        
        _logger.LogInformation(
            "Przycisk dodaj mecz kliknięty - Użytkownik: {Username} (ID: {UserId}), Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            Context.Guild.Id,
            Context.Channel.Id);

        // Clear any existing state and initialize calendar to current month
        _stateService.ClearState(Context.Guild.Id, Context.User.Id);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        _stateService.UpdateCalendarMonth(Context.Guild.Id, Context.User.Id, now.Year, now.Month);
        _stateService.UpdateTime(Context.Guild.Id, Context.User.Id, "18:00"); // Default time

        await ShowAddMatchCalendarAsync();
    }

    private async Task ShowAddMatchCalendarAsync()
    {
        if (Context.Guild == null) return;

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null) return;

        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        int year = state.CurrentCalendarYear > 0 ? state.CurrentCalendarYear : now.Year;
        int month = state.CurrentCalendarMonth > 0 ? state.CurrentCalendarMonth : now.Month;

        // Generate round options (1-18) in Polish
        var roundOptions = new List<SelectMenuOptionBuilder>();
        for (int i = 1; i <= 18; i++)
        {
            roundOptions.Add(new SelectMenuOptionBuilder()
                .WithLabel($"Runda {i}")
                .WithValue(i.ToString())
                .WithDescription($"Runda {i}"));
        }

        // Generate date options for current month (from today or start of viewed month up to 3 months ahead)
        var dateOptions = new List<SelectMenuOptionBuilder>();
        var startDate = new DateTime(year, month, 1);
        var isCurrentMonth = (month == now.Month && year == now.Year);
        if (isCurrentMonth)
        {
            startDate = now.Date; // Start from today if viewing current month
        }
        var endDate = new DateTime(year, month, 1).AddMonths(3); // 3 months from start of viewed month
        var currentDate = startDate;
        var maxDays = 25; // Discord select menu limit
        
        while (currentDate < endDate && dateOptions.Count < maxDays)
        {
            // Only include dates from today onwards
            if (currentDate >= now.Date)
            {
                var dayName = GetPolishDayName(currentDate.DayOfWeek);
                var dateStr = currentDate.ToString("yyyy-MM-dd");
                var label = $"{dateStr} ({dayName})";
                dateOptions.Add(new SelectMenuOptionBuilder()
                    .WithLabel(label)
                    .WithValue(dateStr)
                    .WithDescription(currentDate.ToString("dddd, d MMMM yyyy", new System.Globalization.CultureInfo("pl-PL"))));
            }
            currentDate = currentDate.AddDays(1);
            
            // If we've moved to a new month and have enough options, we can stop
            // But let's continue to fill up to 25 options if possible
            if (currentDate.Day == 1 && dateOptions.Count >= 20)
            {
                // We have enough options, break to avoid going too far
                break;
            }
        }

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

        var roundSelect = new SelectMenuBuilder()
            .WithCustomId("admin_add_match_round")
            .WithPlaceholder("Wybierz rundę")
            .WithOptions(roundOptions)
            .WithMinValues(1)
            .WithMaxValues(1);

        var dateSelect = new SelectMenuBuilder()
            .WithCustomId("admin_add_match_date")
            .WithPlaceholder("Wybierz datę")
            .WithOptions(dateOptions)
            .WithMinValues(1)
            .WithMaxValues(1);

        // Time controls
        var timeMinus15 = new ButtonBuilder()
            .WithCustomId("admin_time_minus_15")
            .WithLabel("⏪ -15 min")
            .WithStyle(ButtonStyle.Secondary);
        var timePlus15 = new ButtonBuilder()
            .WithCustomId("admin_time_plus_15")
            .WithLabel("⏩ +15 min")
            .WithStyle(ButtonStyle.Secondary);
        var timeManual = new ButtonBuilder()
            .WithCustomId("admin_time_manual")
            .WithLabel("✏️ Ustaw godzinę ręcznie")
            .WithStyle(ButtonStyle.Secondary);

        var continueButton = new ButtonBuilder()
            .WithCustomId("admin_add_match_continue")
            .WithLabel("Kontynuuj")
            .WithStyle(ButtonStyle.Success);

        // Calendar navigation
        var prevMonth = new ButtonBuilder()
            .WithCustomId("admin_calendar_prev")
            .WithLabel("« Poprzedni miesiąc")
            .WithStyle(ButtonStyle.Secondary);
        var today = new ButtonBuilder()
            .WithCustomId("admin_calendar_today")
            .WithLabel("📅 Dziś")
            .WithStyle(ButtonStyle.Secondary);
        var nextMonth = new ButtonBuilder()
            .WithCustomId("admin_calendar_next")
            .WithLabel("Następny miesiąc »")
            .WithStyle(ButtonStyle.Secondary);

        var component = new ComponentBuilder()
            .WithSelectMenu(roundSelect, row: 0)
            .WithSelectMenu(dateSelect, row: 1)
            .WithButton(timeMinus15, row: 2)
            .WithButton(timePlus15, row: 2)
            .WithButton(timeManual, row: 2)
            .WithButton(prevMonth, row: 3)
            .WithButton(today, row: 3)
            .WithButton(nextMonth, row: 3)
            .WithButton(continueButton, row: 4)
            .Build();

        if (Context.Interaction.HasResponded)
        {
            await ModifyOriginalResponseAsync(prop => { prop.Embed = embed; prop.Components = component; });
        }
        else
        {
            await RespondAsync(embed: embed, components: component, ephemeral: true);
        }
    }

    private string GetPolishDayName(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => "pon",
            DayOfWeek.Tuesday => "wt",
            DayOfWeek.Wednesday => "śr",
            DayOfWeek.Thursday => "czw",
            DayOfWeek.Friday => "pt",
            DayOfWeek.Saturday => "sob",
            DayOfWeek.Sunday => "nie",
            _ => dayOfWeek.ToString().Substring(0, 3)
        };
    }

    [ComponentInteraction("admin_add_match_round")]
    public async Task HandleRoundSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (selectedValues.Length == 0 || !int.TryParse(selectedValues[0], out var round))
        {
            await RespondAsync("❌ Nieprawidłowy wybór rundy.", ephemeral: true);
            return;
        }

        _stateService.UpdateRound(Context.Guild.Id, Context.User.Id, round);
        _logger.LogInformation(
            "Wybrano rundę - Użytkownik: {Username} (ID: {UserId}), Runda: {Round}, Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            round,
            Context.Guild.Id,
            Context.Channel.Id);

        await DeferAsync();
        await ShowAddMatchCalendarAsync();
    }

    [ComponentInteraction("admin_add_match_date")]
    public async Task HandleDateSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (selectedValues.Length == 0)
        {
            await RespondAsync("❌ Nieprawidłowy wybór daty.", ephemeral: true);
            return;
        }

        _stateService.UpdateDate(Context.Guild.Id, Context.User.Id, selectedValues[0]);
        _logger.LogInformation(
            "Wybrano datę - Użytkownik: {Username} (ID: {UserId}), Data: {Date}, Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            selectedValues[0],
            Context.Guild.Id,
            Context.Channel.Id);

        await DeferAsync();
        await ShowAddMatchCalendarAsync();
    }

    [ComponentInteraction("admin_calendar_prev")]
    public async Task HandleCalendarPrevAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null) return;

        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
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
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null) return;

        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
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
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        _stateService.UpdateCalendarMonth(Context.Guild.Id, Context.User.Id, now.Year, now.Month);

        await DeferAsync();
        await ShowAddMatchCalendarAsync();
    }

    [ComponentInteraction("admin_time_minus_15")]
    public async Task HandleTimeMinus15Async()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null) return;

        var timeStr = !string.IsNullOrEmpty(state.SelectedTime) ? state.SelectedTime : "18:00";
        if (TimeSpan.TryParse(timeStr, out var time))
        {
            var newTime = time.Add(TimeSpan.FromMinutes(-15));
            if (newTime.TotalDays >= 1) newTime = newTime.Add(TimeSpan.FromDays(-1));
            if (newTime.TotalDays < 0) newTime = newTime.Add(TimeSpan.FromDays(1));
            _stateService.UpdateTime(Context.Guild.Id, Context.User.Id, $"{(int)newTime.TotalHours:D2}:{newTime.Minutes:D2}");
        }

        await DeferAsync();
        await ShowAddMatchCalendarAsync();
    }

    [ComponentInteraction("admin_time_plus_15")]
    public async Task HandleTimePlus15Async()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null) return;

        var timeStr = !string.IsNullOrEmpty(state.SelectedTime) ? state.SelectedTime : "18:00";
        if (TimeSpan.TryParse(timeStr, out var time))
        {
            var newTime = time.Add(TimeSpan.FromMinutes(15));
            if (newTime.TotalDays >= 1) newTime = newTime.Add(TimeSpan.FromDays(-1));
            _stateService.UpdateTime(Context.Guild.Id, Context.User.Id, $"{(int)newTime.TotalHours:D2}:{newTime.Minutes:D2}");
        }

        await DeferAsync();
        await ShowAddMatchCalendarAsync();
    }

    [ComponentInteraction("admin_time_manual")]
    public async Task HandleTimeManualAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        var defaultTime = !string.IsNullOrEmpty(state?.SelectedTime) ? state.SelectedTime : "18:00";

        var modal = new ModalBuilder()
            .WithTitle("Ustaw godzinę")
            .WithCustomId("admin_time_modal")
            .AddTextInput("Godzina", "godzina", TextInputStyle.Short, placeholder: "18:30", value: defaultTime, required: true)
            .Build();

        await RespondWithModalAsync(modal);
    }

    [ModalInteraction("admin_time_modal")]
    public async Task HandleTimeModalAsync(string godzina)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        // Validate time format HH:mm
        if (!TimeSpan.TryParse(godzina, out var time) || time.TotalHours >= 24)
        {
            _logger.LogWarning(
                "Nieprawidłowy format godziny - Użytkownik: {Username} (ID: {UserId}), Godzina: {Time}, Serwer: {GuildId}",
                Context.User.Username,
                Context.User.Id,
                godzina,
                Context.Guild.Id);
            await RespondAsync("❌ Nieprawidłowy format godziny. Użyj HH:mm, np. 18:30.", ephemeral: true);
            return;
        }

        _stateService.UpdateTime(Context.Guild.Id, Context.User.Id, $"{(int)time.TotalHours:D2}:{time.Minutes:D2}");
        _logger.LogInformation(
            "Godzina ustawiona ręcznie - Użytkownik: {Username} (ID: {UserId}), Godzina: {Time}, Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            godzina,
            Context.Guild.Id,
            Context.Channel.Id);

        await RespondAsync($"✅ Godzina ustawiona na: {godzina}", ephemeral: true);
    }

    [ComponentInteraction("admin_add_match_continue")]
    public async Task HandleContinueButtonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null || !state.SelectedRound.HasValue || string.IsNullOrEmpty(state.SelectedDate) || string.IsNullOrEmpty(state.SelectedTime))
        {
            await RespondAsync("❌ Wybierz rundę, datę i godzinę przed kontynuowaniem.", ephemeral: true);
            return;
        }

        _logger.LogInformation(
            "Przycisk kontynuuj kliknięty - Użytkownik: {Username} (ID: {UserId}), Runda: {Round}, Data: {Date}, Godzina: {Time}, Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            state.SelectedRound.Value,
            state.SelectedDate,
            state.SelectedTime,
            Context.Guild.Id,
            Context.Channel.Id);

        var modal = new ModalBuilder()
            .WithTitle("Dodaj mecz")
            .WithCustomId("admin_add_match_modal")
            .AddTextInput("Drużyna domowa", "home_team", TextInputStyle.Short, placeholder: "Motor Lublin", required: true)
            .AddTextInput("Drużyna wyjazdowa", "away_team", TextInputStyle.Short, placeholder: "Włókniarz Częstochowa", required: true)
            .Build();

        await RespondWithModalAsync(modal);
    }

    [ModalInteraction("admin_add_match_modal")]
    public async Task HandleAddMatchModalAsync(string homeTeam, string awayTeam)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        // Retrieve cached state
        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null || !state.SelectedRound.HasValue || string.IsNullOrEmpty(state.SelectedDate) || string.IsNullOrEmpty(state.SelectedTime))
        {
            _logger.LogWarning(
                "Modal tworzenia meczu przesłany, ale stan wygasł/brak - Użytkownik: {Username} (ID: {UserId}), Serwer: {GuildId}, Kanał: {ChannelId}",
                Context.User.Username,
                Context.User.Id,
                Context.Guild.Id,
                Context.Channel.Id);
            await RespondAsync("❌ Twój formularz dodawania meczu wygasł, otwórz ponownie /panel-admina i spróbuj ponownie.", ephemeral: true);
            return;
        }

        var roundNum = state.SelectedRound.Value;
        var dateStr = state.SelectedDate;
        var timeStr = state.SelectedTime;

        _logger.LogInformation(
            "Modal dodaj mecz przesłany - Użytkownik: {Username} (ID: {UserId}), Runda: {Round}, Data: {Date}, Godzina: {Time}, Drużyna domowa: {HomeTeam}, Drużyna wyjazdowa: {AwayTeam}, Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            roundNum,
            dateStr,
            timeStr,
            homeTeam,
            awayTeam,
            Context.Guild.Id,
            Context.Channel.Id);

        // Parse date/time
        DateTimeOffset startTime;
        try
        {
            if (!DateTime.TryParse($"{dateStr} {timeStr}", out var localTime))
            {
                _logger.LogError(
                    "Nie udało się sparsować daty/godziny - Użytkownik: {Username} (ID: {UserId}), Data: {Date}, Godzina: {Time}, Serwer: {GuildId}, Kanał: {ChannelId}",
                    Context.User.Username,
                    Context.User.Id,
                    dateStr,
                    timeStr,
                    Context.Guild.Id,
                    Context.Channel.Id);
                await RespondAsync("❌ Nie udało się sparsować daty/godziny meczu. Spróbuj ponownie.", ephemeral: true);
                return;
            }

            // Convert to configured timezone, then to UTC for storage
            var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
            var localDateTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
            startTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);
            
            _logger.LogInformation(
                "Data/godzina sparsowana - Data lokalna: {LocalTime}, UTC: {UtcTime}, Strefa czasowa: {Timezone}",
                localDateTime,
                startTime,
                _settings.Timezone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Wyjątek podczas parsowania daty/godziny - Użytkownik: {Username} (ID: {UserId}), Data: {Date}, Godzina: {Time}, Serwer: {GuildId}, Kanał: {ChannelId}",
                Context.User.Username,
                Context.User.Id,
                dateStr,
                timeStr,
                Context.Guild.Id,
                Context.Channel.Id);
            await RespondAsync("❌ Nie udało się sparsować daty/godziny meczu. Spróbuj ponownie.", ephemeral: true);
            return;
        }

        // Validate start time is in the future
        if (startTime <= DateTimeOffset.UtcNow)
        {
            _logger.LogWarning(
                "Data rozpoczęcia meczu w przeszłości - Użytkownik: {Username} (ID: {UserId}), StartTime UTC: {StartTimeUtc}, StartTime Local: {StartTimeLocal}, Serwer: {GuildId}, Kanał: {ChannelId}",
                Context.User.Username,
                Context.User.Id,
                startTime,
                TimeZoneInfo.ConvertTimeFromUtc(startTime.UtcDateTime, TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone)),
                Context.Guild.Id,
                Context.Channel.Id);
            await RespondAsync("❌ Data rozpoczęcia meczu musi być w przyszłości.", ephemeral: true);
            return;
        }

        // Create match
        Domain.Entities.Match? match = null;
        try
        {
            var (success, error, createdMatch) = await _matchService.CreateMatchAsync(roundNum, homeTeam, awayTeam, startTime);
            
            if (!success)
            {
                _logger.LogError(
                    "Tworzenie meczu nie powiodło się - Użytkownik: {Username} (ID: {UserId}), Runda: {Round}, Data: {Date}, Godzina: {Time}, Drużyna domowa: {HomeTeam}, Drużyna wyjazdowa: {AwayTeam}, Błąd: {Error}, Serwer: {GuildId}, Kanał: {ChannelId}",
                    Context.User.Username,
                    Context.User.Id,
                    roundNum,
                    dateStr,
                    timeStr,
                    homeTeam,
                    awayTeam,
                    error,
                    Context.Guild.Id,
                    Context.Channel.Id);
                await RespondAsync($"❌ Błąd podczas tworzenia meczu: {error ?? "Nieznany błąd"}", ephemeral: true);
                return;
            }

            match = createdMatch;
            if (match == null)
            {
                _logger.LogError(
                    "Tworzenie meczu zwróciło null - Użytkownik: {Username} (ID: {UserId}), Runda: {Round}, Serwer: {GuildId}, Kanał: {ChannelId}",
                    Context.User.Username,
                    Context.User.Id,
                    roundNum,
                    Context.Guild.Id,
                    Context.Channel.Id);
                await RespondAsync("❌ Wystąpił błąd podczas tworzenia meczu. Szczegóły zapisano w logach. Sprawdź poprawność rundy, daty i godziny.", ephemeral: true);
                return;
            }

            // Clear state
            _stateService.ClearState(Context.Guild.Id, Context.User.Id);

            // Post match card to predictions channel
            await PostMatchCardAsync(match, roundNum);

            var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
            var localStartTime = TimeZoneInfo.ConvertTimeFromUtc(match.StartTime.UtcDateTime, tz);
            
            _logger.LogInformation(
                "Mecz utworzony pomyślnie - ID meczu: {MatchId}, Runda: {Round}, {HomeTeam} vs {AwayTeam}, StartTime UTC: {StartTimeUtc}, StartTime Local: {StartTimeLocal}, Kanał: {ChannelName}, Serwer: {GuildId}",
                match.Id,
                roundNum,
                homeTeam,
                awayTeam,
                match.StartTime,
                localStartTime,
                Context.Channel.Name,
                Context.Guild.Id);

            await RespondAsync(
                $"✅ Mecz utworzony: Runda {roundNum}, {homeTeam} vs {awayTeam} o {localStartTime:yyyy-MM-dd HH:mm}.",
                ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Wyjątek podczas tworzenia meczu - Użytkownik: {Username} (ID: {UserId}), Runda: {Round}, Data: {Date}, Godzina: {Time}, Drużyna domowa: {HomeTeam}, Drużyna wyjazdowa: {AwayTeam}, Serwer: {GuildId}, Kanał: {ChannelId}",
                Context.User.Username,
                Context.User.Id,
                roundNum,
                dateStr,
                timeStr,
                homeTeam,
                awayTeam,
                Context.Guild.Id,
                Context.Channel.Id);
            await RespondAsync("❌ Wystąpił błąd podczas tworzenia meczu. Szczegóły zapisano w logach. Sprawdź poprawność rundy, daty i godziny.", ephemeral: true);
        }
    }

    private async Task PostMatchCardAsync(Domain.Entities.Match match, int roundNum, IUserMessage? existingMessage = null)
    {
        var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
        if (predictionsChannel == null)
        {
            _logger.LogError("Kanał typowań nie znaleziony, nie można opublikować karty meczu");
            return;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(match.StartTime.UtcDateTime, tz);

        var timestamp = ((DateTimeOffset)localTime).ToUnixTimeSeconds();
        var embed = new EmbedBuilder()
            .WithTitle($"Runda {roundNum}: {match.HomeTeam} vs {match.AwayTeam}")
            .WithDescription(
                "📋 **Zasady typowania:**\n" +
                "• Typy są tajne (tylko ty je widzisz)\n" +
                "• Suma musi wynosić 90 punktów (np. 50:40, 46:44, 45:45)\n" +
                "• Termin: czas rozpoczęcia meczu"
            )
            .AddField("🏁 Czas rozpoczęcia", $"<t:{timestamp}:F>", inline: true)
            .WithColor(Color.Blue)
            .Build();

        var predictButton = new ButtonBuilder()
            .WithCustomId($"predict_match_{match.Id}")
            .WithLabel("🔢 Typuj wynik")
            .WithStyle(ButtonStyle.Primary);

        var setResultButton = new ButtonBuilder()
            .WithCustomId($"admin_set_result_{match.Id}")
            .WithLabel("✅ Ustaw wynik")
            .WithStyle(ButtonStyle.Success);

        var editButton = new ButtonBuilder()
            .WithCustomId($"admin_edit_match_{match.Id}")
            .WithLabel("✏ Edytuj mecz")
            .WithStyle(ButtonStyle.Secondary);

        var deleteButton = new ButtonBuilder()
            .WithCustomId($"admin_delete_match_{match.Id}")
            .WithLabel("🗑 Usuń mecz")
            .WithStyle(ButtonStyle.Danger);

        var component = new ComponentBuilder()
            .WithButton(predictButton, row: 0)
            .WithButton(setResultButton, row: 0)
            .WithButton(editButton, row: 1)
            .WithButton(deleteButton, row: 1)
            .Build();

        if (existingMessage != null)
        {
            await existingMessage.ModifyAsync(prop => { prop.Embed = embed; prop.Components = component; });
            _logger.LogInformation("Karta meczu zaktualizowana - ID meczu: {MatchId}", match.Id);
        }
        else
        {
            var thread = await predictionsChannel.CreateThreadAsync(
                name: $"Runda {roundNum}: {match.HomeTeam} vs {match.AwayTeam}",
                type: ThreadType.PublicThread
            );

            await thread.SendMessageAsync(embed: embed, components: component);
            _logger.LogInformation("Karta meczu opublikowana w kanale typowań - ID meczu: {MatchId}", match.Id);
        }
    }

    [ComponentInteraction("admin_set_result_*")]
    public async Task HandleSetResultButtonAsync(string matchIdStr)
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

        var modal = new ModalBuilder()
            .WithTitle("Ustaw wynik meczu")
            .WithCustomId($"admin_set_result_modal_{matchId}")
            .AddTextInput("Wynik drużyny domowej", "home_score", TextInputStyle.Short, placeholder: "50", required: true)
            .AddTextInput("Wynik drużyny wyjazdowej", "away_score", TextInputStyle.Short, placeholder: "40", required: true)
            .Build();

        await RespondWithModalAsync(modal);
        _logger.LogInformation(
            "Przycisk ustaw wynik kliknięty - Użytkownik: {Username} (ID: {UserId}), ID meczu: {MatchId}, Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            matchId,
            Context.Guild.Id,
            Context.Channel.Id);
    }

    [ModalInteraction("admin_set_result_modal_*")]
    public async Task HandleSetResultModalAsync(string matchIdStr, string homeScore, string awayScore)
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

        if (!int.TryParse(homeScore, out var home) || !int.TryParse(awayScore, out var away))
        {
            await RespondAsync("❌ Wprowadź prawidłowe liczby dla obu wyników.", ephemeral: true);
            return;
        }

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await RespondAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        // Update match result
        match.HomeScore = home;
        match.AwayScore = away;
        match.Status = MatchStatus.Finished;
        await _matchRepository.UpdateAsync(match);

        // Calculate scores for all predictions
        await _predictionService.RecalculateMatchScoresAsync(matchId);

        await RespondAsync($"✅ Wynik ustawiony: **{home}:{away}**\nPunkty obliczone!", ephemeral: true);
        _logger.LogInformation(
            "Wynik meczu ustawiony - ID meczu: {MatchId}, Wynik: {Home}:{Away}, Punkty obliczone. Serwer: {GuildId}, Kanał: {ChannelId}",
            matchId,
            home,
            away,
            Context.Guild?.Id,
            Context.Channel.Id);

        // Post standings tables
        await PostStandingsAfterResultAsync(match);
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

        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(match.StartTime.UtcDateTime, tz);

        var modal = new ModalBuilder()
            .WithTitle("Edytuj mecz")
            .WithCustomId($"admin_edit_match_modal_{matchId}")
            .AddTextInput("Drużyna domowa", "home_team", TextInputStyle.Short, value: match.HomeTeam, required: true)
            .AddTextInput("Drużyna wyjazdowa", "away_team", TextInputStyle.Short, value: match.AwayTeam, required: true)
            .AddTextInput("Data", "date", TextInputStyle.Short, value: localTime.ToString("yyyy-MM-dd"), required: true)
            .AddTextInput("Godzina", "time", TextInputStyle.Short, value: localTime.ToString("HH:mm"), required: true)
            .Build();

        await RespondWithModalAsync(modal);
        _logger.LogInformation(
            "Przycisk edytuj mecz kliknięty - Użytkownik: {Username} (ID: {UserId}), ID meczu: {MatchId}, Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            matchId,
            Context.Guild.Id,
            Context.Channel.Id);
    }

    [ModalInteraction("admin_edit_match_modal_*")]
    public async Task HandleEditMatchModalAsync(string matchIdStr, string homeTeam, string awayTeam, string date, string time)
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

        var oldHomeTeam = match.HomeTeam;
        var oldAwayTeam = match.AwayTeam;
        var oldStartTime = match.StartTime;

        _logger.LogInformation(
            "Modal edytuj mecz przesłany - Użytkownik: {Username} (ID: {UserId}), ID meczu: {MatchId}, Stare: {OldHome} vs {OldAway} {OldTime}, Nowe: {NewHome} vs {NewAway} {NewDate} {NewTime}, Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            matchId,
            oldHomeTeam,
            oldAwayTeam,
            oldStartTime,
            homeTeam,
            awayTeam,
            date,
            time,
            Context.Guild.Id,
            Context.Channel.Id);

        // Parse date/time
        DateTimeOffset startTime;
        try
        {
            if (!DateTime.TryParse($"{date} {time}", out var localTime))
            {
                _logger.LogError(
                    "Nie udało się sparsować daty/godziny w edycji - Użytkownik: {Username} (ID: {UserId}), ID meczu: {MatchId}, Data: {Date}, Godzina: {Time}",
                    Context.User.Username,
                    Context.User.Id,
                    matchId,
                    date,
                    time);
                await RespondAsync("❌ Nieprawidłowy format daty lub godziny. Użyj YYYY-MM-DD dla daty i HH:MM dla godziny.", ephemeral: true);
                return;
            }

            var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
            var localDateTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
            startTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Wyjątek podczas parsowania daty/godziny w edycji - Użytkownik: {Username} (ID: {UserId}), ID meczu: {MatchId}",
                Context.User.Username,
                Context.User.Id,
                matchId);
            await RespondAsync("❌ Błąd podczas parsowania daty/godziny.", ephemeral: true);
            return;
        }

        // Update match
        match.HomeTeam = homeTeam;
        match.AwayTeam = awayTeam;
        match.StartTime = startTime;
        await _matchRepository.UpdateAsync(match);

        // Update match card (we'd need to find the message, but for now just log)
        // TODO: Find and update the match card message
        var round = match.Round;
        var roundNum = round?.Number ?? 0;
        await PostMatchCardAsync(match, roundNum);

        _logger.LogInformation(
            "Mecz zaktualizowany - ID meczu: {MatchId}, Runda: {Round}, Stare: {OldHome} vs {OldAway} {OldTime}, Nowe: {NewHome} vs {NewAway} {NewTime}",
            matchId,
            roundNum,
            oldHomeTeam,
            oldAwayTeam,
            oldStartTime,
            homeTeam,
            awayTeam,
            startTime);

        await RespondAsync("✅ Mecz został zaktualizowany.", ephemeral: true);
    }

    [ComponentInteraction("admin_delete_match_*")]
    public async Task HandleDeleteMatchButtonAsync(string matchIdStr)
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

        var round = match.Round;
        var roundNum = round?.Number ?? 0;

        _logger.LogInformation(
            "Usuwanie meczu - Użytkownik: {Username} (ID: {UserId}), ID meczu: {MatchId}, Runda: {Round}, {HomeTeam} vs {AwayTeam}, Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            matchId,
            roundNum,
            match.HomeTeam,
            match.AwayTeam,
            Context.Guild.Id,
            Context.Channel.Id);

        // Mark as cancelled instead of deleting (to preserve predictions)
        match.Status = MatchStatus.Cancelled;
        await _matchRepository.UpdateAsync(match);

        await RespondAsync("✅ Mecz został usunięty (oznaczony jako odwołany).", ephemeral: true);
    }

    [SlashCommand("wyniki-gracza", "Wyświetl wyniki konkretnego gracza (tylko dla adminów)")]
    public async Task PlayerResultsAsync([Summary(description: "Użytkownik")] IUser uzytkownik)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        _logger.LogInformation(
            "Komenda wyniki gracza - Użytkownik wykonujący: {Username} (ID: {UserId}), Gracz: {PlayerUsername} (ID: {PlayerId}), Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            uzytkownik.Username,
            uzytkownik.Id,
            Context.Guild.Id,
            Context.Channel.Id);

        var player = await _playerRepository.GetByDiscordUserIdAsync(uzytkownik.Id);
        if (player == null)
        {
            await RespondAsync($"❌ Gracz {uzytkownik.Mention} nie został znaleziony w bazie danych.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await RespondAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        // Get all player scores for the season
        var totalPoints = player.PlayerScores.Sum(ps => ps.Points);
        var roundPoints = new Dictionary<int, int>();
        var bucketCounts = new Dictionary<string, int>();

        foreach (var score in player.PlayerScores)
        {
            var prediction = await _predictionRepository.GetByIdAsync(score.PredictionId);
            if (prediction != null)
            {
                var match = await _matchRepository.GetByIdAsync(prediction.MatchId);
                if (match?.Round != null && match.Round.SeasonId == season.Id)
                {
                    var roundNum = match.Round.Number;
                    if (!roundPoints.ContainsKey(roundNum))
                        roundPoints[roundNum] = 0;
                    roundPoints[roundNum] += score.Points;
                }
            }

            var bucketKey = score.Bucket.ToString();
            if (!bucketCounts.ContainsKey(bucketKey))
                bucketCounts[bucketKey] = 0;
            bucketCounts[bucketKey]++;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"Wyniki gracza: {player.DiscordUsername}")
            .WithDescription($"**Łączne punkty w sezonie: {totalPoints}**")
            .WithColor(Color.Gold);

        // Add round breakdown
        if (roundPoints.Any())
        {
            var roundStr = string.Join("\n", roundPoints.OrderBy(r => r.Key).Select(r => $"Runda {r.Key}: {r.Value} pkt"));
            embed.AddField("Punkty według rundy", roundStr, inline: false);
        }

        // Add bucket breakdown
        if (bucketCounts.Any())
        {
            var bucketStr = string.Join(", ", bucketCounts.OrderByDescending(b => b.Value).Select(b => $"{b.Key}: {b.Value}x"));
            embed.AddField("Rozkład wyników", bucketStr, inline: false);
        }

        await RespondAsync(embed: embed.Build(), ephemeral: true);

        _logger.LogInformation(
            "Wyniki gracza wyświetlone - Gracz: {PlayerUsername} (ID: {PlayerId}), Łączne punkty: {TotalPoints}, Serwer: {GuildId}",
            player.DiscordUsername,
            player.Id,
            totalPoints,
            Context.Guild.Id);
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

    [SlashCommand("admin-eksport-sezonu", "Eksportuj pełne dane sezonu do CSV (tylko dla adminów)")]
    public async Task ExportSeasonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await RespondAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await RespondAsync("❌ Brak aktywnych graczy.", ephemeral: true);
            return;
        }

        try
        {
            var csv = _exportService.ExportSeasonToCsv(season, players);
            await RespondWithFileAsync(
                new Discord.FileAttachment(new MemoryStream(csv), $"eksport-sezonu-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv")
            );
            _logger.LogInformation(
                "Eksport sezonu wygenerowany - Użytkownik: {Username} (ID: {UserId}), Serwer: {GuildId}, Kanał: {ChannelId}",
                Context.User.Username,
                Context.User.Id,
                Context.Guild.Id,
                Context.Channel.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Nie udało się wygenerować eksportu sezonu - Użytkownik: {Username} (ID: {UserId}), Serwer: {GuildId}",
                Context.User.Username,
                Context.User.Id,
                Context.Guild.Id);
            await RespondAsync("❌ Nie udało się wygenerować eksportu sezonu.", ephemeral: true);
        }
    }

    [SlashCommand("admin-eksport-kolejki", "Eksportuj dane kolejki do CSV (tylko dla adminów)")]
    public async Task ExportRoundAsync([Summary(description: "Numer kolejki")] int round)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await RespondAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var roundEntity = await _roundRepository.GetByNumberAsync(season.Id, round);
        if (roundEntity == null)
        {
            await RespondAsync($"❌ Kolejka {round} nie znaleziona.", ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await RespondAsync("❌ Brak aktywnych graczy.", ephemeral: true);
            return;
        }

        try
        {
            var csv = _exportService.ExportRoundToCsv(roundEntity, players);
            await RespondWithFileAsync(
                new Discord.FileAttachment(new MemoryStream(csv), $"eksport-kolejki-{round}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv")
            );
            _logger.LogInformation(
                "Eksport kolejki {Round} wygenerowany - Użytkownik: {Username} (ID: {UserId}), Serwer: {GuildId}, Kanał: {ChannelId}",
                round,
                Context.User.Username,
                Context.User.Id,
                Context.Guild.Id,
                Context.Channel.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Nie udało się wygenerować eksportu kolejki - Użytkownik: {Username} (ID: {UserId}), Kolejka: {Round}, Serwer: {GuildId}",
                Context.User.Username,
                Context.User.Id,
                round,
                Context.Guild.Id);
            await RespondAsync("❌ Nie udało się wygenerować eksportu kolejki.", ephemeral: true);
        }
    }

    [SlashCommand("admin-dane-testowe", "Wypełnij bazę danych danymi testowymi (tylko dla adminów)")]
    public async Task SeedDemoDataAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        _logger.LogInformation(
            "Komenda dane testowe wywołana - Użytkownik: {Username} (ID: {UserId}), Kanał: {ChannelName}, Serwer: {GuildId}",
            Context.User.Username,
            Context.User.Id,
            Context.Channel.Name,
            Context.Guild.Id);

        await DeferAsync(ephemeral: true);

        try
        {
            var result = await _demoDataSeeder.SeedDemoDataAsync();

            _logger.LogInformation(
                "Dane testowe utworzone pomyślnie - Sezony: {Seasons}, Kolejki: {Rounds}, Mecze: {Matches}, Gracze: {Players}, Typy: {Predictions}, Punkty: {Scores}",
                result.SeasonsCreated,
                result.RoundsCreated,
                result.MatchesCreated,
                result.PlayersCreated,
                result.PredictionsCreated,
                result.ScoresCreated);

            await FollowupAsync(
                $"✅ Dane testowe utworzone: {result.SeasonsCreated} sezon(ów), {result.RoundsCreated} kolejka(ek), {result.MatchesCreated} mecz(ów), {result.PlayersCreated} gracz(y), {result.PredictionsCreated} typ(ów), {result.ScoresCreated} wynik(ów punktowych).",
                ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Wyjątek podczas tworzenia danych testowych - Użytkownik: {Username} (ID: {UserId}), Serwer: {GuildId}",
                Context.User.Username,
                Context.User.Id,
                Context.Guild.Id);
            await FollowupAsync("❌ Wystąpił błąd podczas tworzenia danych testowych. Błąd został zapisany w logach.", ephemeral: true);
        }
    }
}

