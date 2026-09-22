#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

dotnet build "$ROOT/Launcher/QuantConnect.Lean.Launcher.csproj" --configuration Release
dotnet build "$ROOT/GE360.Trading.LeanAlgorithm/GE360.Trading.LeanAlgorithm.csproj" --configuration Release

LAUNCH_DIR="$ROOT/Launcher/bin/Release"
ALGO_DIR="$ROOT/GE360.Trading.LeanAlgorithm/bin/Release/net10.0"

cp "$ALGO_DIR"/GE360.Trading.Core.dll "$LAUNCH_DIR"/
cp "$ALGO_DIR"/GE360.Trading.LeanAdapter.dll "$LAUNCH_DIR"/
cp "$ALGO_DIR"/GE360.Trading.LeanAlgorithm.dll "$LAUNCH_DIR"/

python3 - "$LAUNCH_DIR/config.json" <<'PY'
from pathlib import Path
import re
import sys

path = Path(sys.argv[1])
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

path.write_text(text)
PY

cd "$LAUNCH_DIR"
timeout 180s dotnet QuantConnect.Lean.Launcher.dll
