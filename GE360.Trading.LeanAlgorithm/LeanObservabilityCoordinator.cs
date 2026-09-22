using System.Globalization;
using GE360.Trading.Domain;
using GE360.Trading.Execution;
using GE360.Trading.LeanAdapter;
using GE360.Trading.Observability;
using GE360.Trading.Protection;
using GE360.Trading.Recovery;
using GE360.Trading.Risk;
using QuantConnect.Algorithm;
using QuantConnect.Orders;

namespace GE360.Trading.LeanAlgorithm;

public sealed class LeanObservabilityCoordinator
{
    public const string DirectoryEnvironmentVariable =
        "GE360_OBSERVABILITY_DIR";

    private readonly ObservabilityCoordinator _observability;

    public LeanObservabilityCoordinator(
        string? rootDirectory = null)
    {
        var resolved = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                "ge360-state",
                "observability")
            : rootDirectory;

        _observability =
            new ObservabilityCoordinator(resolved);
    }

    public string RootDirectory =>
        _observability.RootDirectory;

    public bool Healthy =>
        _observability.Healthy;

    public string LastError =>
        _observability.LastError;

    public void RecordRuntimeStarted(
        DateTime utcTime,
        bool liveMode,
        string brokerage)
    {
        Record(ObservabilityEvent.Create(
            utcTime,
            ObservabilityEventKind.RuntimeStarted,
            ObservabilitySeverity.Info,
            "RUNTIME_STARTED",
            "GE360 trading runtime started.",
            data: new Dictionary<string, string>
            {
                ["liveMode"] = liveMode.ToString(),
                ["brokerage"] = brokerage ?? string.Empty
            }));
    }

    public void RecordSignal(
        SignalIntent signal,
        PortfolioSnapshot portfolio,
        MarketSnapshot market)
    {
        Record(ObservabilityEvent.Create(
            portfolio.UtcTime,
            ObservabilityEventKind.SignalObserved,
            ObservabilitySeverity.Info,
            "SIGNAL_OBSERVED",
            signal.Reason,
            signal.Symbol,
            signal.StrategyId,
            signal.SignalId,
            new Dictionary<string, string>
            {
                ["direction"] = signal.Direction.ToString(),
                ["confidence"] = F(signal.Confidence),
                ["referencePrice"] = F(signal.ReferencePrice),
                ["marketPrice"] = F(market.LastPrice),
                ["tradingState"] = portfolio.TradingState.ToString(),
                ["grossExposure"] = F(portfolio.GrossExposure)
            }));
    }

    public void RecordApproval(
        SignalIntent signal,
        TradeApprovalResult approval)
    {
        if (approval.Approved &&
            approval.Order is not null)
        {
            Record(ObservabilityEvent.Create(
                approval.Order.ApprovedAtUtc,
                ObservabilityEventKind.RiskApproved,
                ObservabilitySeverity.Info,
                approval.Code,
                approval.Reason,
                signal.Symbol,
                signal.StrategyId,
                signal.SignalId,
                new Dictionary<string, string>
                {
                    ["approvedQuantity"] =
                        F(approval.Order.Quantity),
                    ["isRiskReducing"] =
                        approval.Order.IsRiskReducing.ToString(),
                    ["stopPrice"] =
                        N(approval.Order.StopPrice),
                    ["takeProfitPrice"] =
                        N(approval.Order.TakeProfitPrice)
                }));

            return;
        }

        Record(ObservabilityEvent.Create(
            signal.SignalTimeUtc,
            ObservabilityEventKind.RiskRejected,
            ObservabilitySeverity.Warning,
            approval.Code,
            approval.Reason,
            signal.Symbol,
            signal.StrategyId,
            signal.SignalId));
    }

    public void RecordExecution(
        DateTime utcTime,
        SignalIntent signal,
        TradeApprovalResult approval,
        LeanExecutionResult execution)
    {
        var data = new Dictionary<string, string>
        {
            ["submitted"] =
                execution.Submitted.ToString()
        };

        if (approval.Order is not null)
        {
            data["approvedQuantity"] =
                F(approval.Order.Quantity);
            data["isRiskReducing"] =
                approval.Order.IsRiskReducing.ToString();
        }

        if (execution.LeanOrderId.HasValue)
        {
            data["leanOrderId"] =
                execution.LeanOrderId.Value
                    .ToString(CultureInfo.InvariantCulture);
        }

        Record(ObservabilityEvent.Create(
            utcTime,
            execution.Submitted
                ? ObservabilityEventKind.ExecutionSubmitted
                : ObservabilityEventKind.ExecutionRejected,
            execution.Submitted
                ? ObservabilitySeverity.Info
                : ObservabilitySeverity.Error,
            execution.Code,
            execution.Reason,
            signal.Symbol,
            signal.StrategyId,
            signal.SignalId,
            data));
    }

    public void RecordOrderEvent(
        DateTime utcTime,
        OrderEvent orderEvent)
    {
        Record(ObservabilityEvent.Create(
            utcTime,
            ObservabilityEventKind.OrderEvent,
            orderEvent.Status is
                OrderStatus.Invalid or
                OrderStatus.Canceled
                ? ObservabilitySeverity.Warning
                : ObservabilitySeverity.Info,
            $"ORDER_{orderEvent.Status.ToString().ToUpperInvariant()}",
            orderEvent.Message ?? string.Empty,
            orderEvent.Symbol.Value,
            data: new Dictionary<string, string>
            {
                ["orderId"] =
                    orderEvent.OrderId
                        .ToString(CultureInfo.InvariantCulture),
                ["status"] =
                    orderEvent.Status.ToString(),
                ["fillQuantity"] =
                    F(orderEvent.FillQuantity),
                ["fillPrice"] =
                    F(orderEvent.FillPrice),
                ["orderFee"] =
                    orderEvent.OrderFee.Value.Amount
                        .ToString(CultureInfo.InvariantCulture)
            }));
    }

    public void RecordRecovery(
        DateTime utcTime,
        RecoveryReconciliationResult result)
    {
        Record(ObservabilityEvent.Create(
            utcTime,
            ObservabilityEventKind.RecoveryState,
            result.Mode == RecoveryMode.Synchronized
                ? ObservabilitySeverity.Info
                : ObservabilitySeverity.Warning,
            $"RECOVERY_{result.Mode.ToString().ToUpperInvariant()}",
            string.Join(" | ", result.Reasons),
            data: new Dictionary<string, string>
            {
                ["tradingState"] =
                    result.TradingState.ToString(),
                ["allowNewEntries"] =
                    result.AllowNewEntries.ToString(),
                ["cancelOpenOrders"] =
                    result.RequiresCancelOpenOrders.ToString(),
                ["symbolsToFlatten"] =
                    string.Join(
                        ",",
                        result.SymbolsToFlatten)
            }));
    }

    public void RecordProtectionState(
        DateTime utcTime,
        ProtectionEngine protection)
    {
        ArgumentNullException.ThrowIfNull(protection);

        if (!protection.IsHalted)
        {
            return;
        }

        Record(ObservabilityEvent.Create(
            utcTime,
            ObservabilityEventKind.ProtectionState,
            ObservabilitySeverity.Critical,
            "PROTECTION_HALTED",
            protection.HaltReason));
    }

    public void RecordRuntimeStopped(
        DateTime utcTime,
        bool invested)
    {
        Record(ObservabilityEvent.Create(
            utcTime,
            ObservabilityEventKind.RuntimeStopped,
            invested
                ? ObservabilitySeverity.Warning
                : ObservabilitySeverity.Info,
            "RUNTIME_STOPPED",
            invested
                ? "GE360 runtime stopped with exposure."
                : "GE360 runtime stopped flat.",
            data: new Dictionary<string, string>
            {
                ["invested"] =
                    invested.ToString()
            }));
    }

    public void WriteSnapshot(
        QCAlgorithm algorithm,
        PortfolioSnapshot portfolio,
        ProtectionEngine protection,
        LeanRecoveryCoordinator? recovery)
    {
        ArgumentNullException.ThrowIfNull(algorithm);
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(protection);

        var positions = portfolio.Positions.Values
            .OrderBy(
                x => x.Symbol,
                StringComparer.OrdinalIgnoreCase)
            .Select(x => new RuntimePositionView(
                x.Symbol,
                x.Quantity,
                x.AveragePrice,
                x.MarketPrice,
                x.Notional))
            .ToArray();

        var recoveryMode =
            recovery?.LastResult?.Mode.ToString()
            ?? (recovery is null
                ? "Disabled"
                : "Pending");

        var recoveryDetail =
            recovery?.LastResult is null
                ? recovery?.LastError ?? string.Empty
                : string.Join(
                    " | ",
                    recovery.LastResult.Reasons);

        var snapshot =
            new RuntimeObservabilitySnapshot(
                portfolio.UtcTime,
                portfolio.TradingState.ToString(),
                portfolio.Equity,
                portfolio.Cash,
                portfolio.GrossExposure,
                portfolio.Equity -
                    portfolio.DayStartEquity,
                portfolio.DailyLossPercent,
                portfolio.DrawdownPercent,
                portfolio.OpenPositionCount,
                algorithm.Transactions
                    .GetOpenOrders()
                    .Count,
                positions,
                protection.IsHalted,
                protection.HaltReason,
                recoveryMode,
                recovery?.PersistenceHealthy ?? true,
                recoveryDetail,
                _observability.JournalHealthy,
                _observability.JournalError,
                _observability.CurrentJournalPath(
                    portfolio.UtcTime),
                _observability.RecentEventsPath);

        _observability.TryWriteSnapshot(snapshot);
    }

    private void Record(
        ObservabilityEvent entry)
    {
        _observability.TryRecord(entry);
    }

    private static string F(decimal value)
        => value.ToString(
            CultureInfo.InvariantCulture);

    private static string N(decimal? value)
        => value.HasValue
            ? F(value.Value)
            : string.Empty;
}
