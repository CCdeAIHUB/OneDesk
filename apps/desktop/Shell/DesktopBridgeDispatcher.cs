using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OneDesk.Desktop.Services;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop.Shell;

/// <summary>
/// 桌面前端桥接的唯一业务入口。窗口平台只负责提供文件选择和窗口行为，不能复制工作区规则。
/// </summary>
public sealed partial class DesktopBridgeDispatcher
{
    private readonly DeviceRegistry _devices;
    private readonly JsApiRouter _jsApiRouter;
    private readonly OneDeskRepository _repository;
    private readonly SchemePackageService _packages;
    private readonly PermissionService _permissions;
    private readonly CapabilityDirectoryService _capabilities;
    private readonly PairingService _pairing;
    private readonly QuicGatewayService _gateway;
    private readonly PortableDesktopSettingsService _settings;
    private readonly PluginHostService _plugins;
    private readonly PluginFrontendSessionRegistry _pluginSessions;
    private readonly StructuredLogStore _logs;
    private readonly OneDeskDataPaths _paths;
    private readonly IDesktopShellPlatform _platform;
    private readonly ConcurrentDictionary<string, PendingPackageImport> _pendingImports = new(StringComparer.OrdinalIgnoreCase);

    public DesktopBridgeDispatcher(
        DeviceRegistry devices,
        JsApiRouter jsApiRouter,
        OneDeskRepository repository,
        SchemePackageService packages,
        PermissionService permissions,
        CapabilityDirectoryService capabilities,
        PairingService pairing,
        QuicGatewayService gateway,
        PortableDesktopSettingsService settings,
        PluginHostService plugins,
        PluginFrontendSessionRegistry pluginSessions,
        StructuredLogStore logs,
        OneDeskDataPaths paths,
        IDesktopShellPlatform platform)
    {
        _devices = devices;
        _jsApiRouter = jsApiRouter;
        _repository = repository;
        _packages = packages;
        _permissions = permissions;
        _capabilities = capabilities;
        _pairing = pairing;
        _gateway = gateway;
        _settings = settings;
        _plugins = plugins;
        _pluginSessions = pluginSessions;
        _logs = logs;
        _paths = paths;
        _platform = platform;
    }

    public async Task<BridgeResponse> DispatchAsync(BridgeRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            return request.Type switch
            {
                "getDeviceId" => BridgeResponse.Success(request.RequestId, _devices.DesktopIdentity.DeviceId),
                "callJsApi" => await HandleJsApiAsync(request, cancellationToken),
                "workspace.list" => await HandleWorkspaceListAsync(request, cancellationToken),
                "workspace.saveComponent" => await SaveComponentAsync(request, cancellationToken),
                "workspace.readComponentFiles" => await ReadComponentFilesAsync(request, cancellationToken),
                "workspace.saveComponentFiles" => await SaveComponentFilesAsync(request, cancellationToken),
                "workspace.deleteComponent" => DeleteComponent(request),
                "workspace.saveAction" => await SaveActionAsync(request, cancellationToken),
                "workspace.deleteAction" => DeleteAction(request),
                "workspace.savePage" => await SavePageAsync(request, cancellationToken),
                "workspace.deletePage" => DeletePage(request),
                "workspace.saveScheme" => await SaveSchemeAsync(request, cancellationToken),
                "workspace.deleteScheme" => DeleteScheme(request),
                "workspace.applyScheme" => await ApplySchemeAsync(request, cancellationToken),
                "workspace.exportComponent" => await ExportAsync(request, "Component", cancellationToken),
                "workspace.exportPage" => await ExportAsync(request, "Page", cancellationToken),
                "workspace.exportScheme" => await ExportAsync(request, "Scheme", cancellationToken),
                "workspace.inspectImport" => await InspectWorkspaceImportAsync(request, cancellationToken),
                "workspace.confirmImport" => await ConfirmWorkspaceImportAsync(request, cancellationToken),
                "resource.list" => BridgeResponse.Success(request.RequestId, await _repository.ListMediaResourcesAsync(cancellationToken)),
                "resource.add" => await AddResourceAsync(request, cancellationToken),
                "resource.delete" => DeleteResource(request),
                "resource.copyToComponent" => await CopyResourceAsync(request, true, cancellationToken),
                "resource.copyToPage" => await CopyResourceAsync(request, false, cancellationToken),
                "capability.list" => BridgeResponse.Success(request.RequestId, _capabilities.Categories()),
                "permission.list" => BridgeResponse.Success(request.RequestId, new { grants = _permissions.ListGrants(), categories = _capabilities.Categories() }),
                "permission.grant" => ChangePermission(request, true),
                "permission.revoke" => ChangePermission(request, false),
                "pairing.generate" => GeneratePairing(request),
                "device.status" => DeviceStatus(request),
                "device.rename" => RenameDevice(request),
                "gateway.status" => BridgeResponse.Success(request.RequestId, GatewayPayload()),
                "settings.get" => await GetSettingsAsync(request, cancellationToken),
                "settings.update" => await UpdateSettingsAsync(request, cancellationToken),
                "scheme.cacheManifest" => await SchemeCacheManifestAsync(request, cancellationToken),
                "plugin.list" => BridgeResponse.Success(request.RequestId, _plugins.InstalledPlugins),
                "plugin.frontend.list" => await ListFrontendPluginsAsync(request, cancellationToken),
                "plugin.frontend.callJsApi" => await CallFrontendPluginJsApiAsync(request, cancellationToken),
                "plugin.frontend.invokeBackend" => await InvokeFrontendPluginBackendAsync(request, cancellationToken),
                "plugin.inspectImport" => await InspectPluginImportAsync(request, cancellationToken),
                "plugin.confirmImport" => await ConfirmPluginImportAsync(request, cancellationToken),
                "plugin.delete" => await DeletePluginAsync(request, cancellationToken),
                "plugin.submitSettings" => await SubmitPluginSettingsAsync(request, cancellationToken),
                "log.list" => BridgeResponse.Success(request.RequestId, _logs.Recent()),
                "window.minimize" => await WindowAsync(request, _platform.MinimizeAsync),
                "window.maximize" => BridgeResponse.Success(request.RequestId, await _platform.ToggleMaximizeAsync()),
                "window.dragStart" => await WindowAsync(request, _platform.StartDragAsync),
                "window.resizeStart" => await ResizeWindowAsync(request),
                "window.moveBy" => await MoveWindowAsync(request),
                "window.close" => await WindowAsync(request, _platform.CloseToTrayAsync),
                "window.theme" => await ThemeWindowAsync(request),
                _ => BridgeResponse.Failure(request.RequestId, "CapabilityNotSupported", "未知 OneDesk 桥接请求"),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return BridgeResponse.Failure(request.RequestId, "OperationCancelled", "操作已取消");
        }
        catch (Exception error)
        {
            _logs.Append(_devices.DesktopIdentity.DeviceId, "Error", "DesktopBridge", "桌面桥接请求失败", new Dictionary<string, object?>
            {
                ["requestId"] = request.RequestId,
                ["type"] = request.Type,
                ["error"] = error.Message,
            });
            return BridgeResponse.Failure(request.RequestId, "BridgeRequestFailed", error.Message);
        }
    }

    private async Task<BridgeResponse> HandleJsApiAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        if (request.Source is null || string.IsNullOrWhiteSpace(request.Capability)) return InvalidPayload(request);
        if (request.Source.Kind == "component" &&
            (string.IsNullOrWhiteSpace(request.Source.ComponentId) || await _repository.GetComponentAsync(request.Source.ComponentId, cancellationToken) is null))
        {
            return BridgeResponse.Failure(request.RequestId, "InvalidSourceIdentity", "组件来源不存在，已拒绝 JSAPI 调用");
        }
        if (request.Source.Kind == "plugin" &&
            (string.IsNullOrWhiteSpace(request.Source.PluginId) || _plugins.InstalledPlugins.All(plugin => plugin.Id != request.Source.PluginId)))
        {
            return BridgeResponse.Failure(request.RequestId, "InvalidSourceIdentity", "插件来源不存在，已拒绝 JSAPI 调用");
        }

        // 顶层主前端由 CEF 壳子直接注入，转换为 system；组件和插件仍保留受权限约束的身份。
        var source = request.Source.Kind == "frontend"
            ? new TrustedSource(null, null, null, null, "system")
            : new TrustedSource(request.Source.SchemeId, request.Source.PageId, request.Source.ComponentId, request.Source.PluginId, request.Source.Kind);
        var result = await _jsApiRouter.RouteAsync(new JsApiRequest(
            request.RequestId,
            string.IsNullOrWhiteSpace(request.TargetDeviceId) ? _devices.DesktopIdentity.DeviceId : request.TargetDeviceId,
            source,
            request.Capability,
            request.Payload), cancellationToken);
        return new BridgeResponse(request.RequestId, result.Ok, result.Payload, result.ErrorCode, result.Message);
    }

    private async Task<BridgeResponse> WindowAsync(BridgeRequest request, Func<Task> operation)
    {
        await operation();
        return BridgeResponse.Success(request.RequestId);
    }

    private async Task<BridgeResponse> ResizeWindowAsync(BridgeRequest request)
    {
        var edge = request.Payload is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(edge)) return InvalidPayload(request);
        await _platform.StartResizeAsync(edge);
        return BridgeResponse.Success(request.RequestId);
    }

    private async Task<BridgeResponse> MoveWindowAsync(BridgeRequest request)
    {
        await _platform.MoveByAsync(ReadDouble(request, "dx"), ReadDouble(request, "dy"));
        return BridgeResponse.Success(request.RequestId);
    }

    private async Task<BridgeResponse> ThemeWindowAsync(BridgeRequest request)
    {
        var theme = request.Payload is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
        if (theme is not ("light" or "dark")) return InvalidPayload(request);
        await _platform.SetThemeAsync(theme);
        return BridgeResponse.Success(request.RequestId);
    }

    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    internal static T? DeserializePayload<T>(BridgeRequest request)
    {
        return request.Payload is { } payload ? JsonSerializer.Deserialize<T>(payload.GetRawText(), JsonOptions) : default;
    }

    internal static string? ReadString(BridgeRequest request, string key)
    {
        return request.Payload is { ValueKind: JsonValueKind.Object } payload &&
            payload.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static double ReadDouble(BridgeRequest request, string key)
    {
        return request.Payload is { ValueKind: JsonValueKind.Object } payload &&
            payload.TryGetProperty(key, out var value) && value.TryGetDouble(out var number)
                ? number
                : 0;
    }

    internal static BridgeResponse InvalidPayload(BridgeRequest request) =>
        BridgeResponse.Failure(request.RequestId, "InvalidPayload", "请求参数不完整或格式不正确");

    internal IReadOnlyDictionary<string, string> InstalledPluginVersions() =>
        _plugins.InstalledPlugins.ToDictionary(plugin => plugin.Id, plugin => plugin.Version, StringComparer.OrdinalIgnoreCase);

    internal object GatewayPayload() => new
    {
        running = _gateway.IsRunning,
        port = _gateway.Port,
        peers = _gateway.Peers,
        queuedRequests = _gateway.QueuedRequests,
    };

    internal static IReadOnlyList<string> LocalIpv4Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
        .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
        .Select(address => address.Address.ToString())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
