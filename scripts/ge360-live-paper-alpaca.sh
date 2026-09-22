#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LAUNCH_DIR="$ROOT/Launcher/bin/Release"
ALGO_DIR="$ROOT/GE360.Trading.LeanAlgorithm/bin/Release/net10.0"
PLUGIN_DIR="${GE360_ALPACA_PLUGIN_DIR:-$ROOT/ge360-plugins/alpaca}"
PAPER_STORE="${GE360_FORWARD_PAPER_STORE:-$ROOT/ge360-state/forward-paper.json}"
RECOVERY_CHECKPOINT="${GE360_RECOVERY_CHECKPOINT:-$ROOT/ge360-state/recovery-checkpoint.json}"
OBSERVABILITY_DIR="${GE360_OBSERVABILITY_DIR:-$ROOT/ge360-state/observability}"

: "${APCA_API_KEY_ID:?Set APCA_API_KEY_ID before starting GE360 live-paper}"
: "${APCA_API_SECRET_KEY:?Set APCA_API_SECRET_KEY before starting GE360 live-paper}"

export GE360_FORWARD_PAPER_STORE="$PAPER_STORE"
export GE360_RECOVERY_CHECKPOINT="$RECOVERY_CHECKPOINT"
export GE360_OBSERVABILITY_DIR="$OBSERVABILITY_DIR"

dotnet build "$ROOT/Launcher/QuantConnect.Lean.Launcher.csproj" --configuration Release
dotnet build "$ROOT/GE360.Trading.LeanAlgorithm/GE360.Trading.LeanAlgorithm.csproj" --configuration Release
GE360_ALPACA_PLUGIN_DIR="$PLUGIN_DIR" bash "$ROOT/scripts/ge360-stage-alpaca-plugin.sh"

for dll in \
  GE360.Trading.Core.dll \
  GE360.Trading.Validation.dll \
  GE360.Trading.Recovery.dll \
  GE360.Trading.Observability.dll \
  GE360.Trading.LeanAdapter.dll \
  GE360.Trading.LeanAlgorithm.dll
do
  test -f "$ALGO_DIR/$dll"
  cp "$ALGO_DIR/$dll" "$LAUNCH_DIR/$dll"
done

python3 - "$LAUNCH_DIR/config.json" "$PLUGIN_DIR" "$APCA_API_KEY_ID" "$APCA_API_SECRET_KEY" <<'PY'
from pathlib import Path
import json
import re
import sys

path = Path(sys.argv[1])
plugin_dir = str(Path(sys.argv[2]).resolve())
api_key = sys.argv[3]
api_secret = sys.argv[4]

text = path.read_text()

def replace_once(pattern, replacement, label):
    global text
    text, count = re.subn(pattern, replacement, text, count=1, flags=re.S)
    if count != 1:
        raise SystemExit(f"Unable to patch {label}; matched {count} times")

replace_once(
    r'"algorithm-type-name"\s*:\s*"[^"]+"',
    '"algorithm-type-name": "Ge360OpeningRangePaperAlgorithm"',
    "algorithm-type-name",
)
replace_once(
    r'"algorithm-location"\s*:\s*"[^"]+"',
    '"algorithm-location": "GE360.Trading.LeanAlgorithm.dll"',
    "algorithm-location",
)
replace_once(
    r'"environment"\s*:\s*"[^"]+"',
    '"environment": "live-paper"',
    "environment",
)
replace_once(
    r'"alpaca-api-key"\s*:\s*"[^"]*"',
    '"alpaca-api-key": ' + json.dumps(api_key),
    "alpaca-api-key",
)
replace_once(
    r'"alpaca-api-secret"\s*:\s*"[^"]*"',
    '"alpaca-api-secret": ' + json.dumps(api_secret),
    "alpaca-api-secret",
)
replace_once(
    r'"alpaca-access-token"\s*:\s*"[^"]*"',
    '"alpaca-access-token": ""',
    "alpaca-access-token",
)
replace_once(
    r'"alpaca-paper-trading"\s*:\s*(true|false)',
    '"alpaca-paper-trading": true',
    "alpaca-paper-trading",
)

plugin_value = json.dumps(plugin_dir)
if re.search(r'^\s*"plugin-directory"\s*:', text, flags=re.M):
    replace_once(
        r'"plugin-directory"\s*:\s*"[^"]*"',
        '"plugin-directory": ' + plugin_value,
        "plugin-directory",
    )
else:
    text = text.replace(
        "{\n",
        "{\n  \"plugin-directory\": " + plugin_value + ",\n",
        1,
    )

match = re.search(
    r'("live-paper"\s*:\s*\{.*?\n\s*\})',
    text,
    flags=re.S,
)
if not match:
    raise SystemExit("Unable to find live-paper environment block")

block = match.group(1)
block, count = re.subn(
    r'"data-queue-handler"\s*:\s*\[[^\]]*\]',
    '"data-queue-handler": [ "AlpacaBrokerage" ]',
    block,
    count=1,
    flags=re.S,
)
if count != 1:
    raise SystemExit("Unable to patch live-paper data-queue-handler")

text = text[:match.start()] + block + text[match.end():]
path.write_text(text)
PY

mkdir -p "$(dirname "$PAPER_STORE")" "$OBSERVABILITY_DIR"

echo "GE360 live-paper configuration:"
echo "  brokerage: LEAN PaperBrokerage"
echo "  live data: AlpacaBrokerage"
echo "  real brokerage submission: DISABLED"
echo "  observation store: $PAPER_STORE"
echo "  recovery checkpoint: $RECOVERY_CHECKPOINT"
echo "  observability: $OBSERVABILITY_DIR"
echo "  plugin directory: $PLUGIN_DIR"

cd "$LAUNCH_DIR"
exec dotnet QuantConnect.Lean.Launcher.dll
