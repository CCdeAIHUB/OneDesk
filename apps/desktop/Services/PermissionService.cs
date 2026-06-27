using System.Collections.Concurrent;

namespace OneDesk.Desktop.Services;

public sealed class PermissionService
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _grants = new();
    private readonly CapabilityDirectoryService _capabilities;

    public PermissionService(CapabilityDirectoryService capabilities)
    {
        _capabilities = capabilities;
    }

    public bool IsHighRisk(string capability)
    {
        return _capabilities.IsHighRisk(capability);
    }

    public void Grant(string sourceKey, string capability)
    {
        var permissions = _grants.GetOrAdd(sourceKey, _ => []);
        lock (permissions)
        {
            permissions.Add(capability);
        }
    }

    public void Revoke(string sourceKey, string capability)
    {
        if (!_grants.TryGetValue(sourceKey, out var permissions))
        {
            return;
        }

        lock (permissions)
        {
            permissions.Remove(capability);
        }
    }

    public bool IsGranted(TrustedSource source, string capability)
    {
        if (source.Kind == "system")
        {
            return true;
        }

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
}
