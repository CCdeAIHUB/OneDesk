using System.Collections.Concurrent;
using System.Text.Json;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop.Services;

public sealed class PermissionService
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _grants = new();
    private readonly CapabilityDirectoryService _capabilities;
    private readonly string _grantStorePath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PermissionService(CapabilityDirectoryService capabilities, OneDeskDataPaths paths)
    {
        _capabilities = capabilities;
        paths.EnsureCreated();
        _grantStorePath = Path.Combine(paths.Root, "permission-grants.json");
        Load();
    }

    public bool IsHighRisk(string capability)
    {
        return _capabilities.IsHighRisk(capability);
    }

    public void Grant(string sourceKey, string capability)
    {
        capability = CapabilityDirectoryService.NormalizeId(capability);
        var permissions = _grants.GetOrAdd(sourceKey, _ => []);
        lock (permissions)
        {
            permissions.Add(capability);
        }
        Save();
    }

    public void Revoke(string sourceKey, string capability)
    {
        capability = CapabilityDirectoryService.NormalizeId(capability);
        if (!_grants.TryGetValue(sourceKey, out var permissions))
        {
            return;
        }

        lock (permissions)
        {
            permissions.Remove(capability);
        }
        Save();
    }

    public IReadOnlyList<PermissionGrantSnapshot> ListGrants()
    {
        return _grants
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair =>
            {
                lock (pair.Value)
                {
                    return new PermissionGrantSnapshot(pair.Key, pair.Value.Order(StringComparer.OrdinalIgnoreCase).ToArray());
                }
            })
            .ToArray();
    }

    public bool IsGranted(TrustedSource source, string capability)
    {
        if (source.Kind == "system")
        {
            return true;
        }

        capability = CapabilityDirectoryService.NormalizeId(capability);
        var key = SourceKey(source);
        if (!_grants.TryGetValue(key, out var permissions))
        {
            return false;
        }

        lock (permissions)
        {
            var category = capability.Split('.')[0];
            return permissions.Contains(capability) || permissions.Contains($"{category}.*");
        }
    }

    public static string SourceKey(TrustedSource source)
    {
        return source.Kind switch
        {
            "component" => $"component:{source.ComponentId}",
            "plugin" => $"plugin:{source.PluginId}",
            "system" => "system",
            _ => "unknown"
        };
    }

    private void Load()
    {
        if (!File.Exists(_grantStorePath))
        {
            return;
        }

        try
        {
            PermissionGrantStore? store;
            using (var stream = File.OpenRead(_grantStorePath))
            {
                store = JsonSerializer.Deserialize<PermissionGrantStore>(stream, _jsonOptions);
            }

            var migrated = false;
            foreach (var grant in store?.Grants ?? [])
            {
                var capabilities = grant.Capabilities
                    .Select(CapabilityDirectoryService.NormalizeId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                migrated |= !capabilities.SetEquals(grant.Capabilities);
                _grants[grant.SourceKey] = capabilities;
            }

            if (migrated)
            {
                // 旧权限 ID 只保留读取兼容；加载成功后立即回写标准 ID，避免长期维护双重语义。
                Save();
            }
        }
        catch
        {
            _grants.Clear();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_grantStorePath) ?? ".");
        var store = new PermissionGrantStore(ListGrants());
        using var stream = File.Create(_grantStorePath);
        JsonSerializer.Serialize(stream, store, _jsonOptions);
    }
}

public sealed record PermissionGrantSnapshot(string SourceKey, IReadOnlyList<string> Capabilities);

public sealed record PermissionGrantStore(IReadOnlyList<PermissionGrantSnapshot> Grants);
