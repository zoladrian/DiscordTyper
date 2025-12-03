# Discord Typer Bot

A Discord bot built in C# (.NET 9) for managing a community speedway-match prediction game (PGE Ekstraliga, rounds 1–18).

## Features

- **Secret Predictions**: Predictions are hidden until match results are announced
- **Automatic Scoring**: Points calculated based on prediction accuracy
- **PNG Table Generation**: Beautiful standings tables generated as PNG images
- **Admin Panel**: Visual button-based interface for match management
- **CSV Exports**: Export season and round data
- **Auto-Channel Creation**: Bot creates required channels on first run

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
- If sum ≠ 90, prediction is rejected
- Penalty for sum ≠ 90 is already included in difference calculation
- Predictions are secret until match start time
- Predictions can be changed until original match start time (even if match is postponed)

**Tie-breaks:**
- During season: Points → P50+P35 count
- End of season: Points → P50+P35 → P20 → P18 → P16 → ... → P2

## Permissions

- **Player Role** (`Typer`): Can view matches, submit predictions
- **Admin Role** (`TyperAdmin` or Discord `Administrator`): Full access to admin panel

## Channels

- **#typowanie**: Match prediction threads
- **#wyniki-typera**: Results and standings tables
- **#typer-admin**: Admin panel

## License

MIT

