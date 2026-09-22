# GE360 Forward Paper Observation

Phase 7 is not allowed to complete from historical backtests alone.

GE360 requires a persistent forward-paper observation log before phase 8 can be unlocked.

## Runtime architecture

The supported local forward-paper path is intentionally split:

```text
Alpaca live market data
        |
        v
AlpacaBrokerage IDataQueueHandler
        |
        v
GE360 Strategy -> Risk -> ExecutionGuard
        |
        v
LEAN PaperBrokerage
        |
        v
simulated fills only
```

Alpaca is used as a **data provider only** in this profile. Orders are routed to LEAN's native `PaperBrokerage`. GE360 keeps real-broker live submission disabled.

The runtime policy has two independent checks:

1. LEAN must report `live-mode-brokerage = PaperBrokerage`;
2. GE360 must explicitly enable `AllowPaperBrokerageSubmission`.

The paper flag alone cannot authorize Interactive Brokers, Alpaca brokerage execution, or any other real brokerage.

## Automatic recorder

When the algorithm is running under LEAN `live-paper`, `Ge360OpeningRangePaperAlgorithm` enables `LeanForwardPaperRecorder`.

The recorder uses actual LEAN order events and portfolio state. It records:

- session date and UTC start/end;
- closed round-trip trades inferred from fills;
- session P&L from portfolio equity;
- whether the session ended flat;
- structural failures;
- real-live submission attempts if ever detected.

A session is automatically non-qualifying if, among other conditions:

- the process starts after 09:35 New York time;
- the process ends before 15:59 New York time;
- it ends with a position open;
- the order-fill ledger disagrees with portfolio flat state;
- an order becomes invalid;
- a fill crosses directly through flat into the opposite position;
- a structural execution failure occurs.

A restart during the session therefore cannot silently turn a partial observation into a qualifying full session.

## Default engineering requirement

The current default is:

- 20 qualifying forward-paper sessions;
- every qualifying session must end flat;
- zero structural failures;
- zero real-broker submission attempts;
- real-broker live submission must remain disabled when the final phase-7 gate is evaluated.

A qualifying session does not need to be profitable. This is an engineering observation requirement, not a profitability claim.

## Start automatic live-paper on Debian

The launcher uses the official QuantConnect Alpaca brokerage plugin as the live-data handler and LEAN's native `PaperBrokerage` for simulated orders.

Set Alpaca credentials only in the environment:

```bash
export APCA_API_KEY_ID="..."
export APCA_API_SECRET_KEY="..."
```

Optionally choose another persistent observation-store path:

```bash
export GE360_FORWARD_PAPER_STORE="$PWD/ge360-state/forward-paper.json"
```

Then run:

```bash
bash scripts/ge360-live-paper-alpaca.sh
```

The script:

- builds the local LEAN Launcher;
- builds the GE360 algorithm;
- stages the pinned official `QuantConnect.Brokerages.Alpaca` plugin in `ge360-plugins/alpaca`;
- configures LEAN `live-paper`;
- sets `live-mode-brokerage` to the existing LEAN `PaperBrokerage`;
- uses `AlpacaBrokerage` only as `data-queue-handler`;
- copies GE360 Core/Validation/Adapter/Algorithm assemblies;
- starts LEAN;
- never writes Alpaca credentials to Git.

Runtime-generated `ge360-state/` and `ge360-plugins/` are ignored by Git.


## Install as a Debian systemd service

For persistent operation on Debian, install the service from the repository root:

```bash
bash scripts/ge360-install-paper-service.sh
```

The installer:

- creates `/etc/ge360/trading-paper.env` with mode `0600`;
- creates/enables `ge360-trading-paper.service`;
- runs the service as the invoking non-root user by default;
- persists observations in `ge360-state/forward-paper.json`;
- refuses to start while Alpaca credentials are empty;
- restarts after runtime failures without changing the paper/live safety policy.

After installation, edit only the protected environment file:

```bash
sudo nano /etc/ge360/trading-paper.env
```

Set:

```text
APCA_API_KEY_ID=...
APCA_API_SECRET_KEY=...
```

Then start:

```bash
sudo systemctl start ge360-trading-paper
```

To inspect service state, recent logs and the authoritative paper-session counter:

```bash
bash scripts/ge360-paper-status.sh
```

The same summary is available directly:

```bash
dotnet run \
  --project GE360.Trading.Research/GE360.Trading.Research.csproj \
  --configuration Release \
  -- \
  --paper-summary \
  --paper-store ge360-state/forward-paper.json
```

## Manual session import / recovery

Automatic recording is the normal path. The manual command exists for controlled recovery or importing a verified observation:

```bash
dotnet run \
  --project GE360.Trading.Research/GE360.Trading.Research.csproj \
  --configuration Release \
  -- \
  --record-paper-session \
  --paper-store ge360-state/forward-paper.json \
  --session-date 2026-09-22 \
  --started-utc 2026-09-22T13:30:00Z \
  --ended-utc 2026-09-22T20:00:00Z \
  --closed-trades 3 \
  --net-pnl -18.42 \
  --ended-flat true \
  --structural-failures 0 \
  --live-attempts 0 \
  --note "verified recovery import"
```

The store is written atomically. A second record for the same session date is rejected instead of silently overwriting history.

## Evaluate the authoritative phase-7 gate

After an adequate external historical report exists:

```bash
dotnet run \
  --project GE360.Trading.Research/GE360.Trading.Research.csproj \
  --configuration Release \
  -- \
  --phase7-gate \
  --research-report artifacts/ge360-external-research.json \
  --paper-store ge360-state/forward-paper.json \
  --live-submission-enabled false \
  --output artifacts/ge360-phase7-gate.json
```

Exit codes:

- `0`: `READY_FOR_PHASE_8`
- `3`: `BLOCKED`

The result contains explicit blockers such as:

- `DATASET_QUALITY_NOT_ADEQUATE`
- `HISTORICAL_COUNT_GATE_NOT_REACHED`
- `OUT_OF_SAMPLE_NOT_EXECUTED`
- `WALK_FORWARD_NOT_EXECUTED`
- `FORWARD_PAPER_OBSERVATION_INCOMPLETE`
- `FORWARD_PAPER_STRUCTURAL_FAILURES_PRESENT`
- `LIVE_SUBMISSION_ATTEMPT_DETECTED`
- `LIVE_SUBMISSION_ENABLED`

## State storage

Forward-paper state is runtime state, not source code.

The default local path is:

```text
ge360-state/forward-paper.json
```

The directory is ignored by Git and should be included in the Debian backup policy.
