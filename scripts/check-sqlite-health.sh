#!/usr/bin/env bash
# SQLite health checks for TyperBot (run on Linux server where the bot runs).
# Default DB path matches publish layout: typerbot.db next to TyperBot.DiscordBot.dll.
#
# Usage:
#   chmod +x check-sqlite-health.sh
#   ./check-sqlite-health.sh
#   ./check-sqlite-health.sh /home/ubuntu/typerbot_publish/typerbot.db

set -euo pipefail

DB_PATH="${1:-}"
if [[ -z "$DB_PATH" ]]; then
  for candidate in \
    "/home/ubuntu/typerbot_publish/typerbot.db" \
    "./typerbot.db" \
    "$(dirname "$0")/../TyperBot.DiscordBot/typerbot.db"
  do
    if [[ -f "$candidate" ]]; then
      DB_PATH="$candidate"
      break
    fi
  done
fi

if [[ -z "$DB_PATH" || ! -f "$DB_PATH" ]]; then
  echo "Could not find typerbot.db. Pass full path as first argument."
  echo "Example: $0 /home/ubuntu/typerbot_publish/typerbot.db"
  exit 1
fi

echo "=== Database file ==="
echo "Path: $DB_PATH"
ls -lh "$DB_PATH" 2>/dev/null || true
for sidecar in "$DB_PATH-wal" "$DB_PATH-shm"; do
  if [[ -f "$sidecar" ]]; then
    echo "Sidecar: $sidecar"
    ls -lh "$sidecar"
  fi
done

if ! command -v sqlite3 >/dev/null 2>&1; then
  echo ""
  echo "sqlite3 CLI not installed. Install: sudo apt-get update && sudo apt-get install -y sqlite3"
  echo "File sizes above still show if WAL is huge (common cause of slowness)."
  exit 0
fi

ABS_DB="$(readlink -f "$DB_PATH")"

echo ""
echo "=== PRAGMA (read-only URI; if 'database is locked', stop typerbot briefly or retry) ==="
sqlite3 -uri "file:${ABS_DB}?mode=ro" <<'SQL'
PRAGMA integrity_check;
PRAGMA page_count;
PRAGMA freelist_count;
PRAGMA page_size;
PRAGMA journal_mode;
PRAGMA wal_autocheckpoint;
SQL

echo ""
echo "=== Row counts (approximate load) ==="
sqlite3 -uri "file:${ABS_DB}?mode=ro" <<'SQL'
SELECT 'Players' AS t, COUNT(*) FROM Players
UNION ALL SELECT 'Seasons', COUNT(*) FROM Seasons
UNION ALL SELECT 'Rounds', COUNT(*) FROM Rounds
UNION ALL SELECT 'Matches', COUNT(*) FROM Matches
UNION ALL SELECT 'Predictions', COUNT(*) FROM Predictions
UNION ALL SELECT 'PlayerScores', COUNT(*) FROM PlayerScores;
SQL

echo ""
echo "=== Notes ==="
echo "- integrity_check: must be 'ok'"
echo "- If *-wal is very large vs .db, restart bot or run PRAGMA wal_checkpoint(TRUNCATE) while app is STOPPED"
echo "- Huge row counts alone are fine for SQLite until file is hundreds of MB+ with hot writes"
