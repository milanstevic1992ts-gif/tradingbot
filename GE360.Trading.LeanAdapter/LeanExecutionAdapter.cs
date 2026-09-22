using GE360.Trading.Domain;
using GE360.Trading.Execution;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Configuration;

namespace GE360.Trading.LeanAdapter;

/// <summary>
/// Thin boundary into LEAN. It accepts only ApprovedOrderIntent.
/// Live submission is disabled by default until protective execution safeguards are enabled.
/// </summary>
public sealed class LeanExecutionAdapter
{
    private readonly QCAlgorithm _algorithm;
    private readonly LeanExecutionOptions _options;
    private readonly ExecutionGuard _executionGuard;

    public LeanExecutionAdapter(
        QCAlgorithm algorithm,
        LeanExecutionOptions? options = null,
        ExecutionGuard? executionGuard = null)
    {
        _algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
        _options = options ?? new LeanExecutionOptions();
        _executionGuard = executionGuard ??
            new ExecutionGuard(ExecutionGuardConfig.ConservativePaperDefaults);
    }

    public LeanExecutionResult Submit(ApprovedOrderIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var runtimePolicy = LeanRuntimeSubmissionPolicy.Evaluate(
            _algorithm.LiveMode,
            Config.Get("live-mode-brokerage"),
            _options);

        if (!runtimePolicy.Allowed)
        {
            return LeanExecutionResult.Reject(
                runtimePolicy.Code,
                runtimePolicy.Reason);
        }

        if (!SymbolCache.TryGetSymbol(intent.Symbol, out var symbol))
        {
            symbol = _algorithm.Securities.Keys.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Value,
                    intent.Symbol,
                    StringComparison.OrdinalIgnoreCase));

            if (symbol is null)
            {
                return LeanExecutionResult.Reject(
                    "UNKNOWN_SYMBOL",
                    $"LEAN cannot resolve symbol '{intent.Symbol}'.");
            }
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

        var market = new MarketSnapshot(
            intent.Symbol,
            _algorithm.UtcTime,
            security.Price,
            security.BidPrice,
            security.AskPrice,
            security.Volume);

        var executionDecision = _executionGuard.Evaluate(
            intent,
            market,
            _algorithm.UtcTime);

        if (!executionDecision.Allowed)
        {
            return LeanExecutionResult.Reject(
                executionDecision.Code,
                executionDecision.Reason);
        }

        var quantity = intent.IsRiskReducing
            ? GetRiskReducingQuantity(security.Holdings.Quantity)
            : NormalizeQuantity(intent.Quantity, security.SymbolProperties.LotSize);

        if (quantity == 0m)
        {
            return LeanExecutionResult.Reject(
                intent.IsRiskReducing ? "NO_POSITION_TO_REDUCE" : "QUANTITY_BELOW_LOT_SIZE",
                intent.IsRiskReducing
                    ? "LEAN reports no current position to reduce."
                    : "Approved quantity is below the instrument lot size.");
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

            _executionGuard.RecordSubmission(intent, _algorithm.UtcTime);
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

    private static decimal GetRiskReducingQuantity(decimal currentHoldingsQuantity)
        => currentHoldingsQuantity == 0m
            ? 0m
            : -currentHoldingsQuantity;
}
