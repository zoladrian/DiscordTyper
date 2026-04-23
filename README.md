# Discord Typer Bot

A Discord bot built in C# (.NET 9) for managing a community speedway-match prediction game (PGE Ekstraliga, rounds 1–18).

## Features

- **Secret Predictions**: Predictions are hidden until match results are announced or manually revealed by admin
- **Automatic Scoring**: Points calculated based on prediction accuracy
- **Table Generation**: Beautiful standings tables (text format in Discord, PNG for exports)
- **Admin Panel**: Visual button-based interface for match management
- **CSV Exports**: Export season and round data
- **Auto-Channel Creation**: Bot creates required channels on first run
- **Automatic Reminders**: Reminders for matches without results (3 hours after start)
- **Prediction Reveal**: Admins can reveal predictions after match start time
- **Public Warnings**: Public messages when players try invalid predictions (sum ≠ 90)

## Architecture

### Projects

- **TyperBot.Domain**: Entities (Season, Round, Match, Player, Prediction, PlayerScore), Enums (MatchStatus, Bucket)
- **TyperBot.Application**: Services (ScoreCalculator, PredictionService, TableGenerator, RoundManager, ExportService)
- **TyperBot.Infrastructure**: DbContext, EF Core migrations, Repository pattern
- **TyperBot.DiscordBot**: Discord bot implementation with commands, interactions, and embeds

### Technologies

- **.NET 9**
- **Discord.Net 3.15**
- **Entity Framework Core + SQLite**
- **SkiaSharp** (PNG table generation)
- **EPPlus** (CSV exports)
- **Serilog** (logging)

## Setup

1. Clone the repository
2. Navigate to `TyperBot.DiscordBot` directory
3. Create `appsettings.json` (copy from provided template):
   ```json
   {
     "Discord": {
       "Token": "YOUR_BOT_TOKEN",
       "GuildId": 123456789012345678,
       "PlayerRoleName": "Typer",
       "AdminRoleName": "TyperAdmin",
       "Channels": {
         "PredictionsChannel": "typowanie",
         "ResultsChannel": "wyniki-typera",
         "AdminChannel": "typer-admin"
       },
       "Timezone": "Europe/Warsaw"
     },
     "ConnectionStrings": {
       "DefaultConnection": "Data Source=typerbot.db"
     }
   }
   ```
4. Build and run:
   ```bash
   dotnet build
   dotnet run --project TyperBot.DiscordBot
   ```

## Scoring Rules

### Point System (Speedway Match Predictions)

- **50 points**: Perfect draw (exactly 45:45)
- **35 points**: Exact score match (not a draw)
- **20 points**: Difference 1-2 points
- **18 points**: Difference 3-4 points
- **16 points**: Difference 5-6 points
- **14 points**: Difference 7-8 points
- **12 points**: Difference 9-10 points
- **10 points**: Difference 11-12 points
- **8 points**: Difference 13-14 points
- **6 points**: Difference 15-16 points
- **4 points**: Difference 17-18 points
- **2 points**: Difference 19+ points
- **0 points**: Wrong winner predicted

**Important Rules:**
- Sum of predicted scores must equal 90 points (speedway rules: 15 races × 6 points)
- Official **match results** set by admins are the real totals: **non-negative** scores only; they **do not** need to sum to 90 (e.g. re-runs, forfeits, or other cases where the meeting total differs).
- If sum ≠ 90, prediction is rejected and a public warning message is posted in the match thread
- Penalty for sum ≠ 90 is already included in difference calculation (when calculating scores for valid predictions)
- Predictions are secret until match start time or manually revealed by admin
- Predictions can be changed until original match start time (even if match is postponed)
- Predictions are automatically revealed when match result is entered (for finished matches only)

**Tie-breaks:**
- During season: Points → P50+P35 count
- End of season: Points → P50+P35 → P20 → P18 → P16 → ... → P2

## Commands

### Player Commands
- `/moje-typy [numer kolejki]` - View your predictions (ephemeral)
- `/tabela-sezonu` - View season standings (ephemeral)
- `/tabela-kolejki [numer]` - View round standings (ephemeral)
- `/ping` - Bot health check

### Admin Commands
- `/start-nowego-sezonu` - Start a new season
- `/panel-sezonu` - Open season management panel
- `/admin-tabela-sezonu` - Publish season table to results channel
- `/admin-tabela-kolejki [numer]` - Publish round table to results channel
- `/admin-eksport-sezonu` - Export season data to CSV
- `/admin-eksport-kolejki [numer]` - Export round data to CSV
- `/wyniki-gracza [użytkownik]` - View detailed player results
- `/admin-dane-testowe` - Fill database with test data

### Admin Interactions (Buttons/Modals)
- **Match Cards**: Edit, Delete, Set Result, Reveal Predictions buttons
- **Panel**: Add Round, Manage Round buttons
- **Round Creation**: Interactive form with team selection, date picker, time controls

## Permissions

- **Player Role** (`Typer`): Can view matches, submit predictions, view own standings
- **Admin Role** (`TyperAdmin` or Discord `Administrator`): Full access to admin panel, can publish tables publicly

## Channels

- **#typowanie**: Match prediction threads (auto-created 2 days before match)
- **#wyniki-typera**: Results and standings tables (public announcements)
- **#typer-admin**: Admin panel and reminders

## License

MIT

