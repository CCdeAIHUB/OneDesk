using System.Collections.Concurrent;
using System.Text.Json;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop.Services;

public sealed class StructuredLogStore
{
    private readonly ConcurrentQueue<StructuredLogRecord> _records = new();
    private readonly OneDeskDataPaths _paths;
    private readonly object _fileLock = new();

    public StructuredLogStore(OneDeskDataPaths paths)
    {
        _paths = paths;
        _paths.EnsureCreated();
        LoadRecentFromDisk();
    }

    public void Append(string sourceDeviceId, string level, string category, string message, IReadOnlyDictionary<string, object?>? context = null)
    {
        var record = new StructuredLogRecord(
            $"log-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            sourceDeviceId,
            level,
            category,
            message,
            context ?? new Dictionary<string, object?>());
        Enqueue(record);
        AppendToDisk(record);
    }

    public void ImportDisconnectedMobileLogs(IEnumerable<StructuredLogRecord> records)
    {
        foreach (var record in records)
        {
            Enqueue(record);
            AppendToDisk(record);
        }
    }

    public IReadOnlyCollection<StructuredLogRecord> Recent(int count = 200)
    {
        return _records.Reverse().Take(count).ToArray();
    }

    private void Enqueue(StructuredLogRecord record)
    {
        _records.Enqueue(record);
        while (_records.Count > 1000 && _records.TryDequeue(out _))
        {
        }
    }

    private void AppendToDisk(StructuredLogRecord record)
    {
        var path = CurrentLogPath();
        var json = JsonSerializer.Serialize(record, JsonOptions);
        lock (_fileLock)
        {
            File.AppendAllText(path, json + Environment.NewLine);
        }
    }

    private void LoadRecentFromDisk()
    {
        var path = CurrentLogPath();
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadLines(path).TakeLast(300))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize<StructuredLogRecord>(line, JsonOptions);
                if (record is not null)
                {
                    Enqueue(record);
                }
            }
            catch (JsonException)
            {
                // 忽略中断写入留下的半行 JSONL，保证日志读取不影响启动。
            }
        }
    }

    private string CurrentLogPath()
    {
        Directory.CreateDirectory(_paths.Logs);
        return Path.Combine(_paths.Logs, $"{DateTimeOffset.UtcNow:yyyy-MM-dd}.jsonl");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
