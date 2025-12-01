using Discord;
using Discord.Interactions;

namespace TyperBot.DiscordBot.Modules;

public class SimpleModalTest : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("simple-modal", "Test a simple modal")]
    public async Task SimpleModalCommand()
    {
        await RespondWithModalAsync<SimpleModal>("simple_modal");
    }
}

public class SimpleModal : IModal
{
    public string Title => "Simple Test";

    [InputLabel("Field 1")]
    [ModalTextInput("field1")]
    public string Field1 { get; set; } = string.Empty;

    [InputLabel("Field 2")]
    [ModalTextInput("field2", TextInputStyle.Short, "placeholder...")]
    public string? Field2 { get; set; }
}

