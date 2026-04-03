namespace TyperBot.DiscordBot.Services;

public interface IPredictionsChannelTyperPanelService
{
    /// <summary>Usuwa poprzednie wiadomości panelu (wspólna stopka TyperPanel) i wysyła nowe na dole kanału typowanie.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
