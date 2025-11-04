# Admin Panel Quick Start Guide

## Overview

This guide explains how to use the enhanced admin panel to manage kolejki (rounds) in the DiscordTyper bot.

## Opening the Admin Panel

Type the command:
```
/panel-admina
```

You will see 5 buttons:
- **➕ Dodaj kolejkę** - Create a new round with multiple matches
- **⚙ Zarządzaj kolejką** - Manage existing rounds
- **➕ Dodaj mecz** - Add a single match (legacy)
- **📊 Tabela sezonu** - Generate season standings
- **📊 Tabela kolejki** - Generate round standings

---

## Creating a New Kolejka (Round)

### Step 1: Click "➕ Dodaj kolejkę"

A modal will appear asking for:
- **Numer kolejki (1-18)**: Enter the round number
  - 1-14: Regular rounds
  - 15: First quarter-final round
  - 16: Second quarter-final round
  - 17: Semi-finals
  - 18: Finals
- **Liczba meczów**: Enter how many matches (1-8)

Example: Enter `1` and `4` to create Round 1 with 4 matches.

### Step 2: Configure Each Match

For each match, you'll see a form with:

**Team Selection:**
- Select **home team** from dropdown (8 PGE teams)
- Select **away team** from dropdown (8 PGE teams)
  - ⚠️ Teams must be different!

**Date Selection:**
- Use dropdown to select date from next 3 months
- Or use calendar navigation buttons:
  - **« Poprzedni** - Previous month
  - **📅 Dziś** - Jump to today
  - **Następny »** - Next month

**Time Selection:**
- Default: 18:00
- **⏪ -15 min** - Decrease time by 15 minutes
- **⏩ +15 min** - Increase time by 15 minutes
- **✏️ Ustaw godzinę** - Enter time manually (HH:mm format)

Click **Zatwierdź mecz** when all fields are selected.

### Step 3: Repeat for All Matches

The form shows progress: "Mecz 1/4", "Mecz 2/4", etc.

After the last match, all matches are created automatically!

### Result

✅ You'll see: "Dodano kolejkę {name} z {N} meczami."

All matches are posted to the predictions channel with prediction buttons.

---

## Managing an Existing Kolejka

### Step 1: Click "⚙ Zarządzaj kolejką"

A dropdown appears with all existing rounds:
- "Runda 1 - 4 meczów"
- "Runda 2 - 4 meczów"
- "1/4 finału – 1 - 2 meczów"
- etc.

### Step 2: Select a Round

You'll see:
- List of all matches in that round
- Status of each match (⏳ Scheduled, ✅ Finished, ❌ Cancelled)
- Action buttons for each match

### Available Actions

**For matches without results:**
- **✏️ Edytuj #{ID}** - Edit match details (teams, date, time)
- **🏁 Wynik #{ID}** - Enter match result

**To edit a match:**
1. Click ✏️ button
2. Modal appears with current values
3. Modify as needed
4. Submit

**To enter result:**
1. Click 🏁 button
2. Enter home team score
3. Enter away team score
4. ⚠️ **Important**: Scores must sum to exactly 90!
   - Valid: 50:40, 46:44, 45:45, 52:38, etc.
   - Invalid: 50:41 (sum = 91), 45:40 (sum = 85)
5. Submit

Result processing happens automatically:
- All predictions are scored
- Standings are updated
- Tables are generated and posted

---

## Generating Standings Tables

### Season Table

1. Click **📊 Tabela sezonu**
2. Table is generated and posted to results channel
3. Shows overall standings with all players' total points

### Round Table

1. Click **📊 Tabela kolejki**
2. Select which round from dropdown
3. Table is generated and posted to results channel
4. Shows points earned in that specific round

**Note:** Tables can be generated even if some matches haven't been played yet. Only completed matches are included in calculations.

---

## Team List

The 8 PGE Ekstraliga teams in dropdowns:
1. Motor Lublin
2. Sparta Wrocław
3. Apator Toruń
4. GKM Grudziądz
5. Falubaz Zielona Góra
6. Stal Gorzów
7. Włókniarz Częstochowa
8. Unia Leszno

---

## Polish Round Names

When you see round numbers, they display as:
- **Runda 1** through **Runda 14** - Regular season rounds
- **1/4 finału – 1** - First quarter-final round
- **1/4 finału – 2** - Second quarter-final round
- **1/2 finału** - Semi-finals
- **Finał** - Finals

---

## Common Scenarios

### Scenario 1: Starting a New Season

1. Open `/panel-admina`
2. Click **➕ Dodaj kolejkę**
3. Enter `1` for kolejka number, `4` for number of matches
4. Configure all 4 matches with teams, dates, times
5. Matches are posted automatically

### Scenario 2: Entering Match Results After Games

1. Open `/panel-admina`
2. Click **⚙ Zarządzaj kolejką**
3. Select the round that was played
4. For each finished match, click **🏁 Wynik**
5. Enter scores (must sum to 90!)
6. Tables generate automatically

### Scenario 3: Fixing a Mistake

**Before result is entered:**
1. Open `/panel-admina`
2. Click **⚙ Zarządzaj kolejką**
3. Select the round
4. Click **✏️ Edytuj** for the wrong match
5. Correct the details
6. Submit

**After result is entered:**
- Results are immutable (cannot be edited)
- Contact system administrator if correction needed

### Scenario 4: Creating Playoff Rounds

For quarter-finals (2 rounds):
1. Create kolejka 15 ("1/4 finału – 1") with 2 matches
2. Create kolejka 16 ("1/4 finału – 2") with 2 matches

For semi-finals:
1. Create kolejka 17 ("1/2 finału") with 2 matches

For finals:
1. Create kolejka 18 ("Finał") with 1 match

---

## Error Messages

Common errors you might see:

**"Kolejka o takim numerze już istnieje."**
- You're trying to create a round that already exists
- Solution: Use "⚙ Zarządzaj kolejką" to edit it instead

**"Suma punktów obu drużyn musi wynosić 90."**
- The result scores don't add up to 90
- Solution: Check your math and re-enter (e.g., 50:40, not 50:41)

**"Drużyna domowa i wyjazdowa muszą być różne."**
- You selected the same team for both home and away
- Solution: Choose different teams

**"Wybierz wszystkie pola przed zatwierdzeniem."**
- You haven't selected all required fields
- Solution: Make sure teams, date, and time are all selected

---

## Tips and Best Practices

1. **Create kolejki in advance**
   - Set up all matches for a round before they're played
   - Players can submit predictions early

2. **Check twice before entering results**
   - Results are immutable once submitted
   - Ensure scores are correct and sum to 90

3. **Generate tables regularly**
   - After each kolejka is complete
   - Keep players engaged with updated standings

4. **Use descriptive match times**
   - Set actual match times (not just default 18:00)
   - Helps players plan their predictions

5. **Don't worry about incomplete kolejki**
   - You can enter results as matches finish
   - Tables update dynamically

---

## Support

If you encounter issues:
1. Check the bot logs for detailed error messages
2. Verify you have admin permissions
3. Ensure the bot has proper channel permissions
4. Contact the bot developer if problems persist

All admin actions are logged with full details for troubleshooting.

