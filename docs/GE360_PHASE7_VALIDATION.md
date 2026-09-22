# GE360 Phase 7 — Research and Validation

Phase 7 is the validation gate for GE360 Trading.

Later engineering phases may exist, but no real-broker activation is permitted until the authoritative phase-7 gate is ready.

## What Phase 7 requires

All of the following must be true:

1. an adequate continuous one-minute historical dataset exists;
2. the dataset passes the engineering quality gate;
3. chronological out-of-sample evaluation has executed;
4. walk-forward evaluation has executed;
5. the historical count gate is reached on the adequate dataset;
6. at least 20 qualifying forward-paper sessions exist;
7. forward-paper observations contain zero structural failures;
8. no real-live submission attempt was recorded;
9. real-broker submission remains disabled.

These are engineering validation requirements. They do not establish or guarantee future profitability.

## Default historical validation universe

The automated Debian validator defaults to:

```text
SPY, QQQ, AAPL, MSFT, NVDA
```

with:

```text
lookback: 120 calendar days
resolution: 1 minute
feed: Alpaca IEX
adjustment: all
```

The universe and window are configurable. The defaults are intended to provide a continuous, liquid engineering dataset rather than optimize Strategy V1.

Environment variables:

```text
GE360_PHASE7_SYMBOLS=SPY,QQQ,AAPL,MSFT,NVDA
GE360_PHASE7_LOOKBACK_DAYS=120
GE360_PHASE7_REFRESH_HOURS=168
GE360_PHASE7_FEED=iex
GE360_PHASE7_ADJUSTMENT=all
```

Optional fixed dates for reproducibility:

```text
GE360_PHASE7_START_DATE=2026-05-01
GE360_PHASE7_END_DATE=2026-09-01
```

Set `GE360_PHASE7_FORCE_REFRESH=true` to force a new download even when the current historical report is still fresh.

## One-command validation

From the repository root:

```bash
bash scripts/ge360-phase7-validate.sh
```

The command:

1. builds the research harness;
2. uses Alpaca credentials from the process environment when available;
3. downloads historical minute bars only when a refresh is required;
4. writes provider-neutral CSV data;
5. executes the external dataset quality assessment;
6. executes full sample / chronological IS-OOS / walk-forward evaluation;
7. evaluates the historical session/trade count gate;
8. reads the authoritative forward-paper observation store;
9. writes a phase-7 progress report;
10. evaluates the authoritative final gate when a research report exists.

It never generates synthetic replacement data when historical credentials or data are unavailable.

## Runtime state

Default directory:

```text
ge360-state/phase7/
```

Files:

```text
minute-data.csv
research-report.json
progress.json
gate.json
```

These are runtime validation artifacts and are ignored by Git.

## Status only

To inspect progress without forcing a historical refresh:

```bash
bash scripts/ge360-phase7-status.sh
```

The status explicitly reports:

- historical report present/missing;
- dataset quality;
- historical calendar sessions;
- historical closed trades;
- count gate status;
- OOS status;
- walk-forward status and window count;
- total and qualifying paper sessions;
- paper structural failures;
- live-submission attempts;
- current blockers;
- final ready state.

Typical blockers:

```text
RESEARCH_REPORT_MISSING
DATASET_QUALITY_NOT_ADEQUATE
HISTORICAL_COUNT_GATE_NOT_REACHED
OUT_OF_SAMPLE_NOT_EXECUTED
WALK_FORWARD_NOT_EXECUTED
FORWARD_PAPER_OBSERVATION_INCOMPLETE
FORWARD_PAPER_STRUCTURAL_FAILURES_PRESENT
LIVE_SUBMISSION_ATTEMPT_DETECTED
LIVE_SUBMISSION_ENABLED
```

## Debian automation

Re-run the idempotent installer:

```bash
bash scripts/ge360-install-paper-service.sh
```

It installs:

```text
ge360-phase7-validation.service
ge360-phase7-validation.timer
```

The timer runs nightly. A blocked gate uses exit code `3`, which systemd treats as an expected non-error state.

Historical data is refreshed once every 168 hours by default. The gate and paper progress are evaluated every time the service runs.

The same protected environment file is used:

```text
/etc/ge360/trading-paper.env
```

Alpaca credentials remain outside Git.

## Forward-paper sessions

Forward-paper observations are not simulated by the phase-7 validator.

They are recorded only by the actual LEAN `live-paper` runtime:

```text
Alpaca market data
  -> GE360 Strategy/Risk/Execution
  -> LEAN PaperBrokerage
  -> actual paper observation
```

A qualifying session must meet the rules defined in `docs/GE360_FORWARD_PAPER.md`.

The required 20 sessions therefore take real market sessions to accumulate. The validator can count them automatically but cannot manufacture them.

## Gate authority

The final machine-readable decision is generated through:

```bash
dotnet run \
  --project GE360.Trading.Research/GE360.Trading.Research.csproj \
  --configuration Release \
  -- \
  --phase7-gate \
  --research-report ge360-state/phase7/research-report.json \
  --paper-store ge360-state/forward-paper.json \
  --live-submission-enabled false \
  --output ge360-state/phase7/gate.json
```

Exit code:

- `0`: ready;
- `3`: blocked.

A documentation edit, dashboard state or manual label cannot override this gate.
