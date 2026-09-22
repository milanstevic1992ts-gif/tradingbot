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

## Run an existing CSV dataset

```bash
dotnet run \
  --project GE360.Trading.Research/GE360.Trading.Research.csproj \
  --configuration Release \
  -- \
  --csv-dataset /path/to/minute-data \
  --output artifacts/ge360-external-research.json
```

The external-data report records the quality assessment, full sample, chronological in-sample/out-of-sample results and executed walk-forward windows.

## Optional Alpaca historical downloader

GE360 can optionally download historical US equity minute bars from Alpaca Market Data and immediately pass them through the same neutral CSV + quality + research pipeline.

Credentials are read only from environment variables:

```bash
export APCA_API_KEY_ID="..."
export APCA_API_SECRET_KEY="..."
```

Example:

```bash
dotnet run \
  --project GE360.Trading.Research/GE360.Trading.Research.csproj \
  --configuration Release \
  -- \
  --alpaca-download \
  --symbols SPY,QQQ,AAPL,MSFT,NVDA \
  --start 2026-06-01 \
  --end 2026-08-31 \
  --feed iex \
  --adjustment all \
  --output-data artifacts/ge360-alpaca-minute.csv \
  --output artifacts/ge360-alpaca-research.json
```

Important:

- no API key or secret is written to Git or to the generated CSV;
- historical requests use `1Min` bars;
- pagination is followed until `next_page_token` is empty;
- page size is capped at 10,000 bars;
- requests are paced conservatively between pages;
- the default feed is `iex`; use another Alpaca feed only if the account is entitled to it;
- downloaded data is still subject to the same dataset-quality gate and does not bypass OOS/walk-forward/forward-paper requirements.
