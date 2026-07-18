namespace OneDesk.Desktop.Services;

using System.Diagnostics;
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
    private readonly IReadOnlyList<IDesktopCapabilityProvider> _desktopCapabilityProviders;

    public JsApiRouter(
        DeviceRegistry devices,
        PermissionService permissions,
        StructuredLogStore logs,
        CapabilityDirectoryService capabilities,
        PluginHostService plugins,
        OneDeskDataPaths paths,
        QuicGatewayService gateway,
        IEnumerable<IDesktopCapabilityProvider> desktopCapabilityProviders)
    {
        _devices = devices;
        _permissions = permissions;
        _logs = logs;
        _capabilities = capabilities;
        _plugins = plugins;
        _paths = paths;
        _gateway = gateway;
        _desktopCapabilityProviders = desktopCapabilityProviders.ToArray();
        _plugins.ConfigureOriginatedRequestHandler(RoutePluginOriginatedRequestAsync);
    }

    public Task<JsApiResult> RouteAsync(JsApiRequest request, CancellationToken cancellationToken = default)
    {
        // 协议目录是跨端能力 ID 的唯一事实源；旧 ID 只在入口处兼容，后续路由一律使用标准 ID。
        request = request with { Capability = CapabilityDirectoryService.NormalizeId(request.Capability) };
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
        var provider = _desktopCapabilityProviders.FirstOrDefault(candidate => candidate.CapabilityIds.Contains(request.Capability));
        if (provider is not null)
        {
            try
            {
                return await provider.ExecuteAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                _logs.Append(_devices.DesktopIdentity.DeviceId, "Error", "DesktopCapability", "桌面平台能力执行失败", new Dictionary<string, object?>
                {
                    ["capability"] = request.Capability,
                    ["error"] = error.Message,
                });
                return JsApiResult.Error("ExecutionFailed", error.Message);
            }
        }
        return request.Capability switch
        {
            "device.identity" => JsApiResult.Success(_devices.DesktopIdentity),
            "device.list" => JsApiResult.Success(_devices.All()),
            "capability.list" => JsApiResult.Success(_capabilities.Categories()),
            "log.write" => WriteLog(request),
            "log.read" => JsApiResult.Success(_logs.Recent(ReadInt(request.Payload, "count", 200))),
            "file.private.read" => await ReadPrivateFileAsync(request, cancellationToken),
            "file.private.write" => await WritePrivateFileAsync(request, cancellationToken),
            "file.private.delete" => DeletePrivateFile(request),
            "plugin.invoke" => await InvokePluginAsync(request, cancellationToken),
            "notification.native" => Notify(request),
            "notification.inApp" => JsApiResult.Success(request.Payload),
            "process.list" => ListProcesses(),
            "network.access" => await NetworkAccessAsync(request, cancellationToken),
            "clipboard.read" or "clipboard.write" => JsApiResult.Error("CapabilityPlatformHandlerMissing", "Clipboard access must be executed by the platform shell handler."),
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

    private async Task<object?> RoutePluginOriginatedRequestAsync(
        string pluginId,
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(method, "onedesk.jsapi", StringComparison.Ordinal))
        {
            return JsApiResult.Error("PluginHostMethodNotFound", "插件请求的宿主方法不存在。");
        }

        var targetDeviceId = ReadString(parameters, "targetDeviceId", _devices.DesktopIdentity.DeviceId);
        var capability = ReadString(parameters, "capability", string.Empty);
        if (string.IsNullOrWhiteSpace(capability))
        {
            return JsApiResult.Error("InvalidPayload", "onedesk.jsapi 需要 capability。");
        }

        var payload = parameters.TryGetProperty("payload", out var payloadElement)
            ? payloadElement.Clone()
            : JsonSerializer.SerializeToElement(new { });
        return await RouteAsync(
            new JsApiRequest(
                $"plugin-{Guid.NewGuid():N}",
                targetDeviceId,
                new TrustedSource(null, null, null, pluginId, "plugin"),
                capability,
                payload),
            cancellationToken);
    }

    private static JsApiResult ListProcesses()
    {
        var processes = Process.GetProcesses()
            .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Take(512)
            .Select(process =>
            {
                try
                {
                    return new
                    {
                        process.Id,
                        Name = process.ProcessName,
                        process.MainWindowTitle
                    };
                }
                catch
                {
                    return new
                    {
                        Id = process.Id,
                        Name = process.ProcessName,
                        MainWindowTitle = ""
                    };
                }
            })
            .ToArray();
        return JsApiResult.Success(processes);
    }

    private static async Task<JsApiResult> NetworkAccessAsync(JsApiRequest request, CancellationToken cancellationToken)
    {
        var url = ReadString(request.Payload, "url", "");
        var method = ReadString(request.Payload, "method", "GET").ToUpperInvariant();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return JsApiResult.Error("InvalidPayload", "network.access requires an absolute http or https URL.");
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var httpRequest = new HttpRequestMessage(new HttpMethod(method), uri);
        var body = ReadString(request.Payload, "body", "");
        if (method is "POST" or "PUT" or "PATCH")
        {
            httpRequest.Content = new StringContent(body);
        }

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsApiResult.Success(new
        {
            status = (int)response.StatusCode,
            response.ReasonPhrase,
            body = text.Length > 256_000 ? text[..256_000] : text
        });
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

    private static string ReadString(JsonElement payload, string key, string fallback)
    {
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(key, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static int ReadInt(object? payload, string key, int fallback)
    {
        var value = ReadElement(payload, key);
        return value?.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var number) ? number : fallback;
    }
}
