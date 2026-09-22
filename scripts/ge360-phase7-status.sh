#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATE_DIR="${GE360_PHASE7_DIR:-$ROOT/ge360-state/phase7}"
PAPER_STORE="${GE360_FORWARD_PAPER_STORE:-$ROOT/ge360-state/forward-paper.json}"
RESEARCH_REPORT="${GE360_PHASE7_RESEARCH_REPORT:-$STATE_DIR/research-report.json}"
PROGRESS_REPORT="${GE360_PHASE7_PROGRESS_REPORT:-$STATE_DIR/progress.json}"

mkdir -p "$STATE_DIR"

dotnet run   --project "$ROOT/GE360.Trading.Research/GE360.Trading.Research.csproj"   --configuration Release   --   --phase7-status   --research-report "$RESEARCH_REPORT"   --paper-store "$PAPER_STORE"   --live-submission-enabled false   --output "$PROGRESS_REPORT"   >/dev/null

python3 - "$PROGRESS_REPORT" <<'PY'
import json
import sys
from pathlib import Path

p = Path(sys.argv[1])
d = json.loads(p.read_text())

print("===== GE360 PHASE 7 =====")
print("ready:", d.get("Ready"))
print("historical report:", d.get("HistoricalReportPresent"))
print("dataset adequate:", d.get("DatasetAdequate"))
print("historical sessions:", d.get("HistoricalCalendarSessions"))
print("historical closed trades:", d.get("HistoricalClosedTrades"))
print("historical count gate:", d.get("HistoricalCountGateReached"))
print("OOS executed:", d.get("OutOfSampleExecuted"))
print("walk-forward executed:", d.get("WalkForwardExecuted"))
print("walk-forward windows:", d.get("WalkForwardWindowCount"))
print("paper sessions:", d.get("ForwardPaperSessions"))
print("qualifying paper sessions:", f'{d.get("QualifyingForwardPaperSessions")}/{d.get("RequiredForwardPaperSessions")}')
print("paper structural failures:", d.get("ForwardPaperStructuralFailures"))
print("paper live attempts:", d.get("ForwardPaperLiveSubmissionAttempts"))
print("live submission enabled:", d.get("LiveSubmissionEnabled"))

blockers = d.get("Blockers") or []
print("blockers:")
if blockers:
    for blocker in blockers:
        print("  -", blocker)
else:
    print("  - none")
PY
