using GE360.Trading.Execution;
using QuantConnect;
using QuantConnect.Algorithm;

namespace GE360.Trading.LeanAdapter;

/// <summary>
/// Thin boundary into LEAN. It accepts only ApprovedOrderIntent.
/// Live submission is disabled by default until the protective-order layer is implemented.
/// </summary>
public sealed class LeanExecutionAdapter
{
    private readonly QCAlgorithm _algorithm;
    private readonly LeanExecutionOptions _options;

    public LeanExecutionAdapter(
        QCAlgorithm algorithm,
        LeanExecutionOptions? options = null)
    {
        _algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
        _options = options ?? new LeanExecutionOptions();
    }

    public LeanExecutionResult Submit(ApprovedOrderIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (_algorithm.LiveMode && !_options.EnableLiveSubmission)
        {
            return LeanExecutionResult.Reject(
                "LIVE_SUBMISSION_DISABLED",
                "GE360 live order submission is disabled until protective execution safeguards are enabled.");
        }

        if (!SymbolCache.TryGetSymbol(intent.Symbol, out var symbol))
        {
            return LeanExecutionResult.Reject(
                "UNKNOWN_SYMBOL",
                $"LEAN cannot resolve symbol '{intent.Symbol}'.");
        }

        if (!_algorithm.Securities.ContainsKey(symbol))
        {
            return LeanExecutionResult.Reject(
                "SECURITY_NOT_SUBSCRIBED",
                $"Security '{intent.Symbol}' is not active in the LEAN algorithm.");
        }

        var security = _algorithm.Securities[symbol];

        if (_options.BlockWhenMarketClosed && !security.Exchange.ExchangeOpen)
        {
            return LeanExecutionResult.Reject(
                "MARKET_CLOSED",
                "GE360 blocks market orders while the exchange is closed.");
        }

        var quantity = NormalizeQuantity(intent.Quantity, security.SymbolProperties.LotSize);
        if (quantity == 0m)
        {
            return LeanExecutionResult.Reject(
                "QUANTITY_BELOW_LOT_SIZE",
                "Approved quantity is below the instrument lot size.");
        }

        try
        {
            var tag = $"GE360|{intent.StrategyId}|{intent.SignalId:N}";
            var ticket = _algorithm.MarketOrder(
                symbol,
                quantity,
                _options.Asynchronous,
                tag);

            if (!ticket.SubmitRequest.Response.IsSuccess)
            {
                return LeanExecutionResult.Reject(
                    "LEAN_ORDER_REJECTED",
                    "LEAN rejected the submitted order request.");
            }

            return LeanExecutionResult.Success(ticket.OrderId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return LeanExecutionResult.Reject(
                "LEAN_EXECUTION_ERROR",
                $"LEAN execution failed closed: {exception.GetType().Name}.");
        }
    }

    internal static decimal NormalizeQuantity(decimal quantity, decimal lotSize)
    {
        if (quantity == 0m || lotSize <= 0m)
        {
            return 0m;
        }

        var absoluteLots = Math.Truncate(Math.Abs(quantity) / lotSize);
        return Math.Sign(quantity) * absoluteLots * lotSize;
    }
}
