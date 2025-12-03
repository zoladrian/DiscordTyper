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

    private bool IsAllowedChannel(SocketTextChannel? channel)
    {
        if (channel == null) return true; // Allow in DMs for testing
        
        var user = Context.User as SocketGuildUser;
        // Administrators can use commands anywhere
        if (IsAdmin(user))
        {
            return true;
        }
        
        var allowedChannels = new[] 
        { 
            _settings.Channels.AdminChannel,
            _settings.Channels.PredictionsChannel 
        };
        
        return allowedChannels.Contains(channel.Name);
    }

    private async Task RespondWithErrorAsync(string message, string? details = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle("❌ Błąd")
            .WithDescription(message)
            .WithColor(Color.Red);
        
        if (!string.IsNullOrEmpty(details))
        {
            embed.AddField("Szczegóły", details, false);
        }
        
        if (Context.Interaction.HasResponded)
        {
            await FollowupAsync(embed: embed.Build(), ephemeral: true);
        }
        else
        {
            await RespondAsync(embed: embed.Build(), ephemeral: true);
        }
    }

    private async Task RespondWithSuccessAsync(string message)
    {
        var embed = new EmbedBuilder()
            .WithTitle("✅ Sukces")
            .WithDescription(message)
            .WithColor(Color.Green);
        
        if (Context.Interaction.HasResponded)
        {
            await FollowupAsync(embed: embed.Build(), ephemeral: true);
        }
        else
        {
            await RespondAsync(embed: embed.Build(), ephemeral: true);
        }
    }

    [SlashCommand("test-modal", "Testowy modal do debugowania - SUPER SZCZEGÓŁOWE LOGOWANIE")]
    public async Task TestModalAsync()
    {
        await RespondAsync("❌ Komenda testowa została usunięta.", ephemeral: true);
    }


    [SlashCommand("start-nowego-sezonu", "Rozpocznij nowy sezon typera.")]
    public async Task StartNewSeasonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondWithErrorAsync("Nie masz uprawnień do użycia tej komendy.");
            return;
        }

        await RespondWithModalAsync<StartSeasonModal>("admin_start_season_modal");
    }

    [ModalInteraction("admin_start_season_modal")]
    public async Task HandleStartSeasonModalAsync(StartSeasonModal modal)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(modal.SeasonName))
            {
                await RespondAsync("❌ Nazwa sezonu nie może być pusta.", ephemeral: true);
                return;
            }

            var trimmedName = modal.SeasonName.Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                await RespondAsync("❌ Nazwa sezonu nie może składać się tylko ze spacji.", ephemeral: true);
                return;
            }

            // Deactivate all existing seasons
            var allSeasons = await _seasonRepository.GetAllAsync();
            foreach (var season in allSeasons)
            {
                if (season.IsActive)
                {
                    season.IsActive = false;
                    await _seasonRepository.UpdateAsync(season);
                }
            }

            // Create new season
            var newSeason = new Domain.Entities.Season
            {
                Name = trimmedName,
                IsActive = true
            };

            await _seasonRepository.AddAsync(newSeason);

            _logger.LogInformation(
                "Nowy sezon utworzony - Użytkownik: {Username} (ID: {UserId}), Nazwa: {Name}, ID: {Id}",
                Context.User.Username, Context.User.Id, newSeason.Name, newSeason.Id);

            await RespondAsync($"✅ Nowy sezon **{newSeason.Name}** został utworzony i ustawiony jako aktywny.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Błąd podczas tworzenia sezonu - Użytkownik: {Username} (ID: {UserId}), Nazwa: {Name}",
                Context.User.Username, Context.User.Id, modal.SeasonName);
            await RespondAsync("❌ Wystąpił błąd podczas tworzenia sezonu. Sprawdź logi dla szczegółów.", ephemeral: true);
        }
    }

    [SlashCommand("panel-sezonu", "Otwórz panel sezonu typera.")]
    public async Task AdminPanelAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondWithErrorAsync("Nie masz uprawnień do użycia tej komendy.");
            return;
        }

        // Check if command is used in allowed channel
        var channel = Context.Channel as SocketTextChannel;
        if (!IsAllowedChannel(channel))
        {
            await RespondWithErrorAsync(
                $"Ta komenda może być używana tylko w kanałach: #{_settings.Channels.AdminChannel} lub #{_settings.Channels.PredictionsChannel}",
                $"Używasz: #{channel?.Name ?? "DM"}");
            return;
        }
        
        _logger.LogInformation(
            "Panel sezonu otwarty - Komenda: panel-sezonu, Użytkownik: {Username} (ID: {UserId}), Kanał: {ChannelName} (ID: {ChannelId}), Serwer: {GuildId}",
            Context.User.Username,
            Context.User.Id,
            (Context.Channel as SocketTextChannel)?.Name ?? "DM",
            Context.Channel?.Id ?? 0,
            Context.Guild?.Id);

        var allSeasons = (await _seasonRepository.GetAllAsync()).ToList();
        
        // If more than one season, show selection
        if (allSeasons.Count > 1)
        {
            var seasonOptions = allSeasons.Select(s => new SelectMenuOptionBuilder()
                .WithLabel(s.Name)
                .WithValue(s.Id.ToString())
                .WithDescription(s.IsActive ? "Aktywny" : "Zakończony"))
                .ToList();

            var selectMenu = new SelectMenuBuilder()
                .WithCustomId("admin_select_season")
                .WithPlaceholder("Wybierz sezon")
                .WithOptions(seasonOptions)
                .WithMinValues(1)
                .WithMaxValues(1);

            var embed = new EmbedBuilder()
                .WithTitle("Panel Sezonu Typera")
                .WithDescription("Wybierz sezon, którym chcesz zarządzać:")
                .WithColor(Color.Gold)
                .Build();

            var component = new ComponentBuilder()
                .WithSelectMenu(selectMenu)
                .Build();

            await RespondAsync(embed: embed, components: component, ephemeral: true);
            return;
        }

        // Single season or no season - proceed directly
        var season = allSeasons.FirstOrDefault(s => s.IsActive) ?? allSeasons.FirstOrDefault();
        await ShowSeasonPanelAsync(season);
    }

    [ComponentInteraction("admin_select_season")]
    public async Task HandleSelectSeasonAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (selectedValues.Length == 0 || !int.TryParse(selectedValues[0], out var seasonId))
        {
            await RespondAsync("❌ Nieprawidłowy wybór sezonu.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetByIdAsync(seasonId);
        if (season == null)
        {
            await RespondAsync("❌ Sezon nie znaleziony.", ephemeral: true);
            return;
        }

        await DeferAsync();
        await ShowSeasonPanelAsync(season);
    }

    private async Task ShowSeasonPanelAsync(Domain.Entities.Season? season)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Panel Sezonu Typera")
            .WithColor(Color.Gold);

        if (season != null)
        {
            var rounds = (await _roundRepository.GetBySeasonIdAsync(season.Id))
                .OrderBy(r => r.Number)
                .ToList();

            if (rounds.Any())
            {
                embed.WithDescription($"**Sezon: {season.Name}**\n\nPoniżej lista kolejek z meczami:");
                
                // Show last 5 rounds or upcoming ones
                var displayRounds = rounds.TakeLast(5).ToList(); 
                // Better logic: Show rounds that are not finished or just all of them if few.
                // Let's show up to 10 rounds.
                displayRounds = rounds.Take(10).ToList();

                foreach (var round in displayRounds)
                {
                    var matches = (await _matchRepository.GetByRoundIdAsync(round.Id)).ToList(); // Sorted by date in repo

                    if (matches.Any())
                    {
                        var matchList = string.Join("\n", matches.Select(m =>
                        {
                            var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
                            var localTime = TimeZoneInfo.ConvertTimeFromUtc(m.StartTime.UtcDateTime, tz);
                            
                            var statusEmoji = m.Status switch
                            {
                                MatchStatus.Scheduled => "⏰",
                                MatchStatus.InProgress => "▶️",
                                MatchStatus.Finished => "✅",
                                MatchStatus.Cancelled => "❌",
                                _ => "❓"
                            };

                            var score = "";
                            if (m.Status == MatchStatus.Finished && m.HomeScore.HasValue && m.AwayScore.HasValue)
                            {
                                score = $" **({m.HomeScore.Value}:{m.AwayScore.Value})**";
                            }

                            return $"{statusEmoji} `{localTime:MM-dd HH:mm}` {m.HomeTeam} vs {m.AwayTeam}{score}";
                        }));

                        if (matchList.Length > 1024)
                        {
                            matchList = matchList.Substring(0, 1020) + "...";
                        }
                        
                        embed.AddField($"📋 Kolejka {round.Number}", matchList, inline: false);
                    }
                    else
                    {
                        embed.AddField($"📋 Kolejka {round.Number}", "*Brak meczów*", inline: false);
                    }
                }
                
                if (rounds.Count > 10)
                {
                    embed.WithFooter($"Pokazano 10 z {rounds.Count} kolejek");
                }
            }
            else
            {
                embed.WithDescription("**Sezon aktywny, ale brak kolejek.**\n\nUżyj przycisków poniżej, aby zarządzać meczami.");
            }
        }
        else
        {
            embed.WithDescription("**Brak aktywnego sezonu.**\n\nSystem utworzy sezon automatycznie przy pierwszym dodaniu kolejki.");
        }

        var addKolejkaButton = new ButtonBuilder()
            .WithCustomId("admin_add_kolejka")
            .WithLabel("➕ Dodaj kolejkę")
            .WithStyle(ButtonStyle.Primary);

        var manageKolejkaButton = new ButtonBuilder()
            .WithCustomId("admin_manage_kolejka")
            .WithLabel("⚙ Zarządzaj kolejką")
            .WithStyle(ButtonStyle.Secondary);

        var addMatchButton = new ButtonBuilder()
            .WithCustomId("admin_add_match")
            .WithLabel("➕ Dodaj mecz")
            .WithStyle(ButtonStyle.Secondary);

        var tableSeasonButton = new ButtonBuilder()
            .WithCustomId("admin_table_season")
            .WithLabel("📊 Tabela sezonu")
            .WithStyle(ButtonStyle.Success);

        var tableRoundButton = new ButtonBuilder()
            .WithCustomId("admin_table_round")
            .WithLabel("📊 Tabela kolejki")
            .WithStyle(ButtonStyle.Success);

        var componentBuilder = new ComponentBuilder()
            .WithButton(addKolejkaButton, row: 0)
            .WithButton(manageKolejkaButton, row: 0)
            .WithButton(addMatchButton, row: 1)
            .WithButton(tableSeasonButton, row: 2)
            .WithButton(tableRoundButton, row: 2);

        // Add season management buttons if season exists
        if (season != null)
        {
            if (season.IsActive)
            {
                var endSeasonButton = new ButtonBuilder()
                    .WithCustomId($"admin_end_season_{season.Id}")
                    .WithLabel("🏁 Zakończ sezon")
                    .WithStyle(ButtonStyle.Danger);
                componentBuilder.WithButton(endSeasonButton, row: 3);
            }
            else
            {
                var reactivateSeasonButton = new ButtonBuilder()
                    .WithCustomId($"admin_reactivate_season_{season.Id}")
                    .WithLabel("🔄 Cofnij zakończenie sezonu")
                    .WithStyle(ButtonStyle.Secondary);
                componentBuilder.WithButton(reactivateSeasonButton, row: 3);
            }
        }

        await RespondAsync(embed: embed.Build(), components: componentBuilder.Build());
    }

    [ComponentInteraction("admin_end_season_*")]
    public async Task HandleEndSeasonAsync(string seasonIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(seasonIdStr, out var seasonId))
        {
            await RespondAsync("❌ Nieprawidłowy sezon.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetByIdAsync(seasonId);
        if (season == null)
        {
            await RespondAsync("❌ Sezon nie znaleziony.", ephemeral: true);
            return;
        }

        season.IsActive = false;
        await _seasonRepository.UpdateAsync(season);

        _logger.LogInformation(
            "Sezon zakończony - Użytkownik: {Username} (ID: {UserId}), Sezon: {Name} (ID: {Id})",
            Context.User.Username, Context.User.Id, season.Name, season.Id);

        await RespondAsync($"✅ Sezon **{season.Name}** został oznaczony jako zakończony.", ephemeral: true);
    }

    [ComponentInteraction("admin_reactivate_season_*")]
    public async Task HandleReactivateSeasonAsync(string seasonIdStr)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (!int.TryParse(seasonIdStr, out var seasonId))
        {
            await RespondAsync("❌ Nieprawidłowy sezon.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetByIdAsync(seasonId);
        if (season == null)
        {
            await RespondAsync("❌ Sezon nie znaleziony.", ephemeral: true);
            return;
        }

        // Deactivate all other seasons
        var allSeasons = await _seasonRepository.GetAllAsync();
        foreach (var s in allSeasons)
        {
            if (s.Id != seasonId && s.IsActive)
            {
                s.IsActive = false;
                await _seasonRepository.UpdateAsync(s);
            }
        }

        season.IsActive = true;
        await _seasonRepository.UpdateAsync(season);

        _logger.LogInformation(
            "Sezon reaktywowany - Użytkownik: {Username} (ID: {UserId}), Sezon: {Name} (ID: {Id})",
            Context.User.Username, Context.User.Id, season.Name, season.Id);

        await RespondAsync($"✅ Sezon **{season.Name}** został reaktywowany i ustawiony jako aktywny.", ephemeral: true);
    }

    [ComponentInteraction("admin_add_kolejka")]
    public async Task HandleAddKolejkaButtonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        _logger.LogInformation(
            "Przycisk dodaj kolejkę kliknięty - Użytkownik: {Username} (ID: {UserId}), Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            Context.Guild.Id,
            Context.Channel.Id);

        await RespondWithModalAsync<AddKolejkaModal>("admin_add_kolejka_modal");
    }

    [ModalInteraction("admin_add_kolejka_modal")]
    public async Task HandleAddKolejkaModalAsync(AddKolejkaModal modal)
    {
        var kolejkaNumber = modal.KolejkaNumber;
        var liczbaMeczow = modal.LiczbaMeczow;

        _logger.LogInformation(
            "Modal admin_add_kolejka_modal otrzymany - Użytkownik: {Username} (ID: {UserId}), KolejkaNum: '{Num}', MatchCount: '{Count}', Guild: {GuildId}, Channel: {ChannelId}",
            Context.User.Username, Context.User.Id, kolejkaNumber, liczbaMeczow, Context.Guild?.Id, Context.Channel?.Id);
        
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondWithErrorAsync("Nie masz uprawnień do użycia tej komendy.");
            return;
        }

        var channel = Context.Channel as SocketTextChannel;
        if (!IsAllowedChannel(channel))
        {
            await RespondWithErrorAsync(
                $"Ta komenda może być używana tylko w kanałach: #{_settings.Channels.AdminChannel} lub #{_settings.Channels.PredictionsChannel}",
                $"Używasz: #{channel?.Name ?? "DM"}");
            return;
        }

        if (!int.TryParse(kolejkaNumber, out var roundNumber) || roundNumber < 1 || roundNumber > 18)
        {
            _logger.LogWarning(
                "Nieprawidłowy numer kolejki - Użytkownik: {Username} (ID: {UserId}), Wprowadzono: '{Num}'",
                Context.User.Username, Context.User.Id, kolejkaNumber);
            await RespondWithErrorAsync("Nieprawidłowy numer kolejki.", "Podaj liczbę od 1 do 18.");
            return;
        }

        if (!int.TryParse(liczbaMeczow, out var matchCount) || matchCount < 1 || matchCount > 8)
        {
            _logger.LogWarning(
                "Nieprawidłowa liczba meczów - Użytkownik: {Username} (ID: {UserId}), Wprowadzono: '{Count}'",
                Context.User.Username, Context.User.Id, liczbaMeczow);
            await RespondWithErrorAsync("Nieprawidłowa liczba meczów.", "Podaj liczbę od 1 do 8.");
            return;
        }

        _logger.LogInformation(
            "Modal dodaj kolejkę przesłany - Użytkownik: {Username} (ID: {UserId}), Kolejka: {Round}, Liczba meczów: {MatchCount}, Serwer: {GuildId}",
            Context.User.Username,
            Context.User.Id,
            roundNumber,
            matchCount,
            Context.Guild.Id);

        // Check if round already exists
        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            _logger.LogWarning(
                "Brak aktywnego sezonu - Użytkownik: {Username} (ID: {UserId}), Serwer: {GuildId}, Kanał: {ChannelId}",
                Context.User.Username, Context.User.Id, Context.Guild?.Id ?? 0, Context.Channel?.Id ?? 0);
            await RespondWithErrorAsync(
                "Brak aktywnego sezonu.",
                "System automatycznie utworzy sezon przy pierwszym użyciu. Spróbuj ponownie za chwilę.");
            return; // ← CRITICAL FIX: Return early if no season
        }
        
        var existingRound = await _roundRepository.GetByNumberAsync(season.Id, roundNumber);
        if (existingRound != null)
        {
            await RespondAsync(
                $"❌ Kolejka o numerze {roundNumber} ({Application.Services.RoundHelper.GetRoundLabel(roundNumber)}) już istnieje. " +
                "Możesz ją edytować z panelu '⚙ Zarządzaj kolejką'.",
                ephemeral: true);
            return;
        }

        // Initialize kolejka creation flow
        _stateService.ClearState(Context.Guild.Id, Context.User.Id);
        _stateService.InitializeKolejkaCreation(Context.Guild.Id, Context.User.Id, roundNumber, matchCount);

        // Initialize time and calendar
        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        _stateService.UpdateCalendarMonth(Context.Guild.Id, Context.User.Id, now.Year, now.Month);
        _stateService.UpdateTime(Context.Guild.Id, Context.User.Id, "18:00");

        await RespondAsync(
            $"✅ Rozpoczynam tworzenie kolejki {roundNumber} ({Application.Services.RoundHelper.GetRoundLabel(roundNumber)}) z {matchCount} meczami.\n" +
            "Wypełnij dane dla każdego meczu.",
            ephemeral: true);

        // Show first match form
        await ShowKolejkaMatchFormAsync();
    }

    private async Task ShowKolejkaMatchFormAsync()
    {
        if (Context.Guild == null) return;

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null || !state.IsKolejkaCreation || !state.SelectedRound.HasValue)
        {
            await FollowupAsync("❌ Stan formularza wygasł. Rozpocznij ponownie z /panel-admina.", ephemeral: true);
            return;
        }

        var currentMatch = state.CurrentMatchIndex + 1;
        var totalMatches = state.TotalMatchesInKolejka;
        var roundLabel = Application.Services.RoundHelper.GetRoundLabel(state.SelectedRound.Value);

        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        int year = state.CurrentCalendarYear > 0 ? state.CurrentCalendarYear : now.Year;
        int month = state.CurrentCalendarMonth > 0 ? state.CurrentCalendarMonth : now.Month;

        var selectedDateStr = !string.IsNullOrEmpty(state.SelectedDate) ? $"Wybrana data: {state.SelectedDate}" : "Nie wybrano daty";
        var timeStr = !string.IsNullOrEmpty(state.SelectedTime) ? state.SelectedTime : "18:00";
        var selectedHomeTeam = !string.IsNullOrEmpty(state.SelectedHomeTeam) ? $"Drużyna domowa: {state.SelectedHomeTeam}" : "Nie wybrano drużyny domowej";
        var selectedAwayTeam = !string.IsNullOrEmpty(state.SelectedAwayTeam) ? $"Drużyna wyjazdowa: {state.SelectedAwayTeam}" : "Nie wybrano drużyny wyjazdowej";

        var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy", new System.Globalization.CultureInfo("pl-PL"));

        var embed = new EmbedBuilder()
            .WithTitle($"{roundLabel} - Mecz {currentMatch}/{totalMatches}")
            .WithDescription($"**{selectedHomeTeam}**\n**{selectedAwayTeam}**\n**{selectedDateStr}**\n**Godzina: {timeStr}**\n\nWybierz drużyny, datę i godzinę, a następnie kliknij Zatwierdź mecz.")
            .AddField("Miesiąc", monthName, true)
            .WithColor(Color.Blue)
            .Build();

        // Generate date options
        var dateOptions = new List<SelectMenuOptionBuilder>();
        var startDate = new DateTime(year, month, 1);
        var isCurrentMonth = (month == now.Month && year == now.Year);
        if (isCurrentMonth)
        {
            startDate = now.Date;
        }
        var endDate = new DateTime(year, month, 1).AddMonths(3);
        var currentDate = startDate;
        var maxDays = 25;

        while (currentDate < endDate && dateOptions.Count < maxDays)
        {
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

            if (currentDate.Day == 1 && dateOptions.Count >= 20)
            {
                break;
            }
        }

        // Team select menus
        var homeTeamOptions = Application.Services.TeamConstants.PgeEkstraligaTeams
            .Select(team => new SelectMenuOptionBuilder()
                .WithLabel(team)
                .WithValue(team))
            .ToList();

        var awayTeamOptions = Application.Services.TeamConstants.PgeEkstraligaTeams
            .Select(team => new SelectMenuOptionBuilder()
                .WithLabel(team)
                .WithValue(team))
            .ToList();

        var homeTeamSelect = new SelectMenuBuilder()
            .WithCustomId("admin_kolejka_home_team")
            .WithPlaceholder("Wybierz drużynę domową")
            .WithOptions(homeTeamOptions)
            .WithMinValues(1)
            .WithMaxValues(1);

        var awayTeamSelect = new SelectMenuBuilder()
            .WithCustomId("admin_kolejka_away_team")
            .WithPlaceholder("Wybierz drużynę wyjazdową")
            .WithOptions(awayTeamOptions)
            .WithMinValues(1)
            .WithMaxValues(1);

        var dateSelect = new SelectMenuBuilder()
            .WithCustomId("admin_kolejka_match_date")
            .WithPlaceholder("Wybierz datę")
            .WithOptions(dateOptions)
            .WithMinValues(1)
            .WithMaxValues(1);

        // Time controls
        var timeMinus15 = new ButtonBuilder()
            .WithCustomId("admin_kolejka_time_minus_15")
            .WithLabel("⏪ -15 min")
            .WithStyle(ButtonStyle.Secondary);
        var timePlus15 = new ButtonBuilder()
            .WithCustomId("admin_kolejka_time_plus_15")
            .WithLabel("⏩ +15 min")
            .WithStyle(ButtonStyle.Secondary);
        var timeManual = new ButtonBuilder()
            .WithCustomId("admin_kolejka_time_manual")
            .WithLabel("✏️ Ustaw godzinę")
            .WithStyle(ButtonStyle.Secondary);

        var submitMatchButton = new ButtonBuilder()
            .WithCustomId("admin_kolejka_submit_match")
            .WithLabel("Zatwierdź mecz")
            .WithStyle(ButtonStyle.Success);

        var cancelButton = new ButtonBuilder()
            .WithCustomId("admin_kolejka_cancel")
            .WithLabel("❌ Anuluj")
            .WithStyle(ButtonStyle.Danger);

        // Calendar navigation
        var prevMonth = new ButtonBuilder()
            .WithCustomId("admin_kolejka_calendar_prev")
            .WithLabel("« Poprzedni")
            .WithStyle(ButtonStyle.Secondary);
        var today = new ButtonBuilder()
            .WithCustomId("admin_kolejka_calendar_today")
            .WithLabel("📅 Dziś")
            .WithStyle(ButtonStyle.Secondary);
        var nextMonth = new ButtonBuilder()
            .WithCustomId("admin_kolejka_calendar_next")
            .WithLabel("Następny »")
            .WithStyle(ButtonStyle.Secondary);

        var component = new ComponentBuilder()
            .WithSelectMenu(homeTeamSelect, row: 0)
            .WithSelectMenu(awayTeamSelect, row: 1)
            .WithSelectMenu(dateSelect, row: 2)
            .WithButton(timeMinus15, row: 3)
            .WithButton(timePlus15, row: 3)
            .WithButton(timeManual, row: 3)
            .WithButton(prevMonth, row: 4)
            .WithButton(today, row: 4)
            .WithButton(nextMonth, row: 4)
            .WithButton(submitMatchButton, row: 4)
            .Build();

        await FollowupAsync(embed: embed, components: component, ephemeral: true);
    }

    [ComponentInteraction("admin_add_match")]
    public async Task HandleAddMatchButtonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondWithErrorAsync("Nie masz uprawnień do użycia tej komendy.");
            return;
        }
        
        _logger.LogInformation(
            "Przycisk dodaj mecz kliknięty - Użytkownik: {Username} (ID: {UserId}), Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            Context.Guild.Id,
            Context.Channel.Id);

        // Get active season and rounds for modal
        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await RespondWithErrorAsync("Brak aktywnego sezonu. Utwórz sezon przed dodaniem meczu.");
            return;
        }

        var rounds = (await _roundRepository.GetBySeasonIdAsync(season.Id)).ToList();
        if (!rounds.Any())
        {
            await RespondWithErrorAsync("Brak kolejek w sezonie. Utwórz najpierw kolejkę.");
            return;
        }

        // Get default values
        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var defaultDate = now.Date.AddDays(1).ToString("yyyy-MM-dd");
        var defaultTime = "18:00";
        var defaultRound = rounds.OrderBy(r => r.Number).First().Number;

        var modal = new AddMatchModalV2
        {
            RoundNumber = defaultRound.ToString(),
            MatchDate = defaultDate,
            MatchTime = defaultTime,
            HomeTeam = "Motor Lublin",
            AwayTeam = "Włókniarz Częstochowa"
        };

        try
        {
            await RespondWithModalAsync("admin_add_match_modal_v2", modal);
            _logger.LogInformation("Modal dodaj mecz wyświetlony - Użytkownik: {Username}", Context.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas wyświetlania modala dodaj mecz - Użytkownik: {Username}", Context.User.Username);
            await RespondWithErrorAsync($"Wystąpił błąd podczas wyświetlania formularza: {ex.Message}");
        }
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
            var label = Application.Services.RoundHelper.GetRoundLabel(i);
            var description = Application.Services.RoundHelper.GetRoundDescription(i);
            roundOptions.Add(new SelectMenuOptionBuilder()
                .WithLabel(label)
                .WithValue(i.ToString())
                .WithDescription(description));
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

    // Kolejka creation flow handlers
    [ComponentInteraction("admin_kolejka_home_team")]
    public async Task HandleKolejkaHomeTeamSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        if (selectedValues.Length > 0)
        {
            _stateService.UpdateHomeTeam(Context.Guild.Id, Context.User.Id, selectedValues[0]);
            _logger.LogInformation("Kolejka: wybrano drużynę domową {Team}", selectedValues[0]);
        }

        await DeferAsync();
    }

    [ComponentInteraction("admin_kolejka_away_team")]
    public async Task HandleKolejkaAwayTeamSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        if (selectedValues.Length > 0)
        {
            _stateService.UpdateAwayTeam(Context.Guild.Id, Context.User.Id, selectedValues[0]);
            _logger.LogInformation("Kolejka: wybrano drużynę wyjazdową {Team}", selectedValues[0]);
        }

        await DeferAsync();
    }

    [ComponentInteraction("admin_kolejka_match_date")]
    public async Task HandleKolejkaMatchDateSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        if (selectedValues.Length > 0)
        {
            _stateService.UpdateDate(Context.Guild.Id, Context.User.Id, selectedValues[0]);
            _logger.LogInformation("Kolejka: wybrano datę {Date}", selectedValues[0]);
        }

        await DeferAsync();
    }

    [ComponentInteraction("admin_kolejka_time_minus_15")]
    public async Task HandleKolejkaTimeMinus15Async()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null)
        {
            await RespondAsync("❌ Stan formularza wygasł.", ephemeral: true);
            return;
        }

        var timeStr = !string.IsNullOrEmpty(state.SelectedTime) ? state.SelectedTime : "18:00";
        if (TimeSpan.TryParse(timeStr, out var time))
        {
            var newTime = time.Add(TimeSpan.FromMinutes(-15));
            if (newTime.TotalDays >= 1) newTime = newTime.Add(TimeSpan.FromDays(-1));
            if (newTime.TotalDays < 0) newTime = newTime.Add(TimeSpan.FromDays(1));
            _stateService.UpdateTime(Context.Guild.Id, Context.User.Id, $"{(int)newTime.TotalHours:D2}:{newTime.Minutes:D2}");
        }

        await DeferAsync();
        await ShowKolejkaMatchFormAsync();
    }

    [ComponentInteraction("admin_kolejka_time_plus_15")]
    public async Task HandleKolejkaTimePlus15Async()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null)
        {
            await RespondAsync("❌ Stan formularza wygasł.", ephemeral: true);
            return;
        }

        var timeStr = !string.IsNullOrEmpty(state.SelectedTime) ? state.SelectedTime : "18:00";
        if (TimeSpan.TryParse(timeStr, out var time))
        {
            var newTime = time.Add(TimeSpan.FromMinutes(15));
            if (newTime.TotalDays >= 1) newTime = newTime.Add(TimeSpan.FromDays(-1));
            _stateService.UpdateTime(Context.Guild.Id, Context.User.Id, $"{(int)newTime.TotalHours:D2}:{newTime.Minutes:D2}");
        }

        await DeferAsync();
        await ShowKolejkaMatchFormAsync();
    }

    [ComponentInteraction("admin_kolejka_time_manual")]
    public async Task HandleKolejkaTimeManualAsync()
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
            .WithCustomId("admin_kolejka_time_modal")
            .AddTextInput("Godzina", "godzina", TextInputStyle.Short, placeholder: "18:30", value: defaultTime, required: true)
            .Build();

        await RespondWithModalAsync(modal);
    }

    [ModalInteraction("admin_kolejka_time_modal")]
    public async Task HandleKolejkaTimeModalAsync(string godzina)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        if (!TimeSpan.TryParse(godzina, out var time) || time.TotalHours >= 24)
        {
            _logger.LogWarning(
                "Nieprawidłowy format godziny - Użytkownik: {Username} (ID: {UserId}), Wprowadzono: {Time}, Serwer: {GuildId}",
                Context.User.Username,
                Context.User.Id,
                godzina,
                Context.Guild.Id);
            await RespondAsync("❌ Nieprawidłowy format godziny. Użyj HH:mm, np. 18:30.", ephemeral: true);
            return;
        }

        _stateService.UpdateTime(Context.Guild.Id, Context.User.Id, $"{(int)time.TotalHours:D2}:{time.Minutes:D2}");
        _logger.LogInformation(
            "Godzina ustawiona ręcznie (kolejka) - Użytkownik: {Username} (ID: {UserId}), Godzina: {Time}, Serwer: {GuildId}",
            Context.User.Username,
            Context.User.Id,
            godzina,
            Context.Guild.Id);

        await RespondAsync($"✅ Godzina ustawiona na: {godzina}", ephemeral: true);
    }

    [ComponentInteraction("admin_kolejka_calendar_prev")]
    public async Task HandleKolejkaCalendarPrevAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null)
        {
            await RespondAsync("❌ Stan formularza wygasł.", ephemeral: true);
            return;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        int year = state.CurrentCalendarYear > 0 ? state.CurrentCalendarYear : now.Year;
        int month = state.CurrentCalendarMonth > 0 ? state.CurrentCalendarMonth : now.Month;

        var date = new DateTime(year, month, 1).AddMonths(-1);
        _stateService.UpdateCalendarMonth(Context.Guild.Id, Context.User.Id, date.Year, date.Month);

        await DeferAsync();
        await ShowKolejkaMatchFormAsync();
    }

    [ComponentInteraction("admin_kolejka_calendar_next")]
    public async Task HandleKolejkaCalendarNextAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null)
        {
            await RespondAsync("❌ Stan formularza wygasł.", ephemeral: true);
            return;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        int year = state.CurrentCalendarYear > 0 ? state.CurrentCalendarYear : now.Year;
        int month = state.CurrentCalendarMonth > 0 ? state.CurrentCalendarMonth : now.Month;

        var date = new DateTime(year, month, 1).AddMonths(1);
        _stateService.UpdateCalendarMonth(Context.Guild.Id, Context.User.Id, date.Year, date.Month);

        await DeferAsync();
        await ShowKolejkaMatchFormAsync();
    }

    [ComponentInteraction("admin_kolejka_calendar_today")]
    public async Task HandleKolejkaCalendarTodayAsync()
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
        await ShowKolejkaMatchFormAsync();
    }

    [ComponentInteraction("admin_kolejka_submit_match")]
    public async Task HandleKolejkaSubmitMatchAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null || !state.IsKolejkaCreation || !state.SelectedRound.HasValue)
        {
            await RespondAsync("❌ Stan formularza wygasł. Rozpocznij ponownie.", ephemeral: true);
            return;
        }

        // Validate all fields are selected
        if (string.IsNullOrEmpty(state.SelectedHomeTeam) || string.IsNullOrEmpty(state.SelectedAwayTeam) ||
            string.IsNullOrEmpty(state.SelectedDate) || string.IsNullOrEmpty(state.SelectedTime))
        {
            await RespondAsync("❌ Wybierz wszystkie pola (drużyny, datę, godzinę) przed zatwierdzeniem.", ephemeral: true);
            return;
        }

        // Validate teams are different
        if (state.SelectedHomeTeam == state.SelectedAwayTeam)
        {
            await RespondAsync("❌ Drużyna domowa i wyjazdowa muszą być różne.", ephemeral: true);
            return;
        }

        // Add match to collection
        _stateService.AddMatchToKolejka(Context.Guild.Id, Context.User.Id,
            state.SelectedHomeTeam, state.SelectedAwayTeam, state.SelectedDate, state.SelectedTime);

        _logger.LogInformation(
            "Kolejka: mecz {Index}/{Total} zatwierdzony - {Home} vs {Away}, {Date} {Time}",
            state.CurrentMatchIndex,
            state.TotalMatchesInKolejka,
            state.SelectedHomeTeam,
            state.SelectedAwayTeam,
            state.SelectedDate,
            state.SelectedTime);

        // Clear team selections for next match
        _stateService.UpdateHomeTeam(Context.Guild.Id, Context.User.Id, "");
        _stateService.UpdateAwayTeam(Context.Guild.Id, Context.User.Id, "");

        // Check if we've collected all matches
        var updatedState = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (updatedState != null && updatedState.CurrentMatchIndex >= updatedState.TotalMatchesInKolejka)
        {
            // All matches collected, defer first to keep interaction alive
            await DeferAsync();
            // Create them
            await CreateKolejkaMatchesAsync();
        }
        else
        {
            // Show form for next match
            await DeferAsync();
            await ShowKolejkaMatchFormAsync();
        }
    }

    private async Task CreateKolejkaMatchesAsync()
    {
        if (Context.Guild == null) return;

        var state = _stateService.GetState(Context.Guild.Id, Context.User.Id);
        if (state == null || !state.IsKolejkaCreation || !state.SelectedRound.HasValue)
        {
            try
            {
                await FollowupAsync("❌ Błąd: stan formularza wygasł.", ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nie udało się wysłać odpowiedzi - interakcja wygasła");
            }
            return;
        }

        var roundNumber = state.SelectedRound.Value;
        var roundLabel = Application.Services.RoundHelper.GetRoundLabel(roundNumber);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);

        try
        {
            var createdMatches = new List<Domain.Entities.Match>();
            var errors = new List<string>();

            foreach (var (homeTeam, awayTeam, dateStr, timeStr) in state.CollectedMatches)
            {
                // Parse date/time
                if (!DateTime.TryParse($"{dateStr} {timeStr}", out var localTime))
                {
                    _logger.LogError("Nie udało się sparsować daty/godziny: {Date} {Time}", dateStr, timeStr);
                    errors.Add($"❌ Błąd parsowania daty/godziny dla meczu {homeTeam} vs {awayTeam}.");
                    continue;
                }

                var localDateTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
                var startTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);

                // Create match
                var (success, error, match) = await _matchService.CreateMatchAsync(roundNumber, homeTeam, awayTeam, startTime);

                if (!success || match == null)
                {
                    _logger.LogError(
                        "Tworzenie meczu nie powiodło się - Kolejka: {Round}, {Home} vs {Away}, Błąd: {Error}",
                        roundNumber, homeTeam, awayTeam, error);
                    errors.Add($"❌ Błąd tworzenia meczu {homeTeam} vs {awayTeam}: {error}");
                    continue;
                }

                createdMatches.Add(match);

                // Post match card
                await PostMatchCardAsync(match, roundNumber);
            }

            // Clear state
            _stateService.ClearState(Context.Guild.Id, Context.User.Id);

            _logger.LogInformation(
                "Kolejka utworzona pomyślnie - Kolejka: {Round} ({Label}), Liczba meczów: {Count}",
                roundNumber, roundLabel, createdMatches.Count);

            // Build response message
            var responseMessage = $"✅ Dodano kolejkę {roundLabel} z {createdMatches.Count} meczami.";
            if (errors.Any())
            {
                responseMessage += $"\n\n⚠️ Wystąpiły błędy:\n{string.Join("\n", errors)}";
            }

            try
            {
                await FollowupAsync(responseMessage, ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nie udało się wysłać odpowiedzi - interakcja wygasła, ale mecze zostały utworzone");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Wyjątek podczas tworzenia meczów kolejki {Round}", roundNumber);
            try
            {
                await FollowupAsync("❌ Wystąpił błąd podczas tworzenia kolejki. Szczegóły w logach.", ephemeral: true);
            }
            catch
            {
                _logger.LogError("Nie udało się wysłać odpowiedzi o błędzie - interakcja wygasła");
            }
        }
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
            "Przycisk kontynuuj kliknięty - Użytkownik: {Username} (ID: {UserId}), Runda: {Round}, Data: {Date}, Godzina: {Time}, Serwer: {GuildId}, Kanał: {ChannelId}, HasResponded: {HasResponded}",
            Context.User.Username,
            Context.User.Id,
            state.SelectedRound.Value,
            state.SelectedDate,
            state.SelectedTime,
            Context.Guild.Id,
            Context.Channel.Id,
            Context.Interaction.HasResponded);

        try
        {
            var modal = new ModalBuilder()
                .WithTitle("Dodaj mecz")
                .WithCustomId("admin_add_match_modal")
                .AddTextInput("Drużyna domowa", "home_team", TextInputStyle.Short, placeholder: "Motor Lublin", required: true)
                .AddTextInput("Drużyna wyjazdowa", "away_team", TextInputStyle.Short, placeholder: "Włókniarz Częstochowa", required: true)
                .Build();

            await RespondWithModalAsync(modal);
            _logger.LogInformation("Modal wyświetlony pomyślnie - Użytkownik: {Username}", Context.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas wyświetlania modala - Użytkownik: {Username}, HasResponded: {HasResponded}", 
                Context.User.Username, Context.Interaction.HasResponded);
            if (!Context.Interaction.HasResponded)
            {
                await RespondAsync("❌ Wystąpił błąd podczas wyświetlania formularza. Spróbuj ponownie.", ephemeral: true);
            }
        }
    }

    [ModalInteraction("admin_add_match_modal")]
    public async Task HandleAddMatchModalAsync(string homeTeam, string awayTeam) // ← CRITICAL FIX: Discord.NET converts snake_case to camelCase!
    {
        _logger.LogInformation(
            "Modal admin_add_match_modal otrzymany - Użytkownik: {Username} (ID: {UserId}), HomeTeam: '{HomeTeam}', AwayTeam: '{AwayTeam}', Guild: {GuildId}, Channel: {ChannelId}",
            Context.User.Username, Context.User.Id, homeTeam, awayTeam, Context.Guild?.Id, Context.Channel?.Id);
        
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
                Context.Guild?.Id ?? 0,
                Context.Channel?.Id ?? 0);
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
            Context.Guild?.Id ?? 0,
            Context.Channel?.Id ?? 0);

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
            var round = match.Round;
            var roundNumForCard = round?.Number ?? roundNum;
            await PostMatchCardAsync(match, roundNumForCard);

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
                (Context.Channel as SocketTextChannel)?.Name ?? "DM",
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

    [ModalInteraction("admin_add_match_modal_v2", true)]
    public async Task HandleAddMatchModalV2Async(AddMatchModalV2 modal)
    {
        var roundNumber = modal.RoundNumber;
        var matchDate = modal.MatchDate;
        var matchTime = modal.MatchTime;
        var homeTeam = modal.HomeTeam;
        var awayTeam = modal.AwayTeam;

        _logger.LogInformation(
            "Modal admin_add_match_modal_v2 otrzymany - Użytkownik: {Username} (ID: {UserId}), Round: '{Round}', Date: '{Date}', Time: '{Time}', HomeTeam: '{HomeTeam}', AwayTeam: '{AwayTeam}', Guild: {GuildId}, Channel: {ChannelId}",
            Context.User.Username, Context.User.Id, roundNumber, matchDate, matchTime, homeTeam, awayTeam, Context.Guild?.Id, Context.Channel?.Id);
        
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondWithErrorAsync("Nie masz uprawnień do użycia tej komendy.");
            return;
        }

        // Validate round number
        if (!int.TryParse(roundNumber, out var roundNum) || roundNum < 1 || roundNum > 18)
        {
            await RespondWithErrorAsync("Nieprawidłowy numer kolejki.", "Podaj liczbę od 1 do 18.");
            return;
        }

        // Validate date format
        if (!DateTime.TryParse(matchDate, out var parsedDate))
        {
            await RespondWithErrorAsync("Nieprawidłowy format daty.", "Użyj formatu YYYY-MM-DD, np. 2024-11-15");
            return;
        }

        // Validate time format
        if (!TimeSpan.TryParse(matchTime, out var parsedTime) || parsedTime.TotalHours >= 24)
        {
            await RespondWithErrorAsync("Nieprawidłowy format godziny.", "Użyj formatu HH:mm, np. 18:30");
            return;
        }

        // Validate teams
        if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
        {
            await RespondWithErrorAsync("Nazwy drużyn nie mogą być puste.");
            return;
        }

        if (homeTeam.Equals(awayTeam, StringComparison.OrdinalIgnoreCase))
        {
            await RespondWithErrorAsync("Drużyna domowa i wyjazdowa muszą być różne.");
            return;
        }

        // Parse date/time
        DateTimeOffset startTime;
        try
        {
            if (!DateTime.TryParse($"{matchDate} {matchTime}", out var localTime))
            {
                _logger.LogError(
                    "Nie udało się sparsować daty/godziny - Użytkownik: {Username} (ID: {UserId}), Data: {Date}, Godzina: {Time}, Serwer: {GuildId}, Kanał: {ChannelId}",
                    Context.User.Username, Context.User.Id, matchDate, matchTime, Context.Guild?.Id ?? 0, Context.Channel?.Id ?? 0);
                await RespondWithErrorAsync("Nie udało się sparsować daty/godziny meczu.", "Użyj formatu: Data YYYY-MM-DD, Godzina HH:mm");
                return;
            }

            // Convert to configured timezone, then to UTC for storage
            var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
            var localDateTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
            startTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);
            
            _logger.LogInformation(
                "Data/godzina sparsowana - Data lokalna: {LocalTime}, UTC: {UtcTime}, Strefa czasowa: {Timezone}",
                localDateTime, startTime, _settings.Timezone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Wyjątek podczas parsowania daty/godziny - Użytkownik: {Username} (ID: {UserId}), Data: {Date}, Godzina: {Time}",
                Context.User.Username, Context.User.Id, matchDate, matchTime);
            await RespondWithErrorAsync("Nie udało się sparsować daty/godziny meczu.", ex.Message);
            return;
        }

        // Validate start time is in the future
        if (startTime <= DateTimeOffset.UtcNow)
        {
            _logger.LogWarning(
                "Data rozpoczęcia meczu w przeszłości - Użytkownik: {Username} (ID: {UserId}), StartTime UTC: {StartTimeUtc}",
                Context.User.Username, Context.User.Id, startTime);
            await RespondWithErrorAsync("Data rozpoczęcia meczu musi być w przyszłości.");
            return;
        }

        // Create match
        Domain.Entities.Match? match = null;
        try
        {
            var (success, error, createdMatch) = await _matchService.CreateMatchAsync(roundNum, homeTeam, awayTeam, startTime);
            
            if (!success || createdMatch == null)
            {
                _logger.LogError(
                    "Tworzenie meczu nie powiodło się - Użytkownik: {Username} (ID: {UserId}), Runda: {Round}, {HomeTeam} vs {AwayTeam}, Błąd: {Error}",
                    Context.User.Username, Context.User.Id, roundNum, homeTeam, awayTeam, error);
                await RespondWithErrorAsync("Błąd podczas tworzenia meczu.", error ?? "Nieznany błąd");
                return;
            }

            match = createdMatch;

            // Post match card to predictions channel
            await PostMatchCardAsync(match, roundNum);

            var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
            var localStartTime = TimeZoneInfo.ConvertTimeFromUtc(match.StartTime.UtcDateTime, tz);
            
            _logger.LogInformation(
                "Mecz utworzony pomyślnie - ID meczu: {MatchId}, Runda: {Round}, {HomeTeam} vs {AwayTeam}, StartTime Local: {StartTimeLocal}",
                match.Id, roundNum, match.HomeTeam, match.AwayTeam, localStartTime);

            await RespondWithSuccessAsync($"Mecz utworzony: Runda {roundNum}, {match.HomeTeam} vs {match.AwayTeam} o {localStartTime:yyyy-MM-dd HH:mm}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Wyjątek podczas tworzenia meczu - Użytkownik: {Username} (ID: {UserId}), Runda: {Round}, {HomeTeam} vs {AwayTeam}",
                Context.User.Username, Context.User.Id, roundNum, homeTeam, awayTeam);
            await RespondWithErrorAsync("Wystąpił błąd podczas tworzenia meczu.", ex.Message);
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
        var roundLabel = Application.Services.RoundHelper.GetRoundLabel(roundNum);
        var embed = new EmbedBuilder()
            .WithTitle($"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}")
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
            .WithLabel($"✅ Wynik {TeamNameHelper.GetMatchShortcut(match.HomeTeam, match.AwayTeam)}")
            .WithStyle(ButtonStyle.Success);

        var editButton = new ButtonBuilder()
            .WithCustomId($"admin_edit_match_{match.Id}")
            .WithLabel($"✏ {TeamNameHelper.GetMatchShortcut(match.HomeTeam, match.AwayTeam)}")
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
            // Update existing message
            await existingMessage.ModifyAsync(prop => { prop.Embed = embed; prop.Components = component; });
            _logger.LogInformation("Karta meczu zaktualizowana - ID meczu: {MatchId}", match.Id);
        }
        else
        {
            // Check if thread should be created now
            var shouldCreateNow = !match.ThreadCreationTime.HasValue || 
                                  match.ThreadCreationTime.Value <= DateTimeOffset.UtcNow;
            
            if (shouldCreateNow)
            {
                // Check if thread already exists
                SocketThreadChannel? thread = null;
                if (match.ThreadId.HasValue)
                {
                    thread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
                }
                
                if (thread == null)
                {
                    var threadName = $"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}";
                    // Validate thread name length (Discord limit is 100 characters)
                    if (threadName.Length > 100)
                    {
                        threadName = threadName.Substring(0, 97) + "...";
                    }
                    
                    thread = await predictionsChannel.CreateThreadAsync(
                        name: threadName,
                        type: ThreadType.PublicThread
                    );
                    
                    // Save ThreadId to database
                    match.ThreadId = thread.Id;
                    await _matchRepository.UpdateAsync(match);
                }

                await thread.SendMessageAsync(embed: embed, components: component);
                _logger.LogInformation("Karta meczu opublikowana w kanale typowań - ID meczu: {MatchId}, Thread ID: {ThreadId}", match.Id, thread.Id);
            }
            else
            {
                // Thread will be created later by ThreadCreationService
                _logger.LogInformation("Karta meczu będzie utworzona później - ID meczu: {MatchId}, ThreadCreationTime: {Time}", 
                    match.Id, match.ThreadCreationTime.Value);
            }
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

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await RespondAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        // If match is already finished, show confirmation dialog
        if (match.Status == MatchStatus.Finished && match.HomeScore.HasValue && match.AwayScore.HasValue)
        {
            var embed = new EmbedBuilder()
                .WithTitle("⚠️ Uwaga! Zmiana wyniku zakończonego meczu")
                .WithDescription(
                    $"**Na pewno chcesz zmienić wynik meczu który się już odbył?**\n\n" +
                    $"**{match.HomeTeam} vs {match.AwayTeam}**\n" +
                    $"Aktualny wynik: **{match.HomeScore}:{match.AwayScore}**\n\n" +
                    $"Zmiana wyniku spowoduje przeliczenie punktów wszystkich graczy. " +
                    $"Informacja o tej zmianie zostanie opublikowana na kanale dostępnym dla wszystkich.")
                .WithColor(Color.Orange)
                .Build();

            var confirmButton = new ButtonBuilder()
                .WithCustomId($"admin_confirm_change_result_{matchId}")
                .WithLabel("✅ Tak, zmień wynik")
                .WithStyle(ButtonStyle.Danger);

            var cancelButton = new ButtonBuilder()
                .WithCustomId($"admin_cancel_change_result_{matchId}")
                .WithLabel("❌ Anuluj")
                .WithStyle(ButtonStyle.Secondary);

            var component = new ComponentBuilder()
                .WithButton(confirmButton, row: 0)
                .WithButton(cancelButton, row: 0)
                .Build();

            await RespondAsync(embed: embed, components: component, ephemeral: true);
            return;
        }

        // Match not finished yet, proceed normally
        var modal = new SetResultModal
        {
            HomeScore = match.HomeScore?.ToString() ?? "50",
            AwayScore = match.AwayScore?.ToString() ?? "40"
        };

        await RespondWithModalAsync($"admin_set_result_modal_{matchId}", modal);
        
        _logger.LogInformation(
            "Przycisk ustaw wynik kliknięty - Użytkownik: {Username} (ID: {UserId}), ID meczu: {MatchId}, Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            matchId,
            Context.Guild.Id,
            Context.Channel.Id);
    }

    [ComponentInteraction("admin_confirm_change_result_*")]
    public async Task HandleConfirmChangeResultAsync(string matchIdStr)
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

        var modal = new SetResultModal
        {
            HomeScore = match.HomeScore?.ToString() ?? "50",
            AwayScore = match.AwayScore?.ToString() ?? "40"
        };

        await RespondWithModalAsync($"admin_set_result_modal_{matchId}", modal);
    }

    [ComponentInteraction("admin_cancel_change_result_*")]
    public async Task HandleCancelChangeResultAsync(string matchIdStr)
    {
        await RespondAsync("❌ Anulowano zmianę wyniku.", ephemeral: true);
    }

    [ModalInteraction("admin_set_result_modal_*", true)]
    public async Task HandleSetResultModalAsync(string matchIdStr, SetResultModal modal)
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

        if (!int.TryParse(modal.HomeScore, out var home) || !int.TryParse(modal.AwayScore, out var away))
        {
            _logger.LogWarning("Invalid score format - User: {User}, homeScore: '{Home}', awayScore: '{Away}'", 
                Context.User.Username, modal.HomeScore, modal.AwayScore);
            await RespondAsync("❌ Wprowadź prawidłowe liczby dla obu wyników.", ephemeral: true);
            return;
        }

        // Validate sum = 90
        if (home + away != 90)
        {
            _logger.LogWarning(
                "Nieprawidłowa suma punktów w wyniku - Użytkownik: {Username} (ID: {UserId}), Mecz ID: {MatchId}, Wynik: {Home}:{Away}, Suma: {Sum}",
                Context.User.Username,
                Context.User.Id,
                matchId,
                home,
                away,
                home + away);
            await RespondAsync("❌ Suma punktów obu drużyn musi wynosić 90 (np. 50:40, 46:44, 45:45).", ephemeral: true);
            return;
        }

        // Validate non-negative
        if (home < 0 || away < 0)
        {
            await RespondAsync("❌ Wyniki muszą być nieujemne.", ephemeral: true);
            return;
        }

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await RespondAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        var wasFinished = match.Status == MatchStatus.Finished;
        var oldHomeScore = match.HomeScore;
        var oldAwayScore = match.AwayScore;

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

        // If match was already finished, post notification to public channel
        if (wasFinished && oldHomeScore.HasValue && oldAwayScore.HasValue)
        {
            var resultsChannel = await _lookupService.GetResultsChannelAsync();
            if (resultsChannel != null)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("⚠️ Zmiana wyniku zakończonego meczu")
                    .WithDescription(
                        $"**{match.HomeTeam} vs {match.AwayTeam}**\n\n" +
                        $"Stary wynik: **{oldHomeScore}:{oldAwayScore}**\n" +
                        $"Nowy wynik: **{home}:{away}**\n\n" +
                        $"Punkty wszystkich graczy zostały przeliczone.")
                    .WithColor(Color.Orange)
                    .WithFooter($"Zmienione przez: {Context.User.Username}")
                    .WithCurrentTimestamp()
                    .Build();

                await resultsChannel.SendMessageAsync(embed: embed);
                _logger.LogInformation(
                    "Opublikowano informację o zmianie wyniku meczu {MatchId} na kanale publicznym",
                    matchId);
            }
        }

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

        var modal = new EditMatchModal
        {
            HomeTeam = match.HomeTeam,
            AwayTeam = match.AwayTeam,
            Date = localTime.ToString("yyyy-MM-dd"),
            Time = localTime.ToString("HH:mm")
        };

        await RespondWithModalAsync($"admin_edit_match_modal_{matchId}", modal);
        
        _logger.LogInformation(
            "Przycisk edytuj mecz kliknięty - Użytkownik: {Username} (ID: {UserId}), ID meczu: {MatchId}, Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            matchId,
            Context.Guild.Id,
            Context.Channel.Id);
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

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await RespondAsync("❌ Mecz nie znaleziony.", ephemeral: true);
            return;
        }

        var oldHomeTeam = match.HomeTeam;
        var oldAwayTeam = match.AwayTeam;
        var oldStartTime = match.StartTime;
        var oldRound = match.Round;
        var oldRoundNum = oldRound?.Number ?? 0;

        // Find existing message to update
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
                var messages = await threadToUpdate.GetMessagesAsync(1).FlattenAsync();
                messageToUpdate = messages.FirstOrDefault() as IUserMessage;
            }
        }

        _logger.LogInformation(
            "Modal edytuj mecz przesłany - Użytkownik: {Username} (ID: {UserId}), ID meczu: {MatchId}, Stare: {OldHome} vs {OldAway} {OldTime}, Nowe: {NewHome} vs {NewAway} {NewDate} {NewTime}, Serwer: {GuildId}, Kanał: {ChannelId}",
            Context.User.Username,
            Context.User.Id,
            matchId,
            oldHomeTeam,
            oldAwayTeam,
            oldStartTime,
            modal.HomeTeam,
            modal.AwayTeam,
            modal.Date,
            modal.Time,
            Context.Guild.Id,
            Context.Channel.Id);

        // Parse date/time
        DateTimeOffset startTime;
        try
        {
            if (!DateTime.TryParse($"{modal.Date} {modal.Time}", out var localTime))
            {
                _logger.LogError(
                    "Nie udało się sparsować daty/godziny w edycji - Użytkownik: {Username} (ID: {UserId}), ID meczu: {MatchId}, Data: {Date}, Godzina: {Time}",
                    Context.User.Username,
                    Context.User.Id,
                    matchId,
                    modal.Date,
                    modal.Time);
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
        match.HomeTeam = modal.HomeTeam;
        match.AwayTeam = modal.AwayTeam;
        match.StartTime = startTime;
        await _matchRepository.UpdateAsync(match);

        // Update or create match card
        var round = match.Round;
        var roundNum = round?.Number ?? 0;
        var roundLabel = Application.Services.RoundHelper.GetRoundLabel(roundNum);
        var newThreadName = $"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}";

        // Try to find thread by ThreadId first, then by name
        IUserMessage? cardMessage = messageToUpdate;
        SocketThreadChannel? targetThread = threadToUpdate;
        
        if (cardMessage == null && startTime > DateTimeOffset.UtcNow && predictionsChannel != null)
        {
            // Try to find by ThreadId first
            if (match.ThreadId.HasValue)
            {
                targetThread = predictionsChannel.Threads.FirstOrDefault(t => t.Id == match.ThreadId.Value);
                if (targetThread != null)
                {
                    var messages = await targetThread.GetMessagesAsync(1).FlattenAsync();
                    cardMessage = messages.FirstOrDefault() as IUserMessage;
                }
            }
            
            // Fallback to name search if ThreadId not found
            if (targetThread == null)
            {
                // Validate thread name length
                var searchThreadName = newThreadName;
                if (searchThreadName.Length > 100)
                {
                    searchThreadName = searchThreadName.Substring(0, 97) + "...";
                }
                
                var existingThread = predictionsChannel.Threads.FirstOrDefault(t => t.Name == searchThreadName);
                if (existingThread != null)
                {
                    targetThread = existingThread;
                    var messages = await existingThread.GetMessagesAsync(1).FlattenAsync();
                    cardMessage = messages.FirstOrDefault() as IUserMessage;
                    
                    // Update ThreadId if it wasn't set
                    if (!match.ThreadId.HasValue)
                    {
                        match.ThreadId = existingThread.Id;
                        await _matchRepository.UpdateAsync(match);
                    }
                }
            }
        }

        // Update or create match card
        if (cardMessage != null)
        {
            // Update existing card
            await PostMatchCardAsync(match, roundNum, cardMessage);
        }
        else if (startTime > DateTimeOffset.UtcNow)
        {
            // Create new card (thread doesn't exist or was deleted)
            await PostMatchCardAsync(match, roundNum, null);
        }

        // Update thread name if exists and changed (with error handling)
        if (targetThread != null)
        {
            var validatedThreadName = newThreadName;
            if (validatedThreadName.Length > 100)
            {
                validatedThreadName = validatedThreadName.Substring(0, 97) + "...";
            }
            
            if (targetThread.Name != validatedThreadName)
            {
                try
                {
                    await targetThread.ModifyAsync(props => props.Name = validatedThreadName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Nie udało się zaktualizować nazwy wątku - Thread ID: {ThreadId}, Mecz ID: {MatchId}", 
                        targetThread.Id, match.Id);
                    // Continue execution - thread name update failure is not critical
                }
            }
        }

        _logger.LogInformation(
            "Mecz zaktualizowany - ID meczu: {MatchId}, Runda: {Round}, Stare: {OldHome} vs {OldAway} {OldTime}, Nowe: {NewHome} vs {NewAway} {NewTime}",
            matchId,
            roundNum,
            oldHomeTeam,
            oldAwayTeam,
            oldStartTime,
            match.HomeTeam,
            match.AwayTeam,
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

        await RespondAsync(embed: embed.Build(), components: component, ephemeral: true);
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

        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            await RespondAsync("❌ Mecz nie znaleziony.", ephemeral: true);
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

        await RespondAsync(embed: embed.Build(), components: component, ephemeral: true);
    }

    [ComponentInteraction("admin_confirm_cancel_match_*")]
    public async Task HandleConfirmCancelMatchAsync(string matchIdStr)
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

        match.Status = MatchStatus.Cancelled;
        await _matchRepository.UpdateAsync(match);
        
        _logger.LogInformation(
            "Mecz odwołany - Użytkownik: {Username} (ID: {UserId}), Mecz ID: {MatchId}, {Home} vs {Away}",
            Context.User.Username, Context.User.Id, matchId, match.HomeTeam, match.AwayTeam);
        
        await RespondAsync("✅ Mecz został odwołany (status: Cancelled). Typy zostały zachowane.", ephemeral: true);
    }

    [ComponentInteraction("admin_cancel_action_*")]
    public async Task HandleCancelActionAsync(string matchIdStr)
    {
        await RespondAsync("❌ Akcja anulowana.", ephemeral: true);
    }

    [ComponentInteraction("admin_confirm_hard_delete_match_*")]
    public async Task HandleConfirmHardDeleteMatchAsync(string matchIdStr)
    {
        if (!int.TryParse(matchIdStr, out var matchId)) return;
        
        await _matchRepository.DeleteAsync(matchId);
        
        await RespondAsync("✅ Mecz został trwale usunięty z bazy danych.", ephemeral: true);
    }


    // Table generation handlers
    [ComponentInteraction("admin_table_season")]
    public async Task HandleTableSeasonButtonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await FollowupAsync("❌ Brak aktywnych graczy.", ephemeral: true);
            return;
        }

        try
        {
            // Calculate season scores
            var allScores = new List<(string PlayerName, int TotalPoints, int PredictionsCount, int ExactScores, int CorrectWinners)>();

            foreach (var player in players)
            {
                var playerScores = player.PlayerScores
                    .Where(s => s.Prediction != null && s.Prediction.IsValid)
                    .ToList();

                var totalPoints = playerScores.Sum(s => s.Points);
                var exactScores = playerScores.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50);
                var correctWinners = playerScores.Count(s => s.Points > 0);
                var predCount = playerScores.Count;

                allScores.Add((player.DiscordUsername, totalPoints, predCount, exactScores, correctWinners));
            }

            var sortedScores = allScores.OrderByDescending(s => s.TotalPoints).ToList();

            var embed = new EmbedBuilder()
                .WithTitle($"🏆 Tabela Sezonu")
                .WithDescription($"**Sezon**: {season.Name}")
                .WithColor(Color.Gold)
                .WithCurrentTimestamp();

            var table = "```\n";
            table += "Poz  Gracz                    Pkt   Typ   Cel   Wyg\n";
            table += "═══════════════════════════════════════════════════\n";

            for (int i = 0; i < sortedScores.Count; i++)
            {
                var score = sortedScores[i];
                var playerName = score.PlayerName;
                if (playerName.Length > 22) playerName = playerName.Substring(0, 19) + "...";
                
                var medal = i switch 
                { 
                    0 => "🥇", 
                    1 => "🥈", 
                    2 => "🥉", 
                    _ when i == sortedScores.Count - 1 => "💩", // Last place
                    _ => "  " 
                };
                table += $"{medal} {i + 1,2}  {playerName,-22}  {score.TotalPoints,3}  {score.PredictionsCount,4}  {score.ExactScores,4}  {score.CorrectWinners,4}\n";
            }
            table += "```";

            embed.AddField("Tabela punktowa - Sezon", table, false);
            embed.WithFooter($"Typ = Liczba typów | Cel = Celne wyniki | Wyg = Poprawne zwycięzców");

            var resultsChannel = await _lookupService.GetResultsChannelAsync();
            if (resultsChannel != null)
            {
                await resultsChannel.SendMessageAsync(embed: embed.Build());
                
                _logger.LogInformation(
                    "Tabela sezonu wygenerowana przez admina - {Username} (ID: {UserId}), Gracze: {PlayerCount}",
                    Context.User.Username, Context.User.Id, players.Count);
                    
                await FollowupAsync("✅ Tabela sezonu została opublikowana w kanale wyników.", ephemeral: true);
            }
            else
            {
                await FollowupAsync("❌ Nie znaleziono kanału wyników.", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd generowania tabeli sezonu");
            await FollowupAsync("❌ Wystąpił błąd podczas generowania tabeli.", ephemeral: true);
        }
    }

    [ComponentInteraction("admin_table_round")]
    public async Task HandleTableRoundButtonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await RespondAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var rounds = (await _roundRepository.GetBySeasonIdAsync(season.Id)).ToList();
        if (!rounds.Any())
        {
            await RespondAsync("❌ Brak kolejek w sezonie.", ephemeral: true);
            return;
        }

        // Create select menu with rounds
        var roundOptions = rounds.Select(r => new SelectMenuOptionBuilder()
            .WithLabel(Application.Services.RoundHelper.GetRoundLabel(r.Number))
            .WithValue(r.Id.ToString())
            .WithDescription(Application.Services.RoundHelper.GetRoundDescription(r.Number)))
            .ToList();

        var selectMenu = new SelectMenuBuilder()
            .WithCustomId("admin_table_round_select")
            .WithPlaceholder("Wybierz kolejkę")
            .WithOptions(roundOptions)
            .WithMinValues(1)
            .WithMaxValues(1);

        var component = new ComponentBuilder()
            .WithSelectMenu(selectMenu)
            .Build();

        await RespondAsync("Wybierz kolejkę do wygenerowania tabeli:", components: component, ephemeral: true);
    }

    [ComponentInteraction("admin_table_round_select")]
    public async Task HandleTableRoundSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null || selectedValues.Length == 0)
        {
            await RespondAsync("❌ Nieprawidłowy wybór.", ephemeral: true);
            return;
        }

        if (!int.TryParse(selectedValues[0], out var roundId))
        {
            await RespondAsync("❌ Nieprawidłowy ID kolejki.", ephemeral: true);
            return;
        }

        await DeferAsync();

        var round = await _roundRepository.GetByIdAsync(roundId);
        if (round == null)
        {
            await FollowupAsync("❌ Kolejka nie znaleziona.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await FollowupAsync("❌ Brak aktywnych graczy.", ephemeral: true);
            return;
        }

        try
        {
            // Calculate scores for the round
            var roundMatches = (await _matchRepository.GetByRoundIdAsync(round.Id)).Select(m => m.Id).ToList();
            
            var allScores = new List<(int PlayerId, string PlayerName, int TotalPoints, int PredictionsCount, int ExactScores, int CorrectWinners)>();

            foreach (var player in players)
            {
                var roundPredictions = player.Predictions
                    .Where(p => roundMatches.Contains(p.MatchId) && p.IsValid && p.PlayerScore != null)
                    .ToList();

                var playerScores = roundPredictions.Select(p => p.PlayerScore!).ToList();
                
                var totalPoints = playerScores.Sum(s => s.Points);
                var exactScores = playerScores.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50);
                var correctWinners = playerScores.Count(s => s.Points > 0);
                var predCount = roundPredictions.Count;

                if (predCount > 0)
                {
                    allScores.Add((player.Id, player.DiscordUsername, totalPoints, predCount, exactScores, correctWinners));
                }
            }

            var sortedScores = allScores.OrderByDescending(s => s.TotalPoints).ToList();

            var embed = new EmbedBuilder()
                .WithTitle($"📊 Tabela Kolejki {round.Number}")
                .WithDescription($"**Sezon**: {season.Name}\n**Kolejka**: {round.Description ?? $"Kolejka {round.Number}"}")
                .WithColor(Color.Blue)
                .WithCurrentTimestamp();

            var table = "```\n";
            table += "Poz  Gracz                    Pkt   Typ   Cel   Wyg\n";
            table += "═══════════════════════════════════════════════════\n";

            for (int i = 0; i < sortedScores.Count; i++)
            {
                var score = sortedScores[i];
                var playerName = score.PlayerName;
                if (playerName.Length > 22) playerName = playerName.Substring(0, 19) + "...";
                
                var medal = i switch 
                { 
                    0 => "🥇", 
                    1 => "🥈", 
                    2 => "🥉", 
                    _ when i == sortedScores.Count - 1 => "💩", // Last place
                    _ => "  " 
                };
                
                table += $"{medal} {i + 1,2}  {playerName,-22}  {score.TotalPoints,3}  {score.PredictionsCount,4}  {score.ExactScores,4}  {score.CorrectWinners,4}\n";
            }
            table += "```";

            if (sortedScores.Any())
            {
                embed.AddField("Tabela punktowa", table, false);
            }
            else
            {
                embed.WithDescription($"**Sezon**: {season.Name}\n**Kolejka**: {round.Number}\n\n*Brak wyników dla tej kolejki.*");
            }

            embed.WithFooter($"Typ = Liczba typów | Cel = Celne wyniki | Wyg = Poprawne zwycięzców");

            var resultsChannel = await _lookupService.GetResultsChannelAsync();
            if (resultsChannel != null)
            {
                await resultsChannel.SendMessageAsync(embed: embed.Build());
                await FollowupAsync($"✅ Tabela kolejki {Application.Services.RoundHelper.GetRoundLabel(round.Number)} została opublikowana w kanale wyników.", ephemeral: true);
            }
            else
            {
                await FollowupAsync("❌ Nie znaleziono kanału wyników.", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd generowania tabeli kolejki {Round}", round.Number);
            await FollowupAsync("❌ Wystąpił błąd podczas generowania tabeli.", ephemeral: true);
        }
    }

    // Manage kolejka handler
    [ComponentInteraction("admin_manage_kolejka")]
    public async Task HandleManageKolejkaButtonAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await RespondAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var rounds = (await _roundRepository.GetBySeasonIdAsync(season.Id)).ToList();
        if (!rounds.Any())
        {
            await RespondAsync("❌ Brak kolejek w sezonie. Użyj 'Dodaj kolejkę' aby utworzyć pierwszą.", ephemeral: true);
            return;
        }

        var roundOptions = rounds.Select(r => new SelectMenuOptionBuilder()
            .WithLabel(Application.Services.RoundHelper.GetRoundLabel(r.Number))
            .WithValue(r.Id.ToString())
            .WithDescription($"Kolejka {r.Number} - {r.Matches.Count} meczów"))
            .ToList();

        var selectMenu = new SelectMenuBuilder()
            .WithCustomId("admin_manage_kolejka_select")
            .WithPlaceholder("Wybierz kolejkę do zarządzania")
            .WithOptions(roundOptions)
            .WithMinValues(1)
            .WithMaxValues(1);

        var component = new ComponentBuilder()
            .WithSelectMenu(selectMenu)
            .Build();

        await RespondAsync("Wybierz kolejkę do zarządzania:", components: component, ephemeral: true);
    }

    [ComponentInteraction("admin_manage_kolejka_select")]
    public async Task HandleManageKolejkaSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null || selectedValues.Length == 0)
        {
            await RespondAsync("❌ Nieprawidłowy wybór.", ephemeral: true);
            return;
        }

        if (!int.TryParse(selectedValues[0], out var roundId))
        {
            await RespondAsync("❌ Nieprawidłowy ID kolejki.", ephemeral: true);
            return;
        }

        await DeferAsync();

        var round = await _roundRepository.GetByIdAsync(roundId);
        if (round == null)
        {
            await FollowupAsync("❌ Kolejka nie znaleziona.", ephemeral: true);
            return;
        }

        var roundLabel = Application.Services.RoundHelper.GetRoundLabel(round.Number);
        var matches = round.Matches.OrderBy(m => m.StartTime).ToList();

        var embed = new EmbedBuilder()
            .WithTitle($"Zarządzanie kolejką: {roundLabel}")
            .WithDescription($"Kolejka zawiera {matches.Count} meczów.")
            .WithColor(Color.Gold);

        foreach (var match in matches)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(match.StartTime.UtcDateTime, tz);
            var status = match.Status switch
            {
                MatchStatus.Scheduled => "⏳ Zaplanowany",
                MatchStatus.InProgress => "▶️ W trakcie",
                MatchStatus.Finished => $"✅ Zakończony ({match.HomeScore}:{match.AwayScore})",
                MatchStatus.Cancelled => "❌ Odwołany",
                _ => "❓ Nieznany"
            };

            embed.AddField(
                $"{match.HomeTeam} vs {match.AwayTeam}",
                $"{status}\nData: {localTime:yyyy-MM-dd HH:mm}\nID: {match.Id}",
                inline: true);
        }

        var component = new ComponentBuilder();
        
        // Add buttons for each match (up to Discord limits)
        int buttonCount = 0;
        foreach (var match in matches.Take(8)) // Limit to 8 matches to leave room for delete buttons
        {
            int row = buttonCount / 3; // 3 buttons per row
            
            // Edit button - always available
            var editButton = new ButtonBuilder()
                .WithCustomId($"admin_edit_match_{match.Id}")
                .WithLabel($"✏️ {TeamNameHelper.GetMatchShortcut(match.HomeTeam, match.AwayTeam)}")
                .WithStyle(ButtonStyle.Secondary);
            component.WithButton(editButton, row: row);
            buttonCount++;

            // Result button - always available (to allow corrections)
            var resultLabel = match.HomeScore.HasValue ? $"📝 Zmień wynik" : $"🏁 Wynik";
            var resultStyle = match.HomeScore.HasValue ? ButtonStyle.Secondary : ButtonStyle.Success;
            
            var resultButton = new ButtonBuilder()
                .WithCustomId($"admin_set_result_{match.Id}")
                .WithLabel(resultLabel)
                .WithStyle(resultStyle);
            component.WithButton(resultButton, row: row);
            buttonCount++;

            // Delete button - always available
            var deleteButton = new ButtonBuilder()
                .WithCustomId($"admin_delete_match_{match.Id}")
                .WithLabel($"🗑️ Usuń")
                .WithStyle(ButtonStyle.Danger);
            component.WithButton(deleteButton, row: row);
            buttonCount++;
        }

        // Add "Add Match" button at the bottom
        var addMatchButton = new ButtonBuilder()
            .WithCustomId($"admin_add_match_to_round_{round.Id}")
            .WithLabel("➕ Dodaj mecz do tej kolejki")
            .WithStyle(ButtonStyle.Primary);
        
        component.WithButton(addMatchButton, row: 4); // Put it in the last row

        await FollowupAsync(embed: embed.Build(), components: component.Build(), ephemeral: true);
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

        // Pre-fill modal with round number and default values
        var modal = new AddMatchModalV2
        {
            RoundNumber = round.Number.ToString(),
            MatchDate = DateTime.Now.ToString("yyyy-MM-dd"),
            MatchTime = "18:00",
            HomeTeam = "Motor Lublin",
            AwayTeam = "Włókniarz Częstochowa"
        };
        
        await RespondWithModalAsync("admin_add_match_modal_v2", modal);
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

        var channel = Context.Channel as SocketTextChannel;
        if (!IsAllowedChannel(channel))
        {
            await RespondWithErrorAsync(
                $"Ta komenda może być używana tylko w kanałach: #{_settings.Channels.AdminChannel} lub #{_settings.Channels.PredictionsChannel}",
                $"Używasz: #{channel?.Name ?? "DM"}");
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

        // 1. Always post match results table (with player predictions)
        if (match.Status == MatchStatus.Finished && match.HomeScore.HasValue && match.AwayScore.HasValue)
        {
            await PostMatchResultsTableAsync(match, resultsChannel);
        }

        // 2. Check if this is the last match in the round - if so, post round table
        var round = match.Round;
        if (round != null)
        {
            var roundMatches = await _matchRepository.GetByRoundIdAsync(round.Id);
            var finishedMatches = roundMatches.Where(m => m.Status == MatchStatus.Finished).ToList();
            
            // If all matches in round are finished, post round table
            if (finishedMatches.Count == roundMatches.Count() && roundMatches.Any())
            {
                try
                {
                    await PostRoundTableEmbedAsync(season, round, players, resultsChannel, match);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate round table");
                }
            }
        }
    }

    private async Task PostMatchResultsTableAsync(Domain.Entities.Match match, ITextChannel channel)
    {
        var predictions = (await _predictionRepository.GetValidPredictionsByMatchAsync(match.Id))
            .Where(p => p.PlayerScore != null)
            .OrderByDescending(p => p.PlayerScore!.Points)
            .ThenByDescending(p => p.PlayerScore!.Bucket == Bucket.P35 || p.PlayerScore!.Bucket == Bucket.P50)
            .ToList();

        var embed = new EmbedBuilder()
            .WithTitle($"⚽ Wynik meczu: {match.HomeTeam} vs {match.AwayTeam}")
            .WithDescription($"**Wynik rzeczywisty:** {match.HomeScore.Value}:{match.AwayScore.Value}")
            .WithColor(Color.Green)
            .WithCurrentTimestamp();

        var round = match.Round;
        if (round != null)
        {
            var roundLabel = Application.Services.RoundHelper.GetRoundLabel(round.Number);
            embed.AddField("Kolejka", roundLabel, inline: true);
        }

        if (predictions.Any())
        {
            var table = "```\n";
            table += "Gracz                  Typ      Pkt\n";
            table += "════════════════════════════════════\n";
            
            // Add actual result as first row
            table += $"🏆 Wynik rzeczywisty  {match.HomeScore.Value,2}:{match.AwayScore.Value,2}     -\n";
            table += "────────────────────────────────────\n";

            foreach (var pred in predictions)
            {
                var playerName = pred.Player.DiscordUsername;
                if (playerName.Length > 20) playerName = playerName.Substring(0, 17) + "...";

                var statusIcon = "";
                if (pred.PlayerScore!.Bucket == Bucket.P35 || pred.PlayerScore!.Bucket == Bucket.P50)
                {
                    statusIcon = "👑"; // Exact score (korona)
                }
                else if (pred.PlayerScore.Points > 0)
                {
                    statusIcon = "👍"; // Correct winner (thumbsup)
                }
                else
                {
                    statusIcon = "💩"; // No points (kupa)
                }

                table += $"{playerName,-20}  {pred.HomeTip,2}:{pred.AwayTip,2}     {pred.PlayerScore.Points,3}   {statusIcon}\n";
            }
            table += "```";

            embed.AddField("Typy graczy", table, false);
            embed.WithFooter("👑 = Celny wynik | 👍 = Poprawny zwycięzca | 💩 = Brak punktów");
        }
        else
        {
            embed.AddField("Typy graczy", "*Brak typów dla tego meczu*", false);
        }

        await channel.SendMessageAsync(embed: embed.Build());
        _logger.LogInformation("Match results table posted for match {MatchId}", match.Id);
    }

    private async Task PostRoundTableEmbedAsync(Domain.Entities.Season season, Domain.Entities.Round round, List<Domain.Entities.Player> players, ITextChannel channel, Domain.Entities.Match triggerMatch)
    {
        var roundMatches = (await _matchRepository.GetByRoundIdAsync(round.Id)).Select(m => m.Id).ToList();
        
        var allScores = new List<(int PlayerId, string PlayerName, int TotalPoints, int PredictionsCount, int ExactScores, int CorrectWinners)>();

        foreach (var player in players)
        {
            var roundPredictions = player.Predictions
                .Where(p => roundMatches.Contains(p.MatchId) && p.IsValid && p.PlayerScore != null)
                .ToList();

            var playerScores = roundPredictions.Select(p => p.PlayerScore!).ToList();
            
            var totalPoints = playerScores.Sum(s => s.Points);
            var exactScores = playerScores.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50);
            var correctWinners = playerScores.Count(s => s.Points > 0);
            var predCount = roundPredictions.Count;

            if (predCount > 0)
            {
                allScores.Add((player.Id, player.DiscordUsername, totalPoints, predCount, exactScores, correctWinners));
            }
        }

        var sortedScores = allScores.OrderByDescending(s => s.TotalPoints).ToList();

        var roundLabel = Application.Services.RoundHelper.GetRoundLabel(round.Number);
        var embed = new EmbedBuilder()
            .WithTitle($"📊 Tabela Kolejki {round.Number}")
            .WithDescription(
                string.IsNullOrEmpty(triggerMatch.HomeTeam) 
                    ? $"**Sezon**: {season.Name}\n**Kolejka**: {round.Description ?? roundLabel}"
                    : $"**Sezon**: {season.Name}\n**Kolejka**: {round.Description ?? roundLabel}\n**Po meczu**: {triggerMatch.HomeTeam} vs {triggerMatch.AwayTeam} ({triggerMatch.HomeScore}:{triggerMatch.AwayScore})")
            .WithColor(Color.Blue)
            .WithCurrentTimestamp();

        var table = "```\n";
        table += "Poz  Gracz                    Pkt   Typ   Cel   Wyg\n";
        table += "═══════════════════════════════════════════════════\n";

        for (int i = 0; i < sortedScores.Count; i++)
        {
            var score = sortedScores[i];
            var playerName = score.PlayerName;
            if (playerName.Length > 22) playerName = playerName.Substring(0, 19) + "...";
            
            var medal = i switch 
            { 
                0 => "🥇", 
                1 => "🥈", 
                2 => "🥉", 
                _ when i == sortedScores.Count - 1 => "💩", // Last place
                _ => "  " 
            };
            
            table += $"{medal} {i + 1,2}  {playerName,-22}  {score.TotalPoints,3}  {score.PredictionsCount,4}  {score.ExactScores,4}  {score.CorrectWinners,4}\n";
        }
        table += "```";

        if (sortedScores.Any())
        {
            embed.AddField("Tabela punktowa", table, false);
        }
        else
        {
            embed.WithDescription(
                string.IsNullOrEmpty(triggerMatch.HomeTeam)
                    ? $"**Sezon**: {season.Name}\n**Kolejka**: {roundLabel}\n\n*Brak wyników dla tej kolejki.*"
                    : $"**Sezon**: {season.Name}\n**Kolejka**: {roundLabel}\n**Po meczu**: {triggerMatch.HomeTeam} vs {triggerMatch.AwayTeam}\n\n*Brak wyników dla tej kolejki.*");
        }

        embed.WithFooter($"Typ = Liczba typów | Cel = Celne wyniki | Wyg = Poprawne zwycięzców");

        await channel.SendMessageAsync(embed: embed.Build());
        _logger.LogInformation("Round {Round} standings posted", round.Number);
    }

    private async Task PostSeasonTableEmbedAsync(Domain.Entities.Season season, List<Domain.Entities.Player> players, ITextChannel channel, Domain.Entities.Match triggerMatch)
    {
        var allScores = new List<(string PlayerName, int TotalPoints, int PredictionsCount, int ExactScores, int CorrectWinners)>();

        foreach (var player in players)
        {
            var playerScores = player.PlayerScores
                .Where(s => s.Prediction != null && s.Prediction.IsValid)
                .ToList();

            var totalPoints = playerScores.Sum(s => s.Points);
            var exactScores = playerScores.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50);
            var correctWinners = playerScores.Count(s => s.Points > 0);
            var predCount = playerScores.Count;

            allScores.Add((player.DiscordUsername, totalPoints, predCount, exactScores, correctWinners));
        }

        var sortedScores = allScores.OrderByDescending(s => s.TotalPoints).ToList();

        var embed = new EmbedBuilder()
            .WithTitle($"🏆 Tabela Sezonu")
            .WithDescription(
                string.IsNullOrEmpty(triggerMatch.HomeTeam)
                    ? $"**Sezon**: {season.Name}"
                    : $"**Sezon**: {season.Name}\n**Po meczu**: {triggerMatch.HomeTeam} vs {triggerMatch.AwayTeam} ({triggerMatch.HomeScore}:{triggerMatch.AwayScore})")
            .WithColor(Color.Gold)
            .WithCurrentTimestamp();

        var table = "```\n";
        table += "Poz  Gracz                    Pkt   Typ   Cel   Wyg\n";
        table += "═══════════════════════════════════════════════════\n";

        for (int i = 0; i < sortedScores.Count; i++)
        {
            var score = sortedScores[i];
            var playerName = score.PlayerName;
            if (playerName.Length > 22) playerName = playerName.Substring(0, 19) + "...";
            
            var medal = i switch 
            { 
                0 => "🥇", 
                1 => "🥈", 
                2 => "🥉", 
                _ when i == sortedScores.Count - 1 => "💩", // Last place
                _ => "  " 
            };
            table += $"{medal} {i + 1,2}  {playerName,-22}  {score.TotalPoints,3}  {score.PredictionsCount,4}  {score.ExactScores,4}  {score.CorrectWinners,4}\n";
        }
        table += "```";

        embed.AddField("Tabela punktowa - Sezon", table, false);
        embed.WithFooter($"Typ = Liczba typów | Cel = Celne wyniki | Wyg = Poprawne zwycięzców");

        await channel.SendMessageAsync(embed: embed.Build());
        _logger.LogInformation("Season standings posted");
    }

    [SlashCommand("admin-tabela-sezonu", "Wyślij tabelę sezonu do kanału wyników (tylko dla adminów)")]
    public async Task AdminPostSeasonTableAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await FollowupAsync("❌ Brak aktywnych graczy.", ephemeral: true);
            return;
        }

        try
        {
            var resultsChannel = await _lookupService.GetResultsChannelAsync();
            if (resultsChannel == null)
            {
                await FollowupAsync("❌ Nie znaleziono kanału wyników.", ephemeral: true);
                return;
            }

            // Use existing method but without trigger match
            var dummyMatch = new Domain.Entities.Match
            {
                HomeTeam = "",
                AwayTeam = "",
                HomeScore = 0,
                AwayScore = 0
            };
            await PostSeasonTableEmbedAsync(season, players, resultsChannel, dummyMatch);
            
            await FollowupAsync("✅ Tabela sezonu została opublikowana w kanale wyników.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd generowania tabeli sezonu");
            await FollowupAsync("❌ Wystąpił błąd podczas generowania tabeli.", ephemeral: true);
        }
    }

    [SlashCommand("admin-tabela-kolejki", "Wyślij tabelę kolejki do kanału wyników (tylko dla adminów)")]
    public async Task AdminPostRoundTableAsync([Summary(description: "Numer kolejki")] int round)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień do użycia tej komendy.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var roundEntity = await _roundRepository.GetByNumberAsync(season.Id, round);
        if (roundEntity == null)
        {
            await FollowupAsync($"❌ Kolejka {round} nie znaleziona.", ephemeral: true);
            return;
        }

        var players = (await _playerRepository.GetActivePlayersAsync()).ToList();
        if (!players.Any())
        {
            await FollowupAsync("❌ Brak aktywnych graczy.", ephemeral: true);
            return;
        }

        try
        {
            var resultsChannel = await _lookupService.GetResultsChannelAsync();
            if (resultsChannel == null)
            {
                await FollowupAsync("❌ Nie znaleziono kanału wyników.", ephemeral: true);
                return;
            }

            // Use existing method but without trigger match
            var dummyMatch = new Domain.Entities.Match
            {
                HomeTeam = "",
                AwayTeam = "",
                HomeScore = 0,
                AwayScore = 0
            };
            await PostRoundTableEmbedAsync(season, roundEntity, players, resultsChannel, dummyMatch);
            
            await FollowupAsync($"✅ Tabela kolejki {Application.Services.RoundHelper.GetRoundLabel(round)} została opublikowana w kanale wyników.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd generowania tabeli kolejki");
            await FollowupAsync("❌ Wystąpił błąd podczas generowania tabeli.", ephemeral: true);
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

        var channel = Context.Channel as SocketTextChannel;
        if (!IsAllowedChannel(channel))
        {
            await RespondWithErrorAsync(
                $"Ta komenda może być używana tylko w kanałach: #{_settings.Channels.AdminChannel} lub #{_settings.Channels.PredictionsChannel}",
                $"Używasz: #{channel?.Name ?? "DM"}");
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

        var channel = Context.Channel as SocketTextChannel;
        if (!IsAllowedChannel(channel))
        {
            await RespondWithErrorAsync(
                $"Ta komenda może być używana tylko w kanałach: #{_settings.Channels.AdminChannel} lub #{_settings.Channels.PredictionsChannel}",
                $"Używasz: #{channel?.Name ?? "DM"}");
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

        var channel = Context.Channel as SocketTextChannel;
        if (!IsAllowedChannel(channel))
        {
            await RespondWithErrorAsync(
                $"Ta komenda może być używana tylko w kanałach: #{_settings.Channels.AdminChannel} lub #{_settings.Channels.PredictionsChannel}",
                $"Używasz: #{channel?.Name ?? "DM"}");
            return;
        }

        _logger.LogInformation(
            "Komenda dane testowe wywołana - Użytkownik: {Username} (ID: {UserId}), Kanał: {ChannelName}, Serwer: {GuildId}",
            Context.User.Username,
            Context.User.Id,
            (Context.Channel as SocketTextChannel)?.Name ?? "DM",
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

// Modal classes using IModal interface (REQUIRED for Discord.Net 3.x)

public class EditMatchModal : IModal
{
    public string Title => "Edytuj mecz";

    [InputLabel("Drużyna domowa")]
    [ModalTextInput("home_team", TextInputStyle.Short)]
    [RequiredInput(true)]
    public string HomeTeam { get; set; } = string.Empty;

    [InputLabel("Drużyna wyjazdowa")]
    [ModalTextInput("away_team", TextInputStyle.Short)]
    [RequiredInput(true)]
    public string AwayTeam { get; set; } = string.Empty;

    [InputLabel("Data (YYYY-MM-DD)")]
    [ModalTextInput("date", TextInputStyle.Short)]
    [RequiredInput(true)]
    public string Date { get; set; } = string.Empty;

    [InputLabel("Godzina (HH:mm)")]
    [ModalTextInput("time", TextInputStyle.Short)]
    [RequiredInput(true)]
    public string Time { get; set; } = string.Empty;
}

public class SetResultModal : IModal
{
    public string Title => "Ustaw wynik meczu";

    [InputLabel("Wynik drużyny domowej")]
    [ModalTextInput("home_score", TextInputStyle.Short, placeholder: "50")]
    [RequiredInput(true)]
    public string HomeScore { get; set; } = string.Empty;

    [InputLabel("Wynik drużyny wyjazdowej")]
    [ModalTextInput("away_score", TextInputStyle.Short, placeholder: "40")]
    [RequiredInput(true)]
    public string AwayScore { get; set; } = string.Empty;
}

public class AddMatchModalV2 : IModal
{
    public string Title => "Dodaj mecz";

    [InputLabel("Nr Kolejki")]
    [ModalTextInput("round_number", TextInputStyle.Short)]
    [RequiredInput(true)]
    public string RoundNumber { get; set; } = string.Empty;

    [InputLabel("Drużyna domowa")]
    [ModalTextInput("home_team", TextInputStyle.Short)]
    [RequiredInput(true)]
    public string HomeTeam { get; set; } = string.Empty;

    [InputLabel("Drużyna wyjazdowa")]
    [ModalTextInput("away_team", TextInputStyle.Short)]
    [RequiredInput(true)]
    public string AwayTeam { get; set; } = string.Empty;

    [InputLabel("Data (YYYY-MM-DD)")]
    [ModalTextInput("match_date", TextInputStyle.Short, placeholder: "2023-05-01")]
    [RequiredInput(true)]
    public string MatchDate { get; set; } = string.Empty;

    [InputLabel("Godzina (HH:mm)")]
    [ModalTextInput("match_time", TextInputStyle.Short, placeholder: "18:00")]
    [RequiredInput(true)]
    public string MatchTime { get; set; } = string.Empty;
}

public class StartSeasonModal : IModal
{
    public string Title => "Rozpocznij nowy sezon";

    [InputLabel("Nazwa sezonu")]
    [ModalTextInput("season_name", TextInputStyle.Short, placeholder: "PGE Ekstraliga 2025", maxLength: 200, minLength: 1)]
    [RequiredInput(true)]
    public string SeasonName { get; set; } = string.Empty;
}

public class AddKolejkaModal : IModal
{
    public string Title => "Dodaj kolejkę";

    [InputLabel("Numer kolejki (1-18)")]
    [ModalTextInput("kolejka_number", TextInputStyle.Short, placeholder: "1", minLength: 1, maxLength: 2)]
    [RequiredInput(true)]
    public string KolejkaNumber { get; set; } = string.Empty;

    [InputLabel("Liczba meczów w kolejce")]
    [ModalTextInput("liczba_meczow", TextInputStyle.Short, placeholder: "4", minLength: 1, maxLength: 1)]
    [RequiredInput(true)]
    public string LiczbaMeczow { get; set; } = string.Empty;
}

