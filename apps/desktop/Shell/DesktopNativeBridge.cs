using System.Text.Json;

namespace OneDesk.Desktop.Shell;

/// <summary>
/// 该对象由 CEF 直接注入顶层页面；JavaScript 方法名由 CefGlue 自动转换为 camelCase。
/// </summary>
public sealed class DesktopNativeBridge
{
    private readonly DesktopBridgeDispatcher _dispatcher;

    public DesktopNativeBridge(DesktopBridgeDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task<string> Send(string type, string? payloadJson = null)
    {
        var request = Request(type, payloadJson);
        return JsonSerializer.Serialize(await _dispatcher.DispatchAsync(request), DesktopBridgeDispatcher.JsonOptions);
    }

    public async Task<string> GetDeviceId()
    {
        var response = await _dispatcher.DispatchAsync(Request("getDeviceId", null));
        return response.Payload?.ToString() ?? string.Empty;
    }

    public Task<string> CallJsApi(string targetDeviceId, string capability, string payloadJson) =>
        CallJsApiWithSource(targetDeviceId, capability, payloadJson, new BridgeSource(null, null, null, null, "frontend"));

    public Task<string> CallComponentJsApi(string componentId, string targetDeviceId, string capability, string payloadJson) =>
        CallJsApiWithSource(targetDeviceId, capability, payloadJson, new BridgeSource(null, null, componentId, null, "component"));

    public Task<string> CallPluginJsApi(string pluginId, string targetDeviceId, string capability, string payloadJson) =>
        CallJsApiWithSource(targetDeviceId, capability, payloadJson, new BridgeSource(null, null, null, pluginId, "plugin"));

    public Task<string> MinimizeWindow() => Send("window.minimize");
    public Task<string> MaximizeWindow() => Send("window.maximize");
    public Task<string> StartWindowDrag() => Send("window.dragStart");
    public Task<string> StartWindowResize(string edge) => Send("window.resizeStart", JsonSerializer.Serialize(edge));
    public Task<string> CloseWindow() => Send("window.close");
    public Task<string> SetShellTheme(string theme) => Send("window.theme", JsonSerializer.Serialize(theme));

    private async Task<string> CallJsApiWithSource(
        string targetDeviceId,
        string capability,
        string payloadJson,
        BridgeSource source)
    {
        var payload = ParsePayload(payloadJson);
        var request = new BridgeRequest(
            "callJsApi",
            $"bridge-{Guid.NewGuid():N}",
            payload,
            targetDeviceId,
            capability,
            source);
        return JsonSerializer.Serialize(await _dispatcher.DispatchAsync(request), DesktopBridgeDispatcher.JsonOptions);
    }

    private static BridgeRequest Request(string type, string? payloadJson) =>
        new(type, $"bridge-{Guid.NewGuid():N}", ParsePayload(payloadJson));

    private static JsonElement? ParsePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.Clone();
    }
}
