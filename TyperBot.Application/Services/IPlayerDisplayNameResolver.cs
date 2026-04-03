using TyperBot.Domain.Entities;

namespace TyperBot.Application.Services;

/// <summary>
/// Nazwa wyświetlana gracza w tabelach/wykresach (bez zmian w bazie).
/// W bocie: nick serwera → globalna nazwa → login; poza Discordem: <see cref="Player.DiscordUsername"/>.
/// </summary>
public interface IPlayerDisplayNameResolver
{
    string GetDisplayName(Player player);
}
