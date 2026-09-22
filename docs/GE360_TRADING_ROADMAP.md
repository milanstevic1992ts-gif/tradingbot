# GE360 Trading — implementation roadmap

The LEAN upstream core stays authoritative. GE360 features live in separate projects and adapters.

## Current phase

**Phase 7 — Research and validation (IN PROGRESS / validation gate not yet passed).**

Phases 1–6 are implemented. Do not start phase 8 until the phase-7 validation gate below is satisfied.

## Non-negotiable architecture

```text
Market data
  -> GE360 strategy
  -> SignalIntent
  -> position sizing
  -> portfolio risk
  -> pre-trade risk gate
  -> approved order intent
  -> LEAN adapter/execution
  -> brokerage
```

A strategy must never own a brokerage reference or submit an order directly.

## Phases

1. **Core contracts — COMPLETE**
   - Strategy interface emits only `SignalIntent`.
   - Immutable risk limits and portfolio snapshots.
   - Explicit trading states.
   - No LEAN core modification.

2. **Fail-closed pre-trade risk gate — COMPLETE**
   - Halt/reducing-state checks.
   - stale signal and stale market checks.
   - daily-loss and drawdown circuit breakers.
   - max positions, exposure, notional and spread limits.
   - mandatory stop validation.
   - deterministic position sizing.

3. **Protection engine — COMPLETE**
   - consecutive-loss guard.
   - cooldown per symbol/strategy.
   - structural rejection-storm circuit breaker.
   - persistent halt reason and manual reset boundary.

4. **LEAN adapter — COMPLETE**
   - Convert GE360 approved intents into LEAN targets/orders.
   - Preserve LEAN buying-power, fee, slippage and brokerage checks.
   - Explicitly prevent adapter bypass.

5. **Execution safeguards — COMPLETE**
   - volume participation limit.
   - spread/slippage gate.
   - duplicate-order guard.
   - order-rate throttling.
   - reduce-only behavior while de-risking.

6. **Strategy V1 — COMPLETE**
   - liquid-universe price filter.
   - relative-volume filter.
   - VWAP/trend confirmation.
   - opening-range/momentum breakout.
   - confidence-scored signals only.
   - minimum ATR/price-based stop distance.

7. **Research and validation — IN PROGRESS**
   - [x] unit tests for risk, protection, strategy, feature and execution behavior.
   - [x] deterministic research harness reusing production Strategy V1 + Risk + Protection.
   - [x] explicit fee/slippage cost model.
   - [x] historical LEAN bundled-data smoke test.
   - [x] chronological in-sample/out-of-sample split without future leakage.
   - [x] walk-forward window generator.
   - [x] LEAN paper/backtest reality model with explicit deterministic fee/slippage assumptions.
   - [x] mandatory 15:55 New York no-new-risk / protected flatten boundary.
   - [x] live submission remains disabled by default.
   - [ ] run OOS + walk-forward on an adequate minute-history dataset.
   - [ ] reach the configurable minimum engineering sample gate (default: 20 sessions AND 30 closed trades).
   - [ ] complete a forward paper-trading observation period before any live brokerage mode.

   **Validation rule:** the bundled SPY sample (2013-10-07 through 2013-10-11) is only a smoke dataset.
   It must never be presented as evidence of profitability or statistical significance.

8. **Recovery/reconciliation — NOT STARTED**
   - compare local positions/orders with broker state at startup.
   - block new entries on mismatch.
   - allow risk-reducing actions.

9. **Observability — NOT STARTED**
   - journal every signal, rejection, sizing decision and order.
   - dashboard trading state, exposure, daily P&L, drawdown and halt reason.

## Phase-7 exit gate

Phase 7 is complete only when all of the following are true:

1. a sufficiently broad minute-history dataset has been loaded;
2. chronological out-of-sample results have been produced with explicit costs;
3. walk-forward windows have been executed without future leakage;
4. the minimum engineering sample gate is reached;
5. forward paper trading has been observed;
6. no live-broker submission is enabled during validation.

Passing this gate does **not** imply future profitability. It only permits engineering work to proceed to phase 8.

## Safety invariant

No AI, strategy, dashboard or external API may weaken or bypass hard risk limits at runtime.
