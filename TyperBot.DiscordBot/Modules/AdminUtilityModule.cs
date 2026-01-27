using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.Application.Services;
using TyperBot.DiscordBot.Models;

namespace TyperBot.DiscordBot.Modules;

public class AdminUtilityModule : BaseAdminModule
{
    private readonly ILogger<AdminUtilityModule> _logger;
    private readonly DemoDataSeeder _demoDataSeeder;

    public AdminUtilityModule(
        ILogger<AdminUtilityModule> logger,
        IOptions<DiscordSettings> settings,
        DemoDataSeeder demoDataSeeder) : base(settings.Value)
    {
        _logger = logger;
        _demoDataSeeder = demoDataSeeder;
    }

    [SlashCommand("admin-dane-testowe", "Wypełnij bazę danych danymi testowymi (tylko dla adminów)")]
    public async Task SeedDemoDataAsync()
    {
        var user = Context.User as SocketGuildUser;
        if (!IsAdmin(user))
        {
            await RespondWithErrorAsync("Nie masz uprawnień do użycia tej komendy.");
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            var result = await _demoDataSeeder.SeedDemoDataAsync();
            await FollowupAsync(
                $"✅ Dane testowe zostały wygenerowane!\n" +
                $"• Sezony: {result.SeasonsCreated}\n" +
                $"• Kolejki: {result.RoundsCreated}\n" +
                $"• Mecze: {result.MatchesCreated}\n" +
                $"• Gracze: {result.PlayersCreated}\n" +
                $"• Typy: {result.PredictionsCreated}\n" +
                $"• Wyniki: {result.ScoresCreated}", 
                ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas generowania danych testowych");
            await FollowupAsync($"❌ Wystąpił błąd: {ex.Message}", ephemeral: true);
        }
    }

    [ComponentInteraction("admin_cancel_action_*")]
    public async Task HandleCancelActionAsync(string matchIdStr)
    {
        await RespondAsync("❌ Akcja anulowana.", ephemeral: true);
    }
}
