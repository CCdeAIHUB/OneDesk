using System.IO;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OneDesk.Desktop.Storage;

public sealed class JsonFileStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public JsonFileStore()
    {
        _jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public async Task SaveAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        var normalizedPath = Path.GetFullPath(path);
        var pathLock = PathLocks.GetOrAdd(normalizedPath, static _ => new SemaphoreSlim(1, 1));
        await pathLock.WaitAsync(cancellationToken);
        string? temp = null;
        try
        {
            var directory = Path.GetDirectoryName(normalizedPath) ?? ".";
            Directory.CreateDirectory(directory);
            temp = Path.Combine(directory, $".{Path.GetFileName(normalizedPath)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            // 同一路径的读写由路径锁串行化；临时文件写完后再原子替换，读取者不会看到半份 JSON。
            if (File.Exists(normalizedPath))
            {
                File.Replace(temp, normalizedPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, normalizedPath);
            }
            temp = null;
        }
        finally
        {
            if (temp is not null && File.Exists(temp))
            {
                File.Delete(temp);
            }
            pathLock.Release();
        }
    }

    public async Task<T?> LoadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = Path.GetFullPath(path);
        var pathLock = PathLocks.GetOrAdd(normalizedPath, static _ => new SemaphoreSlim(1, 1));
        await pathLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(normalizedPath))
            {
                return default;
            }

            await using var stream = new FileStream(normalizedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
        }
        finally
        {
            pathLock.Release();
        }
    }

    public async Task<IReadOnlyList<T>> LoadDirectoryAsync<T>(string directory, string pattern = "*.json", CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var results = new List<T>();
        foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
        {
            var value = await LoadAsync<T>(file, cancellationToken);
            if (value is not null)
            {
                results.Add(value);
            }
        }

        return results;
    }
}
