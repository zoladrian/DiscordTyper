using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.DiscordBot.Models;
using TyperBot.Domain.Entities;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Services;

public class RoundManagementService
{
    private readonly ILogger<RoundManagementService> _logger;
    private readonly DiscordSettings _settings;
    private readonly IRoundRepository _roundRepository;
    private readonly ISeasonRepository _seasonRepository;

    public RoundManagementService(
        ILogger<RoundManagementService> logger,
        IOptions<DiscordSettings> settings,
        IRoundRepository roundRepository,
        ISeasonRepository seasonRepository)
    {
        _logger = logger;
        _settings = settings.Value;
        _roundRepository = roundRepository;
        _seasonRepository = seasonRepository;
    }

    public async Task<(bool success, string message)> AddRoundAsync(int roundNumber, ulong userId, string username)
    {
        try
        {
            var activeSeason = await _seasonRepository.GetActiveSeasonAsync();
            if (activeSeason == null)
            {
                return (false, "Brak aktywnego sezonu. Najpierw rozpocznij nowy sezon.");
            }

            var existingRound = await _roundRepository.GetByNumberAsync(activeSeason.Id, roundNumber);
            if (existingRound != null)
            {
                return (false, $"Kolejka {roundNumber} już istnieje w tym sezonie.");
            }

            var newRound = new Round
            {
                SeasonId = activeSeason.Id,
                Number = roundNumber,
                Description = $"Kolejka {roundNumber}"
            };

            await _roundRepository.AddAsync(newRound);

            _logger.LogInformation(
                "New round created - User: {Username} (ID: {UserId}), Season: {SeasonId}, Round: {Round}",
                username, userId, activeSeason.Id, roundNumber);

            return (true, $"Kolejka **{roundNumber}** została pomyślnie dodana do sezonu **{activeSeason.Name}**.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding round - User: {Username} (ID: {UserId}), Round: {Round}",
                username, userId, roundNumber);
            return (false, "Wystąpił błąd podczas dodawania kolejki. Sprawdź logi dla szczegółów.");
        }
    }
}
