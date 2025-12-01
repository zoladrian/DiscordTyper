# Migracja z PNG na Discord Message Embeds

## Podsumowanie zmian

Wszystkie tabele i wyświetlane dane zostały przeniesione z generowanych obrazów PNG na natywne Discord Message Embeds, co daje następujące korzyści:

### Korzyści
✅ **Kopiowalne treści** - Użytkownicy mogą teraz kopiować tekst z tabel  
✅ **Szybsze ładowanie** - Brak renderowania obrazów  
✅ **Lepsza dostępność** - Czytniki ekranu mogą odczytać zawartość  
✅ **Mniejsze użycie zasobów** - Brak generowania PNG po stronie serwera  
✅ **Lepszy UX** - Natywne dla Discord, ładniejsze wyświetlanie  

## Zmienione funkcje

### 1. Panel Admina (`/panel-admina`)
**Przed:** Prosty opis tekstowy  
**Po:** Wyświetla listę kolejek z meczami jako Embed fields

**Funkcje:**
- Pokazuje aktywny sezon
- Lista kolejek z meczami (maksymalnie 10 pierwszych)
- Każdy mecz z:
  - Emoji statusu (⏰ zaplanowany, ▶️ w trakcie, ✅ zakończony, ❌ odwołany)
  - Data i godzina (lokalna strefa czasowa)
  - Wynik (dla zakończonych meczów)

**Kod:** `AdminModule.cs`, metoda `AdminPanelAsync()`

---

### 2. Tabela Kolejki (`/tabela-kolejki`)
**Przed:** Wygenerowany obraz PNG  
**Po:** Czytelna tabela w formacie monospace w Embed

**Funkcje:**
- Pozycja, nazwa gracza, punkty
- Statystyki: liczba typów, celne wyniki, poprawni zwycięzcy
- Używa code block (```) dla wyrównania monospace
- Legenda w footer

**Kod:** `PlayerModule.cs`, metoda `RoundTableAsync()`

---

### 3. Tabela Sezonu (`/tabela-sezonu`)
**Przed:** Wygenerowany obraz PNG  
**Po:** Czytelna tabela w formacie monospace w Embed

**Funkcje:**
- Pozycja z medalami (🥇🥈🥉) dla TOP 3
- Nazwa gracza, łączne punkty
- Statystyki sezonowe
- Code block dla wyrównania monospace
- Legenda w footer

**Kod:** `PlayerModule.cs`, metoda `SeasonTableAsync()`

---

### 4. Moje Typy (`/moje-typy`)
**Przed:** Prosty opis tekstowy z wynikami  
**Po:** Szczegółowa tabela pogrupowana po kolejkach

**Funkcje:**
- Grupowanie po kolejkach
- Dla każdego meczu:
  - Emoji statusu
  - Data i godzina w lokalnej strefie
  - Drużyny
  - Typ gracza (wyróżniony)
  - Wynik rzeczywisty (jeśli zakończony)
  - Zdobyte punkty z ikonami:
    - 🎯 Celny wynik (+3pkt)
    - ✓ Poprawny zwycięzca (+1pkt)
    - ✗ Brak punktów
- Footer z łącznymi punktami i statystykami

**Kod:** `PlayerModule.cs`, metoda `MyPredictionsAsync()`

---

### 5. Admin: Tabela Sezonu (przycisk w panelu)
**Przed:** Wysyłał PNG do kanału wyników  
**Po:** Wysyła Embed z tabelą

**Funkcje:**
- Identyczna do `/tabela-sezonu`
- Publikowana w kanale wyników
- Medal emojis dla TOP 3

**Kod:** `AdminModule.cs`, metoda `HandleTableSeasonButtonAsync()`

---

### 6. Admin: Tabela Kolejki (przycisk w panelu)
**Przed:** Wysyłał PNG do kanału wyników  
**Po:** Wysyła Embed z tabelą

**Funkcje:**
- Identyczna do `/tabela-kolejki`
- Publikowana w kanale wyników

**Kod:** `AdminModule.cs`, metoda `HandleTableRoundSelectAsync()`

---

## Format tabel

Wszystkie tabele używają code block z formatowaniem monospace:

```
Poz  Gracz                  Pkt   Typ   Cel   Wyg
════════════════════════════════════════════════
  1  PlayerName             120    45    15    28
  2  AnotherPlayer           95    40    12    20
...
```

### Kolumny:
- **Poz** - Pozycja w tabeli
- **Gracz** - Nazwa gracza (max 20 znaków)
- **Pkt** - Łączne punkty
- **Typ** - Liczba oddanych typów
- **Cel** - Celne wyniki (dokładny wynik, 3 punkty)
- **Wyg** - Poprawni zwycięzcy (1 punkt)

---

## Techniczne szczegóły

### Użyte elementy Discord.Net:
- `EmbedBuilder` - Tworzenie embedów
- `AddField()` - Dodawanie pól z danymi
- `WithTitle()`, `WithDescription()`, `WithColor()` - Formatowanie
- `WithFooter()` - Legendy i dodatkowe info
- `WithCurrentTimestamp()` - Timestamp embeda
- Code blocks (\`\`\`) - Monospace formatting

### Strefa czasowa:
Wszystkie daty/godziny są konwertowane do lokalnej strefy czasowej serwera:
```csharp
var tz = TimeZoneInfo.FindSystemTimeZoneById(_settings.Timezone);
var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz);
```

---

## Usunięte zależności

Po tej migracji **NIE** jest już potrzebny `TableGenerator` do generowania PNG. 
Można rozważyć jego usunięcie w przyszłości, jeśli nie jest używany nigdzie indziej.

---

## Testowanie

Przetestuj wszystkie komendy:
1. `/panel-admina` - Sprawdź czy pokazuje kolejki z meczami
2. `/tabela-kolejki [numer]` - Sprawdź formatowanie tabeli
3. `/tabela-sezonu` - Sprawdź medale i formatowanie
4. `/moje-typy` - Sprawdź szczegółowe wyświetlanie
5. Przyciski w panelu admina dla tabel - Sprawdź publikację w kanale wyników

---

## Data migracji
2025-12-01

