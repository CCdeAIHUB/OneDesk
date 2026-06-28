using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop.Services;

public sealed class DeviceRegistry
{
    private readonly ConcurrentDictionary<string, DeviceIdentity> _devices = new();
    private readonly OneDeskDataPaths _paths;
    private readonly string _identityPath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DeviceRegistry(OneDeskDataPaths paths)
    {
        _paths = paths;
        _paths.EnsureCreated();
        _identityPath = Path.Combine(_paths.Root, "desktop-identity.json");
        DesktopIdentity = LoadOrCreateDesktopIdentity();
        _devices[DesktopIdentity.DeviceId] = DesktopIdentity;
    }

    public DeviceIdentity DesktopIdentity { get; }

    public DeviceIdentity RegisterMobile(string displayName, string platform, string architecture, string? deviceId = null)
    {
        var id = string.IsNullOrWhiteSpace(deviceId) ? $"mobile-{Guid.NewGuid():N}" : deviceId;
        var identity = new DeviceIdentity(id, displayName, DeviceKind.Mobile, platform, architecture);
        _devices[identity.DeviceId] = identity;
        return identity;
    }

    public DeviceIdentity? Find(string deviceId)
    {
        return _devices.TryGetValue(deviceId, out var identity) ? identity : null;
    }

    public IReadOnlyCollection<DeviceIdentity> All() => _devices.Values.ToArray();

    private DeviceIdentity LoadOrCreateDesktopIdentity()
    {
        if (File.Exists(_identityPath))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<DeviceIdentity>(File.ReadAllText(_identityPath), JsonOptions);
                if (existing is not null && existing.Kind == DeviceKind.Desktop && !string.IsNullOrWhiteSpace(existing.DeviceId))
                {
                    return existing with
                    {
                        DisplayName = Environment.MachineName,
                        Platform = RuntimeInformation.OSDescription,
                        Architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
                    };
                }
            }
            catch (JsonException)
            {
                // Regenerate a stable identity if the persisted file is corrupt.
            }
        }

        var identity = new DeviceIdentity(
            $"desktop-{Guid.NewGuid():N}",
            Environment.MachineName,
            DeviceKind.Desktop,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
        File.WriteAllText(_identityPath, JsonSerializer.Serialize(identity, JsonOptions));
        return identity;
    }
}
