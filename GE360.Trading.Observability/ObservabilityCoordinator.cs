namespace GE360.Trading.Observability;

/// <summary>
/// Read-only telemetry boundary. Observability failures are reported but never
/// modify trading decisions, risk limits or execution permissions.
/// </summary>
public sealed class ObservabilityCoordinator
{
    private readonly ObservabilityStore _store;

    public ObservabilityCoordinator(
        string rootDirectory,
        int recentEventLimit = 100)
    {
        _store = new ObservabilityStore(
            rootDirectory,
            recentEventLimit);
    }

    public string RootDirectory =>
        _store.RootDirectory;

    public bool JournalHealthy { get; private set; } =
        true;

    public string JournalError { get; private set; } =
        string.Empty;

    public bool SnapshotHealthy { get; private set; } =
        true;

    public string SnapshotError { get; private set; } =
        string.Empty;

    public bool Healthy =>
        JournalHealthy && SnapshotHealthy;

    public string LastError =>
        !string.IsNullOrWhiteSpace(JournalError)
            ? JournalError
            : SnapshotError;

    public string CurrentJournalPath(DateTime utcTime)
        => _store.JournalPath(utcTime);

    public string StatusPath =>
        _store.StatusPath;

    public string RecentEventsPath =>
        _store.RecentEventsPath;

    public bool TryRecord(ObservabilityEvent entry)
    {
        try
        {
            _store.Append(entry);
            JournalHealthy = true;
            JournalError = string.Empty;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            JournalHealthy = false;
            JournalError =
                $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    public bool TryWriteSnapshot(
        RuntimeObservabilitySnapshot snapshot)
    {
        try
        {
            _store.WriteSnapshot(snapshot);
            SnapshotHealthy = true;
            SnapshotError = string.Empty;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SnapshotHealthy = false;
            SnapshotError =
                $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }
}
