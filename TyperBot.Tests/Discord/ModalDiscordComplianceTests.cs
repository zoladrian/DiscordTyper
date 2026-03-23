using Discord;
using FluentAssertions;
using TyperBot.DiscordBot;
using TyperBot.DiscordBot.Modules;

namespace TyperBot.Tests.Discord;

public class ModalDiscordComplianceTests
{
    [Fact]
    public void All_concrete_IModal_types_in_bot_assembly_are_compliant()
    {
        var assembly = typeof(DiscordApiLimits).Assembly;
        var modalTypes = DiscordModalCompliance.GetConcreteModalTypes(assembly).ToList();
        modalTypes.Should().NotBeEmpty();

        var allErrors = new List<string>();
        foreach (var type in modalTypes)
        {
            var errors = DiscordModalCompliance.ValidateModalType(type);
            foreach (var e in errors)
                allErrors.Add($"{type.Name}: {e}");
        }

        allErrors.Should().BeEmpty(because: string.Join("\n", allErrors));
    }

    [Fact]
    public void BuiltModal_prediction_pattern_with_truncation_is_compliant()
    {
        var longTeam = new string('x', 120);
        var homeLabel = DiscordApiLimits.Truncate(longTeam, DiscordApiLimits.TextInputLabel);
        var awayLabel = DiscordApiLimits.Truncate(longTeam, DiscordApiLimits.TextInputLabel);
        var homePh = DiscordApiLimits.Truncate(longTeam, DiscordApiLimits.TextInputPlaceholder);
        var awayPh = DiscordApiLimits.Truncate(longTeam, DiscordApiLimits.TextInputPlaceholder);

        var modal = new ModalBuilder()
            .WithTitle("Złóż swój typ")
            .WithCustomId("predict_match_modal_12345")
            .AddTextInput(homeLabel, "home_points", TextInputStyle.Short, placeholder: homePh, required: true)
            .AddTextInput(awayLabel, "away_points", TextInputStyle.Short, placeholder: awayPh, required: true)
            .Build();

        DiscordModalCompliance.ValidateBuiltModal(modal, nameof(BuiltModal_prediction_pattern_with_truncation_is_compliant))
            .Should().BeEmpty();
    }

    [Fact]
    public void BuiltModal_set_result_pattern_with_truncation_is_compliant()
    {
        var longTeam = new string('z', 200);
        var modal = new ModalBuilder()
            .WithTitle("Ustaw wynik meczu")
            .WithCustomId("admin_set_result_modal_999")
            .AddTextInput(
                DiscordApiLimits.Truncate(longTeam, DiscordApiLimits.TextInputLabel),
                "home_score",
                TextInputStyle.Short,
                placeholder: DiscordApiLimits.Truncate(longTeam, DiscordApiLimits.TextInputPlaceholder),
                value: "50",
                required: true)
            .AddTextInput(
                DiscordApiLimits.Truncate(longTeam, DiscordApiLimits.TextInputLabel),
                "away_score",
                TextInputStyle.Short,
                placeholder: DiscordApiLimits.Truncate(longTeam, DiscordApiLimits.TextInputPlaceholder),
                value: "40",
                required: true)
            .Build();

        DiscordModalCompliance.ValidateBuiltModal(modal)
            .Should().BeEmpty();
    }

    [Fact]
    public void BuiltModal_admin_add_match_calendar_continuation_is_compliant()
    {
        var modal = new ModalBuilder()
            .WithTitle("Dodaj mecz")
            .WithCustomId("admin_add_match_modal")
            .AddTextInput("Drużyna domowa", "home_team", TextInputStyle.Short, placeholder: "Motor Lublin", required: true)
            .AddTextInput("Drużyna wyjazdowa", "away_team", TextInputStyle.Short, placeholder: "Włókniarz Częstochowa", required: true)
            .Build();

        DiscordModalCompliance.ValidateBuiltModal(modal)
            .Should().BeEmpty();
    }

    [Fact]
    public void BuiltModal_time_modal_is_compliant()
    {
        var modal = new ModalBuilder()
            .WithTitle("Ustaw godzinę")
            .WithCustomId("admin_time_modal")
            .AddTextInput("Godzina", "time", TextInputStyle.Short, placeholder: "18:30", value: "18:00", required: true)
            .Build();

        DiscordModalCompliance.ValidateBuiltModal(modal)
            .Should().BeEmpty();
    }

    [Fact]
    public void ValidateBuiltModal_detects_title_over_45_chars()
    {
        var modal = new ModalBuilder()
            .WithTitle(new string('T', 46))
            .WithCustomId("x")
            .AddTextInput("L", "a", TextInputStyle.Short, required: true)
            .Build();

        DiscordModalCompliance.ValidateBuiltModal(modal)
            .Should().Contain(e => e.Contains("Title length"));
    }

    [Fact]
    public void ValidateBuiltModal_detects_label_over_45_chars()
    {
        var modal = new ModalBuilder()
            .WithTitle("Ok")
            .WithCustomId("modal_x")
            .AddTextInput(new string('L', 46), "field1", TextInputStyle.Short, required: true)
            .Build();

        DiscordModalCompliance.ValidateBuiltModal(modal)
            .Should().Contain(e => e.Contains("Label length"));
    }

    [Fact]
    public void ModalBuilder_rejects_sixth_text_input_row_before_build()
    {
        var b = new ModalBuilder()
            .WithTitle("T")
            .WithCustomId("many_rows");
        for (var i = 0; i < 5; i++)
            b = b.AddTextInput($"L{i}", $"id{i}", TextInputStyle.Short, required: true);

        var ex = Assert.Throws<ArgumentException>(() =>
            b.AddTextInput("L5", "id5", TextInputStyle.Short, required: true));

        ex.ParamName.Should().Be("row");
    }

    [Fact]
    public void PredictionModal_static_definition_matches_IModal_scan()
    {
        var errors = DiscordModalCompliance.ValidateModalType(typeof(PredictionModal));
        errors.Should().BeEmpty();
    }
}
