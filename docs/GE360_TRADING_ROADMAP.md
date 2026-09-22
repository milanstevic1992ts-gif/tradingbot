# GE360 Trading — implementation roadmap

The LEAN upstream core stays authoritative. GE360 features live in separate projects and adapters.

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

1. **Core contracts**
   - Strategy interface emits only `SignalIntent`.
   - Immutable risk limits and portfolio snapshots.
   - Explicit trading states.
   - No LEAN core modification.

2. **Fail-closed pre-trade risk gate**
   - Halt/reducing-state checks.
   - stale signal and stale market checks.
   - daily-loss and drawdown circuit breakers.
   - max positions, exposure, notional and spread limits.
   - mandatory stop validation.
   - deterministic position sizing.

3. **Protection engine**
   - consecutive-loss guard.
   - cooldown per symbol/strategy.
   - rejection-storm circuit breaker.
   - persistent halt reason and manual reset boundary.

4. **LEAN adapter**
   - Convert GE360 approved intents into LEAN targets/orders.
   - Preserve LEAN buying-power, fee, slippage and brokerage checks.
   - Explicitly prevent adapter bypass.

5. **Execution safeguards**
   - volume participation limit.
   - spread/slippage gate.
   - duplicate-order guard.
   - order-rate throttling.
   - reduce-only behavior while de-risking.

6. **Strategy V1**
   - liquid-universe filter.
   - relative-volume filter.
   - VWAP/trend confirmation.
   - opening-range/momentum breakout.
   - confidence-scored signals only.

7. **Research and validation**
   - unit tests for every risk rejection.
   - historical backtests with fees/slippage.
   - out-of-sample and walk-forward validation.
   - paper trading before any live brokerage mode.

8. **Recovery/reconciliation**
   - compare local positions/orders with broker state at startup.
   - block new entries on mismatch.
   - allow risk-reducing actions.

9. **Observability**
   - journal every signal, rejection, sizing decision and order.
   - dashboard trading state, exposure, daily P&L, drawdown and halt reason.

## Safety invariant

No AI, strategy, dashboard or external API may weaken or bypass hard risk limits at runtime.
