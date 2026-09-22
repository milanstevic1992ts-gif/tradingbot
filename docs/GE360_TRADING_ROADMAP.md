# GE360 Trading — implementation roadmap

The LEAN upstream core stays authoritative. GE360 features live in separate projects and adapters.

## Current engineering phase

**Phase 8 — Recovery/reconciliation (IN PROGRESS).**

Phase 7 validation remains open: adequate continuous history and 20 qualifying forward-paper sessions are still required before any real-broker live activation. Phase-8 engineering was started by explicit operator instruction; this does not bypass the phase-7 live-activation gate.

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
   - [x] real-broker live submission disabled by default.
   - [x] provider-neutral external minute CSV loader.
   - [x] automatic dataset continuity/depth/duplicate quality assessment.
   - [x] optional authenticated Alpaca historical 1-minute downloader feeding the neutral CSV pipeline.
   - [x] authoritative forward-paper observation store and phase-7 gate.
   - [x] automatic forward-paper recorder wired to actual LEAN fills and portfolio state.
   - [x] runtime policy separates LEAN `PaperBrokerage` from every real brokerage.
   - [x] official Alpaca plugin prepared as a data-only feed for local LEAN paper observation.
   - [x] local Debian launcher prepared: Alpaca live data -> GE360 -> LEAN PaperBrokerage.
   - [ ] load an adequate continuous minute-history dataset.
   - [ ] run OOS + walk-forward on that adequate dataset.
   - [ ] reach the configurable count gate on an adequate dataset (default: 20 sessions AND 30 closed trades).
   - [ ] supply valid data credentials and start the actual forward-paper observation.
   - [ ] complete 20 qualifying forward-paper sessions.

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

### Forward-paper runtime

The supported local paper path is:

```text
AlpacaBrokerage (market data only)
        ->
GE360 Strategy/Risk/ExecutionGuard
        ->
LEAN PaperBrokerage (simulated orders/fills)
```

A separate runtime policy verifies that the effective LEAN brokerage is exactly `PaperBrokerage` before paper submission is allowed. The paper flag cannot authorize a real brokerage.

The forward-paper recorder derives observations from LEAN order-fill events and portfolio state. Late start, early shutdown, non-flat end state, invalid orders and fill/portfolio mismatches disqualify the session.

See `docs/GE360_RESEARCH_DATA.md` and `docs/GE360_FORWARD_PAPER.md`.

8. **Recovery/reconciliation — IN PROGRESS**
   - [x] persistent atomic recovery checkpoint.
   - [x] persist expected positions, open orders and stop/target metadata.
   - [x] compare checkpoint against LEAN broker-synchronized holdings/orders at startup.
   - [x] any startup open order forces fail-closed reducing mode and cancellation.
   - [x] quantity mismatch or missing/stale protection metadata forces reducing mode.
   - [x] matching recent protected position can restore the software protection monitor.
   - [x] broker already flat after mismatch performs a one-cycle baseline reset before entries resume.
   - [x] new-risk signals are blocked through existing `TradingState.Reducing` risk logic.
   - [x] reconciliation exits use the normal `SignalIntent Flat -> Protection -> Risk -> ExecutionGuard -> LEAN` path.
   - [x] recovery checkpoint persistence failure blocks new risk.
   - [ ] pass GE360 CI + LEAN smoke with the integrated recovery layer.

9. **Observability — NOT STARTED**
   - journal every signal, rejection, sizing decision and order.
   - dashboard trading state, exposure, daily P&L, drawdown and halt reason.

## Phase-7 exit gate

Phase 7 is complete only when all of the following are true:

1. an adequate continuous minute-history dataset passes the automatic data-quality gate;
2. chronological out-of-sample results have been produced with explicit costs;
3. walk-forward windows have been executed without future leakage;
4. the count gate is reached on that adequate dataset;
5. the authoritative forward-paper store contains at least 20 qualifying sessions with zero structural failures and zero real-broker submission attempts;
6. real-broker live submission remains disabled during validation.

Passing this gate does **not** imply future profitability. Phase-8 engineering may exist, but the gate is still required before any real-broker live activation.

## Safety invariant

No AI, strategy, dashboard or external API may weaken or bypass hard risk limits at runtime.
