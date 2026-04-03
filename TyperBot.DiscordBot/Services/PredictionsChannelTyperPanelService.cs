using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TyperBot.Application.Services;
using TyperBot.DiscordBot.Constants;
using TyperBot.Domain.Entities;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Services;

public sealed class PredictionsChannelTyperPanelService : IPredictionsChannelTyperPanelService
{
    private const int MaxSelectOptions = 25;

    private readonly DiscordSocketClient _client;
    private readonly DiscordLookupService _lookupService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PredictionsChannelTyperPanelService> _logger;

    public PredictionsChannelTyperPanelService(
        DiscordSocketClient client,
        DiscordLookupService lookupService,
        IServiceScopeFactory scopeFactory,
        ILogger<PredictionsChannelTyperPanelService> logger)
    {
        _client = client;
        _lookupService = lookupService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var channel = await _lookupService.GetPredictionsChannelAsync();
        if (channel == null)
        {
            _logger.LogDebug("Predictions channel missing; skip typer panel refresh");
            return;
        }

        var botUser = _client.CurrentUser;
        if (botUser == null)
            return;

        try
        {
            var collected = await channel.GetMessagesAsync(100).FlattenAsync();
            foreach (var msg in collected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (msg.Author.Id != botUser.Id) continue;
                var foot = msg.Embeds.FirstOrDefault()?.Footer?.Text;
                if (foot != CustomIds.PredictionsPanel.FooterToken) continue;
                if (msg is IUserMessage um)
                    await um.DeleteAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete old typer panel messages; continuing with new posts");
        }

        Season? season;
        IReadOnlyList<Round> roundsOrdered;
        IReadOnlyList<Match> pktMatchChoices;

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var seasonRepo = scope.ServiceProvider.GetRequiredService<ISeasonRepository>();
            season = await seasonRepo.GetActiveSeasonAsync();
            if (season != null)
                season = await seasonRepo.GetByIdWithRoundsAndMatchesAsync(season.Id) ?? season;

            roundsOrdered = season?.Rounds?.OrderBy(r => r.Number).Take(MaxSelectOptions).ToList()
                            ?? (IReadOnlyList<Round>)Array.Empty<Round>();

            pktMatchChoices = Array.Empty<Match>();
            if (season != null)
            {
                var ordered = StandingsAnalyticsGenerator.OrderFinishedMatches(season);
                var skip = Math.Max(0, ordered.Count - MaxSelectOptions);
                pktMatchChoices = ordered.Skip(skip).ToList();
            }
        }

        var mainEmbed = BuildMainEmbed(season);
        var mainComponents = BuildMainComponents(roundsOrdered);
        await channel.SendMessageAsync(embed: mainEmbed, components: mainComponents);

        if (pktMatchChoices.Count > 0)
        {
            var matchEmbed = new EmbedBuilder()
                .WithTitle("📊 Pkt meczu")
                .WithDescription(
                    "Wybierz **zakończony mecz z wynikiem** — dostaniesz PNG jak z `/pkt-meczu` (punkty w meczu i Δ vs poprzedni zakończony mecz).")
                .WithColor(Color.Teal)
                .WithFooter(CustomIds.PredictionsPanel.FooterToken)
                .Build();

            var matchMenu = new SelectMenuBuilder()
                .WithCustomId(CustomIds.PredictionsPanel.SelPktMatch)
                .WithPlaceholder("Pkt meczu — wybierz mecz")
                .WithMinValues(1)
                .WithMaxValues(1);

            foreach (var m in pktMatchChoices)
            {
                var shortName = TeamNameHelper.GetMatchShortcut(m.HomeTeam, m.AwayTeam);
                var label = $"{shortName} {m.HomeScore}:{m.AwayScore}";
                if (label.Length > 100)
                    label = label[..100];
                matchMenu.AddOption(label, m.Id.ToString());
            }

            var matchComponents = new ComponentBuilder()
                .WithSelectMenu(matchMenu, row: 0)
                .Build();

            await channel.SendMessageAsync(embed: matchEmbed, components: matchComponents);
        }

        _logger.LogInformation("Typer panel refreshed in #{Channel}", channel.Name);
    }

    private static Embed BuildMainEmbed(Season? season)
    {
        var seasonLine = season == null
            ? "*Brak aktywnego sezonu* — przyciski i listy zadziałają po starcie sezonu."
            : $"**Aktywny sezon:** {season.Name}";

        var desc =
            "To samo co slash komendy gracza — odpowiedzi **tylko dla Ciebie**.\n\n" +
            "**Przyciski (pierwszy rząd):** typy w całym sezonie · tabela sezonu · wykres skumulowany · histogram punktów · wykres kołowy (Twój sezon).\n\n" +
            "**Listy kolejek:** *Moje typy* / *Tabela kolejki* / *Pkt kolejki* — wybierz numer kolejki.\n" +
            "**Druga wiadomość (jeśli jest):** *Pkt meczu* — lista ostatnich zakończonych meczów.\n\n" +
            seasonLine;

        return new EmbedBuilder()
            .WithTitle("🎮 Typer — skróty (jak na kartach meczu)")
            .WithDescription(desc)
            .WithColor(Color.Green)
            .WithFooter(CustomIds.PredictionsPanel.FooterToken)
            .WithCurrentTimestamp()
            .Build();
    }

    private static MessageComponent BuildMainComponents(IReadOnlyList<Round> rounds)
    {
        var row = 0;
        var builder = new ComponentBuilder()
            .WithButton("Moje typy (sezon)", CustomIds.PredictionsPanel.BtnMojeTypySezon, ButtonStyle.Primary, row: row)
            .WithButton("Tabela sezonu", CustomIds.PredictionsPanel.BtnTabelaSezonu, ButtonStyle.Primary, row: row)
            .WithButton("Wykres punktów", CustomIds.PredictionsPanel.BtnWykres, ButtonStyle.Secondary, row: row)
            .WithButton("Rozkład punktów", CustomIds.PredictionsPanel.BtnRozklad, ButtonStyle.Secondary, row: row)
            .WithButton("Kołowy rozkład", CustomIds.PredictionsPanel.BtnKolowy, ButtonStyle.Secondary, row: row);

        if (rounds.Count == 0)
            return builder.Build();

        row++;
        var mojeMenu = new SelectMenuBuilder()
            .WithCustomId(CustomIds.PredictionsPanel.SelMojeTypyRound)
            .WithPlaceholder("Moje typy — wybierz kolejkę")
            .WithMinValues(1)
            .WithMaxValues(1);
        foreach (var r in rounds)
            mojeMenu.AddOption($"Kolejka {r.Number}", r.Number.ToString());

        var tabelaMenu = new SelectMenuBuilder()
            .WithCustomId(CustomIds.PredictionsPanel.SelTabelaRound)
            .WithPlaceholder("Tabela kolejki — wybierz kolejkę")
            .WithMinValues(1)
            .WithMaxValues(1);
        foreach (var r in rounds)
            tabelaMenu.AddOption($"Kolejka {r.Number}", r.Number.ToString());

        var pktMenu = new SelectMenuBuilder()
            .WithCustomId(CustomIds.PredictionsPanel.SelPktRound)
            .WithPlaceholder("Pkt kolejki — wybierz kolejkę")
            .WithMinValues(1)
            .WithMaxValues(1);
        foreach (var r in rounds)
            pktMenu.AddOption($"Kolejka {r.Number}", r.Number.ToString());

        builder.WithSelectMenu(mojeMenu, row: row);
        row++;
        builder.WithSelectMenu(tabelaMenu, row: row);
        row++;
        builder.WithSelectMenu(pktMenu, row: row);

        return builder.Build();
    }
}
