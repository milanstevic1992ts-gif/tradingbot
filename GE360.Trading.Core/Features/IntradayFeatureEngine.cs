using GE360.Trading.Domain;
using GE360.Trading.Strategies;

namespace GE360.Trading.Features;

/// <summary>
/// Stateful per-symbol feature calculator.
/// The caller supplies exchange-local timestamps so this layer stays timezone agnostic.
/// </summary>
public sealed class IntradayFeatureEngine
{
    private readonly string _symbol;
    private readonly IntradayFeatureConfig _config;
    private readonly Queue<decimal> _trueRanges = new();
    private readonly Queue<decimal> _priorVolumes = new();

    private DateTime? _sessionDate;
    private decimal? _openingRangeHigh;
    private decimal? _openingRangeLow;
    private decimal _cumulativePriceVolume;
    private decimal _cumulativeVolume;
    private decimal? _previousClose;

    public IntradayFeatureEngine(
        string symbol,
        IntradayFeatureConfig? config = null)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        }

        _symbol = symbol.Trim().ToUpperInvariant();
        _config = config ?? IntradayFeatureConfig.UsEquityDefaults;
        ValidateConfig(_config);
    }

    public IntradayFeatureOutput? Update(IntradayBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);

        if (!string.Equals(bar.Symbol, _symbol, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Bar symbol does not match feature engine symbol.", nameof(bar));
        }

        ValidateBar(bar);

        if (_sessionDate != bar.ExchangeLocalTime.Date)
        {
            ResetSession(bar.ExchangeLocalTime.Date);
        }

        var localTime = bar.ExchangeLocalTime.TimeOfDay;
        if (localTime < _config.SessionOpenLocalTime)
        {
            return null;
        }

        var openingRangeEnd = _config.SessionOpenLocalTime + _config.OpeningRangeDuration;
        var openingRangeComplete = localTime >= openingRangeEnd;

        if (!openingRangeComplete)
        {
            _openingRangeHigh = _openingRangeHigh.HasValue
                ? Math.Max(_openingRangeHigh.Value, bar.High)
                : bar.High;

            _openingRangeLow = _openingRangeLow.HasValue
                ? Math.Min(_openingRangeLow.Value, bar.Low)
                : bar.Low;
        }

        var typicalPrice = (bar.High + bar.Low + bar.Close) / 3m;
        _cumulativePriceVolume += typicalPrice * bar.Volume;
        _cumulativeVolume += bar.Volume;

        var vwap = _cumulativeVolume > 0m
            ? _cumulativePriceVolume / _cumulativeVolume
            : bar.Close;

        var relativeVolume = ComputeRelativeVolume(bar.Volume);
        var atr = ComputeAtr(bar);

        _previousClose = bar.Close;
        AddPriorVolume(bar.Volume);

        var market = new MarketSnapshot(
            _symbol,
            bar.UtcTime,
            bar.Close,
            bar.Close,
            bar.Close,
            bar.Volume,
            atr);

        var features = new StrategyFeatures(
            _symbol,
            vwap,
            _openingRangeHigh ?? 0m,
            _openingRangeLow ?? 0m,
            relativeVolume,
            openingRangeComplete && _openingRangeHigh.HasValue && _openingRangeLow.HasValue);

        return new IntradayFeatureOutput(market, features);
    }

    private decimal ComputeRelativeVolume(decimal currentVolume)
    {
        if (_priorVolumes.Count == 0)
        {
            return 0m;
        }

        var average = _priorVolumes.Average();
        return average > 0m ? currentVolume / average : 0m;
    }

    private decimal? ComputeAtr(IntradayBar bar)
    {
        var trueRange = bar.High - bar.Low;
        if (_previousClose.HasValue)
        {
            trueRange = Math.Max(
                trueRange,
                Math.Max(
                    Math.Abs(bar.High - _previousClose.Value),
                    Math.Abs(bar.Low - _previousClose.Value)));
        }

        _trueRanges.Enqueue(trueRange);
        while (_trueRanges.Count > _config.AtrPeriod)
        {
            _trueRanges.Dequeue();
        }

        return _trueRanges.Count == _config.AtrPeriod
            ? _trueRanges.Average()
            : null;
    }

    private void AddPriorVolume(decimal volume)
    {
        _priorVolumes.Enqueue(volume);
        while (_priorVolumes.Count > _config.RelativeVolumeLookback)
        {
            _priorVolumes.Dequeue();
        }
    }

    private void ResetSession(DateTime sessionDate)
    {
        _sessionDate = sessionDate;
        _openingRangeHigh = null;
        _openingRangeLow = null;
        _cumulativePriceVolume = 0m;
        _cumulativeVolume = 0m;
        _previousClose = null;
        _trueRanges.Clear();
        _priorVolumes.Clear();
    }

    private static void ValidateBar(IntradayBar bar)
    {
        if (bar.High < bar.Low ||
            bar.Open <= 0m ||
            bar.High <= 0m ||
            bar.Low <= 0m ||
            bar.Close <= 0m ||
            bar.Volume < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(bar), "Intraday bar contains invalid OHLCV values.");
        }
    }

    private static void ValidateConfig(IntradayFeatureConfig config)
    {
        if (config.SessionOpenLocalTime < TimeSpan.Zero ||
            config.SessionOpenLocalTime >= TimeSpan.FromDays(1) ||
            config.OpeningRangeDuration <= TimeSpan.Zero ||
            config.AtrPeriod <= 0 ||
            config.RelativeVolumeLookback <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "Feature configuration contains invalid values.");
        }
    }
}
