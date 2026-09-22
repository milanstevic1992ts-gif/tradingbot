namespace GE360.Trading.Features;

public sealed record IntradayBar(
    string Symbol,
    DateTime UtcTime,
    DateTime ExchangeLocalTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);
