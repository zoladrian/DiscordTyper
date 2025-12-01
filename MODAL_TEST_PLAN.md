# 🧪 Plan Testów Modali - Discord.Net 3.x IModal Migration

## ✅ NAPRAWIONE I GOTOWE DO TESTOWANIA

### 1. **test_modal_ultra_debug** ⭐ 
- **Status**: ✅ PRZETESTOWANY I DZIAŁA!
- **Komenda**: `/test-ultra-modal`
- **Test**: Wypełnij oba pola, sprawdź czy handler odbiera wartości
- **Oczekiwany rezultat**: Bot odpowiada z potwierdzeniem obu wartości

### 2. **admin_add_kolejka_modal**
- **Status**: ✅ Naprawiony, gotowy do testu
- **Trigger**: `/panel-admina` → przycisk "Dodaj kolejkę"
- **Pola**: Numer kolejki (1-18), Liczba meczów (1-8)
- **Test**: 
  1. Kliknij "Dodaj kolejkę"
  2. Wpisz numer: `1`, liczba meczów: `4`
  3. Zatwierdź
- **Oczekiwany rezultat**: Kolejka zostanie utworzona, bot wyświetli następny ekran

### 3. **admin_add_match_modal_v2**  
- **Status**: ✅ Naprawiony, gotowy do testu
- **Trigger**: `/panel-admina` → przycisk "Dodaj mecz"
- **Pola**: Numer kolejki, Data, Godzina, Drużyna domowa, Drużyna wyjazdowa
- **Test**:
  1. Kliknij "Dodaj mecz"
  2. Wypełnij: Kolejka `1`, Data `2025-12-10`, Czas `18:00`, Motor Lublin vs Stal Gorzów
  3. Zatwierdź
- **Oczekiwany rezultat**: Mecz zostanie utworzony, karta meczu pojawi się w kanale typowania

## 🚧 DO NAPRAWIENIA (Pozostałe modale)

### 4. **admin_add_match_modal** (starsza wersja)
- Potrzebuje aktualizacji handlera + RespondWithModalAsync
  
### 5. **admin_time_modal** + **admin_kolejka_time_modal**
- Używają tej samej klasy `TimeModal`
- Potrzebują aktualizacji wywołań

### 6. **admin_set_result_modal_{matchId}** (wildcard)
- Dynamiczny CustomId z ID meczu
- Potrzebuje specjalnej obsługi

### 7. **admin_edit_match_modal_{matchId}** (wildcard)
- Dynamiczny CustomId z ID meczu
- Potrzebuje spec jalnej obsługi

### 8. **predict_match_modal_{matchId}** (PredictionModule)
- Potrzebuje utworzenia klasy `PredictionModal : IModal`
- Aktualizacji w `PredictionModule.cs`

### 9. **debug_modal** (DebugModule)
- Prosty modal do naprawy

## 📊 Testy Integracyjne

### Test 1: Pełny Flow Tworzenia Kolejki
```
Kroki:
1. /panel-admina
2. Dodaj kolejkę → numer 1, 4 mecze
3. Wypełnij dane pierwszego meczu (data/czas/drużyny)
4. Wypełnij dane drugiego meczu
5. Wypełnij dane trzeciego meczu
6. Wypełnij dane czwartego meczu
7. Potwierdź utworzenie

Oczekiwany rezultat:
- Kolejka utworzona w bazie
- 4 karty meczów w kanale typowania
- Gracze mogą typować
```

### Test 2: Dodanie Pojedynczego Meczu
```
Kroki:
1. /panel-admina
2. Dodaj mecz
3. Wypełnij wszystkie pola w jednym modalu
4. Zatwierdź

Oczekiwany rezultat:
- Mecz dodany do wybranej kolejki
- Karta meczu w kanale typowania
```

### Test 3: Ustawianie Wyniku
```
Kroki:
1. /panel-admina
2. Ustaw wynik
3. Wybierz mecz
4. Wypełnij wyniki (50:40)
5. Zatwierdź

Oczekiwany rezultat:
- Wynik zapisany
- Punkty obliczone dla graczy
- Status meczu: Finished
```

### Test 4: Typowanie Gracza
```
Kroki:
1. Gracz klika przycisk "Typuj" na karcie meczu
2. Wypełnia wyniki (52:38)
3. Zatwierdza

Oczekiwany rezultat:
- Typ zapisany (sekretny)
- Gracz otrzymuje potwierdzenie
```

## 🔧 Skrypt Diagnostyczny

Uruchom po każdej zmianie:
```powershell
dotnet build ./TyperBot.DiscordBot/TyperBot.DiscordBot.csproj
dotnet run --project ./TyperBot.DiscordBot/TyperBot.DiscordBot.csproj
```

Sprawdź w logach:
```
[INF] Loaded X interaction module(s):
   - AdminModule (6 commands, X modals, 30 components)  <- X powinno być > 1
   
[INF] 📋 Registered X modal handler(s):            <- Powinno być > 0!
   ✅ Modal: 'test_modal_ultra_debug' → ...
   ✅ Modal: 'admin_add_kolejka_modal' → ...
   ✅ Modal: 'admin_add_match_modal_v2' → ...
```

## ✨ Oczekiwany Wynik Po Pełnej Naprawie

```
[INF] Loaded 6 interaction module(s):
   - AdminModule (6 commands, 8 modals, 30 components)  ✅ 8 modali!
   - DebugModule (2 commands, 2 modals, 0 components)   ✅ 2 modale!
   - PredictionModule (0 commands, 1 modals, 1 components) ✅ 1 modal!
   
[INF] 📋 Registered 11 modal handler(s):  ✅ 11 total!
```

## 🎯 Następne Kroki

1. **Przetestuj naprawione modale** (test_modal, admin_add_kolejka, admin_add_match_v2)
2. **Napraw pozostałe modale** (admin_add_match, time_modal, set_result, edit_match)
3. **Napraw modale w innych modulach** (PredictionModule, DebugModule)
4. **Przeprowadź testy integracyjne**
5. **Wyczyść stary kod** (usuń zakomentowany kod ModalBuilder)

