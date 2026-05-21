using Microsoft.Extensions.Options;
using TyperBot.DiscordBot.Models;

namespace TyperBot.DiscordBot.Services;

public class PredictionThreadMessageToggleService
{
    private bool _enabled;

    public PredictionThreadMessageToggleService(IOptions<DiscordSettings> settings)
    {
        _enabled = settings.Value.EnablePredictionThreadMessages;
    }

    public bool IsEnabled => _enabled;

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
    }
}
