using System.Reflection;
using Discord;
using Discord.Interactions;

namespace TyperBot.DiscordBot;

/// <summary>
/// Validates modals against Discord REST limits (title, labels, placeholders, custom ids, row count).
/// </summary>
public static class DiscordModalCompliance
{
    /// <summary>Discord allows at most 5 top-level component rows in a modal.</summary>
    public const int MaxModalActionRows = 5;

    /// <summary>Short text inputs accept at most 400 characters of value (API).</summary>
    public const int TextInputShortValueMax = 400;

    /// <summary>Paragraph text inputs accept at most 4000 characters of value (API).</summary>
    public const int TextInputParagraphValueMax = 4000;

    /// <summary>
    /// All concrete <see cref="IModal"/> types in <paramref name="assembly"/> (excluding abstract and interfaces).
    /// </summary>
    public static IEnumerable<Type> GetConcreteModalTypes(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => typeof(IModal).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false });

    /// <summary>
    /// Validates a modal POCO marked with <see cref="ModalTextInputAttribute"/> (compile-time defined modals).
    /// </summary>
    public static IReadOnlyList<string> ValidateModalType(Type modalType)
    {
        var errors = new List<string>();
        if (!typeof(IModal).IsAssignableFrom(modalType) || modalType is not { IsClass: true, IsAbstract: false })
        {
            errors.Add($"{modalType.Name} is not a concrete IModal type.");
            return errors;
        }

        object instance;
        try
        {
            instance = Activator.CreateInstance(modalType)
                       ?? throw new InvalidOperationException("Activator returned null");
        }
        catch (Exception ex)
        {
            errors.Add($"{modalType.Name}: cannot instantiate ({ex.GetType().Name}: {ex.Message})");
            return errors;
        }

        var titleProp = modalType.GetProperty("Title", BindingFlags.Public | BindingFlags.Instance);
        var title = titleProp?.GetValue(instance) as string ?? string.Empty;
        CollectTitleErrors(errors, modalType.Name, title);

        var fieldProps = modalType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ModalTextInputAttribute>() != null)
            .ToList();

        if (fieldProps.Count > MaxModalActionRows)
            errors.Add($"{modalType.Name}: {fieldProps.Count} text fields exceed Discord max {MaxModalActionRows} rows.");

        foreach (var prop in fieldProps)
        {
            var mti = prop.GetCustomAttribute<ModalTextInputAttribute>()!;
            var labelAttr = prop.GetCustomAttribute<InputLabelAttribute>();
            var label = labelAttr?.Label;
            if (string.IsNullOrEmpty(label))
                label = prop.Name;

            CollectTextInputErrors(errors, $"{modalType.Name}.{prop.Name}", mti.CustomId, label, mti.Placeholder, null, mti.Style, mti.MaxLength);
        }

        return errors;
    }

    /// <summary>
    /// Validates a <see cref="Modal"/> built with <see cref="ModalBuilder"/> (runtime-built modals).
    /// </summary>
    public static IReadOnlyList<string> ValidateBuiltModal(Modal modal, string context = "Modal")
    {
        var errors = new List<string>();
        CollectTitleErrors(errors, context, modal.Title);

        if (string.IsNullOrEmpty(modal.CustomId))
            errors.Add($"{context}: CustomId is empty.");
        else if (modal.CustomId.Length > DiscordApiLimits.ComponentCustomId)
            errors.Add($"{context}: CustomId length {modal.CustomId.Length} > {DiscordApiLimits.ComponentCustomId}.");

        var rows = modal.Component.Components;
        if (rows.Count > MaxModalActionRows)
            errors.Add($"{context}: {rows.Count} action rows > {MaxModalActionRows}.");

        var textInputIndex = 0;
        foreach (var row in rows)
        {
            if (row is not ActionRowComponent actionRow)
            {
                errors.Add($"{context}: row is {row.GetType().Name}, expected ActionRowComponent.");
                continue;
            }

            foreach (var component in actionRow.Components)
            {
                if (component is not TextInputComponent ti)
                {
                    errors.Add($"{context}: non-text component {component.Type} in modal.");
                    continue;
                }

                textInputIndex++;
                CollectTextInputErrors(
                    errors,
                    $"{context}[#{textInputIndex}]",
                    ti.CustomId,
                    ti.Label,
                    ti.Placeholder,
                    ti.Value,
                    ti.Style,
                    ti.MaxLength);
            }
        }

        return errors;
    }

    private static void CollectTitleErrors(ICollection<string> errors, string context, string title)
    {
        if (string.IsNullOrEmpty(title))
            errors.Add($"{context}: Title is empty (Discord requires 1–{DiscordApiLimits.ModalTitle} characters).");
        else if (title.Length > DiscordApiLimits.ModalTitle)
            errors.Add($"{context}: Title length {title.Length} > {DiscordApiLimits.ModalTitle}.");
    }

    private static void CollectTextInputErrors(
        ICollection<string> errors,
        string context,
        string customId,
        string? label,
        string? placeholder,
        string? value,
        TextInputStyle style,
        int? maxLength)
    {
        if (string.IsNullOrEmpty(customId))
            errors.Add($"{context}: TextInput CustomId is empty.");
        else if (customId.Length > DiscordApiLimits.ComponentCustomId)
            errors.Add($"{context}: TextInput CustomId length {customId.Length} > {DiscordApiLimits.ComponentCustomId}.");

        if (string.IsNullOrEmpty(label))
            errors.Add($"{context}: Label is empty (Discord requires 1–{DiscordApiLimits.TextInputLabel} characters).");
        else if (label.Length > DiscordApiLimits.TextInputLabel)
            errors.Add($"{context}: Label length {label.Length} > {DiscordApiLimits.TextInputLabel}: \"{DiscordApiLimits.Truncate(label, 30)}\"");

        if (!string.IsNullOrEmpty(placeholder) && placeholder.Length > DiscordApiLimits.TextInputPlaceholder)
            errors.Add($"{context}: Placeholder length {placeholder.Length} > {DiscordApiLimits.TextInputPlaceholder}.");

        if (!string.IsNullOrEmpty(value))
        {
            var valueMax = style == TextInputStyle.Paragraph ? TextInputParagraphValueMax : TextInputShortValueMax;
            if (value.Length > valueMax)
                errors.Add($"{context}: Default value length {value.Length} > {valueMax} for {style}.");
        }

        if (maxLength.HasValue)
        {
            var cap = style == TextInputStyle.Paragraph ? TextInputParagraphValueMax : TextInputShortValueMax;
            // Discord.Net uses 4000 as a default/sentinel on Short ModalTextInputAttribute when no max is specified;
            // the library clamps when serializing. Only flag explicit impossible values.
            if (maxLength.Value > cap &&
                !(style == TextInputStyle.Short && maxLength.Value == TextInputParagraphValueMax))
                errors.Add($"{context}: MaxLength {maxLength} exceeds Discord limit {cap} for {style}.");
        }
    }
}
