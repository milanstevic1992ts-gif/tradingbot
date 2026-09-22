namespace GE360.Trading.Research;

public sealed record ResearchCostModel(
    decimal FeePerOrder,
    decimal SlippagePercent)
{
    public static ResearchCostModel DeterministicSmokeDefaults => new(
        FeePerOrder: 0.50m,
        SlippagePercent: 0.0005m);

    public decimal ApplyFillPrice(decimal referencePrice, decimal signedQuantity)
    {
        if (referencePrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(referencePrice));
        }

        if (SlippagePercent < 0m || SlippagePercent >= 1m || FeePerOrder < 0m)
        {
            throw new InvalidOperationException("Research cost model contains invalid values.");
        }

        if (signedQuantity > 0m)
        {
            return referencePrice * (1m + SlippagePercent);
        }

        if (signedQuantity < 0m)
        {
            return referencePrice * (1m - SlippagePercent);
        }

        return referencePrice;
    }
}
