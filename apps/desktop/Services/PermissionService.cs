using System.Collections.Concurrent;

namespace OneDesk.Desktop.Services;

public sealed class PermissionService
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _grants = new();

    public bool IsHighRisk(string capability)
    {
        return capability is
            "file.writeExternal" or
            "file.deleteExternal" or
            "process.control" or
            "memory.read" or
            "memory.write" or
            "input.keyboardMouseSimulation" or
            "network.access" or
            "clipboard.read" or
            "clipboard.write" or
            "camera.access" or
            "microphone.access" or
            "screen.capture" or
            "screen.record" or
            "background.persistent" or
            "credential.access" or
            "shell.execute" or
            "crossDevice.sensitiveJsApi";
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
