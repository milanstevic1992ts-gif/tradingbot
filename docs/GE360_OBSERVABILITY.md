# GE360 Observability

Phase 9 adds a read-only telemetry layer around the existing GE360 trading pipeline.

Observability must never approve, reject, resize, cancel or submit an order. Risk, protection, recovery and execution remain authoritative even if the telemetry layer is unavailable.

## Runtime files

Default directory:

```text
ge360-state/observability/
```

Files:

```text
runtime-status.json
recent-events.json
events-YYYY-MM-DD.jsonl
```

The directory can be overridden with:

```bash
export GE360_OBSERVABILITY_DIR=/path/to/observability
```

Runtime state is ignored by Git.

## Journal

The daily JSONL journal records event-oriented telemetry instead of logging every market-data bar.

Current event kinds:

- `RuntimeStarted`
- `SignalObserved`
- `RiskApproved`
- `RiskRejected`
- `ExecutionSubmitted`
- `ExecutionRejected`
- `OrderEvent`
- `RecoveryState`
- `ProtectionState`
- `RuntimeStopped`
- `ObservabilityWarning`

Signal events preserve correlation fields including strategy id and signal id. Approved-risk events record the approved quantity, stop/take-profit metadata and whether the order is risk-reducing. Execution events record LEAN order ids when submission succeeds. Order events record fill quantity, fill price, fee and status.

The journal uses the algorithm UTC clock, so historical backtests, paper runs and future live runs remain temporally coherent.

## Runtime snapshot

`runtime-status.json` is rewritten atomically and exposes:

- trading state;
- equity and cash;
- gross exposure;
- daily P&L;
- daily-loss percentage;
- drawdown percentage;
- open position count;
- open order count;
- position quantities, average prices and notionals;
- protection halt state and halt reason;
- recovery mode, health and details;
- journal health and last journal error;
- active journal/recent-event paths.

This file is the read-only data contract for dashboards and other monitoring clients.

## Failure behavior

Observability is fail-soft.

A journal or dashboard write failure:

- is exposed through observability health;
- does not bypass or weaken hard risk limits;
- does not authorize trading;
- does not block a risk-reducing exit;
- does not become an alternate execution path.

Recovery checkpoint failures remain fail-closed as defined by phase 8. Observability failures are intentionally separate from recovery authority.

## Terminal status

From the repository root:

```bash
bash scripts/ge360-observability-status.sh
```

The existing paper status command also includes the observability summary:

```bash
bash scripts/ge360-paper-status.sh
```

## Read-only dashboard

The dashboard server has no external dependencies beyond Python 3.

Manual start:

```bash
python3 scripts/ge360-observability-dashboard.py \
  --dashboard-dir dashboard/ge360-observability
```

Default address:

```text
http://127.0.0.1:9891
```

Optional environment variables:

```text
GE360_OBSERVABILITY_BIND=127.0.0.1
GE360_OBSERVABILITY_PORT=9891
```

The HTTP service supports only read operations for:

- `/`
- `/runtime-status.json`
- `/recent-events.json`

POST, PUT, PATCH and DELETE return HTTP 405.

The Debian installer creates a separate `ge360-trading-dashboard.service`. It is isolated from the trading process and binds to localhost by default.

## Debian service

Re-run the idempotent installer after updating the repository:

```bash
bash scripts/ge360-install-paper-service.sh
```

It preserves existing Alpaca credentials, adds missing observability defaults, and enables the read-only dashboard service when Python 3 is available.

## Safety invariant

No observability file, dashboard request or telemetry consumer may mutate risk limits, trading state, strategy state, recovery state, orders or broker configuration.
