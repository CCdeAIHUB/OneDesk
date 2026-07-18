using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using OneDesk.Desktop.Shell;

namespace OneDesk.Windows;

public sealed partial class MainForm
{
    private static readonly JsonSerializerOptions BridgeJsonOptions = new(JsonSerializerDefaults.Web);

    private async Task InitializeChromiumAsync()
    {
        try
        {
            AppDiagnostics.Write("Chromium initialization entered.");
            EnableLocalModuleLoading();
            _browser = new WebView2 { DefaultBackgroundColor = Color.Transparent };
            Controls.Add(_browser);
            _browser.BringToFront();
            LayoutBrowser();
            RemoveLoadingSurface();

            await _browser.EnsureCoreWebView2Async();
            _browser.ZoomFactor = 1d;
            _browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _browser.CoreWebView2.WebMessageReceived += Browser_OnWebMessageReceived;
            _browser.CoreWebView2.NavigationCompleted += Browser_OnNavigationCompleted;
            _browser.CoreWebView2.ProcessFailed += Browser_OnProcessFailed;
            _browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            _browser.CoreWebView2.WebResourceRequested += Browser_OnWebResourceRequested;
            await _browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(NativeBridgeScript);

            var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
            if (!File.Exists(indexPath))
            {
                throw new FileNotFoundException("未找到桌面前端入口文件", indexPath);
            }
            _browser.CoreWebView2.Navigate(new Uri(indexPath).AbsoluteUri);
        }
        catch (Exception error)
        {
            AppDiagnostics.Write(error.ToString());
            MessageBox.Show(this, $"Chromium 内核初始化失败。\n\n{error.Message}", "OneDesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void EnableLocalModuleLoading()
    {
        const string argument = "--allow-file-access-from-files";
        var existing = Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS") ?? string.Empty;
        if (existing.Contains(argument, StringComparison.Ordinal)) return;
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
            string.IsNullOrWhiteSpace(existing) ? argument : $"{existing} {argument}");
    }

    private void Browser_OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs eventArgs)
    {
        var message = $"WebView2 进程异常终止：{eventArgs.ProcessFailedKind} / {eventArgs.Reason} / {eventArgs.ExitCode}";
        AppDiagnostics.Write(message);
        _logs?.Append(_devices?.DesktopIdentity.DeviceId ?? "desktop", "Error", "Chromium", message, new Dictionary<string, object?>
        {
            ["processFailedKind"] = eventArgs.ProcessFailedKind.ToString(),
            ["reason"] = eventArgs.Reason.ToString(),
            ["exitCode"] = eventArgs.ExitCode,
            ["processDescription"] = eventArgs.ProcessDescription,
        });
    }

    private async void Browser_OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        AppDiagnostics.Write($"Navigation completed. Success={eventArgs.IsSuccess}; Uri={_browser?.Source}");
        if (!eventArgs.IsSuccess || _browser?.CoreWebView2 is null) return;

        await Task.Delay(500);
        var state = await _browser.CoreWebView2.ExecuteScriptAsync(
            "JSON.stringify({appChildren:document.getElementById('app')?.children.length??-1})");
        if (state.Contains("\\\"appChildren\\\":0", StringComparison.Ordinal))
        {
            await ExecuteBundledFrontendScriptsAsync();
        }
        await CaptureFrontendForDiagnosticsAsync();
    }

    private async Task ExecuteBundledFrontendScriptsAsync()
    {
        if (_browser?.CoreWebView2 is null) return;
        var assetsDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets");
        if (!Directory.Exists(assetsDirectory)) return;
        foreach (var scriptPath in Directory.EnumerateFiles(assetsDirectory, "*.js").OrderBy(Path.GetFileName))
        {
            await _browser.CoreWebView2.ExecuteScriptAsync(await File.ReadAllTextAsync(scriptPath));
        }
    }

    private async Task CaptureFrontendForDiagnosticsAsync()
    {
        var outputPath = Environment.GetEnvironmentVariable("ONEDESK_CAPTURE_FRONTEND");
        if (string.IsNullOrWhiteSpace(outputPath) || _browser?.CoreWebView2 is null) return;
        try
        {
            var result = await _browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Page.captureScreenshot",
                "{\"format\":\"png\",\"captureBeyondViewport\":false}");
            using var document = JsonDocument.Parse(result);
            var data = document.RootElement.GetProperty("data").GetString();
            if (string.IsNullOrWhiteSpace(data)) throw new InvalidDataException("Chromium 截图数据为空");
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, Convert.FromBase64String(data));
        }
        catch (Exception error)
        {
            AppDiagnostics.Write($"Frontend diagnostic screenshot failed: {error}");
        }
    }

    private void Browser_OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs eventArgs)
    {
        if (!Uri.TryCreate(eventArgs.Request.Uri, UriKind.Absolute, out var uri) || _browser?.CoreWebView2 is null) return;
        if (uri.Scheme == "file" && uri.AbsolutePath.EndsWithAny(".js", ".css") && File.Exists(uri.LocalPath))
        {
            var contentType = Path.GetExtension(uri.LocalPath).Equals(".css", StringComparison.OrdinalIgnoreCase)
                ? "text/css; charset=utf-8"
                : "application/javascript; charset=utf-8";
            eventArgs.Response = _browser.CoreWebView2.Environment.CreateWebResourceResponse(
                new MemoryStream(File.ReadAllBytes(uri.LocalPath)), 200, "OK", $"Content-Type: {contentType}\r\nCache-Control: no-store");
            return;
        }
        if (uri.Scheme is "http" or "https" or "ws" or "wss")
        {
            eventArgs.Response = _browser.CoreWebView2.Environment.CreateWebResourceResponse(
                new MemoryStream(), 403, "Blocked by OneDesk", "Content-Type: text/plain\r\nX-OneDesk-Policy: frontend-network-blocked");
        }
    }

    private async void Browser_OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        BridgeResponse response;
        try
        {
            using var document = JsonDocument.Parse(eventArgs.WebMessageAsJson);
            if (document.RootElement.TryGetProperty("type", out var type) && type.GetString() == "diagnostic.error")
            {
                AppDiagnostics.Write($"Frontend error: {eventArgs.WebMessageAsJson}");
                return;
            }

            var request = JsonSerializer.Deserialize<BridgeRequest>(eventArgs.WebMessageAsJson, BridgeJsonOptions)
                ?? throw new JsonException("桥接请求为空");
            response = _bridgeDispatcher is null
                ? BridgeResponse.Failure(request.RequestId, "ShellNotReady", "OneDesk 桥接服务尚未初始化完成")
                : await _bridgeDispatcher.DispatchAsync(request);
        }
        catch (Exception error)
        {
            AppDiagnostics.Write($"Bridge message failed: {error}");
            response = BridgeResponse.Failure(ReadRequestId(eventArgs.WebMessageAsJson), "InvalidPayload", "请求参数不完整或格式不正确");
        }
        PostBridgeResponse(response);
    }

    private static string ReadRequestId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("requestId", out var value) ? value.GetString() ?? "invalid-request" : "invalid-request";
        }
        catch (JsonException) { return "invalid-request"; }
    }

    private void PostBridgeResponse(BridgeResponse response)
    {
        _browser?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(response, BridgeJsonOptions));
    }

    private void RemoveLoadingSurface()
    {
        if (_loadingLabel is null) return;
        Controls.Remove(_loadingLabel);
        _loadingLabel.Dispose();
        _loadingLabel = null;
    }

    private const string NativeBridgeScript = """
(() => {
  const report = (type, payload) => window.chrome.webview.postMessage({ type, requestId: type, payload });
  window.addEventListener('error', event => report('diagnostic.error', { message: event.message, filename: event.filename, lineno: event.lineno, error: String(event.error?.stack || event.error || '') }));
  window.addEventListener('unhandledrejection', event => report('diagnostic.error', { message: 'Unhandled promise rejection', reason: String(event.reason?.stack || event.reason || '') }));
  const pending = new Map();
  window.chrome.webview.addEventListener('message', event => {
    const resolve = pending.get(event.data.requestId);
    if (!resolve) return;
    pending.delete(event.data.requestId);
    resolve(JSON.stringify(event.data));
  });
  const send = (type, payload = {}) => {
    const requestId = payload.requestId || `bridge-${crypto.randomUUID()}`;
    return new Promise(resolve => {
      pending.set(requestId, resolve);
      window.chrome.webview.postMessage({ ...payload, type, requestId });
    });
  };
  window.OneDeskNative = {
    send(type, payloadJson) { return send(type, payloadJson ? { payload: JSON.parse(payloadJson) } : {}); },
    getDeviceId() { return send('getDeviceId').then(raw => JSON.parse(raw).payload); },
    callJsApi(targetDeviceId, capability, payloadJson) { return send('callJsApi', { targetDeviceId, capability, payload: payloadJson ? JSON.parse(payloadJson) : null, source: { kind: 'frontend' } }); },
    callComponentJsApi(componentId, targetDeviceId, capability, payloadJson) { return send('callJsApi', { targetDeviceId, capability, payload: payloadJson ? JSON.parse(payloadJson) : null, source: { kind: 'component', componentId } }); },
    callPluginJsApi(pluginId, targetDeviceId, capability, payloadJson) { return send('callJsApi', { targetDeviceId, capability, payload: payloadJson ? JSON.parse(payloadJson) : null, source: { kind: 'plugin', pluginId } }); },
    listWorkspace() { return send('workspace.list'); },
    minimizeWindow() { return send('window.minimize'); },
    maximizeWindow() { return send('window.maximize'); },
    startWindowDrag() { return send('window.dragStart'); },
    startWindowResize(edge) { return send('window.resizeStart', { payload: edge }); },
    closeWindow() { return send('window.close'); },
    setShellTheme(theme) { return send('window.theme', { payload: theme }); }
  };
  window.fetch = () => Promise.reject(new Error('OneDesk blocks direct frontend networking'));
  window.XMLHttpRequest = window.WebSocket = window.EventSource = function () { throw new Error('OneDesk blocks direct frontend networking'); };
  navigator.sendBeacon = () => { throw new Error('OneDesk blocks direct frontend networking'); };
  document.addEventListener('click', event => {
    const href = event.target?.closest?.('a[href]')?.getAttribute('href') || '';
    if (/^(https?:|wss?:)/i.test(href)) { event.preventDefault(); throw new Error('OneDesk blocks direct frontend navigation'); }
  }, true);
})();
""";
}

internal static class UriPathExtensions
{
    public static bool EndsWithAny(this string value, params string[] suffixes) =>
        suffixes.Any(suffix => value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}
