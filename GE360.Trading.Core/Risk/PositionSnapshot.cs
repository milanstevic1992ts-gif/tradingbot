namespace GE360.Trading.Risk;

public sealed record PositionSnapshot(
    string Symbol,
    decimal Quantity,
    decimal AveragePrice,
    decimal MarketPrice)
{
    public decimal Notional => Math.Abs(Quantity * MarketPrice);
    public bool IsLong => Quantity > 0m;
    public bool IsShort => Quantity < 0m;
}
