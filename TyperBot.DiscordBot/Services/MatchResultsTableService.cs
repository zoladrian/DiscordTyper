using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using TyperBot.Application.Services;
using TyperBot.DiscordBot;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Services;

/// <summary>
/// Builds and posts the per-match results embed (typy + punkty) to the match thread — public message.
/// </summary>
public class MatchResultsTableService
{
    private readonly ILogger<MatchResultsTableService> _logger;
    private readonly DiscordLookupService _lookupService;
    private readonly IPredictionRepository _predictionRepository;

    public MatchResultsTableService(
        ILogger<MatchResultsTableService> logger,
        DiscordLookupService lookupService,
        IPredictionRepository predictionRepository)
    {
        _logger = logger;
        _lookupService = lookupService;
        _predictionRepository = predictionRepository;
    }

    /// <summary>
    /// Resolves the match thread and posts the results table. Returns false if channel/thread missing or match not finished.
    /// </summary>
    public async Task<bool> TryPostToMatchThreadAsync(Match match)
    {
        if (match.Status != MatchStatus.Finished || !match.HomeScore.HasValue || !match.AwayScore.HasValue)
            return false;

        var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();
        if (predictionsChannel == null)
        {
            _logger.LogWarning("Match table: predictions channel not found for match {MatchId}", match.Id);
            return false;
        }

        var thread = ResolveThread(match, predictionsChannel);
        if (thread == null)
        {
            _logger.LogWarning("Match table: thread not found for match {MatchId}", match.Id);
            return false;
        }

        await PostEmbedAsync(match, thread);
        return true;
    }

    /// <summary>
    /// Posts the table to an already-resolved thread (e.g. admin manual send).
    /// </summary>
    public Task PostToThreadAsync(Match match, IThreadChannel thread) => PostEmbedAsync(match, thread);

    /// <summary>
    /// Posts the match results embed to any text channel (e.g. admin-chosen destination).
    /// </summary>
    public Task PostToTextChannelAsync(Match match, ITextChannel channel) => PostEmbedAsync(match, channel);

    private static SocketThreadChannel? ResolveThread(Match match, SocketTextChannel predictionsChannel)
    {
        if (match.ThreadId.HasValue)
        {
            var t = predictionsChannel.Threads.FirstOrDefault(x => x.Id == match.ThreadId.Value);
            if (t != null) return t;
        }

        var roundLabel = RoundHelper.GetRoundLabel(match.Round?.Number ?? 0);
        var threadName = $"{roundLabel}: {match.HomeTeam} vs {match.AwayTeam}";
        return predictionsChannel.Threads.FirstOrDefault(t => t.Name == threadName);
    }

    private async Task PostEmbedAsync(Match match, IMessageChannel channel)
    {
        var predictions = (await _predictionRepository.GetValidPredictionsByMatchAsync(match.Id))
            .OrderByDescending(p => p.PlayerScore?.Points ?? -1)
            .ThenByDescending(p =>
                p.PlayerScore != null && (p.PlayerScore.Bucket == Bucket.P35 || p.PlayerScore.Bucket == Bucket.P50))
            .ToList();

        SocketGuild? guild = null;
        if (channel is SocketThreadChannel st)
            guild = st.Guild;
        else if (channel is SocketTextChannel txt)
            guild = txt.Guild;

        var embed = BuildResultsEmbed(match, predictions, guild);
        await channel.SendMessageAsync(embed: embed);
        _logger.LogInformation("Match results table posted — Match ID: {MatchId}, Channel ID: {ChannelId}", match.Id, channel.Id);
    }

    public static Embed BuildResultsEmbed(Match match, IReadOnlyList<Prediction> predictions, SocketGuild? guild = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle(DiscordApiLimits.Truncate($"⚽ Wynik meczu: {match.HomeTeam} vs {match.AwayTeam}", DiscordApiLimits.EmbedTitle))
            .WithDescription($"**Wynik rzeczywisty:** {match.HomeScore?.ToString() ?? "?"}:{match.AwayScore?.ToString() ?? "?"}")
            .WithColor(Color.Green)
            .WithCurrentTimestamp();

        var round = match.Round;
        if (round != null)
            embed.AddField("Kolejka", RoundHelper.GetRoundLabel(round.Number), inline: true);

        if (predictions.Count > 0)
        {
            var table = "```\n";
            table += "Gracz                  Typ      Pkt\n";
            table += "════════════════════════════════════\n";
            table += $"🏆 Wynik rzeczywisty  {match.HomeScore?.ToString() ?? "?",2}:{match.AwayScore?.ToString() ?? "?",2}     -\n";
            table += "────────────────────────────────────\n";

            foreach (var pred in predictions)
            {
                var playerName = DiscordDisplayNameHelper.ForPlayerInGuild(pred.Player, guild);
                if (playerName.Length > 20) playerName = playerName[..17] + "...";

                if (pred.PlayerScore == null)
                {
                    table += $"{playerName,-20}  {pred.HomeTip,2}:{pred.AwayTip,2}       —   ⏳\n";
                    continue;
                }

                var statusIcon = pred.PlayerScore.Bucket is Bucket.P35 or Bucket.P50
                    ? "👑"
                    : pred.PlayerScore.Points > 0 ? "👍" : "💩";

                table += $"{playerName,-20}  {pred.HomeTip,2}:{pred.AwayTip,2}     {pred.PlayerScore.Points,3}   {statusIcon}\n";
            }

            table += "```";
            embed.AddField("Typy graczy", table, false);
            embed.WithFooter("👑 = Celny wynik | 👍 = Poprawny zwycięzca | 💩 = Brak punktów | ⏳ = Punkty jeszcze nie naliczone");
        }
        else
        {
            embed.AddField("Typy graczy", "*Brak typów dla tego meczu*", false);
        }

        return embed.Build();
    }
}
