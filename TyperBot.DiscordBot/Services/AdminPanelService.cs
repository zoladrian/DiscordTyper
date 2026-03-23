using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.DiscordBot.Models;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.DiscordBot;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Services;

public class AdminPanelService
{
    private readonly ILogger<AdminPanelService> _logger;
    private readonly DiscordSettings _settings;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRoundRepository _roundRepository;
    private readonly IMatchRepository _matchRepository;

    public AdminPanelService(
        ILogger<AdminPanelService> logger,
        IOptions<DiscordSettings> settings,
        ISeasonRepository seasonRepository,
        IRoundRepository roundRepository,
        IMatchRepository matchRepository)
    {
        _logger = logger;
        _settings = settings.Value;
        _seasonRepository = seasonRepository;
        _roundRepository = roundRepository;
        _matchRepository = matchRepository;
    }

    public async Task<(Embed embed, MessageComponent components)> GetSeasonSelectionPanelAsync()
    {
        var allSeasons = (await _seasonRepository.GetAllAsync()).ToList();
        
        var selectMenu = new SelectMenuBuilder()
            .WithCustomId("admin_select_season")
            .WithPlaceholder("Wybierz sezon...")
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var season in allSeasons)
        {
            var desc = season.IsActive ? "Aktywny" : "Zakończony";
            selectMenu.AddOption(
                DiscordApiLimits.Truncate(season.Name, DiscordApiLimits.SelectOptionLabel),
                season.Id.ToString(),
                DiscordApiLimits.Truncate(desc, DiscordApiLimits.SelectOptionDescription));
        }

        var embed = new EmbedBuilder()
            .WithTitle("Panel Sezonu Typera")
            .WithDescription("Wybierz sezon, którym chcesz zarządzać:")
            .WithColor(Color.Gold)
            .Build();

        var component = new ComponentBuilder()
            .WithSelectMenu(selectMenu)
            .Build();

        return (embed, component);
    }

    public async Task<(Embed embed, MessageComponent components)> GetSeasonPanelAsync(Season? season)
    {
        var embedBuilder = new EmbedBuilder()
            .WithTitle("Panel Sezonu Typera")
            .WithColor(Color.Gold);

        var componentBuilder = new ComponentBuilder();

        if (season != null)
        {
            var rounds = (await _roundRepository.GetBySeasonIdAsync(season.Id))
                .OrderBy(r => r.Number)
                .ToList();

            if (rounds.Any())
            {
                embedBuilder.WithDescription($"**Sezon: {season.Name}**\n\nPoniżej lista kolejek z meczami:");
                
                var displayRounds = rounds.Take(10).ToList();

                foreach (var round in displayRounds)
                {
                    var matches = (await _matchRepository.GetByRoundIdAsync(round.Id)).ToList();

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

                        embedBuilder.AddField($"Kolejka {round.Number}", matchList, false);
                    }
                    else
                    {
                        embedBuilder.AddField($"Kolejka {round.Number}", "Brak meczów w tej kolejce.", false);
                    }
                }
            }
            else
            {
                embedBuilder.WithDescription($"**Sezon: {season.Name}**\n\nBrak dodanych kolejek.");
            }

            // Add buttons
            var addKolejkaButton = new ButtonBuilder()
                .WithCustomId("admin_add_kolejka")
                .WithLabel("➕ Dodaj kolejkę")
                .WithStyle(ButtonStyle.Primary);

            var addMatchButton = new ButtonBuilder()
                .WithCustomId("admin_add_match")
                .WithLabel("⚽ Dodaj mecz")
                .WithStyle(ButtonStyle.Secondary);

            var tableButton = new ButtonBuilder()
                .WithCustomId("admin_table_season")
                .WithLabel("🏆 Tabela sezonu")
                .WithStyle(ButtonStyle.Secondary);

            componentBuilder
                .WithButton(addKolejkaButton, row: 0)
                .WithButton(addMatchButton, row: 0)
                .WithButton(tableButton, row: 0);

            if (season.IsActive)
            {
                var endSeasonButton = new ButtonBuilder()
                    .WithCustomId($"admin_end_season_{season.Id}")
                    .WithLabel("🏁 Zakończ sezon")
                    .WithStyle(ButtonStyle.Danger);
                componentBuilder.WithButton(endSeasonButton, row: 1);
            }
            else
            {
                var reactivateSeasonButton = new ButtonBuilder()
                    .WithCustomId($"admin_reactivate_season_{season.Id}")
                    .WithLabel("♻️ Reaktywuj sezon")
                    .WithStyle(ButtonStyle.Success);
                componentBuilder.WithButton(reactivateSeasonButton, row: 1);
            }
        }
        else
        {
            embedBuilder.WithDescription("Brak aktywnego sezonu. Rozpocznij nowy sezon używając komendy `/start-nowego-sezonu`.");
        }

        return (embedBuilder.Build(), componentBuilder.Build());
    }
}
