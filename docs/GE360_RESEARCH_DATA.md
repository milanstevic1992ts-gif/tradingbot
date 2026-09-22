# GE360 Research Data

Phase 7 accepts provider-neutral minute data without coupling the strategy to a broker or data vendor.

## CSV schema

Required header:

```csv
timestamp_utc,symbol,open,high,low,close,volume
```

Example:

```csv
2026-09-01T13:30:00Z,SPY,500.10,500.30,499.95,500.20,152340
```

Rules:

- one row per one-minute bar;
- `timestamp_utc` must contain UTC or an explicit offset;
- symbol is normalized to uppercase;
- OHLC prices must be positive;
- high must be greater than or equal to low;
- volume must be non-negative;
- duplicate `symbol + timestamp_utc` rows fail the default quality gate.

A directory may contain multiple CSV files. The loader recursively merges them and sorts by timestamp.

## Automatic phase-7 data-quality gate

The default engineering checks are intentionally independent from strategy profitability:

- at least 20 distinct calendar sessions;
- weekday coverage ratio of at least 75% over the dataset span;
- median of at least 300 minute bars per symbol/session;
- zero duplicate symbol/timestamp bars.

The weekday coverage ratio is only an engineering continuity check. It does not model exchange holidays exactly and it is not a statistical significance test.

Even when the dataset passes this gate, live trading remains disabled until chronological OOS, walk-forward and forward paper observation are completed.

## Run

```bash
dotnet run \
  --project GE360.Trading.Research/GE360.Trading.Research.csproj \
  --configuration Release \
  -- \
  --csv-dataset /path/to/minute-data \
  --output artifacts/ge360-external-research.json
```

The external-data report records the quality assessment, full sample, chronological in-sample/out-of-sample results and executed walk-forward windows.
