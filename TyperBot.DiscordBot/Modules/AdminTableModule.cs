using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.DiscordBot.Models;
using TyperBot.DiscordBot.Services;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Modules;

public class AdminTableModule : BaseAdminModule
{
    private readonly ILogger<AdminTableModule> _logger;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRoundRepository _roundRepository;
    private readonly IMatchRepository _matchRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly ExportService _exportService;
    private readonly TableGenerator _tableGenerator;

    public AdminTableModule(
        ILogger<AdminTableModule> logger,
        IOptions<DiscordSettings> settings,
        ISeasonRepository seasonRepository,
        IRoundRepository roundRepository,
        IMatchRepository matchRepository,
        IPlayerRepository playerRepository,
        ExportService exportService,
        TableGenerator tableGenerator) : base(settings.Value)
    {
        _logger = logger;
        _seasonRepository = seasonRepository;
        _roundRepository = roundRepository;
        _matchRepository = matchRepository;
        _playerRepository = playerRepository;
        _exportService = exportService;
        _tableGenerator = tableGenerator;
    }

    [SlashCommand("admin-tabela-sezonu", "Wyślij tabelę sezonu do kanału wyników (tylko dla adminów)")]
    public async Task SendSeasonTableAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondWithErrorAsync("Nie masz uprawnień do użycia tej komendy.");
            return;
        }

        await DeferAsync(ephemeral: true);

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var resultsChannel = await _lookupService.GetResultsChannelAsync();
        if (resultsChannel == null)
        {
            await FollowupAsync("❌ Kanał wyników nie został znaleziony.", ephemeral: true);
            return;
        }

        try
        {
            var imageStream = await _tableGenerator.GenerateSeasonTableImageAsync(season.Id);
            await resultsChannel.SendFileAsync(imageStream, $"tabela_sezonu_{DateTime.Now:yyyyMMdd}.png", $"🏆 **Aktualna tabela sezonu: {season.Name}**");
            await FollowupAsync("✅ Tabela sezonu została wysłana na kanał wyników.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas generowania tabeli sezonu");
            await FollowupAsync($"❌ Wystąpił błąd podczas generowania tabeli: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("admin-tabela-kolejki", "Wyślij tabelę kolejki do kanału wyników (tylko dla adminów)")]
    public async Task SendRoundTableAsync(int roundNumber)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondWithErrorAsync("Nie masz uprawnień do użycia tej komendy.");
            return;
        }

        await DeferAsync(ephemeral: true);

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        var round = await _roundRepository.GetByNumberAsync(season.Id, roundNumber);
        if (round == null)
        {
            await FollowupAsync($"❌ Kolejka {roundNumber} nie została znaleziona.", ephemeral: true);
            return;
        }

        var resultsChannel = await _lookupService.GetResultsChannelAsync();
        if (resultsChannel == null)
        {
            await FollowupAsync("❌ Kanał wyników nie został znaleziony.", ephemeral: true);
            return;
        }

        try
        {
            var imageStream = await _tableGenerator.GenerateRoundTableImageAsync(round.Id);
            var roundLabel = Application.Services.RoundHelper.GetRoundLabel(roundNumber);
            await resultsChannel.SendFileAsync(imageStream, $"tabela_kolejki_{roundNumber}_{DateTime.Now:yyyyMMdd}.png", $"📊 **Tabela kolejki: {roundLabel}**");
            await FollowupAsync("✅ Tabela kolejki została wysłana na kanał wyników.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas generowania tabeli kolejki");
            await FollowupAsync($"❌ Wystąpił błąd podczas generowania tabeli: {ex.Message}", ephemeral: true);
        }
    }

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

        try
        {
            var imageStream = await _tableGenerator.GenerateSeasonTableImageAsync(season.Id);
            await FollowupWithFileAsync(imageStream, $"tabela_sezonu_{DateTime.Now:yyyyMMdd}.png", $"🏆 **Aktualna tabela sezonu: {season.Name}**", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas generowania tabeli sezonu");
            await FollowupAsync($"❌ Wystąpił błąd: {ex.Message}", ephemeral: true);
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

        var rounds = (await _roundRepository.GetBySeasonIdAsync(season.Id))
            .OrderBy(r => r.Number)
            .ToList();

        if (!rounds.Any())
        {
            await RespondAsync("❌ Brak kolejek w tym sezonie.", ephemeral: true);
            return;
        }

        var selectMenu = new SelectMenuBuilder()
            .WithCustomId("admin_table_round_select")
            .WithPlaceholder("Wybierz kolejkę...");

        foreach (var round in rounds)
        {
            selectMenu.AddOption($"Kolejka {round.Number}", round.Id.ToString());
        }

        var component = new ComponentBuilder()
            .WithSelectMenu(selectMenu)
            .Build();

        await RespondAsync("Wybierz kolejkę, dla której chcesz wygenerować tabelę:", components: component, ephemeral: true);
    }

    [ComponentInteraction("admin_table_round_select")]
    public async Task HandleTableRoundSelectAsync(string[] selectedValues)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user) || Context.Guild == null)
        {
            await RespondAsync("❌ Nie masz uprawnień.", ephemeral: true);
            return;
        }

        if (selectedValues.Length == 0 || !int.TryParse(selectedValues[0], out var roundId))
        {
            await RespondAsync("❌ Nieprawidłowy wybór.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            var imageStream = await _tableGenerator.GenerateRoundTableImageAsync(roundId);
            await FollowupWithFileAsync(imageStream, $"tabela_kolejki_{roundId}_{DateTime.Now:yyyyMMdd}.png", $"📊 **Tabela kolejki**", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas generowania tabeli kolejki");
            await FollowupAsync($"❌ Wystąpił błąd: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("wyniki-gracza", "Wyświetl wyniki konkretnego gracza (tylko dla adminów)")]
    public async Task ShowPlayerResultsAsync(IUser discordUser)
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondWithErrorAsync("Nie masz uprawnień do użycia tej komendy.");
            return;
        }

        await DeferAsync(ephemeral: true);

        var player = await _playerRepository.GetByDiscordUserIdAsync(discordUser.Id);
        if (player == null)
        {
            await FollowupAsync("❌ Gracz nie został znaleziony w bazie danych.", ephemeral: true);
            return;
        }

        var season = await _seasonRepository.GetActiveSeasonAsync();
        if (season == null)
        {
            await FollowupAsync("❌ Brak aktywnego sezonu.", ephemeral: true);
            return;
        }

        try
        {
            var playerScores = player.PlayerScores
                .Where(s => s.Prediction?.Match?.Round?.SeasonId == season.Id)
                .OrderBy(s => s.Prediction?.Match?.StartTime)
                .ToList();

            if (!playerScores.Any())
            {
                await FollowupAsync($"❌ Gracz {discordUser.Username} nie ma jeszcze żadnych wyników w obecnym sezonie.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle($"Wyniki gracza: {discordUser.Username}")
                .WithDescription($"**Sezon:** {season.Name}\n**Suma punktów:** {playerScores.Sum(s => s.Points)}")
                .WithColor(Color.Blue)
                .WithCurrentTimestamp();

            foreach (var score in playerScores)
            {
                var match = score.Prediction?.Match;
                if (match != null)
                {
                    embed.AddField(
                        $"{match.HomeTeam} vs {match.AwayTeam}",
                        $"Typ: {score.Prediction?.HomeTip}:{score.Prediction?.AwayTip}\nWynik: {match.HomeScore}:{match.AwayScore}\nPunkty: **{score.Points}**",
                        true);
                }
            }

            await FollowupAsync(embed: embed.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas pobierania wyników gracza");
            await FollowupAsync($"❌ Wystąpił błąd: {ex.Message}", ephemeral: true);
        }
    }
}
