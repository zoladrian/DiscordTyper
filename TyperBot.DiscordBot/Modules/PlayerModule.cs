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

public class PlayerModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger<PlayerModule> _logger;
    private readonly DiscordSettings _settings;
    private readonly IPlayerRepository _playerRepository;
    private readonly IMatchRepository _matchRepository;
    private readonly IPredictionRepository _predictionRepository;
    private readonly RoundManager _roundManager;
    private readonly TableGenerator _tableGenerator;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRoundRepository _roundRepository;

    public PlayerModule(
        ILogger<PlayerModule> logger,
        IOptions<DiscordSettings> settings,
        IPlayerRepository playerRepository,
        IMatchRepository matchRepository,
        IPredictionRepository predictionRepository,
        RoundManager roundManager,
        TableGenerator tableGenerator,
        ISeasonRepository seasonRepository,
        IRoundRepository roundRepository)
    {
        _logger = logger;
        _settings = settings.Value;
        _playerRepository = playerRepository;
        _matchRepository = matchRepository;
        _predictionRepository = predictionRepository;
        _roundManager = roundManager;
        _tableGenerator = tableGenerator;
        _seasonRepository = seasonRepository;
        _roundRepository = roundRepository;
    }

    [SlashCommand("my-predictions", "View your predictions for upcoming matches or a specific round")]
    public async Task MyPredictionsAsync(
        [Summary(description: "Round number (optional, shows all upcoming if not provided)")] int? round = null)
    {
        var player = await _playerRepository.GetByDiscordUserIdAsync(Context.User.Id);
        if (player == null)
        {
            await RespondAsync("❌ You haven't made any predictions yet.", ephemeral: true);
            return;
        }

        var predictions = (await _predictionRepository.GetByPlayerIdAsync(player.Id)).ToList();
        
        if (round.HasValue)
        {
            // Filter by round
            var upcomingMatches = await _matchRepository.GetUpcomingMatchesAsync();
            var roundMatches = upcomingMatches.Where(m => m.Round?.Number == round.Value).ToList();
            predictions = predictions.Where(p => roundMatches.Any(m => m.Id == p.MatchId)).ToList();
        }
        else
        {
            // Show all upcoming
            var upcomingMatches = await _matchRepository.GetUpcomingMatchesAsync();
            var upcomingMatchIds = upcomingMatches.Select(m => m.Id).ToList();
            predictions = predictions.Where(p => upcomingMatchIds.Contains(p.MatchId)).ToList();
        }

        if (!predictions.Any())
        {
            var message = round.HasValue 
                ? $"❌ You have no predictions for round {round.Value}."
                : "❌ You have no predictions for upcoming matches.";
            await RespondAsync(message, ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle(round.HasValue ? $"My Predictions - Round {round.Value}" : "My Predictions - Upcoming Matches")
            .WithColor(Color.Blue);

        var description = string.Empty;
        foreach (var pred in predictions)
        {
            var match = await _matchRepository.GetByIdAsync(pred.MatchId);
            if (match != null)
            {
                description += $"**{match.HomeTeam} vs {match.AwayTeam}**: {pred.HomeTip}:{pred.AwayTip}\n";
            }
        }

        embed.WithDescription(description);
        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }

    [SlashCommand("round-table", "View standings for a specific round")]
    public async Task RoundTableAsync([Summary(description: "Round number")] int round)
    {
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
            var png = _tableGenerator.GenerateRoundTable(season, roundEntity, players);
            await RespondWithFileAsync(
                new Discord.FileAttachment(new MemoryStream(png), $"round-{round}-standings.png")
            );
            _logger.LogInformation("Round {Round} table generated by {User}", round, Context.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate round table");
            await RespondAsync("❌ Failed to generate round table.", ephemeral: true);
        }
    }

    [SlashCommand("season-table", "View overall season standings")]
    public async Task SeasonTableAsync()
    {
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
            var png = _tableGenerator.GenerateSeasonTable(season, players);
            await RespondWithFileAsync(
                new Discord.FileAttachment(new MemoryStream(png), $"season-standings.png")
            );
            _logger.LogInformation("Season table generated by {User}", Context.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate season table");
            await RespondAsync("❌ Failed to generate season table.", ephemeral: true);
        }
    }
}

