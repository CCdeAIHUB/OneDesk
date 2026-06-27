using System.Collections.Concurrent;

namespace OneDesk.Desktop.Services;

public sealed class StructuredLogStore
{
    private readonly ConcurrentQueue<StructuredLogRecord> _records = new();

    public void Append(string sourceDeviceId, string level, string category, string message, IReadOnlyDictionary<string, object?>? context = null)
    {
        _records.Enqueue(new StructuredLogRecord(
            $"log-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            sourceDeviceId,
            level,
            category,
            message,
            context ?? new Dictionary<string, object?>()));
    }

    public void ImportDisconnectedMobileLogs(IEnumerable<StructuredLogRecord> records)
    {
        foreach (var record in records)
        {
            _records.Enqueue(record);
        }
    }

    public IReadOnlyCollection<StructuredLogRecord> Recent(int count = 200)
    {
        return _records.Reverse().Take(count).ToArray();
    }
}
