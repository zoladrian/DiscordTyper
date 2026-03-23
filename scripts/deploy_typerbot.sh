#!/usr/bin/env bash
# Deploy TyperBot na serwer (git pull → publish → restart systemd).
#
# Użycie (domyślnie ścieżki jak u Ciebie):
#   ./scripts/deploy_typerbot.sh
#
# Zmienne środowiskowe:
#   REPO_ROOT=/ścieżka/do/DiscordTyper
#   PUBLISH_DIR=/home/ubuntu/typerbot_publish
#   SERVICE_NAME=typerbot
#   DRY_RUN=1          — tylko pull + publish, bez restartu
#   RUN_EF_MIGRATIONS_LIST=1 — po deployu: lista migracji EF (wymaga: dotnet tool install -g dotnet-ef)
#
set -euo pipefail

REPO_ROOT="${REPO_ROOT:-/home/ubuntu/DiscordTyper}"
PUBLISH_DIR="${PUBLISH_DIR:-/home/ubuntu/typerbot_publish}"
SERVICE_NAME="${SERVICE_NAME:-typerbot}"

cd "$REPO_ROOT"

echo "═══════════════════════════════════════════════════════════"
echo "TyperBot deploy — $(date -u +"%Y-%m-%dT%H:%M:%SZ") UTC"
echo "Repo: $REPO_ROOT → Publish: $PUBLISH_DIR → Service: $SERVICE_NAME"
echo "═══════════════════════════════════════════════════════════"

echo "➡️ git pull"
git pull

echo "➡️ dotnet publish → $PUBLISH_DIR (linux-x64 — wymagane m.in. dla SkiaSharp / libSkiaSharp.so)"
dotnet publish ./TyperBot.DiscordBot/TyperBot.DiscordBot.csproj \
  -c Release \
  -o "$PUBLISH_DIR" \
  -r linux-x64 \
  --self-contained false \
  --verbosity minimal

if [[ ! -f "$PUBLISH_DIR/TyperBot.DiscordBot.dll" ]]; then
  echo "❌ Brak $PUBLISH_DIR/TyperBot.DiscordBot.dll po publish — przerywam."
  exit 1
fi

echo "📦 Opublikowany główny assembly:"
ls -la "$PUBLISH_DIR/TyperBot.DiscordBot.dll"

for f in appsettings.json appsettings.Production.json; do
  if [[ -f "$PUBLISH_DIR/$f" ]]; then
    echo "📄 Jest: $PUBLISH_DIR/$f"
  else
    echo "⚠️  Brak: $PUBLISH_DIR/$f (sprawdź csproj CopyToOutputDirectory)"
  fi
done

if [[ "${DRY_RUN:-0}" == "1" ]]; then
  echo "⏭️  DRY_RUN=1 — pomijam restart usługi."
  exit 0
fi

echo "➡️ restart $SERVICE_NAME"
sudo systemctl restart "$SERVICE_NAME"
sleep 2

if ! systemctl is-active --quiet "$SERVICE_NAME"; then
  echo "❌ Usługa $SERVICE_NAME nie jest active po restarcie."
  sudo systemctl status "$SERVICE_NAME" --no-pager -l || true
  exit 1
fi

echo "✅ deploy finished — stan: $(systemctl is-active "$SERVICE_NAME")"
echo "   PID: $(systemctl show -p MainPID --value "$SERVICE_NAME")"

echo "➡️ Ostatnie logi ($SERVICE_NAME):"
sudo journalctl -u "$SERVICE_NAME" -n 40 --no-pager

if [[ "${RUN_EF_MIGRATIONS_LIST:-0}" == "1" ]]; then
  echo "➡️ dotnet ef migrations list (opcjonalnie)"
  if dotnet ef migrations list \
    --project ./TyperBot.Infrastructure/TyperBot.Infrastructure.csproj \
    --startup-project ./TyperBot.DiscordBot/TyperBot.DiscordBot.csproj; then
    :
  else
    echo "⚠️  Nie udało się uruchomić 'dotnet ef'. Zainstaluj: dotnet tool install -g dotnet-ef"
  fi
fi

echo "═══════════════════════════════════════════════════════════"
echo "Gotowe. Pełny strumień logów: sudo journalctl -u $SERVICE_NAME -f"
echo "═══════════════════════════════════════════════════════════"
