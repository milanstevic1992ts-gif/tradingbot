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
  -> execution guard
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
   - [x] shared-portfolio multi-symbol research runner.
   - [x] production `ExecutionGuard` enforced in portfolio research before simulated fills.
   - [x] explicit fee/slippage cost model and synthetic spread assumption.
   - [x] historical LEAN bundled-data smoke test.
   - [x] chronological in-sample/out-of-sample split without future leakage.
   - [x] walk-forward windows executed on the bundled engineering dataset.
   - [x] LEAN paper/backtest reality model with explicit deterministic fee/slippage assumptions.
   - [x] mandatory 15:55 New York no-new-risk / protected flatten boundary.
   - [x] live submission remains disabled by default.
   - [x] provider-neutral external minute CSV loader.
   - [x] automatic dataset continuity/depth/duplicate quality assessment.
   - [x] optional authenticated Alpaca historical 1-minute downloader feeding the neutral CSV pipeline.
   - [ ] load an adequate continuous minute-history dataset.
   - [ ] run OOS + walk-forward on that adequate dataset.
   - [ ] reach the configurable count gate on an adequate dataset (default: 20 sessions AND 30 closed trades).
   - [ ] complete a forward paper-trading observation period before any live brokerage mode.

### Current bundled-data evidence

The bundled LEAN equity dataset contains 41 minute trade archives, 10 symbols, 20 distinct calendar sessions and 41 symbol/session combinations.

The cross-symbol engineering run produced 108 closed trades. This exceeds the raw count gate, but the dataset is fragmented across 2008–2023 and therefore receives:

**`EngineeringSampleOnly` — NOT phase-7 validated.**

Observed bundled engineering metrics with the current deterministic cost assumptions:

- full sample: 108 trades, 28.70% win rate, net P&L about -$1,316.59, profit factor about 0.297;
- chronological out-of-sample: 15 trades, 13.33% win rate, net P&L about -$265.28, profit factor about 0.125;
- every executed walk-forward test window containing trades was net negative.

These values are diagnostic evidence that Strategy V1 must not be promoted to live trading. They must not be used as profitability claims or as a tuning target for overfitting.

### Dataset-quality gate

External CSV research data is automatically checked independently from strategy performance. Default engineering checks:

- at least 20 distinct calendar sessions;
- at least 75% weekday coverage over the dataset span;
- median of at least 300 minute bars per symbol/session;
- zero duplicate `symbol + timestamp` bars.

Optional historical providers must convert into the same neutral data model before research. Provider credentials must never be stored in source control.

See `docs/GE360_RESEARCH_DATA.md`.

8. **Recovery/reconciliation — NOT STARTED**
   - compare local positions/orders with broker state at startup.
   - block new entries on mismatch.
   - allow risk-reducing actions.

9. **Observability — NOT STARTED**
   - journal every signal, rejection, sizing decision and order.
   - dashboard trading state, exposure, daily P&L, drawdown and halt reason.

## Phase-7 exit gate

Phase 7 is complete only when all of the following are true:

1. an adequate continuous minute-history dataset passes the automatic data-quality gate;
2. chronological out-of-sample results have been produced with explicit costs;
3. walk-forward windows have been executed without future leakage;
4. the count gate is reached on that adequate dataset;
5. forward paper trading has been observed;
6. no live-broker submission is enabled during validation.

Passing this gate does **not** imply future profitability. It only permits engineering work to proceed to phase 8.

## Safety invariant

No AI, strategy, dashboard or external API may weaken or bypass hard risk limits at runtime.
