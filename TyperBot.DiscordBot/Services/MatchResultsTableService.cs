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
/// Publikuje wynik meczu w wątku/kanał: embed + PNG tabeli (Gracz / Typ / Pkt), kolorystyka jak tabela kolejki/sezonu.
/// </summary>
public class MatchResultsTableService
{
    private readonly ILogger<MatchResultsTableService> _logger;
    private readonly DiscordLookupService _lookupService;
    private readonly IPredictionRepository _predictionRepository;
    private readonly MatchResultsTableImageGenerator _tableImageGenerator;

    public MatchResultsTableService(
        ILogger<MatchResultsTableService> logger,
        DiscordLookupService lookupService,
        IPredictionRepository predictionRepository,
        MatchResultsTableImageGenerator tableImageGenerator)
    {
        _logger = logger;
        _lookupService = lookupService;
        _predictionRepository = predictionRepository;
        _tableImageGenerator = tableImageGenerator;
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

        var rows = BuildResultRows(match, predictions, guild);
        var footer = BuildFooterLine(match, predictions.Count);
        var png = _tableImageGenerator.Generate(rows, footer);
        var embed = BuildSummaryEmbed(match);

        await using var stream = new MemoryStream(png, writable: false);
        await channel.SendFileAsync(stream, $"wynik_meczu_{match.Id}.png", embed: embed);
        _logger.LogInformation("Match results table posted — Match ID: {MatchId}, Channel ID: {ChannelId}", match.Id, channel.Id);
    }

    private static Embed BuildSummaryEmbed(Match match)
    {
        var embed = new EmbedBuilder()
            .WithTitle(DiscordApiLimits.Truncate($"Wynik meczu: {match.HomeTeam} vs {match.AwayTeam}", DiscordApiLimits.EmbedTitle))
            .WithDescription($"Wynik rzeczywisty: {match.HomeScore?.ToString() ?? "?"}:{match.AwayScore?.ToString() ?? "?"}")
            .WithColor(Color.Green)
            .WithCurrentTimestamp();

        var round = match.Round;
        if (round != null)
            embed.AddField("Kolejka", RoundHelper.GetRoundLabel(round.Number), inline: true);

        return embed.Build();
    }

    /// <summary>Wiersze jak w poprzedniej tabeli tekstowej: pierwszy = wynik rzeczywisty, potem gracze (bez ikon statusu).</summary>
    private static List<MatchResultTableRow> BuildResultRows(Match match, IReadOnlyList<Prediction> predictions, SocketGuild? guild)
    {
        var rows = new List<MatchResultTableRow>();
        var wynikTyp =
            $"{(match.HomeScore?.ToString() ?? "?"),2}:{(match.AwayScore?.ToString() ?? "?"),2}";
        rows.Add(new MatchResultTableRow("Wynik rzeczywisty", wynikTyp, "-"));

        if (predictions.Count == 0)
        {
            rows.Add(new MatchResultTableRow("Brak typów dla tego meczu", "—", "—"));
            return rows;
        }

        foreach (var pred in predictions)
        {
            var playerName = DiscordDisplayNameHelper.ForPlayerInGuild(pred.Player, guild);
            var typ = $"{pred.HomeTip,2}:{pred.AwayTip,2}";
            if (pred.PlayerScore == null)
                rows.Add(new MatchResultTableRow(playerName, typ, "—"));
            else
                rows.Add(new MatchResultTableRow(playerName, typ, $"{pred.PlayerScore.Points,3}"));
        }

        return rows;
    }

    private static string BuildFooterLine(Match match, int typCount)
    {
        var parts = new List<string>();
        if (match.Round != null)
            parts.Add(RoundHelper.GetRoundLabel(match.Round.Number));
        parts.Add($"{match.HomeTeam} vs {match.AwayTeam}");
        parts.Add($"Wynik {match.HomeScore}:{match.AwayScore}");
        parts.Add($"{typCount} typów");
        return string.Join(" • ", parts);
    }
}
