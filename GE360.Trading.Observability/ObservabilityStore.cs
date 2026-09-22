using System.Text.Json;
using System.Text.Json.Serialization;

namespace GE360.Trading.Observability;

public sealed class ObservabilityStore
{
    private readonly object _sync = new();
    private readonly string _rootDirectory;
    private readonly string _statusPath;
    private readonly string _recentEventsPath;
    private readonly Queue<ObservabilityEvent> _recentEvents = new();
    private readonly int _recentEventLimit;

    public ObservabilityStore(
        string rootDirectory,
        int recentEventLimit = 100)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException(
                "Observability root directory is required.",
                nameof(rootDirectory));
        }

        if (recentEventLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recentEventLimit));
        }

        _rootDirectory = Path.GetFullPath(rootDirectory);
        _statusPath = Path.Combine(
            _rootDirectory,
            "runtime-status.json");
        _recentEventsPath = Path.Combine(
            _rootDirectory,
            "recent-events.json");
        _recentEventLimit = recentEventLimit;
    }

    public string RootDirectory => _rootDirectory;
    public string StatusPath => _statusPath;
    public string RecentEventsPath => _recentEventsPath;

    public string JournalPath(DateTime utcTime)
    {
        var utc = utcTime.Kind == DateTimeKind.Utc
            ? utcTime
            : utcTime.ToUniversalTime();

        return Path.Combine(
            _rootDirectory,
            $"events-{utc:yyyy-MM-dd}.jsonl");
    }

    public void Append(ObservabilityEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_sync)
        {
            Directory.CreateDirectory(_rootDirectory);

            var line = JsonSerializer.Serialize(
                entry,
                JsonOptions(writeIndented: false));

            using (var writer = new StreamWriter(
                       JournalPath(entry.UtcTime),
                       append: true))
            {
                writer.WriteLine(line);
                writer.Flush();
            }

            _recentEvents.Enqueue(entry);
            while (_recentEvents.Count > _recentEventLimit)
            {
                _recentEvents.Dequeue();
            }

            WriteAtomic(
                _recentEventsPath,
                JsonSerializer.Serialize(
                    _recentEvents.ToArray(),
                    JsonOptions(writeIndented: true)));
        }
    }

    public void WriteSnapshot(
        RuntimeObservabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_sync)
        {
            Directory.CreateDirectory(_rootDirectory);
            WriteAtomic(
                _statusPath,
                JsonSerializer.Serialize(
                    snapshot,
                    JsonOptions(writeIndented: true)));
        }
    }

    public RuntimeObservabilitySnapshot? LoadSnapshot()
    {
        lock (_sync)
        {
            if (!File.Exists(_statusPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<RuntimeObservabilitySnapshot>(
                File.ReadAllText(_statusPath),
                JsonOptions(writeIndented: false));
        }
    }

    public IReadOnlyList<ObservabilityEvent> LoadRecentEvents()
    {
        lock (_sync)
        {
            if (!File.Exists(_recentEventsPath))
            {
                return Array.Empty<ObservabilityEvent>();
            }

            return JsonSerializer.Deserialize<ObservabilityEvent[]>(
                       File.ReadAllText(_recentEventsPath),
                       JsonOptions(writeIndented: false))
                   ?? Array.Empty<ObservabilityEvent>();
        }
    }

    private static void WriteAtomic(
        string path,
        string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    private static JsonSerializerOptions JsonOptions(
        bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        options.Converters.Add(
            new JsonStringEnumConverter());

        return options;
    }
}
