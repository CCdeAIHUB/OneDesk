namespace OneDesk.Desktop.Services;

public sealed class JsApiRouter
{
    private readonly DeviceRegistry _devices;
    private readonly PermissionService _permissions;
    private readonly StructuredLogStore _logs;

    public JsApiRouter(DeviceRegistry devices, PermissionService permissions, StructuredLogStore logs)
    {
        _devices = devices;
        _permissions = permissions;
        _logs = logs;
    }

    public Task<JsApiResult> RouteAsync(JsApiRequest request, CancellationToken cancellationToken = default)
    {
        var target = _devices.Find(request.TargetDeviceId);
        if (target is null)
        {
            return Task.FromResult(JsApiResult.Error("TargetNotFound", "The target device is unknown."));
        }

        if (!_permissions.IsGranted(request.Source, request.Capability) && request.Source.Kind != "system")
        {
            _logs.Append(_devices.DesktopIdentity.DeviceId, "Warning", "Permission", "Denied JSAPI call", new Dictionary<string, object?>
            {
                ["capability"] = request.Capability,
                ["targetDeviceId"] = request.TargetDeviceId
            });
            return Task.FromResult(JsApiResult.Error("PermissionDenied", "The caller is not authorized for this capability."));
        }

        if (target.DeviceId == _devices.DesktopIdentity.DeviceId)
        {
            return ExecuteLocalAsync(request, cancellationToken);
        }

        _logs.Append(_devices.DesktopIdentity.DeviceId, "Info", "JsApiRouting", "Forwarded JSAPI call through desktop gateway", new Dictionary<string, object?>
        {
            ["capability"] = request.Capability,
            ["targetDeviceId"] = request.TargetDeviceId
        });

        return Task.FromResult(JsApiResult.Error("TargetOffline", "Remote forwarding transport is not connected yet."));
    }

    private static Task<JsApiResult> ExecuteLocalAsync(JsApiRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return request.Capability switch
        {
            "device.identity" => Task.FromResult(JsApiResult.Success(new { name = Environment.MachineName })),
            "notification.native" => Task.FromResult(JsApiResult.Success()),
            _ => Task.FromResult(JsApiResult.Error("CapabilityNotSupported", "This desktop handler is not implemented yet."))
        };
    }
}
