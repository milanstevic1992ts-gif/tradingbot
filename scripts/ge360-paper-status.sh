#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SERVICE_NAME="${GE360_PAPER_SERVICE_NAME:-ge360-trading-paper}"
DASHBOARD_SERVICE_NAME="${GE360_DASHBOARD_SERVICE_NAME:-ge360-trading-dashboard}"
STORE="${GE360_FORWARD_PAPER_STORE:-$ROOT/ge360-state/forward-paper.json}"

echo "===== GE360 PAPER SERVICE ====="
if command -v systemctl >/dev/null 2>&1; then
  systemctl --no-pager --full status "$SERVICE_NAME.service" 2>/dev/null || true
else
  echo "systemctl unavailable"
fi

echo
echo "===== FORWARD PAPER SUMMARY ====="
dotnet run \
  --project "$ROOT/GE360.Trading.Research/GE360.Trading.Research.csproj" \
  --configuration Release \
  -- \
  --paper-summary \
  --paper-store "$STORE"

echo
bash "$ROOT/scripts/ge360-observability-status.sh"

echo
echo "===== DASHBOARD SERVICE ====="
if command -v systemctl >/dev/null 2>&1; then
  systemctl --no-pager --full status "$DASHBOARD_SERVICE_NAME.service" 2>/dev/null || true
fi
echo "Local dashboard default: http://127.0.0.1:9891"

echo
echo "===== RECENT PAPER LOGS ====="
if command -v journalctl >/dev/null 2>&1; then
  journalctl -u "$SERVICE_NAME.service" -n 40 --no-pager 2>/dev/null || true
else
  echo "journalctl unavailable"
fi
