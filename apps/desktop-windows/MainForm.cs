using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using OneDesk.Desktop.Services;
using OneDesk.Desktop.Storage;

namespace OneDesk.Windows;

public sealed class MainForm : Form
{
    private WebView2? _browser;
    private ServiceProvider? _services;
    private DeviceRegistry? _devices;
    private JsApiRouter? _jsApiRouter;
    private OneDeskRepository? _repository;

    public MainForm()
    {
        AppDiagnostics.Write("MainForm constructor entered.");
        Text = "OneDesk";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 760);
        MinimumSize = new Size(980, 640);
        BackColor = Color.FromArgb(248, 252, 255);
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;

        var loadingLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "OneDesk 正在加载...",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(54, 65, 82),
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Point)
        };
        Controls.Add(loadingLabel);
        AppDiagnostics.Write("Loading surface created.");

        Shown += async (_, _) =>
        {
            AppDiagnostics.Write("MainForm shown.");
            await InitializeServicesAsync();
            await InitializeChromiumAsync();
        };
    }

    private async Task InitializeServicesAsync()
    {
        AppDiagnostics.Write("Service initialization entered.");
        var collection = new ServiceCollection();
        collection.AddSingleton<DeviceRegistry>();
        collection.AddSingleton<PermissionService>();
        collection.AddSingleton<StructuredLogStore>();
        collection.AddSingleton<PairingService>();
        collection.AddSingleton<OneDeskDataPaths>();
        collection.AddSingleton<JsonFileStore>();
        collection.AddSingleton<OneDeskRepository>();
        collection.AddSingleton<SchemePackageService>();
        collection.AddSingleton<PluginHostService>();
        collection.AddSingleton<JsApiRouter>();
        collection.AddSingleton<FrontendNetworkPolicy>();
        collection.AddSingleton<WorkspaceBootstrapper>();
        _services = collection.BuildServiceProvider();
        AppDiagnostics.Write("Service provider built.");

        _devices = _services.GetRequiredService<DeviceRegistry>();
        _jsApiRouter = _services.GetRequiredService<JsApiRouter>();
        _repository = _services.GetRequiredService<OneDeskRepository>();
        AppDiagnostics.Write("Core services resolved.");
        await _services.GetRequiredService<WorkspaceBootstrapper>().EnsureSeedDataAsync();
        AppDiagnostics.Write("Service initialization completed.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _browser?.Dispose();
            _services?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task InitializeChromiumAsync()
    {
        try
        {
            AppDiagnostics.Write("Chromium initialization entered.");
            _browser = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.Transparent
            };
            Controls.Add(_browser);
            _browser.BringToFront();
            AppDiagnostics.Write("WebView2 control created.");

            await _browser.EnsureCoreWebView2Async();
            AppDiagnostics.Write("WebView2 initialized.");
            _browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _browser.CoreWebView2.WebMessageReceived += Browser_OnWebMessageReceived;
            _browser.CoreWebView2.NavigationCompleted += Browser_OnNavigationCompleted;
            _browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            _browser.CoreWebView2.WebResourceRequested += Browser_OnWebResourceRequested;

            await _browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(NativeBridgeScript);

            var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
            if (!File.Exists(indexPath))
            {
                MessageBox.Show($"未找到前端入口文件：{indexPath}", "OneDesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _browser.CoreWebView2.Navigate(new Uri(indexPath).AbsoluteUri);
            AppDiagnostics.Write($"Navigated to {indexPath}.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Write(ex.ToString());
            MessageBox.Show(
                $"OneDesk 窗口已启动，但 Chromium 内核初始化失败。\n\n{ex.Message}",
                "OneDesk",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async void Browser_OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        AppDiagnostics.Write($"Navigation completed. Success={e.IsSuccess}; WebError={e.WebErrorStatus}; Uri={_browser?.Source}");
        if (!e.IsSuccess || _browser?.CoreWebView2 is null)
        {
            return;
        }

        await Task.Delay(1000);
        var state = await _browser.CoreWebView2.ExecuteScriptAsync("""
(() => JSON.stringify({
  bodyText: document.body.innerText.slice(0, 80),
  htmlLength: document.documentElement.outerHTML.length,
  appChildren: document.getElementById('app')?.children.length ?? -1,
  scripts: [...document.scripts].map(script => script.src || script.textContent?.slice(0, 30)),
  styles: [...document.styleSheets].length
}))()
""");
        AppDiagnostics.Write($"DOM state: {state}");
        if (state.Contains("\\\"appChildren\\\":0", StringComparison.Ordinal))
        {
            await ExecuteBundledFrontendScriptsAsync();
            await Task.Delay(500);
            var injectedState = await _browser.CoreWebView2.ExecuteScriptAsync("""
(() => JSON.stringify({
  bodyText: document.body.innerText.slice(0, 80),
  htmlLength: document.documentElement.outerHTML.length,
  appChildren: document.getElementById('app')?.children.length ?? -1
}))()
""");
            AppDiagnostics.Write($"DOM state after script injection: {injectedState}");
        }
    }

    private async Task ExecuteBundledFrontendScriptsAsync()
    {
        if (_browser?.CoreWebView2 is null)
        {
            return;
        }

        var assetsDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets");
        if (!Directory.Exists(assetsDirectory))
        {
            AppDiagnostics.Write($"Assets directory does not exist: {assetsDirectory}");
            return;
        }

        foreach (var scriptPath in Directory.EnumerateFiles(assetsDirectory, "*.js").OrderBy(Path.GetFileName))
        {
            AppDiagnostics.Write($"Executing bundled frontend script: {scriptPath}");
            var script = await File.ReadAllTextAsync(scriptPath);
            await _browser.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    private void Browser_OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (uri.Scheme == "file" && (uri.AbsolutePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase)))
        {
            AppDiagnostics.Write($"Local resource requested: {uri}");
            if (_browser?.CoreWebView2 is null)
            {
                return;
            }

            var localPath = uri.LocalPath;
            if (!File.Exists(localPath))
            {
                return;
            }

            var extension = Path.GetExtension(localPath).ToLowerInvariant();
            var contentType = extension switch
            {
                ".js" => "application/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                _ => "application/octet-stream"
            };
            var stream = new MemoryStream(File.ReadAllBytes(localPath));
            e.Response = _browser.CoreWebView2.Environment.CreateWebResourceResponse(
                stream,
                200,
                "OK",
                $"Content-Type: {contentType}\r\nCache-Control: no-store");
            return;
        }

        if (uri.Scheme is "http" or "https" or "ws" or "wss")
        {
            var stream = new MemoryStream();
            if (_browser?.CoreWebView2 is null)
            {
                return;
            }

            e.Response = _browser.CoreWebView2.Environment.CreateWebResourceResponse(
                stream,
                403,
                "Blocked by OneDesk",
                "Content-Type: text/plain\r\nX-OneDesk-Policy: frontend-network-blocked");
        }
    }

    private async void Browser_OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var document = JsonDocument.Parse(e.WebMessageAsJson);
        if (document.RootElement.TryGetProperty("type", out var typeElement) &&
            typeElement.GetString() == "diagnostic.error")
        {
            AppDiagnostics.Write($"Frontend error: {e.WebMessageAsJson}");
            return;
        }

        var message = JsonSerializer.Deserialize<BridgeMessage>(e.WebMessageAsJson, JsonOptions);
        if (message is null)
        {
            return;
        }

        object response = message.Type switch
        {
            "getDeviceId" => new BridgeResponse(message.RequestId, true, _devices?.DesktopIdentity.DeviceId),
            "callJsApi" => await HandleJsApiAsync(message),
            "workspace.list" => await HandleWorkspaceListAsync(message),
            "window.minimize" => HandleWindowMinimize(message),
            "window.maximize" => HandleWindowMaximize(message),
            "window.close" => HandleWindowClose(message),
            _ => new BridgeResponse(message.RequestId, false, null, "CapabilityNotSupported", "未知 OneDesk 桥接请求")
        };

        var json = JsonSerializer.Serialize(response, JsonOptions);
        _browser?.CoreWebView2?.PostWebMessageAsJson(json);
    }

    private async Task<BridgeResponse> HandleJsApiAsync(BridgeMessage message)
    {
        var request = new JsApiRequest(
            message.RequestId,
            message.TargetDeviceId ?? _devices?.DesktopIdentity.DeviceId ?? "desktop",
            new TrustedSource(
                message.Source?.SchemeId,
                message.Source?.PageId,
                message.Source?.ComponentId,
                message.Source?.PluginId,
                message.Source?.Kind ?? "system"),
            message.Capability ?? "unknown",
            message.Payload);
        if (_jsApiRouter is null)
        {
            return new BridgeResponse(message.RequestId, false, null, "ShellNotReady", "OneDesk 桥接服务尚未初始化完成");
        }

        var result = await _jsApiRouter.RouteAsync(request);
        return new BridgeResponse(message.RequestId, result.Ok, result.Payload, result.ErrorCode, result.Message);
    }

    private async Task<BridgeResponse> HandleWorkspaceListAsync(BridgeMessage message)
    {
        object components = _repository is null ? Array.Empty<object>() : await _repository.ListComponentsAsync();
        object pages = _repository is null ? Array.Empty<object>() : await _repository.ListPagesAsync();
        object schemes = _repository is null ? Array.Empty<object>() : await _repository.ListSchemesAsync();
        var payload = new
        {
            components,
            pages,
            schemes
        };
        return new BridgeResponse(message.RequestId, true, payload);
    }

    private BridgeResponse HandleWindowMinimize(BridgeMessage message)
    {
        WindowState = FormWindowState.Minimized;
        return new BridgeResponse(message.RequestId, true, null);
    }

    private BridgeResponse HandleWindowMaximize(BridgeMessage message)
    {
        ToggleMaximize();
        return new BridgeResponse(message.RequestId, true, null);
    }

    private BridgeResponse HandleWindowClose(BridgeMessage message)
    {
        Close();
        return new BridgeResponse(message.RequestId, true, null);
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string NativeBridgeScript = """
(() => {
  window.addEventListener('error', event => {
    window.chrome.webview.postMessage({
      type: 'diagnostic.error',
      requestId: 'diagnostic-error',
      message: event.message,
      payload: {
        filename: event.filename,
        lineno: event.lineno,
        colno: event.colno,
        error: event.error ? String(event.error.stack || event.error.message || event.error) : null
      }
    });
  });

  window.addEventListener('unhandledrejection', event => {
    window.chrome.webview.postMessage({
      type: 'diagnostic.error',
      requestId: 'diagnostic-rejection',
      message: 'Unhandled promise rejection',
      payload: {
        reason: String(event.reason?.stack || event.reason?.message || event.reason)
      }
    });
  });

  const pending = new Map();
  window.chrome.webview.addEventListener('message', event => {
    const message = event.data;
    const resolver = pending.get(message.requestId);
    if (!resolver) return;
    pending.delete(message.requestId);
    resolver(JSON.stringify(message));
  });

  function send(type, payload = {}) {
    const requestId = payload.requestId || `bridge-${crypto.randomUUID()}`;
    return new Promise(resolve => {
      pending.set(requestId, resolve);
      window.chrome.webview.postMessage({ ...payload, type, requestId });
    });
  }

  window.OneDeskNative = {
    getDeviceId() {
      return send('getDeviceId').then(raw => JSON.parse(raw).payload);
    },
    callJsApi(targetDeviceId, capability, payloadJson) {
      return send('callJsApi', {
        targetDeviceId,
        capability,
        payload: payloadJson ? JSON.parse(payloadJson) : null,
        source: { kind: 'system' }
      });
    },
    listWorkspace() {
      return send('workspace.list');
    },
    minimizeWindow() {
      return send('window.minimize');
    },
    maximizeWindow() {
      return send('window.maximize');
    },
    closeWindow() {
      return send('window.close');
    }
  };

  window.fetch = () => Promise.reject(new Error('OneDesk blocks direct frontend networking'));
  window.WebSocket = function () {
    throw new Error('OneDesk blocks direct frontend networking');
  };
})();
""";

    private sealed record BridgeMessage(
        string Type,
        string RequestId,
        string? TargetDeviceId,
        string? Capability,
        JsonElement? Payload,
        BridgeSource? Source);

    private sealed record BridgeSource(
        string? SchemeId,
        string? PageId,
        string? ComponentId,
        string? PluginId,
        string? Kind);

    private sealed record BridgeResponse(
        string RequestId,
        bool Ok,
        object? Payload,
        string? ErrorCode = null,
        string? Message = null);
}
