#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLUGIN_DIR="${GE360_PLUGIN_DIR:-}"

dotnet build "$ROOT/Launcher/QuantConnect.Lean.Launcher.csproj" --configuration Release
dotnet build "$ROOT/GE360.Trading.LeanAlgorithm/GE360.Trading.LeanAlgorithm.csproj" --configuration Release

LAUNCH_DIR="$ROOT/Launcher/bin/Release"
ALGO_DIR="$ROOT/GE360.Trading.LeanAlgorithm/bin/Release/net10.0"

for dll in \
  GE360.Trading.Core.dll \
  GE360.Trading.Validation.dll \
  GE360.Trading.Recovery.dll \
  GE360.Trading.LeanAdapter.dll \
  GE360.Trading.LeanAlgorithm.dll
do
  test -f "$ALGO_DIR/$dll"
  cp "$ALGO_DIR/$dll" "$LAUNCH_DIR/$dll"
done

python3 - "$LAUNCH_DIR/config.json" "$PLUGIN_DIR" <<'PY'
from pathlib import Path
import json
import re
import sys

path = Path(sys.argv[1])
plugin_dir = sys.argv[2].strip()
text = path.read_text()

text = re.sub(
    r'"algorithm-type-name"\s*:\s*"[^"]+"',
    '"algorithm-type-name": "Ge360OpeningRangePaperAlgorithm"',
    text,
    count=1,
)
text = re.sub(
    r'"algorithm-location"\s*:\s*"[^"]+"',
    '"algorithm-location": "GE360.Trading.LeanAlgorithm.dll"',
    text,
    count=1,
)
text = re.sub(
    r'"environment"\s*:\s*"[^"]+"',
    '"environment": "backtesting"',
    text,
    count=1,
)

if plugin_dir:
    plugin_dir = str(Path(plugin_dir).resolve())
    if not Path(plugin_dir).is_dir():
        raise SystemExit(f"Plugin directory does not exist: {plugin_dir}")

    plugin_value = json.dumps(plugin_dir)

    if re.search(r'^\s*"plugin-directory"\s*:', text, flags=re.M):
        text = re.sub(
            r'"plugin-directory"\s*:\s*"[^"]*"',
            '"plugin-directory": ' + plugin_value,
            text,
            count=1,
        )
    else:
        text = text.replace(
            "{\n",
            "{\n  \"plugin-directory\": " + plugin_value + ",\n",
            1,
        )

path.write_text(text)
PY

cd "$LAUNCH_DIR"
rm -f Ge360OpeningRangePaperAlgorithm-log.txt
timeout 180s dotnet QuantConnect.Lean.Launcher.dll

LOG="$LAUNCH_DIR/Ge360OpeningRangePaperAlgorithm-log.txt"

test -f "$LOG"
grep -q "GE360 ENTRY submitted" "$LOG"
grep -q "GE360 EXIT submitted" "$LOG"

if grep -q "GE360 EXEC REJECT" "$LOG"; then
  echo "GE360 smoke failed: execution rejection detected"
  grep "GE360 EXEC REJECT" "$LOG"
  exit 1
fi

if grep -q "PROTECTION_HALTED" "$LOG"; then
  echo "GE360 smoke failed: protection halt detected"
  grep "PROTECTION_HALTED" "$LOG"
  exit 1
fi

if grep -qi "Runtime Error" "$LOG"; then
  echo "GE360 smoke failed: runtime error detected"
  grep -i "Runtime Error" "$LOG"
  exit 1
fi

if [ -n "$PLUGIN_DIR" ]; then
  echo "GE360 LEAN smoke completed with plugin-directory: $PLUGIN_DIR"
fi
