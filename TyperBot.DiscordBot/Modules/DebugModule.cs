using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace TyperBot.DiscordBot.Modules;

public class DebugModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger<DebugModule> _logger;

    public DebugModule(ILogger<DebugModule> logger)
    {
        _logger = logger;
    }

    [ModalInteraction("test_modal_ultra_debug")]
    public async Task HandleTestModalUltraDebugAsync(string testField1, string? testField2 = null)
    {
        _logger.LogInformation("DEBUG MODULE: Modal received! Params: {P1}, {P2}", testField1, testField2);
        await RespondAsync($"Debug Module caught this! 1: {testField1}, 2: {testField2}", ephemeral: true);
    }

    [ModalInteraction("debug_modal")]
    public async Task HandleDebugModal(string field1, string field2)
    {
        _logger.LogInformation("DEBUG MODAL RECEIVED: {Field1}, {Field2}", field1, field2);
        await RespondAsync($"Debug modal received! 1: {field1}, 2: {field2}", ephemeral: true);
    }

    [SlashCommand("debug-modal", "Shows a debug modal")]
    public async Task ShowDebugModal()
    {
        var modal = new ModalBuilder()
            .WithTitle("Debug Modal")
            .WithCustomId("debug_modal")
            .AddTextInput("Field 1", "field1", placeholder: "Type something...")
            .AddTextInput("Field 2", "field2", placeholder: "Type something else...")
            .Build();

        await RespondWithModalAsync(modal);
    }

    [SlashCommand("test-ultra", "Shows the ultra debug modal")]
    public async Task ShowUltraModal()
    {
        var modal = new ModalBuilder()
            .WithTitle("🔬 Test Modal - Ultra Debug")
            .WithCustomId("test_modal_ultra_debug")
            .AddTextInput("Test Field 1", "test_field_1", TextInputStyle.Short, placeholder: "Wpisz coś", required: true)
            .AddTextInput("Test Field 2", "test_field_2", TextInputStyle.Short, placeholder: "Opcjonalne", required: false)
            .Build();

        await RespondWithModalAsync(modal);
    }
}

