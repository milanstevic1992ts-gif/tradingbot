# GE360 Forward Paper Observation

Phase 7 is not allowed to complete from historical backtests alone.

GE360 requires a persistent forward-paper observation log before phase 8 can be unlocked.

## Default engineering requirement

The current default is:

- 20 qualifying forward-paper sessions;
- every qualifying session must end flat;
- zero structural failures;
- zero live-submission attempts;
- live submission must remain disabled when the final phase-7 gate is evaluated.

A qualifying session does not need to be profitable. This is an engineering observation requirement, not a profitability claim.

## Record a completed paper session

Example:

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
  --note "paper observation"
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

The recommended local repository-relative path is:

```text
ge360-state/forward-paper.json
```

The `ge360-state/` directory is ignored by Git and should be backed up separately on the Debian host.
