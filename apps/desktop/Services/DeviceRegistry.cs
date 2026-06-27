using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace OneDesk.Desktop.Services;

public sealed class DeviceRegistry
{
    private readonly ConcurrentDictionary<string, DeviceIdentity> _devices = new();

    public DeviceRegistry()
    {
        DesktopIdentity = new DeviceIdentity(
            $"desktop-{Guid.NewGuid():N}",
            Environment.MachineName,
            DeviceKind.Desktop,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
        _devices[DesktopIdentity.DeviceId] = DesktopIdentity;
    }

    public DeviceIdentity DesktopIdentity { get; }

    public DeviceIdentity RegisterMobile(string displayName, string platform, string architecture)
    {
        var identity = new DeviceIdentity($"mobile-{Guid.NewGuid():N}", displayName, DeviceKind.Mobile, platform, architecture);
        _devices[identity.DeviceId] = identity;
        return identity;
    }

    public DeviceIdentity? Find(string deviceId)
    {
        return _devices.TryGetValue(deviceId, out var identity) ? identity : null;
    }

    public IReadOnlyCollection<DeviceIdentity> All() => _devices.Values.ToArray();
}
