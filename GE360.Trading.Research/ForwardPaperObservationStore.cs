using System.Text.Json;

namespace GE360.Trading.Research;

public sealed class ForwardPaperObservationStore
{
    private const int CurrentSchemaVersion = 1;

    public ForwardPaperObservationStore(
        IReadOnlyCollection<ForwardPaperSession>? sessions = null)
    {
        Sessions = sessions?
            .OrderBy(x => x.SessionDate.Date)
            .ToList()
            ?? new List<ForwardPaperSession>();
    }

    public List<ForwardPaperSession> Sessions { get; }

    public void Add(ForwardPaperSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.SessionDate == default ||
            session.StartedAtUtc.Kind != DateTimeKind.Utc ||
            session.EndedAtUtc.Kind != DateTimeKind.Utc ||
            session.EndedAtUtc <= session.StartedAtUtc ||
            session.ClosedTrades < 0 ||
            session.StructuralFailureCount < 0 ||
            session.LiveSubmissionAttemptCount < 0)
        {
            throw new ArgumentException(
                "Forward-paper session contains invalid values.",
                nameof(session));
        }

        if (Sessions.Any(x => x.SessionDate.Date == session.SessionDate.Date))
        {
            throw new InvalidOperationException(
                $"Forward-paper session {session.SessionDate:yyyy-MM-dd} already exists.");
        }

        Sessions.Add(session);
        Sessions.Sort((left, right) =>
            left.SessionDate.Date.CompareTo(right.SessionDate.Date));
    }

    public ForwardPaperSummary Summarize(int minimumRequiredSessions = 20)
        => ForwardPaperSummary.Assess(
            Sessions,
            minimumRequiredSessions);

    public static ForwardPaperObservationStore Load(string path)
    {
        if (!File.Exists(path))
        {
            return new ForwardPaperObservationStore();
        }

        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<StoreDocument>(
            json,
            JsonOptions())
            ?? throw new InvalidDataException(
                "Unable to deserialize forward-paper observation store.");

        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported forward-paper schema version {document.SchemaVersion}.");
        }

        return new ForwardPaperObservationStore(
            document.Sessions ?? Array.Empty<ForwardPaperSession>());
    }

    public void Save(string path)
    {
        var absolute = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(absolute);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new StoreDocument(
            CurrentSchemaVersion,
            Sessions
                .OrderBy(x => x.SessionDate.Date)
                .ToArray());

        var json = JsonSerializer.Serialize(
            document,
            JsonOptions());

        var temp = absolute + ".tmp";
        File.WriteAllText(temp, json);

        if (File.Exists(absolute))
        {
            File.Move(temp, absolute, overwrite: true);
        }
        else
        {
            File.Move(temp, absolute);
        }
    }

    private static JsonSerializerOptions JsonOptions()
        => new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    private sealed record StoreDocument(
        int SchemaVersion,
        IReadOnlyList<ForwardPaperSession>? Sessions);
}
