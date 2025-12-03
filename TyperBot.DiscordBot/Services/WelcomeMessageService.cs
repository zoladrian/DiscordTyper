using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyperBot.DiscordBot.Models;

namespace TyperBot.DiscordBot.Services;

public class WelcomeMessageService
{
    private readonly DiscordSocketClient _client;
    private readonly ILogger<WelcomeMessageService> _logger;
    private readonly DiscordSettings _settings;
    private readonly DiscordLookupService _lookupService;
    private bool _welcomeMessagesSent = false;

    public WelcomeMessageService(
        DiscordSocketClient client,
        ILogger<WelcomeMessageService> logger,
        IOptions<DiscordSettings> settings,
        DiscordLookupService lookupService)
    {
        _client = client;
        _logger = logger;
        _settings = settings.Value;
        _lookupService = lookupService;
    }

    public async Task SendWelcomeMessagesIfNeededAsync()
    {
        if (_welcomeMessagesSent) return;

        try
        {
            var guild = await _lookupService.GetGuildAsync();
            if (guild == null)
            {
                _logger.LogWarning("Guild not found, cannot send welcome messages");
                return;
            }

            var adminChannel = await _lookupService.GetAdminChannelAsync();
            var predictionsChannel = await _lookupService.GetPredictionsChannelAsync();

            if (adminChannel != null)
            {
                await SendAdminWelcomeMessageAsync(adminChannel);
            }

            if (predictionsChannel != null)
            {
                await SendPlayerWelcomeMessagesAsync(predictionsChannel);
            }

            _welcomeMessagesSent = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending welcome messages");
        }
    }

    private bool AreEmbedsIdentical(Embed embed1, Embed embed2)
    {
        if (embed1.Title != embed2.Title) return false;
        if (embed1.Description != embed2.Description) return false;
        
        var fields1 = embed1.Fields.ToList();
        var fields2 = embed2.Fields.ToList();
        
        if (fields1.Count != fields2.Count) return false;

        for (int i = 0; i < fields1.Count; i++)
        {
            var field1 = fields1[i];
            var field2 = fields2[i];
            if (field1.Name != field2.Name || field1.Value != field2.Value)
            {
                return false;
            }
        }

        return true;
    }

    private async Task SendAdminWelcomeMessageAsync(SocketTextChannel channel)
    {
        try
        {
            var newEmbed = new EmbedBuilder()
                .WithTitle("📋 Komendy Admina - TyperBot")
                .WithDescription("Lista dostępnych komend administracyjnych dla bota typera.")
                .WithColor(Color.Blue)
                .AddField("🏁 Zarządzanie sezonem",
                    "`/start-nowego-sezonu` - Rozpocznij nowy sezon (deaktywuje poprzednie)\n" +
                    "`/panel-sezonu` - Otwórz panel zarządzania sezonem", inline: false)
                .AddField("⚽ Zarządzanie meczami",
                    "**Dodawanie:**\n" +
                    "• Użyj `/panel-sezonu` → Dodaj kolejkę → Dodaj mecz\n" +
                    "• Lub użyj przycisku \"➕ Dodaj mecz\" w panelu kolejki\n\n" +
                    "**Edycja/Usuwanie:**\n" +
                    "• Kliknij przycisk \"✏️ Edytuj\" na karcie meczu\n" +
                    "• Kliknij przycisk \"🗑️ Usuń\" na karcie meczu\n\n" +
                    "**Wyniki:**\n" +
                    "• Kliknij przycisk \"📝 Wpisz wynik\" na karcie meczu", inline: false)
                .AddField("📊 Publikowanie tabel",
                    "`/admin-tabela-sezonu` - Wyślij tabelę sezonu do kanału wyników\n" +
                    "`/admin-tabela-kolejki [numer]` - Wyślij tabelę kolejki do kanału wyników", inline: false)
                .AddField("💾 Eksport danych",
                    "`/admin-eksport-sezonu` - Eksportuj dane sezonu do CSV\n" +
                    "`/admin-eksport-kolejki [numer]` - Eksportuj dane kolejki do CSV", inline: false)
                .AddField("👤 Inne",
                    "`/wyniki-gracza [użytkownik]` - Wyświetl szczegółowe wyniki gracza\n" +
                    "`/admin-dane-testowe` - Wypełnij bazę danymi testowymi", inline: false)
                .AddField("⚠️ Automatyczne funkcje",
                    "• Przypomnienia o wynikach - automatycznie na kanale adminów dla meczów bez wyniku (3h po rozpoczęciu)\n" +
                    "• Automatyczne publikowanie wyników - po wpisaniu wyniku na kanale `#wyniki-typera`\n" +
                    "• Automatyczne publikowanie tabel - po zakończeniu ostatniego meczu w kolejce", inline: false)
                .WithFooter("TyperBot - System zarządzania typerem")
                .WithCurrentTimestamp()
                .Build();

            // Check pinned messages from bot
            var pinnedMessages = await channel.GetPinnedMessagesAsync();
            var existingMessage = pinnedMessages
                .Where(m => m.Author.Id == _client.CurrentUser.Id && m is SocketUserMessage)
                .Cast<SocketUserMessage>()
                .FirstOrDefault(m => 
                    m.Embeds.Any(e => e.Title?.Contains("Komendy Admina") == true));

            if (existingMessage != null)
            {
                var existingEmbed = existingMessage.Embeds.FirstOrDefault(e => e.Title?.Contains("Komendy Admina") == true);
                if (existingEmbed != null)
                {
                    if (AreEmbedsIdentical(existingEmbed, newEmbed))
                    {
                        _logger.LogInformation("Admin welcome message already exists and is identical, skipping");
                        return;
                    }
                    else
                    {
                        _logger.LogInformation("Admin welcome message exists but differs, updating...");
                        try
                        {
                            await existingMessage.UnpinAsync();
                            await existingMessage.DeleteAsync();
                            _logger.LogInformation("Old admin welcome message removed");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to remove old admin welcome message, continuing anyway");
                        }
                    }
                }
            }

            var message = await channel.SendMessageAsync(embed: newEmbed);
            await message.PinAsync();
            
            _logger.LogInformation("Admin welcome message sent and pinned in channel {ChannelName}", channel.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send admin welcome message");
        }
    }

    private async Task SendPlayerWelcomeMessagesAsync(SocketTextChannel channel)
    {
        try
        {
            var pinnedMessages = await channel.GetPinnedMessagesAsync();
            var botMessages = pinnedMessages
                .Where(m => m.Author.Id == _client.CurrentUser.Id && m is SocketUserMessage)
                .Cast<SocketUserMessage>()
                .ToList();

            // Commands message
            var commandsEmbed = new EmbedBuilder()
                .WithTitle("📝 Jak używać Typera?")
                .WithDescription("Witaj w systemie typera! Oto jak możesz typować mecze i sprawdzać swoje wyniki.")
                .WithColor(Color.Green)
                .AddField("🎯 Jak typować?",
                    "1. Znajdź wątek meczu w tym kanale (każdy mecz ma swój wątek)\n" +
                    "2. Kliknij przycisk **\"Typuj\"** na karcie meczu\n" +
                    "3. Wpisz swój typ (np. 3:2 dla drużyny domowej 3, wyjazdowej 2)\n" +
                    "4. Gotowe! Możesz zmienić typ w każdej chwili przed rozpoczęciem meczu", inline: false)
                .AddField("📊 Sprawdzanie wyników",
                    "`/moje-typy` - Zobacz wszystkie swoje typy\n" +
                    "`/moje-typy [numer kolejki]` - Zobacz typy dla konkretnej kolejki\n\n" +
                    "Widzisz tam:\n" +
                    "• ✅ Zakończone mecze z wynikami\n" +
                    "• 🎯 Zdobyte punkty za celne typy\n" +
                    "• ⏰ Nadchodzące mecze", inline: false)
                .AddField("🏆 Tabele",
                    "`/tabela-sezonu` - Zobacz ogólną tabelę sezonu\n" +
                    "`/tabela-kolejki [numer]` - Zobacz tabelę konkretnej kolejki\n\n" +
                    "Tabele pokazują:\n" +
                    "• Pozycję każdego gracza\n" +
                    "• Zdobyte punkty\n" +
                    "• Liczbę typów i celnych wyników", inline: false)
                .AddField("💡 Wskazówki",
                    "• Typuj przed rozpoczęciem meczu - po starcie nie możesz już zmienić typu\n" +
                    "• Możesz zmienić typ w każdej chwili przed meczem\n" +
                    "• Po zatypowaniu pojawi się wiadomość w wątku meczu\n" +
                    "• Punkty są przyznawane automatycznie po wpisaniu wyniku przez admina", inline: false)
                .WithFooter("Masz pytania? Skontaktuj się z administratorem")
                .WithCurrentTimestamp()
                .Build();

            var existingCommandsMessage = botMessages.FirstOrDefault(m => 
                m.Embeds.Any(e => e.Title?.Contains("Jak używać Typera") == true));

            if (existingCommandsMessage != null)
            {
                var existingEmbed = existingCommandsMessage.Embeds.FirstOrDefault(e => e.Title?.Contains("Jak używać Typera") == true);
                if (existingEmbed != null)
                {
                    if (AreEmbedsIdentical(existingEmbed, commandsEmbed))
                    {
                        _logger.LogInformation("Player commands welcome message already exists and is identical, skipping");
                    }
                    else
                    {
                        _logger.LogInformation("Player commands welcome message exists but differs, updating...");
                        try
                        {
                            await existingCommandsMessage.UnpinAsync();
                            await existingCommandsMessage.DeleteAsync();
                            _logger.LogInformation("Old player commands welcome message removed");
                            
                            var newMessage = await channel.SendMessageAsync(embed: commandsEmbed);
                            await newMessage.PinAsync();
                            _logger.LogInformation("Player commands welcome message updated and pinned");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to update player commands welcome message");
                        }
                    }
                }
            }
            else
            {
                var commandsMessage = await channel.SendMessageAsync(embed: commandsEmbed);
                await commandsMessage.PinAsync();
                _logger.LogInformation("Player commands welcome message sent and pinned");
            }

            // Rules message
            var rulesEmbed = new EmbedBuilder()
                .WithTitle("📜 Zasady Typera")
                .WithDescription("Zasady i informacje o systemie punktowania.")
                .WithColor(Color.Gold)
                .AddField("🎯 System punktowania",
                    "**50 punktów** - Dokładny wynik (np. typowałeś 3:2, wynik to 3:2)\n" +
                    "**35 punktów** - Dokładny remis (np. typowałeś 2:2, wynik to 2:2)\n" +
                    "**20 punktów** - Poprawny zwycięzca + różnica bramek (np. typowałeś 3:1, wynik to 2:0)\n" +
                    "**2 punkty** - Tylko poprawny zwycięzca (np. typowałeś 2:1, wynik to 1:0)\n" +
                    "**0 punktów** - Niepoprawny typ", inline: false)
                .AddField("⏰ Kiedy typować?",
                    "• Typuj **przed rozpoczęciem meczu**\n" +
                    "• Po starcie meczu nie możesz już zmienić typu\n" +
                    "• Każdy mecz ma swój wątek w kanale `#typowanie`\n" +
                    "• Wątki są tworzone automatycznie 2 dni przed meczem", inline: false)
                .AddField("📍 Gdzie typować?",
                    "1. Wejdź do kanału `#typowanie`\n" +
                    "2. Znajdź wątek z meczem, który Cię interesuje\n" +
                    "3. W wątku znajdziesz kartę meczu z przyciskiem **\"Typuj\"**\n" +
                    "4. Kliknij przycisk i wpisz swój typ", inline: false)
                .AddField("✅ Co dalej?",
                    "• Po zatypowaniu pojawi się wiadomość w wątku\n" +
                    "• Możesz zmienić typ w każdej chwili przed meczem\n" +
                    "• Po zakończeniu meczu admin wpisze wynik\n" +
                    "• Punkty są przyznawane automatycznie\n" +
                    "• Sprawdź swoje wyniki komendą `/moje-typy`", inline: false)
                .AddField("❓ Częste pytania",
                    "**Czy mogę zmienić typ?**\n" +
                    "Tak, ale tylko przed rozpoczęciem meczu.\n\n" +
                    "**Kiedy dostanę punkty?**\n" +
                    "Automatycznie po wpisaniu wyniku przez admina.\n\n" +
                    "**Gdzie zobaczę tabelę?**\n" +
                    "Użyj `/tabela-sezonu` lub `/tabela-kolejki [numer]`", inline: false)
                .WithFooter("Powodzenia w typowaniu! 🍀")
                .WithCurrentTimestamp()
                .Build();

            var existingRulesMessage = botMessages.FirstOrDefault(m => 
                m.Embeds.Any(e => e.Title?.Contains("Zasady Typera") == true));

            if (existingRulesMessage != null)
            {
                var existingEmbed = existingRulesMessage.Embeds.FirstOrDefault(e => e.Title?.Contains("Zasady Typera") == true);
                if (existingEmbed != null)
                {
                    if (AreEmbedsIdentical(existingEmbed, rulesEmbed))
                    {
                        _logger.LogInformation("Player rules welcome message already exists and is identical, skipping");
                    }
                    else
                    {
                        _logger.LogInformation("Player rules welcome message exists but differs, updating...");
                        try
                        {
                            await existingRulesMessage.UnpinAsync();
                            await existingRulesMessage.DeleteAsync();
                            _logger.LogInformation("Old player rules welcome message removed");
                            
                            var newMessage = await channel.SendMessageAsync(embed: rulesEmbed);
                            await newMessage.PinAsync();
                            _logger.LogInformation("Player rules welcome message updated and pinned");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to update player rules welcome message");
                        }
                    }
                }
            }
            else
            {
                var rulesMessage = await channel.SendMessageAsync(embed: rulesEmbed);
                await rulesMessage.PinAsync();
                _logger.LogInformation("Player rules welcome message sent and pinned");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send player welcome messages");
        }
    }
}

