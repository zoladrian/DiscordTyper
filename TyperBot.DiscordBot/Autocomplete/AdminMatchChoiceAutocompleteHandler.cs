using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.DiscordBot.Models;
using TyperBot.Domain.Entities;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Autocomplete;

/// <summary>
/// Slash autocomplete: wybór meczu po fragmencie nazwy drużyny, numerze kolejki lub ID.
/// </summary>
public class AdminMatchChoiceAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        _ = parameter;

        if (context is not SocketInteractionContext { Guild: not null })
            return AutocompletionResult.FromSuccess(Array.Empty<AutocompleteResult>());

        await using var scope = services.CreateAsyncScope();
        var seasonRepository = scope.ServiceProvider.GetRequiredService<ISeasonRepository>();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<DiscordSettings>>().Value;

        var season = await seasonRepository.GetActiveSeasonAsync();
        if (season?.Rounds == null || season.Rounds.Count == 0)
            return AutocompletionResult.FromSuccess();

        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone);
        }
        catch
        {
            tz = TimeZoneInfo.Utc;
        }

        var userInput = (autocompleteInteraction.Data.Current?.Value?.ToString() ?? "").Trim();
        var q = userInput.ToLowerInvariant();

        var pairs = season.Rounds
            .SelectMany(r => (r.Matches ?? Enumerable.Empty<Match>()).Select(m => (Round: r, Match: m)))
            .OrderBy(x => x.Match.StartTime)
            .ToList();

        var filtered = q.Length == 0
            ? pairs
            : pairs.Where(x =>
                x.Match.HomeTeam.ToLowerInvariant().Contains(q) ||
                x.Match.AwayTeam.ToLowerInvariant().Contains(q) ||
                x.Match.Id.ToString().Contains(q) ||
                RoundHelper.GetRoundLabel(x.Round.Number).ToLowerInvariant().Contains(q) ||
                $"kolejka {x.Round.Number}".Contains(q) ||
                x.Round.Number.ToString() == q);

        var results = new List<AutocompleteResult>();
        foreach (var x in filtered.Take(25))
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(x.Match.StartTime.UtcDateTime, tz);
            var label =
                $"{RoundHelper.GetRoundLabel(x.Round.Number)}: {x.Match.HomeTeam} vs {x.Match.AwayTeam} · {local:dd.MM HH:mm} (#{x.Match.Id})";
            if (label.Length > 100)
                label = label[..97] + "...";

            results.Add(new AutocompleteResult(label, x.Match.Id.ToString()));
        }

        return results.Count == 0
            ? AutocompletionResult.FromSuccess()
            : AutocompletionResult.FromSuccess(results);
    }
}
