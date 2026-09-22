#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATE_DIR="${GE360_OBSERVABILITY_DIR:-$ROOT/ge360-state/observability}"
STATUS="$STATE_DIR/runtime-status.json"
EVENTS="$STATE_DIR/recent-events.json"

python3 - "$STATUS" "$EVENTS" <<'PY'
import json
import sys
from pathlib import Path

status_path = Path(sys.argv[1])
events_path = Path(sys.argv[2])

print("===== GE360 OBSERVABILITY =====")
if not status_path.exists():
    print("No runtime snapshot yet.")
    raise SystemExit(0)

s = json.loads(status_path.read_text())
for label, key in [
    ("UTC", "utcTime"),
    ("Trading state", "tradingState"),
    ("Equity", "equity"),
    ("Daily P&L", "dailyPnl"),
    ("Gross exposure", "grossExposure"),
    ("Daily loss %", "dailyLossPercent"),
    ("Drawdown %", "drawdownPercent"),
    ("Open positions", "openPositionCount"),
    ("Open orders", "openOrderCount"),
    ("Protection halted", "protectionHalted"),
    ("Halt reason", "protectionHaltReason"),
    ("Recovery", "recoveryMode"),
    ("Recovery healthy", "recoveryHealthy"),
    ("Journal healthy", "journalHealthy"),
    ("Journal error", "journalError"),
]:
    print(f"{label}: {s.get(key, '—')}")

print("\n===== POSITIONS =====")
positions = s.get("positions") or []
if not positions:
    print("FLAT")
else:
    for p in positions:
        print(f"{p.get('symbol')}: qty={p.get('quantity')} avg={p.get('averagePrice')} market={p.get('marketPrice')} notional={p.get('notional')}")

print("\n===== RECENT EVENTS =====")
if not events_path.exists():
    print("No events yet.")
else:
    events = json.loads(events_path.read_text())
    for e in events[-12:]:
        print(f"{e.get('utcTime')} | {e.get('severity')} | {e.get('kind')} | {e.get('code')} | {e.get('symbol') or '-'} | {e.get('message') or ''}")
PY
