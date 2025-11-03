# TyperBot Setup Guide

## Prerequisites

- .NET 9 SDK installed
- Discord Bot Token (from https://discord.com/developers/applications)
- Discord Server where you have admin permissions

## Quick Start

1. **Copy Configuration**
   ```bash
   # Navigate to DiscordBot project
   cd TyperBot.DiscordBot
   ```

2. **Update appsettings.json**
   - Replace `YOUR_BOT_TOKEN_HERE` with your actual Discord bot token
   - Set `GuildId` to your Discord server ID
   - Adjust role names if needed (default: "Typer", "TyperAdmin")
   - Adjust channel names if needed

3. **Run the Bot**
   ```bash
   dotnet run
   ```

The bot will:
- Create the SQLite database automatically
- Apply migrations
- Start the bot (currently placeholder mode)
- Logs will appear in console and in `logs/` directory

## Current Status

⚠️ **The bot currently runs in placeholder mode.** The Discord integration layer needs to be implemented.

### What's Complete

✅ **Backend Infrastructure (100%)**
- Domain entities and enums
- Database context with SQLite
- Repository pattern
- All business logic services:
  - Score calculation
  - Prediction validation
  - Round management
  - PNG table generation
  - CSV exports

### What's Missing

❌ **Discord Integration (0%)**
- Discord client connection
- Slash commands
- Button interactions
- Modal handlers
- Channel/thread management
- Embed builders
- Auto-channel creation

## Next Implementation Steps

To complete the bot, implement:

1. **DiscordBotService** - Main hosted service that:
   - Initializes DiscordSocketClient
   - Registers interaction modules
   - Handles bot ready event
   - Manages bot lifecycle

2. **Interaction Modules** - Discord.Net interaction modules for:
   - Slash commands (commands starting with `/`)
   - Button click handlers
   - Modal submit handlers

3. **Discord Services** - Helper services for:
   - Permission checking (role validation)
   - Channel/thread creation
   - Embed message building
   - File upload (PNG tables)

4. **Integration Points** - Connect Discord events to existing services:
   - Button click → PredictionService
   - Admin action → TableGenerator + ExportService
   - Match result → ScoreCalculator

## Development Tips

- Use Serilog for all logging
- All Discord errors should be user-friendly (no stack traces)
- PNG tables go to `#wyniki-typera`
- Match threads in `#typowanie`
- Admin panel in `#typer-admin`
- Follow timezone settings (Europe/Warsaw by default)

## Architecture

```
DiscordBot (UI Layer) → Application (Business Logic) → Infrastructure (Data Access) → Domain (Entities)
```

Each layer only references layers below it, maintaining clean separation.

## Testing

Currently no automated tests. Manual testing recommended:
1. Create test Discord server
2. Add bot with necessary permissions
3. Test each interaction flow
4. Verify data persistence
5. Check PNG generation
6. Validate scoring calculations

## File Structure

```
TyperBot/
├── Domain/              # Entities, Enums
├── Application/         # Services, Business Logic
├── Infrastructure/      # DbContext, Repositories, DI
├── DiscordBot/          # Bot entry point (needs implementation)
├── README.md            # Main documentation
├── SETUP_GUIDE.md       # This file
└── IMPLEMENTATION_STATUS.md  # Detailed status
```

## Resources

- [Discord.Net Documentation](https://docs.stillu.cc/)
- [Discord Developer Portal](https://discord.com/developers/docs)
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/)
- [SkiaSharp Documentation](https://learn.microsoft.com/en-us/xamarin/graphics-games/skiasharp/)

## License

MIT

