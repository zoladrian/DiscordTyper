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

- **Perfect Draw** (all equal): 50 points
- **Exact Score**: 35 points
- **Otherwise**: Based on margin difference + penalty for sum not equal to 90
  - Points range from 20 (closest) to 2 (furthest)

Tie-breaks are determined by bucket counts (P50 → P35 → P20 → ... → P2).

## Permissions

- **Player Role** (`Typer`): Can view matches, submit predictions
- **Admin Role** (`TyperAdmin` or Discord `Administrator`): Full access to admin panel

## Channels

- **#typowanie**: Match prediction threads
- **#wyniki-typera**: Results and standings tables
- **#typer-admin**: Admin panel

## License

MIT

