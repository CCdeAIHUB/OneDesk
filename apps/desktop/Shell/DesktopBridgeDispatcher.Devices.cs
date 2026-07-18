using System.Text.Json;
using OneDesk.Desktop.Services;

namespace OneDesk.Desktop.Shell;

public sealed partial class DesktopBridgeDispatcher
{
    private BridgeResponse ChangePermission(BridgeRequest request, bool grant)
    {
        var sourceKey = ReadString(request, "sourceKey");
        var capability = ReadString(request, "capability");
        if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(capability)) return InvalidPayload(request);
        if (grant) _permissions.Grant(sourceKey, capability);
        else _permissions.Revoke(sourceKey, capability);
        return BridgeResponse.Success(request.RequestId, _permissions.ListGrants());
    }

    private BridgeResponse GeneratePairing(BridgeRequest request)
    {
        var code = _pairing.GenerateVerificationCode();
        var host = LocalIpv4Addresses().FirstOrDefault() ?? "127.0.0.1";
        var port = _gateway.Port;
        if (request.Payload is { ValueKind: JsonValueKind.Object } payload &&
            payload.TryGetProperty("port", out var portValue) && portValue.TryGetInt32(out var requestedPort) &&
            requestedPort is >= 1024 and <= 65535)
        {
            port = requestedPort;
        }

        return BridgeResponse.Success(request.RequestId, new
        {
            code,
            expiresInSeconds = 300,
            host,
            port,
            localIps = LocalIpv4Addresses(),
            qrPayload = _pairing.CreateQrPayload(host, port, code),
        });
    }

    private BridgeResponse DeviceStatus(BridgeRequest request)
    {
        return BridgeResponse.Success(request.RequestId, new
        {
            desktop = _devices.DesktopIdentity,
            devices = _devices.All().Where(device => device.Kind == DeviceKind.Mobile).ToArray(),
            trusted = _pairing.TrustedDevices(),
            gateway = GatewayPayload(),
            localIps = LocalIpv4Addresses(),
            logs = _logs.Recent(80),
        });
    }

    private BridgeResponse RenameDevice(BridgeRequest request)
    {
        var deviceId = ReadString(request, "deviceId");
        if (string.IsNullOrWhiteSpace(deviceId)) return InvalidPayload(request);
        var renamed = _pairing.RenameTrustedDevice(deviceId, ReadString(request, "remark") ?? string.Empty);
        return renamed is null
            ? BridgeResponse.Failure(request.RequestId, "DeviceNotFound", "未找到该移动设备")
            : BridgeResponse.Success(request.RequestId, renamed);
    }

    private async Task<BridgeResponse> GetSettingsAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var settings = await _settings.LoadAsync(cancellationToken);
        return BridgeResponse.Success(request.RequestId, settings with { GatewayPort = _gateway.Port });
    }

    private async Task<BridgeResponse> UpdateSettingsAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var requested = DeserializePayload<DesktopAppSettings>(request);
        if (requested is null || requested.GatewayPort is < 1024 or > 65535)
        {
            return BridgeResponse.Failure(request.RequestId, "InvalidSettings", "监听端口必须在 1024 到 65535 之间");
        }

        var current = await _settings.LoadAsync(cancellationToken);
        var portChanged = requested.GatewayPort != _gateway.Port;
        if (portChanged)
        {
            await _gateway.StopAsync();
            try
            {
                await _gateway.StartAsync(requested.GatewayPort);
            }
            catch (Exception error)
            {
                // 新端口失败时恢复原监听，设置失败不能让移动网关永久离线。
                await _gateway.StartAsync(current.GatewayPort);
                return BridgeResponse.Failure(request.RequestId, "GatewayPortUnavailable", $"端口 {requested.GatewayPort} 无法监听：{error.Message}");
            }
        }

        try
        {
            await _settings.SaveAsync(requested, cancellationToken);
        }
        catch (Exception error)
        {
            if (portChanged)
            {
                await _gateway.StopAsync();
                await _gateway.StartAsync(current.GatewayPort);
            }
            return BridgeResponse.Failure(request.RequestId, "SettingsSaveFailed", error.Message);
        }

        _logs.Append(_devices.DesktopIdentity.DeviceId, "Info", "Settings", "桌面设置已更新", new Dictionary<string, object?>
        {
            ["startWithSystem"] = requested.StartWithWindows,
            ["gatewayPort"] = requested.GatewayPort,
        });
        return BridgeResponse.Success(request.RequestId, requested);
    }
}
