using System.Text.Json;

namespace GE360.Trading.Recovery;

public sealed class RecoveryCheckpointStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _path;

    public RecoveryCheckpointStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Recovery checkpoint path is required.",
                nameof(path));
        }

        _path = System.IO.Path.GetFullPath(path);
    }

    public string Path => _path;

    public RecoveryCheckpoint? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var document = JsonSerializer.Deserialize<StoreDocument>(
            File.ReadAllText(_path),
            JsonOptions())
            ?? throw new InvalidDataException(
                "Unable to deserialize GE360 recovery checkpoint.");

        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported recovery checkpoint schema version {document.SchemaVersion}.");
        }

        return document.Checkpoint
            ?? throw new InvalidDataException(
                "Recovery checkpoint document is missing checkpoint data.");
    }

    public void Save(RecoveryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new StoreDocument(
            CurrentSchemaVersion,
            checkpoint);

        var json = JsonSerializer.Serialize(
            document,
            JsonOptions());

        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _path, overwrite: true);
    }

    private static JsonSerializerOptions JsonOptions()
        => new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    private sealed record StoreDocument(
        int SchemaVersion,
        RecoveryCheckpoint? Checkpoint);
}
