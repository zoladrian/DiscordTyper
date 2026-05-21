using FluentAssertions;
using Microsoft.Extensions.Options;
using TyperBot.DiscordBot.Models;
using TyperBot.DiscordBot.Services;

namespace TyperBot.Tests.Services;

public class PredictionThreadMessageToggleServiceTests
{
    [Fact]
    public void Constructor_UsesConfigDefaultValue()
    {
        var settings = Options.Create(new DiscordSettings
        {
            EnablePredictionThreadMessages = false
        });

        var service = new PredictionThreadMessageToggleService(settings);

        service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void SetEnabled_UpdatesCurrentValue()
    {
        var settings = Options.Create(new DiscordSettings
        {
            EnablePredictionThreadMessages = false
        });
        var service = new PredictionThreadMessageToggleService(settings);

        service.SetEnabled(true);

        service.IsEnabled.Should().BeTrue();
    }
}
