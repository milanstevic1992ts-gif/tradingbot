using GE360.Trading.Validation;
using QuantConnect.Orders;

namespace GE360.Trading.LeanAlgorithm;

public sealed class LeanForwardPaperRecorder
{
    public const string StorePathEnvironmentVariable = "GE360_FORWARD_PAPER_STORE";

    private static readonly TimeSpan LatestQualifyingStartLocal = new(9, 35, 0);
    private static readonly TimeSpan EarliestQualifyingEndLocal = new(15, 59, 0);

    private readonly string _storePath;
    private readonly Action<string>? _log;

    private ForwardPaperSessionAccumulator? _current;
    private DateTime _currentSessionDate;
    private DateTime _lastUtc;
    private DateTime _lastLocal;
    private decimal _lastEquity;
    private bool _lastInvested;
    private bool _skipCurrentSession;

    public LeanForwardPaperRecorder(
        string? storePath = null,
        Action<string>? log = null)
    {
        _storePath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(storePath)
                ? Path.Combine("ge360-state", "forward-paper.json")
                : storePath);
        _log = log;
    }

    public string StorePath => _storePath;
    public string? LastPersistenceError { get; private set; }

    public void ObserveBar(
        DateTime utcTime,
        DateTime exchangeLocalTime,
        decimal totalPortfolioValue,
        bool portfolioInvested)
    {
        if (utcTime.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("utcTime must be UTC.", nameof(utcTime));
        }

        var sessionDate = exchangeLocalTime.Date;

        if (_currentSessionDate != sessionDate)
        {
            FinalizePreviousSessionOnRollover();

            _currentSessionDate = sessionDate;
            _skipCurrentSession = ForwardPaperObservationStore
                .Load(_storePath)
                .ContainsSession(sessionDate);

            if (_skipCurrentSession)
            {
                _log?.Invoke(
                    $"GE360 paper observation already exists for {sessionDate:yyyy-MM-dd}; recorder will not overwrite it.");
                _current = null;
            }
            else
            {
                var startedLate =
                    exchangeLocalTime.TimeOfDay > LatestQualifyingStartLocal;

                _current = new ForwardPaperSessionAccumulator(
                    sessionDate,
                    utcTime,
                    totalPortfolioValue,
                    startedLate);

                if (startedLate)
                {
                    _log?.Invoke(
                        $"GE360 paper session {sessionDate:yyyy-MM-dd} started late and will not qualify.");
                }
            }
        }

        _lastUtc = utcTime;
        _lastLocal = exchangeLocalTime;
        _lastEquity = totalPortfolioValue;
        _lastInvested = portfolioInvested;

        if (_current is not null &&
            exchangeLocalTime.TimeOfDay >= EarliestQualifyingEndLocal)
        {
            FinalizeCurrentSession(
                utcTime,
                totalPortfolioValue,
                portfolioInvested,
                endedTooEarly: false,
                "automatic end-of-session observation");
        }
    }

    public void ObserveOrderEvent(OrderEvent orderEvent)
    {
        ArgumentNullException.ThrowIfNull(orderEvent);

        if (_current is null)
        {
            return;
        }

        if (orderEvent.Status == OrderStatus.Invalid)
        {
            _current.RecordStructuralFailure();
            _log?.Invoke(
                $"GE360 paper structural failure: order {orderEvent.OrderId} became invalid.");
        }

        if (orderEvent.FillQuantity != 0m &&
            orderEvent.Status is OrderStatus.PartiallyFilled or OrderStatus.Filled)
        {
            _current.RecordFill(orderEvent.FillQuantity);
        }
    }

    public void RecordStructuralFailure(string reason)
    {
        if (_current is null)
        {
            return;
        }

        _current.RecordStructuralFailure();
        _log?.Invoke($"GE360 paper structural failure: {reason}");
    }

    public void FinalizeAtShutdown(
        DateTime utcTime,
        DateTime exchangeLocalTime,
        decimal totalPortfolioValue,
        bool portfolioInvested)
    {
        if (_current is null)
        {
            return;
        }

        var endedTooEarly =
            exchangeLocalTime.Date != _current.SessionDate ||
            exchangeLocalTime.TimeOfDay < EarliestQualifyingEndLocal;

        FinalizeCurrentSession(
            utcTime,
            totalPortfolioValue,
            portfolioInvested,
            endedTooEarly,
            "algorithm shutdown");
    }

    private void FinalizePreviousSessionOnRollover()
    {
        if (_current is null || _lastUtc == default)
        {
            return;
        }

        var endedTooEarly =
            _lastLocal.TimeOfDay < EarliestQualifyingEndLocal;

        FinalizeCurrentSession(
            _lastUtc,
            _lastEquity,
            _lastInvested,
            endedTooEarly,
            "session rollover");
    }

    private void FinalizeCurrentSession(
        DateTime endedAtUtc,
        decimal endingEquity,
        bool portfolioInvested,
        bool endedTooEarly,
        string note)
    {
        if (_current is null)
        {
            return;
        }

        var session = _current.Complete(
            endedAtUtc,
            endingEquity,
            portfolioFlat: !portfolioInvested,
            endedTooEarly,
            liveSubmissionAttemptCount: 0,
            note);

        try
        {
            var store = ForwardPaperObservationStore.Load(_storePath);

            if (!store.ContainsSession(session.SessionDate))
            {
                store.Add(session);
                store.Save(_storePath);
                _log?.Invoke(
                    $"GE360 paper session saved: {session.SessionDate:yyyy-MM-dd}, qualifies={session.Qualifies}, trades={session.ClosedTrades}, pnl={session.NetPnl:F2}.");
            }

            LastPersistenceError = null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LastPersistenceError = exception.Message;
            _log?.Invoke(
                $"GE360 paper observation persistence failed: {exception.GetType().Name}.");
        }
        finally
        {
            _current = null;
        }
    }
}
