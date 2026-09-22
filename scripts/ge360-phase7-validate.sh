#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATE_DIR="${GE360_PHASE7_DIR:-$ROOT/ge360-state/phase7}"
PAPER_STORE="${GE360_FORWARD_PAPER_STORE:-$ROOT/ge360-state/forward-paper.json}"
SYMBOLS="${GE360_PHASE7_SYMBOLS:-SPY,QQQ,AAPL,MSFT,NVDA}"
LOOKBACK_DAYS="${GE360_PHASE7_LOOKBACK_DAYS:-120}"
FEED="${GE360_PHASE7_FEED:-iex}"
ADJUSTMENT="${GE360_PHASE7_ADJUSTMENT:-all}"
REFRESH_HOURS="${GE360_PHASE7_REFRESH_HOURS:-168}"
FORCE_REFRESH="${GE360_PHASE7_FORCE_REFRESH:-false}"
DATASET="${GE360_PHASE7_DATASET:-$STATE_DIR/minute-data.csv}"
RESEARCH_REPORT="${GE360_PHASE7_RESEARCH_REPORT:-$STATE_DIR/research-report.json}"
PROGRESS_REPORT="${GE360_PHASE7_PROGRESS_REPORT:-$STATE_DIR/progress.json}"
GATE_REPORT="${GE360_PHASE7_GATE_REPORT:-$STATE_DIR/gate.json}"

mkdir -p "$STATE_DIR"

if [ -n "${GE360_PHASE7_END_DATE:-}" ]; then
  END_DATE="$GE360_PHASE7_END_DATE"
else
  END_DATE="$(python3 - <<'PY'
from datetime import datetime, timedelta, timezone
print((datetime.now(timezone.utc).date() - timedelta(days=1)).isoformat())
PY
)"
fi

if [ -n "${GE360_PHASE7_START_DATE:-}" ]; then
  START_DATE="$GE360_PHASE7_START_DATE"
else
  START_DATE="$(python3 - "$END_DATE" "$LOOKBACK_DAYS" <<'PY'
from datetime import date, timedelta
import sys
end = date.fromisoformat(sys.argv[1])
days = int(sys.argv[2])
if days < 30:
    raise SystemExit("GE360_PHASE7_LOOKBACK_DAYS must be at least 30")
print((end - timedelta(days=days)).isoformat())
PY
)"
fi

echo "===== GE360 PHASE 7 VALIDATION ====="
echo "symbols: $SYMBOLS"
echo "window:  $START_DATE -> $END_DATE"
echo "feed:    $FEED"
echo "state:   $STATE_DIR"

dotnet build   "$ROOT/GE360.Trading.Research/GE360.Trading.Research.csproj"   --configuration Release

needs_refresh=true
if [ -f "$RESEARCH_REPORT" ] && [ "$FORCE_REFRESH" != "true" ]; then
  needs_refresh="$(python3 - "$RESEARCH_REPORT" "$REFRESH_HOURS" <<'PY'
from pathlib import Path
from datetime import datetime, timezone
import sys
p = Path(sys.argv[1])
hours = float(sys.argv[2])
age = (datetime.now(timezone.utc).timestamp() - p.stat().st_mtime) / 3600.0
print("true" if age >= hours else "false")
PY
)"
fi

if [ "$needs_refresh" = "true" ] &&
   [ -n "${APCA_API_KEY_ID:-}" ] &&
   [ -n "${APCA_API_SECRET_KEY:-}" ]; then
  echo
  echo "===== HISTORICAL DOWNLOAD + RESEARCH ====="
  dotnet run     --project "$ROOT/GE360.Trading.Research/GE360.Trading.Research.csproj"     --configuration Release     --no-build     --     --alpaca-download     --symbols "$SYMBOLS"     --start "$START_DATE"     --end "$END_DATE"     --feed "$FEED"     --adjustment "$ADJUSTMENT"     --output-data "$DATASET"     --output "$RESEARCH_REPORT"
elif [ "$needs_refresh" = "false" ]; then
  echo
  echo "Historical report is fresh; download skipped."
  echo "Set GE360_PHASE7_FORCE_REFRESH=true to force a refresh."
else
  echo
  echo "Alpaca credentials are not present in this process."
  echo "Historical download was not attempted and no synthetic data was created."
fi

echo
echo "===== PHASE 7 PROGRESS ====="
dotnet run   --project "$ROOT/GE360.Trading.Research/GE360.Trading.Research.csproj"   --configuration Release   --no-build   --   --phase7-status   --research-report "$RESEARCH_REPORT"   --paper-store "$PAPER_STORE"   --live-submission-enabled false   --output "$PROGRESS_REPORT"

if [ ! -f "$RESEARCH_REPORT" ]; then
  echo
  echo "Phase 7 remains BLOCKED: no adequate external research report is available yet."
  exit 3
fi

echo
echo "===== AUTHORITATIVE PHASE 7 GATE ====="
set +e
dotnet run   --project "$ROOT/GE360.Trading.Research/GE360.Trading.Research.csproj"   --configuration Release   --no-build   --   --phase7-gate   --research-report "$RESEARCH_REPORT"   --paper-store "$PAPER_STORE"   --live-submission-enabled false   --output "$GATE_REPORT"
gate_code=$?
set -e

if [ "$gate_code" -eq 0 ]; then
  echo
  echo "GE360 PHASE 7: READY"
  exit 0
fi

if [ "$gate_code" -eq 3 ]; then
  echo
  echo "GE360 PHASE 7: BLOCKED (expected until every real validation requirement is satisfied)"
  exit 3
fi

exit "$gate_code"
