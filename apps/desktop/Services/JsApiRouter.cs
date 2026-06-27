namespace OneDesk.Desktop.Services;

using System.Text.Json;
using OneDesk.Desktop.Storage;

public sealed class JsApiRouter
{
    private readonly DeviceRegistry _devices;
    private readonly PermissionService _permissions;
    private readonly StructuredLogStore _logs;
    private readonly CapabilityDirectoryService _capabilities;
    private readonly PluginHostService _plugins;
    private readonly OneDeskDataPaths _paths;
    private readonly QuicGatewayService _gateway;

    public JsApiRouter(
        DeviceRegistry devices,
        PermissionService permissions,
        StructuredLogStore logs,
        CapabilityDirectoryService capabilities,
        PluginHostService plugins,
        OneDeskDataPaths paths,
        QuicGatewayService gateway)
    {
        _devices = devices;
        _permissions = permissions;
        _logs = logs;
        _capabilities = capabilities;
        _plugins = plugins;
        _paths = paths;
        _gateway = gateway;
    }

    public Task<JsApiResult> RouteAsync(JsApiRequest request, CancellationToken cancellationToken = default)
    {
        var target = _devices.Find(request.TargetDeviceId);
        if (target is null)
        {
            return Task.FromResult(JsApiResult.Error("TargetNotFound", "The target device is unknown."));
        }

        if (_capabilities.Find(request.Capability) is null)
        {
            return Task.FromResult(JsApiResult.Error("CapabilityNotFound", "The requested capability is not registered in the OneDesk capability directory."));
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

        return _gateway.ForwardJsApiAsync(request, cancellationToken);
    }

    private async Task<JsApiResult> ExecuteLocalAsync(JsApiRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return request.Capability switch
        {
            "device.identity" => JsApiResult.Success(_devices.DesktopIdentity),
            "device.list" => JsApiResult.Success(_devices.All()),
            "capability.list" => JsApiResult.Success(_capabilities.Categories()),
            "log.write" => WriteLog(request),
            "log.read" => JsApiResult.Success(_logs.Recent(ReadInt(request.Payload, "count", 200))),
            "file.readPrivate" => await ReadPrivateFileAsync(request, cancellationToken),
            "file.writePrivate" => await WritePrivateFileAsync(request, cancellationToken),
            "file.deletePrivate" => DeletePrivateFile(request),
            "plugin.invoke" => await InvokePluginAsync(request, cancellationToken),
            "notification.native" => Notify(request),
            "notification.inApp" => JsApiResult.Success(request.Payload),
            "clipboard.read" or "clipboard.write" => JsApiResult.Error("CapabilityPlatformHandlerMissing", "Clipboard access must be executed by the platform shell handler."),
            "file.readExternal" or "file.writeExternal" or "file.deleteExternal" => JsApiResult.Error("CapabilityRequiresUserPath", "External file access requires an explicit user-selected path and permission grant."),
            _ => JsApiResult.Error("CapabilityNotSupported", "This capability is registered but does not have a desktop local handler yet.")
        };
    }

    private JsApiResult WriteLog(JsApiRequest request)
    {
        _logs.Append(
            _devices.DesktopIdentity.DeviceId,
            ReadString(request.Payload, "level", "Info"),
            ReadString(request.Payload, "category", "JsApi"),
            ReadString(request.Payload, "message", "JSAPI log entry"),
            new Dictionary<string, object?>
            {
                ["sourceKind"] = request.Source.Kind,
                ["componentId"] = request.Source.ComponentId,
                ["pluginId"] = request.Source.PluginId
            });
        return JsApiResult.Success();
    }

    private JsApiResult Notify(JsApiRequest request)
    {
        _logs.Append(
            _devices.DesktopIdentity.DeviceId,
            "Info",
            "Notification",
            ReadString(request.Payload, "message", "OneDesk notification"),
            new Dictionary<string, object?>
            {
                ["title"] = ReadString(request.Payload, "title", "OneDesk")
            });
        return JsApiResult.Success();
    }

    private async Task<JsApiResult> ReadPrivateFileAsync(JsApiRequest request, CancellationToken cancellationToken)
    {
        var pathResult = ResolvePrivateFile(request);
        if (!pathResult.Ok)
        {
            return pathResult;
        }

        var path = (string)pathResult.Payload!;
        if (!File.Exists(path))
        {
            return JsApiResult.Error("FileNotFound", "The private file does not exist.");
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken);
        return JsApiResult.Success(new { path = ReadString(request.Payload, "path", ""), content = text });
    }

    private async Task<JsApiResult> WritePrivateFileAsync(JsApiRequest request, CancellationToken cancellationToken)
    {
        var pathResult = ResolvePrivateFile(request);
        if (!pathResult.Ok)
        {
            return pathResult;
        }

        var path = (string)pathResult.Payload!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, ReadString(request.Payload, "content", ""), cancellationToken);
        return JsApiResult.Success(new { path = ReadString(request.Payload, "path", "") });
    }

    private JsApiResult DeletePrivateFile(JsApiRequest request)
    {
        var pathResult = ResolvePrivateFile(request);
        if (!pathResult.Ok)
        {
            return pathResult;
        }

        var path = (string)pathResult.Payload!;
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return JsApiResult.Success();
    }

    private async Task<JsApiResult> InvokePluginAsync(JsApiRequest request, CancellationToken cancellationToken)
    {
        var pluginId = ReadString(request.Payload, "pluginId", "");
        var method = ReadString(request.Payload, "method", "");
        if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(method))
        {
            return JsApiResult.Error("InvalidPayload", "plugin.invoke requires pluginId and method.");
        }

        var parameters = ReadElement(request.Payload, "parameters");
        var result = await _plugins.InvokeAsync(pluginId, method, parameters, cancellationToken);
        return JsApiResult.Success(result);
    }

    private JsApiResult ResolvePrivateFile(JsApiRequest request)
    {
        var relativePath = ReadString(request.Payload, "path", "");
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return JsApiResult.Error("InvalidPath", "Private file path must be a relative path.");
        }

        var sourceKey = PermissionService.SourceKey(request.Source)
            .Replace(':', '-')
            .Replace('/', '-')
            .Replace('\\', '-');
        var root = Path.GetFullPath(Path.Combine(_paths.Root, "private", sourceKey));
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return JsApiResult.Error("InvalidPath", "Private file path cannot escape the caller storage root.");
        }

        return JsApiResult.Success(fullPath);
    }

    private static JsonElement? ReadElement(object? payload, string key)
    {
        if (payload is JsonElement element && element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value))
        {
            return value;
        }

        return null;
    }

    private static string ReadString(object? payload, string key, string fallback)
    {
        var value = ReadElement(payload, key);
        return value?.ValueKind == JsonValueKind.String ? value.Value.GetString() ?? fallback : fallback;
    }

    private static int ReadInt(object? payload, string key, int fallback)
    {
        var value = ReadElement(payload, key);
        return value?.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var number) ? number : fallback;
    }
}
