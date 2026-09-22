# GE360 Recovery / Reconciliation

Phase 8 adds fail-closed restart recovery without modifying the LEAN core.

## Persistent checkpoint

Runtime state is stored outside Git at:

```text
ge360-state/recovery-checkpoint.json
```

The path can be overridden with:

```bash
export GE360_RECOVERY_CHECKPOINT=/path/to/recovery-checkpoint.json
```

The checkpoint is written atomically and records:

- last observed UTC time;
- whether shutdown was graceful;
- expected positions and quantities;
- average entry prices;
- strategy id;
- protective stop and take-profit metadata;
- LEAN open-order state.

## Startup rules

At the first live-paper data bar, GE360 compares the checkpoint with LEAN's broker-synchronized `Portfolio` and `Transactions.GetOpenOrders()`.

No strategy entry is allowed on the reconciliation bar.

### Synchronized

A position may be resumed only when:

- symbol and quantity match;
- there are no startup open orders;
- exposed checkpoint age is within the configured limit;
- protection metadata is present.

GE360 restores the software stop/target monitor before allowing new strategy risk on a later bar.

### Reduce-only

GE360 enters `TradingState.Reducing` when:

- runtime exposure exists with no checkpoint;
- quantities differ;
- startup open orders exist;
- the checkpoint itself contains unresolved open orders;
- an exposed checkpoint is stale;
- protection metadata is missing;
- the checkpoint cannot be loaded or persisted.

The existing risk gate blocks all new Long/Short risk while `Reducing`. `Flat` signals remain allowed.

Startup open orders are cancelled first. Remaining positions are then reduced through the normal GE360 path:

```text
SignalIntent Flat
  -> ProtectionEngine
  -> PreTradeRiskGate
  -> ApprovedOrderIntent
  -> ExecutionGuard
  -> LeanExecutionAdapter
  -> LEAN PaperBrokerage
```

There is no direct recovery bypass to `Liquidate()` or to the brokerage.

### Safe flat baseline reset

If checkpoint and broker state disagree but LEAN is already flat with no open orders, GE360 writes a fresh flat baseline and consumes that bar. Strategy entries can resume only on a later bar.

## Persistence failure

Checkpoint write failures fail closed. New risk is blocked until persistence becomes healthy again. Risk-reducing actions remain available.

## Phase-7 relationship

Phase-8 engineering is complete, but this does not unlock real-broker live trading.

The phase-7 historical-quality, OOS/walk-forward and forward-paper observation requirements remain authoritative before any real-broker activation.
