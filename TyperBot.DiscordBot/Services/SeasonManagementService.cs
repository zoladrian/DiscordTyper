using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.DiscordBot.Models;
using TyperBot.Domain.Entities;
using TyperBot.Infrastructure.Data;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.DiscordBot.Services;

public class SeasonManagementService
{
    private readonly ILogger<SeasonManagementService> _logger;
    private readonly DiscordSettings _settings;
    private readonly ISeasonRepository _seasonRepository;
    private readonly TyperContext _context;

    public SeasonManagementService(
        ILogger<SeasonManagementService> logger,
        IOptions<DiscordSettings> settings,
        ISeasonRepository seasonRepository,
        TyperContext context)
    {
        _logger = logger;
        _settings = settings.Value;
        _seasonRepository = seasonRepository;
        _context = context;
    }

    public async Task<(bool success, string message)> StartNewSeasonAsync(string seasonName, ulong userId, string username)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(seasonName))
            {
                return (false, "Nazwa sezonu nie może być pusta.");
            }

            var trimmedName = seasonName.Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                return (false, "Nazwa sezonu nie może składać się tylko ze spacji.");
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var allSeasons = await _seasonRepository.GetAllAsync();
                foreach (var season in allSeasons)
                {
                    if (season.IsActive)
                    {
                        season.IsActive = false;
                        await _seasonRepository.UpdateAsync(season);
                    }
                }

                var newSeason = new Season
                {
                    Name = trimmedName,
                    IsActive = true
                };

                await _seasonRepository.AddAsync(newSeason);
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Nowy sezon utworzony - Użytkownik: {Username} (ID: {UserId}), Nazwa: {Name}, ID: {Id}",
                    username, userId, newSeason.Name, newSeason.Id);

                return (true, $"Nowy sezon **{newSeason.Name}** został utworzony i ustawiony jako aktywny.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas tworzenia sezonu - Użytkownik: {Username} (ID: {UserId}), Nazwa: {Name}",
                username, userId, seasonName);
            return (false, "Wystąpił błąd podczas tworzenia sezonu. Sprawdź logi dla szczegółów.");
        }
    }

    public async Task<(bool success, string message)> EndSeasonAsync(int seasonId, ulong userId, string username)
    {
        var season = await _seasonRepository.GetByIdAsync(seasonId);
        if (season == null)
        {
            return (false, "Sezon nie znaleziony.");
        }

        season.IsActive = false;
        await _seasonRepository.UpdateAsync(season);

        _logger.LogInformation(
            "Sezon zakończony - Użytkownik: {Username} (ID: {UserId}), Sezon: {Name} (ID: {Id})",
            username, userId, season.Name, season.Id);

        return (true, $"Sezon **{season.Name}** został oznaczony jako zakończony.");
    }

    public async Task<(bool success, string message)> ReactivateSeasonAsync(int seasonId, ulong userId, string username)
    {
        var season = await _seasonRepository.GetByIdAsync(seasonId);
        if (season == null)
        {
            return (false, "Sezon nie znaleziony.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var allSeasons = await _seasonRepository.GetAllAsync();
            foreach (var s in allSeasons)
            {
                if (s.IsActive && s.Id != seasonId)
                {
                    s.IsActive = false;
                    await _seasonRepository.UpdateAsync(s);
                }
            }

            season.IsActive = true;
            await _seasonRepository.UpdateAsync(season);
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Sezon reaktywowany - Użytkownik: {Username} (ID: {UserId}), Sezon: {Name} (ID: {Id})",
                username, userId, season.Name, season.Id);

            return (true, $"Sezon **{season.Name}** został ustawiony jako aktywny.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Błąd reaktywacji sezonu {SeasonId}", seasonId);
            return (false, "Wystąpił błąd podczas reaktywacji sezonu.");
        }
    }
}
