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
                    "• Kliknij przycisk \"📝 Wpisz wynik\" na karcie meczu\n\n" +
                    "**Ujawnianie typów:**\n" +
                    "• Kliknij przycisk \"👁️ Ujawnij typy\" na karcie meczu (po godzinie rozpoczęcia)\n" +
                    "• Typy są automatycznie ujawniane po wpisaniu wyniku", inline: false)
                .AddField("📊 Publikowanie tabel",
                    "`/admin-tabela-sezonu [kanal_lub_watek?]` — tekst; domyślnie **ten kanał**, parametr = inny\n" +
                    "`/admin-tabela-kolejki [numer] [kanal_lub_watek?]` — tekst; j.w.\n" +
                    "`/admin-tabela-sezonu-obraz [kanal_lub_watek?]` — PNG; j.w.\n" +
                    "`/admin-tabela-kolejki-obraz [numer] [kanal_lub_watek?]` — PNG; j.w.\n" +
                    "`/admin-tabela-meczu [mecz] [kanal_lub_watek?]` — embed; j.w.\n" +
                    "• **Inny kanał/wątek:** w `kanal_lub_watek` często trzeba **zacząć pisać nazwę wątku** (wyszukiwanie).", inline: false)
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
                .Where(m => m.Author.Id == _client.CurrentUser.Id && m.Embeds.Any(e => e.Title?.Contains("Komendy Admina") == true))
                .FirstOrDefault();

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
                            // Always fetch from channel to ensure we have IUserMessage
                            var msg = await channel.GetMessageAsync(existingMessage.Id);
                            if (msg is IUserMessage channelUserMsg)
                            {
                                await channelUserMsg.UnpinAsync();
                                await channelUserMsg.DeleteAsync();
                                _logger.LogInformation("Old admin welcome message removed");
                            }
                            else
                            {
                                _logger.LogWarning("Could not delete existing admin welcome message - wrong message type: {Type}", msg?.GetType().Name ?? "null");
                            }
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
                .Where(m => m.Author.Id == _client.CurrentUser.Id)
                .ToList();

            // Commands message
            var commandsEmbed = new EmbedBuilder()
                .WithTitle("📝 Jak używać Typera?")
                .WithDescription("Witaj w systemie typera! Oto jak możesz typować mecze i sprawdzać swoje wyniki.")
                .WithColor(Color.Green)
                .AddField("🎯 Jak typować?",
                    "1. Znajdź wątek meczu w tym kanale (każdy mecz ma swój wątek)\n" +
                    "2. Kliknij przycisk **\"Typuj\"** na karcie meczu\n" +
                    "3. Wpisz swój typ (np. 52:38)\n" +
                    "4. Gotowe! Możesz zmienić typ w każdej chwili przed pierwotną godziną rozpoczęcia meczu", inline: false)
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
                            // Always fetch from channel to ensure we have IUserMessage
                            var msg = await channel.GetMessageAsync(existingCommandsMessage.Id);
                            if (msg is IUserMessage channelUserMsg)
                            {
                                await channelUserMsg.UnpinAsync();
                                await channelUserMsg.DeleteAsync();
                                _logger.LogInformation("Old player commands welcome message removed");
                            }
                            else
                            {
                                _logger.LogWarning("Could not delete existing player commands welcome message - wrong message type: {Type}", msg?.GetType().Name ?? "null");
                            }
                            
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
                    "**50 punktów** - Dokładny remis 45:45 (np. typowałeś 45:45, wynik to 45:45)\n" +
                    "**35 punktów** - Dokładny wynik który nie jest remisem (np. typowałeś 50:40, wynik to 50:40)\n" +
                    "**20 punktów** - Różnica 1-2 punktów (np. typowałeś 50:40, wynik 49:41)\n" +
                    "**18 punktów** - Różnica 3-4 punktów\n" +
                    "**16 punktów** - Różnica 5-6 punktów\n" +
                    "**14 punktów** - Różnica 7-8 punktów\n" +
                    "**12 punktów** - Różnica 9-10 punktów\n" +
                    "**10 punktów** - Różnica 11-12 punktów\n" +
                    "**8 punktów** - Różnica 13-14 punktów\n" +
                    "**6 punktów** - Różnica 15-16 punktów\n" +
                    "**4 punkty** - Różnica 17-18 punktów\n" +
                    "**2 punkty** - Różnica 19+ punktów\n" +
                    "**0 punktów** - Źle wyznaczony zwycięzca meczu\n\n" +
                    "**Uwaga:** Suma typowanego wyniku musi wynosić 90 punktów. Jeśli suma jest inna, typ zostanie odrzucony i pojawi się publiczna wiadomość w wątku meczu.", inline: false)
                .AddField("⏰ Kiedy typować?",
                    "• Typuj **przed pierwotną godziną rozpoczęcia meczu**\n" +
                    "• Jeśli mecz jest opóźniony, godzina typowania pozostaje taka sama\n" +
                    "• Po pierwotnej godzinie rozpoczęcia nie możesz już zmienić typu\n" +
                    "• Dla meczów przełożonych możesz zmienić typ przed pierwotną godziną rozpoczęcia\n" +
                    "• Każdy mecz ma swój wątek w kanale `#typowanie`\n" +
                    "• Wątki są tworzone automatycznie 2 dni przed meczem", inline: false)
                .AddField("📍 Gdzie typować?",
                    "1. Wejdź do kanału `#typowanie`\n" +
                    "2. Znajdź wątek z meczem, który Cię interesuje\n" +
                    "3. W wątku znajdziesz kartę meczu z przyciskiem **\"Typuj\"**\n" +
                    "4. Kliknij przycisk i wpisz swój typ", inline: false)
                .AddField("✅ Co dalej?",
                    "• Po zatypowaniu pojawi się wiadomość w wątku\n" +
                    "• Możesz zmienić typ w każdej chwili przed pierwotną godziną rozpoczęcia\n" +
                    "• Typy są tajne do momentu rozpoczęcia meczu\n" +
                    "• Po zakończeniu meczu admin wpisze wynik\n" +
                    "• Punkty są przyznawane automatycznie\n" +
                    "• Sprawdź swoje wyniki komendą `/moje-typy`", inline: false)
                .AddField("❓ Częste pytania",
                    "**Czy mogę zmienić typ?**\n" +
                    "Tak, ale tylko przed pierwotną godziną rozpoczęcia meczu. Jeśli mecz jest przełożony, możesz zmienić typ przed pierwotną godziną.\n\n" +
                    "**Co jeśli mecz jest opóźniony?**\n" +
                    "Godzina typowania pozostaje taka sama - typujesz do pierwotnej godziny rozpoczęcia, nie do faktycznej.\n\n" +
                    "**Dlaczego suma musi wynosić 90?**\n" +
                    "To zasady żużlowe - każdy mecz ma łącznie 90 punktów do zdobycia (15 biegów × 6 punktów).\n\n" +
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
                            // Always fetch from channel to ensure we have IUserMessage
                            var msg = await channel.GetMessageAsync(existingRulesMessage.Id);
                            if (msg is IUserMessage channelUserMsg)
                            {
                                await channelUserMsg.UnpinAsync();
                                await channelUserMsg.DeleteAsync();
                                _logger.LogInformation("Old player rules welcome message removed");
                            }
                            else
                            {
                                _logger.LogWarning("Could not delete existing player rules welcome message - wrong message type: {Type}", msg?.GetType().Name ?? "null");
                            }
                            
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

