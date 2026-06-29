using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using OneDesk.Desktop.Domain;
using OneDesk.Desktop.Services;
using OneDesk.Desktop.Storage;

namespace OneDesk.Windows;

public sealed partial class MainForm : Form
{
    private static readonly Size BaseInitialWindowSize = new(1200, 780);
    private static readonly Color TransparentShellColor = Color.FromArgb(1, 2, 3);
    private const int BaseResizeGripSize = 14;
    private const int CornerRadius = 0;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private bool _allowExit;
    private bool _isDarkTheme;
    private const int WmNcCalcSize = 0x0083;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcPaint = 0x0085;
    private const int WmNcActivate = 0x0086;
    private const int WmSetCursor = 0x0020;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmSysCommand = 0x0112;
    private const int ScMove = 0xF010;
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsMaximizeBox = 0x00010000;
    private const int WsSysMenu = 0x00080000;
    private const int IdcSizenwse = 32642;
    private const int IdcSizenesw = 32643;
    private const int IdcSizewe = 32644;
    private const int IdcSizens = 32645;
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmncrpDisabled = 2;
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private WebView2? _browser;
    private ServiceProvider? _services;
    private DeviceRegistry? _devices;
    private JsApiRouter? _jsApiRouter;
    private OneDeskRepository? _repository;
    private SchemePackageService? _packages;
    private PermissionService? _permissions;
    private CapabilityDirectoryService? _capabilityDirectory;
    private PairingService? _pairing;
    private QuicGatewayService? _gateway;
    private PluginHostService? _plugins;
    private StructuredLogStore? _logs;
    private Label? _loadingLabel;
    private NotifyIcon? _notifyIcon;
    private readonly Dictionary<string, PendingPackageImport> _pendingPackageImports = new(StringComparer.OrdinalIgnoreCase);

    public MainForm()
    {
        AppDiagnostics.Write("MainForm constructor entered.");
        Text = "OneDesk";
        StartPosition = FormStartPosition.CenterScreen;
        // 按当前 DPI 缩放初始窗口和最小窗口尺寸，避免高分屏下前端内容被压缩溢出。
        var dpiScale = DeviceDpi / 96d;
        Size = new Size(
            (int)Math.Round(BaseInitialWindowSize.Width * dpiScale),
            (int)Math.Round(BaseInitialWindowSize.Height * dpiScale));
        MinimumSize = new Size(
            (int)Math.Round(1120 * dpiScale),
            (int)Math.Round(720 * dpiScale));
        BackColor = TransparentShellColor;
        TransparencyKey = TransparentShellColor;
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);

        _loadingLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "OneDesk 正在加载...",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(54, 65, 82),
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Point)
        };
        Controls.Add(_loadingLabel);
        AppDiagnostics.Write("Loading surface created.");

        Shown += async (_, _) =>
        {
            AppDiagnostics.Write("MainForm shown.");
            EnsureInitialWindowBounds();
            ApplyDwmTheme(false);
            ApplyRoundedWindow();
            EnsureTrayIcon();
            await InitializeServicesAsync();
            await InitializeChromiumAsync();
        };
        FormClosing += MainForm_FormClosing;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.Style &= ~WsCaption;
            createParams.Style |= WsThickFrame;
            return createParams;
        }
    }

    private void EnsureInitialWindowBounds()
    {
        // 构造函数里已经完成 DPI 缩放，这里直接使用当前 Size 和 MinimumSize。
        var workingArea = Screen.FromControl(this).WorkingArea;
        var width = Math.Min(Size.Width, workingArea.Width - 48);
        var height = Math.Min(Size.Height, workingArea.Height - 48);
        width = Math.Max(width, MinimumSize.Width);
        height = Math.Max(height, MinimumSize.Height);
        var left = workingArea.Left + (workingArea.Width - width) / 2;
        var top = workingArea.Top + (workingArea.Height - height) / 2;
        SetWindowPos(Handle, nint.Zero, left, top, width, height, SwpNoZOrder | SwpNoActivate);
        AppDiagnostics.Write($"Initial window bounds applied: {width}x{height} at {left},{top}.");
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutBrowser();
        Invalidate();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        var newScale = e.DeviceDpiNew / 96d;
        // 显示器 DPI 变化时同步更新最小尺寸，保证窗口缩放边界和前端布局一致。
        MinimumSize = new Size(
            (int)Math.Round(1120 * newScale),
            (int)Math.Round(720 * newScale));
        AppDiagnostics.Write($"DPI changed: OldDpi={e.DeviceDpiOld}, NewDpi={e.DeviceDpiNew}, NewMinimumSize={MinimumSize}");
    }

    private void LayoutBrowser()
    {
        if (_browser is null)
        {
            return;
        }

        _browser.Bounds = new Rectangle(1, 1, Math.Max(0, ClientSize.Width - 2), Math.Max(0, ClientSize.Height - 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var color = _isDarkTheme ? Color.FromArgb(51, 65, 85) : Color.FromArgb(148, 163, 184);
        using var pen = new Pen(color, 1);
        var rect = ClientRectangle;
        e.Graphics.DrawRectangle(pen, 0, 0, rect.Width - 1, rect.Height - 1);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcPaint || m.Msg == WmNcActivate)
        {
            m.Result = (nint)1;
            return;
        }

        if (m.Msg == WmSetCursor && m.WParam == Handle)
        {
            var hitTest = (int)(m.LParam.ToInt64() & 0xFFFF);
            var cursorId = hitTest switch
            {
                HtLeft or HtRight => IdcSizewe,
                HtTop or HtBottom => IdcSizens,
                HtTopLeft or HtBottomRight => IdcSizenwse,
                HtTopRight or HtBottomLeft => IdcSizenesw,
                _ => 0
            };
            if (cursorId != 0)
            {
                var hCursor = LoadCursor(nint.Zero, cursorId);
                if (hCursor != nint.Zero)
                {
                    SetCursor(hCursor);
                    m.Result = (nint)1;
                    return;
                }
            }
        }

        if (m.Msg == WmNcCalcSize && m.WParam != nint.Zero)
        {
            m.Result = nint.Zero;
            return;
        }

        if (m.Msg == WmNcHitTest)
        {
            var screenPoint = new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
            var point = PointToClient(screenPoint);
            var hitTest = HitTestResizeBorder(point);
            if (hitTest != HtClient)
            {
                m.Result = hitTest;
                return;
            }
        }

        base.WndProc(ref m);
    }

    private int DpiScaledResizeGrip => (int)Math.Round(BaseResizeGripSize * (DeviceDpi / 96d));

    private nint HitTestResizeBorder(Point point)
    {
        var gripSize = DpiScaledResizeGrip;
        var left = point.X <= gripSize;
        var right = point.X >= ClientSize.Width - gripSize;
        var top = point.Y <= gripSize;
        var bottom = point.Y >= ClientSize.Height - gripSize;

        return (top, bottom, left, right) switch
        {
            (true, false, true, false) => HtTopLeft,
            (true, false, false, true) => HtTopRight,
            (false, true, true, false) => HtBottomLeft,
            (false, true, false, true) => HtBottomRight,
            (true, false, false, false) => HtTop,
            (false, true, false, false) => HtBottom,
            (false, false, true, false) => HtLeft,
            (false, false, false, true) => HtRight,
            _ => HtClient
        };
    }

    private void ApplyRoundedWindow()
    {
        NotifyFrontendWindowRadius();
    }

    private void NotifyFrontendWindowRadius()
    {
        if (_browser?.CoreWebView2 is null)
        {
            return;
        }

        _ = _browser.CoreWebView2.ExecuteScriptAsync(
            "document.documentElement.style.setProperty('--onedesk-window-radius', '0px')");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int message, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern nint LoadCursor(nint hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    private static extern bool SetCursor(nint hCursor);

    private async Task InitializeServicesAsync()
    {
        AppDiagnostics.Write("Service initialization entered.");
        var collection = new ServiceCollection();
        collection.AddSingleton<DeviceRegistry>();
        collection.AddSingleton<CapabilityDirectoryService>();
        collection.AddSingleton<PermissionService>();
        collection.AddSingleton<StructuredLogStore>();
        collection.AddSingleton<PairingService>();
        collection.AddSingleton<QuicGatewayService>();
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
        _packages = _services.GetRequiredService<SchemePackageService>();
        _permissions = _services.GetRequiredService<PermissionService>();
        _capabilityDirectory = _services.GetRequiredService<CapabilityDirectoryService>();
        _pairing = _services.GetRequiredService<PairingService>();
        _gateway = _services.GetRequiredService<QuicGatewayService>();
        _plugins = _services.GetRequiredService<PluginHostService>();
        _logs = _services.GetRequiredService<StructuredLogStore>();
        AppDiagnostics.Write("Core services resolved.");
        await _services.GetRequiredService<WorkspaceBootstrapper>().EnsureSeedDataAsync();
        await _gateway.StartAsync();
        AppDiagnostics.Write("Service initialization completed.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loadingLabel?.Dispose();
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
            }
            _notifyIcon?.Dispose();
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
                DefaultBackgroundColor = Color.Transparent
            };
            Controls.Add(_browser);
            _browser.BringToFront();
            LayoutBrowser();
            ApplyRoundedWindow();
            RemoveLoadingSurface();
            AppDiagnostics.Write("WebView2 control created.");

            await _browser.EnsureCoreWebView2Async();
            AppDiagnostics.Write("WebView2 initialized.");
            // WebView2 自己处理 DPI 感知渲染。ZoomFactor=1 表示浏览器视口按 Per-Monitor V2 的逻辑像素工作，
            // 避免壳子和前端同时缩放导致布局被二次放大或压缩。
            _browser.ZoomFactor = 1d;
            AppDiagnostics.Write($"WebView2 zoom factor set to 1 (DPI-aware mode); DeviceDpi={DeviceDpi}.");
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

    private void Browser_OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        var reason = e.Reason.ToString();
        var kind = e.ProcessFailedKind.ToString();
        var message = $"WebView2 进程异常终止：{kind} / {reason} / {e.ExitCode}";
        AppDiagnostics.Write(message);
        _logs?.Append(
            _devices?.DesktopIdentity.DeviceId ?? "desktop",
            "Error",
            "Chromium",
            message,
            new Dictionary<string, object?>
            {
                ["processFailedKind"] = kind,
                ["reason"] = reason,
                ["exitCode"] = e.ExitCode,
                ["processDescription"] = e.ProcessDescription,
                ["source"] = _browser?.Source?.ToString() ?? ""
            });
    }

    private async void Browser_OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        AppDiagnostics.Write($"Navigation completed. Success={e.IsSuccess}; WebError={e.WebErrorStatus}; Uri={_browser?.Source}");
        if (!e.IsSuccess || _browser?.CoreWebView2 is null)
        {
            return;
        }

        NotifyFrontendWindowRadius();

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
        string? requestId = null;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            if (document.RootElement.TryGetProperty("type", out var typeElement) &&
                typeElement.GetString() == "diagnostic.error")
            {
                AppDiagnostics.Write($"Frontend error: {e.WebMessageAsJson}");
                return;
            }

            if (document.RootElement.TryGetProperty("requestId", out var requestIdElement) &&
                requestIdElement.ValueKind == JsonValueKind.String)
            {
                requestId = requestIdElement.GetString();
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Write($"Bridge message parse failed: {ex}");
            return;
        }

        BridgeMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<BridgeMessage>(e.WebMessageAsJson, JsonOptions);
            if (message is null)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Write($"Bridge message deserialize failed: {ex}");
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                PostBridgeResponse(new BridgeResponse(requestId, false, null, "InvalidPayload", "请求参数不完整或格式不正确"));
            }

            return;
        }

        object response;
        try
        {
            response = message.Type switch
            {
                "getDeviceId" => new BridgeResponse(message.RequestId, true, _devices?.DesktopIdentity.DeviceId),
                "callJsApi" => await HandleJsApiAsync(message),
                "workspace.list" => await HandleWorkspaceListAsync(message),
                "workspace.saveComponent" => await HandleSaveComponentAsync(message),
                "workspace.readComponentFiles" => await HandleReadComponentFilesAsync(message),
                "workspace.saveComponentFiles" => await HandleSaveComponentFilesAsync(message),
                "resource.list" => await HandleResourceListAsync(message),
                "resource.add" => await HandleResourceAddAsync(message),
                "resource.delete" => HandleResourceDelete(message),
                "resource.copyToComponent" => await HandleResourceCopyToComponentAsync(message),
                "resource.copyToPage" => await HandleResourceCopyToPageAsync(message),
                "workspace.deleteComponent" => HandleDeleteComponent(message),
                "workspace.saveAction" => await HandleSaveActionAsync(message),
                "workspace.deleteAction" => HandleDeleteAction(message),
                "workspace.savePage" => await HandleSavePageAsync(message),
                "workspace.deletePage" => HandleDeletePage(message),
                "workspace.saveScheme" => await HandleSaveSchemeAsync(message),
                "workspace.deleteScheme" => HandleDeleteScheme(message),
                "workspace.applyScheme" => await HandleApplySchemeAsync(message),
                "workspace.exportComponent" => await HandleExportComponentAsync(message),
                "workspace.exportPage" => await HandleExportPageAsync(message),
                "workspace.exportScheme" => await HandleExportSchemeAsync(message),
                "workspace.inspectImport" => HandleInspectWorkspaceImport(message),
                "workspace.confirmImport" => HandleConfirmWorkspaceImport(message),
                "workspace.importComponent" => HandleImportComponent(message),
                "workspace.importPage" => HandleImportPage(message),
                "workspace.importScheme" => HandleImportScheme(message),
                "capability.list" => new BridgeResponse(message.RequestId, true, _capabilityDirectory?.Categories() ?? []),
                "permission.list" => new BridgeResponse(message.RequestId, true, new
                {
                    grants = _permissions?.ListGrants() ?? [],
                    categories = _capabilityDirectory?.Categories() ?? []
                }),
                "permission.grant" => HandlePermissionGrant(message),
                "permission.revoke" => HandlePermissionRevoke(message),
                "pairing.generate" => HandlePairingGenerate(message),
                "device.status" => HandleDeviceStatus(message),
                "device.rename" => HandleDeviceRename(message),
                "gateway.status" => HandleGatewayStatus(message),
                "scheme.cacheManifest" => await HandleSchemeCacheManifestAsync(message),
                "plugin.list" => new BridgeResponse(message.RequestId, true, _plugins?.InstalledPlugins ?? []),
                "plugin.inspectImport" => HandleInspectPluginImport(message),
                "plugin.confirmImport" => await HandleConfirmPluginImportAsync(message),
                "plugin.import" => await HandlePluginImportAsync(message),
                "plugin.delete" => await HandlePluginDeleteAsync(message),
                "plugin.submitSettings" => await HandlePluginSubmitSettingsAsync(message),
                "log.list" => new BridgeResponse(message.RequestId, true, _logs?.Recent() ?? []),
                "window.minimize" => HandleWindowMinimize(message),
                "window.maximize" => HandleWindowMaximize(message),
                "window.dragStart" => HandleWindowDragStart(message),
                "window.resizeStart" => HandleWindowResizeStart(message),
                "window.close" => HandleWindowClose(message),
                "window.theme" => HandleWindowTheme(message),
                _ => new BridgeResponse(message.RequestId, false, null, "CapabilityNotSupported", "未知 OneDesk 桥接请求")
            };
        }
        catch (Exception ex)
        {
            AppDiagnostics.Write($"Bridge request failed: {message.Type}; {ex}");
            response = new BridgeResponse(message.RequestId, false, null, "BridgeRequestFailed", ex.Message);
        }

        PostBridgeResponse(response);
    }

    private void PostBridgeResponse(object response)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        _browser?.CoreWebView2?.PostWebMessageAsJson(json);
    }

    private async Task<BridgeResponse> HandleJsApiAsync(BridgeMessage message)
    {
        if (message.Source?.Kind is "frontend" or null)
        {
            return new BridgeResponse(message.RequestId, false, null, "InvalidSourceIdentity", "JSAPI 调用必须由可信组件、插件或系统运行容器注入来源身份");
        }

        if (message.Source?.Kind == "component" && (string.IsNullOrWhiteSpace(message.Source.ComponentId) || _repository is null || await _repository.GetComponentAsync(message.Source.ComponentId) is null))
        {
            return new BridgeResponse(message.RequestId, false, null, "InvalidSourceIdentity", "组件来源不存在，已拒绝 JSAPI 调用");
        }

        if (message.Source?.Kind == "plugin" && (string.IsNullOrWhiteSpace(message.Source.PluginId) || _plugins?.InstalledPlugins.Any(plugin => plugin.Id == message.Source.PluginId) != true))
        {
            return new BridgeResponse(message.RequestId, false, null, "InvalidSourceIdentity", "插件来源不存在，已拒绝 JSAPI 调用");
        }

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
        if (result.Ok &&
            request.Capability == "notification.native" &&
            request.TargetDeviceId == (_devices?.DesktopIdentity.DeviceId ?? "desktop"))
        {
            ShowNativeNotification(request.Payload);
        }
        return new BridgeResponse(message.RequestId, result.Ok, result.Payload, result.ErrorCode, result.Message);
    }

    private void ShowNativeNotification(object? payload)
    {
        var title = ReadJsonString(payload, "title", "OneDesk");
        var message = ReadJsonString(payload, "message", "OneDesk 通知");
        EnsureTrayIcon();
        _notifyIcon?.ShowBalloonTip(4000, title, message, ToolTipIcon.Info);
    }

    private void EnsureTrayIcon()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = true;
            return;
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("显示程序", null, (_, _) => ShowMainWindow());
        menu.Items.Add("退出程序", null, (_, _) => ConfirmExitFromTray());

        _notifyIcon = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Visible = true,
            Text = "OneDesk",
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        EnsureTrayIcon();
        Hide();
        ShowInTaskbar = false;
    }

    private void ConfirmExitFromTray()
    {
        var result = MessageBox.Show(
            "是否退出 OneDesk 程序？",
            "退出程序",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return;
        }

        _allowExit = true;
        _notifyIcon!.Visible = false;
        Close();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowExit)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private async Task<BridgeResponse> HandleWorkspaceListAsync(BridgeMessage message)
    {
        object components = _repository is null ? Array.Empty<object>() : await _repository.ListComponentsAsync();
        object actions = _repository is null ? Array.Empty<object>() : await _repository.ListActionsAsync();
        object pages = _repository is null ? Array.Empty<object>() : await _repository.ListPagesAsync();
        object schemes = _repository is null ? Array.Empty<object>() : await _repository.ListSchemesAsync();
        object? activeScheme = _repository is null ? null : await _repository.GetActiveSchemeAsync();
        var payload = new
        {
            components,
            actions,
            pages,
            schemes,
            activeScheme,
            devices = _devices?.All() ?? []
        };
        return new BridgeResponse(message.RequestId, true, payload);
    }

    private async Task<BridgeResponse> HandleSaveComponentAsync(BridgeMessage message)
    {
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        var component = DeserializePayload<ComponentDefinition>(message);
        if (component is null)
        {
            return InvalidPayload(message);
        }

        await _repository.SaveComponentAsync(component);
        return new BridgeResponse(message.RequestId, true, component);
    }

    private async Task<BridgeResponse> HandleReadComponentFilesAsync(BridgeMessage message)
    {
        var id = ReadPayloadString(message, "id");
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return InvalidPayload(message);
        }

        try
        {
            return new BridgeResponse(message.RequestId, true, await _repository.ReadComponentFilesAsync(id));
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "ComponentFileReadFailed", ex.Message);
        }
    }

    private async Task<BridgeResponse> HandleSaveComponentFilesAsync(BridgeMessage message)
    {
        var payload = DeserializePayload<ComponentFilesPayload>(message);
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Id))
        {
            return InvalidPayload(message);
        }

        try
        {
            await _repository.SaveComponentFilesAsync(payload.Id, payload.Files);
            return new BridgeResponse(message.RequestId, true, await _repository.ReadComponentFilesAsync(payload.Id));
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "ComponentFileSaveFailed", ex.Message);
        }
    }

    private BridgeResponse HandleDeleteComponent(BridgeMessage message)
    {
        var id = ReadPayloadString(message, "id");
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return InvalidPayload(message);
        }

        _repository.DeleteComponent(id);
        return new BridgeResponse(message.RequestId, true, null);
    }

    private async Task<BridgeResponse> HandleSaveActionAsync(BridgeMessage message)
    {
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        var action = DeserializePayload<ActionDefinition>(message);
        if (action is null)
        {
            return InvalidPayload(message);
        }

        await _repository.SaveActionAsync(action);
        return new BridgeResponse(message.RequestId, true, action);
    }

    private BridgeResponse HandleDeleteAction(BridgeMessage message)
    {
        var id = ReadPayloadString(message, "id");
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return InvalidPayload(message);
        }

        _repository.DeleteAction(id);
        return new BridgeResponse(message.RequestId, true, null);
    }

    private async Task<BridgeResponse> HandleSavePageAsync(BridgeMessage message)
    {
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        var page = DeserializePayload<PageDefinition>(message);
        if (page is null)
        {
            return InvalidPayload(message);
        }

        await _repository.SavePageAsync(page);
        return new BridgeResponse(message.RequestId, true, page);
    }

    private BridgeResponse HandleDeletePage(BridgeMessage message)
    {
        var id = ReadPayloadString(message, "id");
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return InvalidPayload(message);
        }

        _repository.DeletePage(id);
        return new BridgeResponse(message.RequestId, true, null);
    }

    private async Task<BridgeResponse> HandleSaveSchemeAsync(BridgeMessage message)
    {
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        var scheme = DeserializePayload<SchemeDefinition>(message);
        if (scheme is null)
        {
            return InvalidPayload(message);
        }

        await _repository.SaveSchemeAsync(scheme);
        return new BridgeResponse(message.RequestId, true, scheme);
    }

    private BridgeResponse HandleDeleteScheme(BridgeMessage message)
    {
        var id = ReadPayloadString(message, "id");
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return InvalidPayload(message);
        }

        _repository.DeleteScheme(id);
        return new BridgeResponse(message.RequestId, true, null);
    }

    private async Task<BridgeResponse> HandleApplySchemeAsync(BridgeMessage message)
    {
        var id = ReadPayloadString(message, "id");
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        if (string.IsNullOrWhiteSpace(id) || await _repository.GetSchemeAsync(id) is null)
        {
            return new BridgeResponse(message.RequestId, false, null, "SchemeNotFound", "方案不存在，无法应用");
        }

        var deviceId = ReadPayloadString(message, "deviceId");
        await _repository.ApplySchemeAsync(id, string.IsNullOrWhiteSpace(deviceId) ? null : deviceId);
        return new BridgeResponse(message.RequestId, true, await _repository.GetActiveSchemeAsync(deviceId));
    }

    private async Task<BridgeResponse> HandleExportComponentAsync(BridgeMessage message)
    {
        var id = ReadPayloadString(message, "id");
        if (_packages is null)
        {
            return ShellNotReady(message);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return InvalidPayload(message);
        }

        try
        {
            return new BridgeResponse(message.RequestId, true, await _packages.ExportComponentByIdAsync(id));
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "ExportFailed", ex.Message);
        }
    }

    private async Task<BridgeResponse> HandleExportPageAsync(BridgeMessage message)
    {
        var id = ReadPayloadString(message, "id");
        if (_packages is null)
        {
            return ShellNotReady(message);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return InvalidPayload(message);
        }

        try
        {
            return new BridgeResponse(message.RequestId, true, await _packages.ExportPageByIdAsync(id));
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "ExportFailed", ex.Message);
        }
    }

    private async Task<BridgeResponse> HandleExportSchemeAsync(BridgeMessage message)
    {
        var id = ReadPayloadString(message, "id");
        if (_packages is null)
        {
            return ShellNotReady(message);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return InvalidPayload(message);
        }

        try
        {
            return new BridgeResponse(message.RequestId, true, await _packages.ExportSchemeByIdAsync(id));
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "ExportFailed", ex.Message);
        }
    }

    private BridgeResponse HandleInspectWorkspaceImport(BridgeMessage message)
    {
        var kind = ReadPayloadString(message, "kind");
        if (kind is not ("Component" or "Page" or "Scheme"))
        {
            return InvalidPayload(message);
        }

        var (title, filter) = kind switch
        {
            "Component" => ("组件包", "OneDesk Component Package (*.zip;*.onedesk-component)|*.zip;*.onedesk-component"),
            "Page" => ("页面包", "OneDesk Page Package (*.zip;*.onedesk-page)|*.zip;*.onedesk-page"),
            _ => ("方案包", "OneDesk Scheme Package (*.zip;*.onedesk-scheme)|*.zip;*.onedesk-scheme")
        };

        using var dialog = new OpenFileDialog
        {
            Title = $"选择{title}",
            Filter = $"{filter}|All Files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return new BridgeResponse(message.RequestId, false, null, "UserCancelled", "已取消导入");
        }

        try
        {
            var inspection = InspectWorkspacePackage(kind, dialog.FileName);
            var token = Guid.NewGuid().ToString("N");
            _pendingPackageImports[token] = new PendingPackageImport(token, kind, dialog.FileName, inspection.SourceKeys);
            return new BridgeResponse(message.RequestId, true, inspection with { Token = token });
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "ImportInspectionFailed", ex.Message);
        }
    }

    private BridgeResponse HandleConfirmWorkspaceImport(BridgeMessage message)
    {
        if (_packages is null)
        {
            return ShellNotReady(message);
        }

        var token = ReadPayloadString(message, "token");
        if (string.IsNullOrWhiteSpace(token) || !_pendingPackageImports.Remove(token, out var pending))
        {
            return new BridgeResponse(message.RequestId, false, null, "ImportSessionExpired", "导入会话不存在或已过期");
        }

        try
        {
            var installedPluginIds = new HashSet<string>(_plugins?.InstalledPlugins.Select(plugin => plugin.Id) ?? [], StringComparer.OrdinalIgnoreCase);
            var result = pending.Kind switch
            {
                "Component" => _packages.ImportComponent(pending.Path),
                "Page" => _packages.ImportPage(pending.Path),
                "Scheme" => _packages.ImportScheme(pending.Path, installedPluginIds),
                _ => throw new InvalidOperationException("Unsupported import kind.")
            };

            GrantImportedPermissions(message, pending.SourceKeys);
            return new BridgeResponse(message.RequestId, result.Ready, result, result.Ready ? null : "DependencyMissing", result.Ready ? null : "导入完成，但存在缺失或冲突的插件依赖");
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "ImportFailed", ex.Message);
        }
    }

    private PackageInspection InspectWorkspacePackage(string kind, string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var permissions = new List<PermissionDeclaration>();
        var sourceKeys = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var pluginDependencies = new List<DependencyDefinition>();
        var title = Path.GetFileName(packagePath);

        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            if (entry.FullName.EndsWith("onedesk.component.json", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = entry.Open();
                var component = JsonSerializer.Deserialize<ComponentDefinition>(stream, JsonOptions);
                if (component is null)
                {
                    continue;
                }

                title = component.Name;
                var componentPermissions = component.RequestedPermissions
                    .Select(permission => new PermissionDeclaration(permission.Category, permission.Capability, permission.HighRisk, permission.Description))
                    .ToArray();
                permissions.AddRange(componentPermissions);
                sourceKeys[$"component:{component.Id}"] = componentPermissions.Select(permission => permission.Capability).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                pluginDependencies.AddRange(component.PluginDependencies);
            }

            if (entry.FullName.EndsWith("onedesk.scheme.json", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = entry.Open();
                var scheme = JsonSerializer.Deserialize<SchemeDefinition>(stream, JsonOptions);
                if (scheme is not null)
                {
                    title = scheme.Name;
                    pluginDependencies.AddRange(scheme.PluginDependencies);
                }
            }
        }

        var distinctPermissions = permissions
            .GroupBy(permission => permission.Capability, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(permission => permission.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(permission => permission.Capability, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PackageInspection(
            "",
            kind,
            title,
            packagePath,
            distinctPermissions,
            pluginDependencies
                .GroupBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray(),
            sourceKeys);
    }

    private void GrantImportedPermissions(BridgeMessage message, IReadOnlyDictionary<string, IReadOnlyList<string>> sourceKeys)
    {
        if (_permissions is null)
        {
            return;
        }

        var allowed = ReadPayloadStringArray(message, "grantedCapabilities");
        var allowedSet = allowed.Count == 0
            ? null
            : new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);

        foreach (var (sourceKey, capabilities) in sourceKeys)
        {
            foreach (var capability in capabilities)
            {
                if (allowedSet is null || allowedSet.Contains(capability))
                {
                    _permissions.Grant(sourceKey, capability);
                }
            }
        }
    }

    private BridgeResponse HandleImportComponent(BridgeMessage message)
    {
        return ImportPackage(message, "组件包", "OneDesk Component Package (*.zip;*.onedesk-component)|*.zip;*.onedesk-component", path => _packages!.ImportComponent(path));
    }

    private BridgeResponse HandleImportPage(BridgeMessage message)
    {
        return ImportPackage(message, "页面包", "OneDesk Page Package (*.zip;*.onedesk-page)|*.zip;*.onedesk-page", path => _packages!.ImportPage(path));
    }

    private BridgeResponse HandleImportScheme(BridgeMessage message)
    {
        return ImportPackage(message, "方案包", "OneDesk Scheme Package (*.zip;*.onedesk-scheme)|*.zip;*.onedesk-scheme", path => _packages!.ImportScheme(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    private BridgeResponse ImportPackage(BridgeMessage message, string title, string filter, Func<string, PackageImportResult> importer)
    {
        if (_packages is null)
        {
            return ShellNotReady(message);
        }

        using var dialog = new OpenFileDialog
        {
            Title = $"导入{title}",
            Filter = $"{filter}|All Files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return new BridgeResponse(message.RequestId, false, null, "UserCancelled", "已取消导入");
        }

        try
        {
            return new BridgeResponse(message.RequestId, true, importer(dialog.FileName));
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "ImportFailed", ex.Message);
        }
    }

    private BridgeResponse HandlePermissionGrant(BridgeMessage message)
    {
        var sourceKey = ReadPayloadString(message, "sourceKey");
        var capability = ReadPayloadString(message, "capability");
        if (_permissions is null)
        {
            return ShellNotReady(message);
        }

        if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(capability))
        {
            return InvalidPayload(message);
        }

        _permissions.Grant(sourceKey, capability);
        return new BridgeResponse(message.RequestId, true, _permissions.ListGrants());
    }

    private BridgeResponse HandlePermissionRevoke(BridgeMessage message)
    {
        var sourceKey = ReadPayloadString(message, "sourceKey");
        var capability = ReadPayloadString(message, "capability");
        if (_permissions is null)
        {
            return ShellNotReady(message);
        }

        if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(capability))
        {
            return InvalidPayload(message);
        }

        _permissions.Revoke(sourceKey, capability);
        return new BridgeResponse(message.RequestId, true, _permissions.ListGrants());
    }

    private BridgeResponse HandleDeviceStatus(BridgeMessage message)
    {
        var mobileDevices = (_devices?.All() ?? [])
            .Where(device => device.Kind == DeviceKind.Mobile)
            .ToArray();
        return new BridgeResponse(message.RequestId, true, new
        {
            desktop = _devices?.DesktopIdentity,
            devices = mobileDevices,
            trusted = _pairing?.TrustedDevices() ?? [],
            gateway = GatewayPayload(),
            localIps = GetLocalIpv4Addresses(),
            logs = _logs?.Recent(80) ?? []
        });
    }

    private BridgeResponse HandleDeviceRename(BridgeMessage message)
    {
        var deviceId = ReadPayloadString(message, "deviceId");
        var remark = ReadPayloadString(message, "remark") ?? string.Empty;
        if (_pairing is null)
        {
            return ShellNotReady(message);
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return InvalidPayload(message);
        }

        var renamed = _pairing.RenameTrustedDevice(deviceId, remark);
        return renamed is null
            ? new BridgeResponse(message.RequestId, false, null, "DeviceNotFound", "未找到该移动设备")
            : new BridgeResponse(message.RequestId, true, renamed);
    }

    private BridgeResponse HandleGatewayStatus(BridgeMessage message)
    {
        return new BridgeResponse(message.RequestId, true, GatewayPayload());
    }

    private async Task<BridgeResponse> HandleSchemeCacheManifestAsync(BridgeMessage message)
    {
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        var active = await _repository.GetActiveSchemeAsync();
        if (active is null)
        {
            return new BridgeResponse(message.RequestId, true, null);
        }

        var scheme = await _repository.GetSchemeAsync(active.SchemeId);
        var pages = new List<PageDefinition>();
        var components = new List<ComponentDefinition>();
        if (scheme is not null)
        {
            foreach (var pageId in scheme.PageIds)
            {
                var page = await _repository.GetPageAsync(pageId);
                if (page is null)
                {
                    continue;
                }

                pages.Add(page);
                foreach (var componentId in page.Cells.Select(cell => cell.ComponentId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var component = await _repository.GetComponentAsync(componentId!);
                    if (component is not null)
                    {
                        components.Add(component);
                    }
                }
            }
        }

        var json = JsonSerializer.Serialize(new { active, scheme, pages, components }, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new BridgeResponse(message.RequestId, true, new
        {
            activeSchemeId = active.SchemeId,
            active.AppliedAt,
            pageCount = pages.Count,
            componentCount = components.Select(component => component.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            hash
        });
    }

    private object GatewayPayload()
    {
        return new
        {
            running = _gateway?.IsRunning ?? false,
            port = _gateway?.Port ?? 48320,
            peers = _gateway?.Peers ?? [],
            queuedRequests = _gateway?.QueuedRequests ?? []
        };
    }

    private async Task<BridgeResponse> HandlePluginImportAsync(BridgeMessage message)
    {
        if (_plugins is null || _services is null)
        {
            return ShellNotReady(message);
        }

        using var dialog = new OpenFileDialog
        {
            Title = "导入插件包",
            Filter = "OneDesk Plugin Package (*.onedesk-plugin;*.zip)|*.onedesk-plugin;*.zip|All Files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return new BridgeResponse(message.RequestId, false, null, "UserCancelled", "已取消插件导入");
        }

        try
        {
            var manifest = await InstallPluginPackageAsync(dialog.FileName);
            return new BridgeResponse(message.RequestId, true, manifest);
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "PluginImportFailed", ex.Message);
        }
    }

    private BridgeResponse HandleInspectPluginImport(BridgeMessage message)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择插件包",
            Filter = "OneDesk Plugin Package (*.onedesk-plugin;*.zip)|*.onedesk-plugin;*.zip|All Files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return new BridgeResponse(message.RequestId, false, null, "UserCancelled", "已取消插件导入");
        }

        try
        {
            using var archive = ZipFile.OpenRead(dialog.FileName);
            var manifestEntry = archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith("onedesk.plugin.json", StringComparison.OrdinalIgnoreCase));
            if (manifestEntry is null)
            {
                return new BridgeResponse(message.RequestId, false, null, "PluginManifestMissing", "插件包缺少 onedesk.plugin.json");
            }

            using var stream = manifestEntry.Open();
            var manifest = JsonSerializer.Deserialize<PluginManifest>(stream, JsonOptions);
            if (manifest is null)
            {
                return new BridgeResponse(message.RequestId, false, null, "InvalidPluginManifest", "插件清单格式不正确");
            }

            var token = Guid.NewGuid().ToString("N");
            _pendingPackageImports[token] = new PendingPackageImport(
                token,
                "Plugin",
                dialog.FileName,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [$"plugin:{manifest.Id}"] = manifest.Permissions.Select(permission => permission.Capability).ToArray()
                });

            return new BridgeResponse(message.RequestId, true, new PackageInspection(
                token,
                "Plugin",
                manifest.Name,
                dialog.FileName,
                manifest.Permissions,
                [],
                _pendingPackageImports[token].SourceKeys));
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "PluginInspectionFailed", ex.Message);
        }
    }

    private async Task<BridgeResponse> HandleConfirmPluginImportAsync(BridgeMessage message)
    {
        if (_plugins is null || _services is null)
        {
            return ShellNotReady(message);
        }

        var token = ReadPayloadString(message, "token");
        if (string.IsNullOrWhiteSpace(token) || !_pendingPackageImports.Remove(token, out var pending) || pending.Kind != "Plugin")
        {
            return new BridgeResponse(message.RequestId, false, null, "ImportSessionExpired", "插件导入会话不存在或已过期");
        }

        try
        {
            var manifest = await InstallPluginPackageAsync(pending.Path);
            GrantImportedPermissions(message, pending.SourceKeys);
            return new BridgeResponse(message.RequestId, true, manifest);
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "PluginImportFailed", ex.Message);
        }
    }

    private async Task<PluginManifest> InstallPluginPackageAsync(string packagePath)
    {
        if (_plugins is null || _services is null)
        {
            throw new InvalidOperationException("Plugin service is not ready.");
        }

        var paths = _services.GetRequiredService<OneDeskDataPaths>();
        var pluginRoot = Path.Combine(paths.Plugins, Path.GetFileNameWithoutExtension(packagePath));
        var temp = $"{pluginRoot}.tmp-{Guid.NewGuid():N}";
        SchemePackageService.SafeExtractPackage(packagePath, temp);
        var manifestPath = Directory.EnumerateFiles(temp, "onedesk.plugin.json", SearchOption.AllDirectories).FirstOrDefault();
        if (manifestPath is null)
        {
            Directory.Delete(temp, recursive: true);
            throw new InvalidDataException("插件包缺少 onedesk.plugin.json");
        }

        var manifest = JsonSerializer.Deserialize<PluginManifest>(await File.ReadAllTextAsync(manifestPath), JsonOptions)
            ?? throw new InvalidDataException("插件清单格式不正确");

        if (Directory.Exists(pluginRoot))
        {
            Directory.Delete(pluginRoot, recursive: true);
        }

        Directory.Move(temp, pluginRoot);
        await _plugins.RegisterManifestAsync(manifest, pluginRoot);
        return manifest;
    }

    private async Task<BridgeResponse> HandlePluginSubmitSettingsAsync(BridgeMessage message)
    {
        if (_plugins is null)
        {
            return ShellNotReady(message);
        }

        var pluginId = ReadPayloadString(message, "pluginId");
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return InvalidPayload(message);
        }

        try
        {
            var settings = message.Payload is { ValueKind: JsonValueKind.Object } payload && payload.TryGetProperty("settings", out var value)
                ? value
                : default(JsonElement?);
            var result = await _plugins.SubmitSettingsAsync(pluginId, settings);
            return new BridgeResponse(message.RequestId, true, result);
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "PluginSettingsFailed", ex.Message);
        }
    }

    private async Task<BridgeResponse> HandlePluginDeleteAsync(BridgeMessage message)
    {
        if (_plugins is null)
        {
            return ShellNotReady(message);
        }

        var pluginId = ReadPayloadString(message, "id");
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return InvalidPayload(message);
        }

        try
        {
            var removed = await _plugins.RemoveAsync(pluginId);
            return removed
                ? new BridgeResponse(message.RequestId, true, new { pluginId })
                : new BridgeResponse(message.RequestId, false, null, "PluginNotFound", "插件不存在");
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "PluginDeleteFailed", ex.Message);
        }
    }

    private BridgeResponse HandlePairingGenerate(BridgeMessage message)
    {
        if (_pairing is null)
        {
            return ShellNotReady(message);
        }

        var host = ReadPayloadString(message, "host") ?? GetLocalIpv4Addresses().FirstOrDefault() ?? "127.0.0.1";
        var port = ReadPayloadInt(message, "port", 48320);
        var code = _pairing.GenerateVerificationCode();
        return new BridgeResponse(message.RequestId, true, new
        {
            code,
            expiresInSeconds = 300,
            host,
            port,
            localIps = GetLocalIpv4Addresses(),
            qrPayload = _pairing.CreateQrPayload(string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host, port, code)
        });
    }

    private static IReadOnlyList<string> GetLocalIpv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
            .Select(address => address.Address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private BridgeResponse HandleWindowMinimize(BridgeMessage message)
    {
        WindowState = FormWindowState.Minimized;
        return new BridgeResponse(message.RequestId, true, null);
    }

    private BridgeResponse HandleWindowMaximize(BridgeMessage message)
    {
        var maximized = ToggleMaximize();
        return new BridgeResponse(message.RequestId, true, maximized);
    }

    private BridgeResponse HandleWindowDragStart(BridgeMessage message)
    {
        BeginNativeWindowDrag();
        return new BridgeResponse(message.RequestId, true, null);
    }

    private BridgeResponse HandleWindowResizeStart(BridgeMessage message)
    {
        var edge = message.Payload?.ValueKind == JsonValueKind.String ? message.Payload.Value.GetString() : null;
        var hitTest = edge switch
        {
            "left" => HtLeft,
            "right" => HtRight,
            "top" => HtTop,
            "bottom" => HtBottom,
            "top-left" => HtTopLeft,
            "top-right" => HtTopRight,
            "bottom-left" => HtBottomLeft,
            "bottom-right" => HtBottomRight,
            _ => HtClient
        };

        if (hitTest != HtClient)
        {
            SuspendBlurForDrag();
            ReleaseCapture();
            SendMessage(Handle, WmNcLButtonDown, hitTest, 0);
            ResumeBlurAfterDrag();
        }

        return new BridgeResponse(message.RequestId, hitTest != HtClient, hitTest != HtClient);
    }

    private BridgeResponse HandleWindowClose(BridgeMessage message)
    {
        HideToTray();
        return new BridgeResponse(message.RequestId, true, null);
    }

    private BridgeResponse HandleWindowTheme(BridgeMessage message)
    {
        var theme = message.Payload?.ValueKind == JsonValueKind.String ? message.Payload.Value.GetString() : "light";
        var dark = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);
        Opacity = 1d;
        BackColor = TransparentShellColor;

        ApplyDwmTheme(dark);
        ApplyRoundedWindow();
        return new BridgeResponse(message.RequestId, true, null);
    }

    private bool ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
            ApplyRoundedWindow();
            return false;
        }

        MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
        Region = null;
        WindowState = FormWindowState.Maximized;
        return true;
    }

    private void BeginNativeWindowDrag()
    {
        SuspendBlurForDrag();
        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
        ResumeBlurAfterDrag();
    }

    private void SuspendBlurForDrag()
    {
        SetBlurEnabled(false);
    }

    private void ResumeBlurAfterDrag()
    {
        SetBlurEnabled(true);
    }

    private void SetBlurEnabled(bool enabled)
    {
        var gradient = _isDarkTheme ? unchecked((int)0x800F172A) : unchecked((int)0x80F1F5F9);
        var accent = new AccentPolicy
        {
            AccentState = enabled ? AccentEnableAcrylicBlurBehind : 0,
            AccentFlags = 0,
            GradientColor = gradient,
        };
        var accentSize = Marshal.SizeOf(accent);
        var accentPtr = Marshal.AllocHGlobal(accentSize);
        try
        {
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = accentPtr,
                SizeOfData = accentSize,
            };
            SetWindowCompositionAttribute(Handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    private void RemoveLoadingSurface()
    {
        if (_loadingLabel is null)
        {
            return;
        }

        Controls.Remove(_loadingLabel);
        _loadingLabel.Dispose();
        _loadingLabel = null;
        AppDiagnostics.Write("Loading surface removed.");
    }

    private void ApplyDwmTheme(bool dark)
    {
        _isDarkTheme = dark;
        Invalidate();

        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return;
        }

        try
        {
            var darkMode = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

            var ncPolicy = DwmncrpDisabled;
            _ = DwmSetWindowAttribute(Handle, DwmwaNcRenderingPolicy, ref ncPolicy, sizeof(int));
        }
        catch
        {
            // 不同 Windows 构建的 DWM 深色模式支持不一致；设置失败不影响 WebView 主流程。
        }

        ApplyBlurBehind();
    }

    private void ApplyBlurBehind()
    {
        SetBlurEnabled(true);
    }

    private static T? DeserializePayload<T>(BridgeMessage message)
    {
        if (message.Payload is not { } payload || payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(payload.GetRawText(), JsonOptions);
    }

    private static string? ReadPayloadString(BridgeMessage message, string key)
    {
        if (message.Payload is { ValueKind: JsonValueKind.Object } payload &&
            payload.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static int ReadPayloadInt(BridgeMessage message, string key, int fallback)
    {
        if (message.Payload is { ValueKind: JsonValueKind.Object } payload &&
            payload.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number))
        {
            return number;
        }

        return fallback;
    }

    private static string ReadJsonString(object? payload, string key, string fallback)
    {
        if (payload is JsonElement element &&
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? fallback;
        }

        return fallback;
    }

    private static IReadOnlyList<string> ReadPayloadStringArray(BridgeMessage message, string key)
    {
        if (message.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static BridgeResponse ShellNotReady(BridgeMessage message)
    {
        return new BridgeResponse(message.RequestId, false, null, "ShellNotReady", "OneDesk 桥接服务尚未初始化完成");
    }

    private static BridgeResponse InvalidPayload(BridgeMessage message)
    {
        return new BridgeResponse(message.RequestId, false, null, "InvalidPayload", "请求参数不完整或格式不正确");
    }

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

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
    send(type, payloadJson) {
      return send(type, payloadJson ? { payload: JSON.parse(payloadJson) } : {});
    },
    getDeviceId() {
      return send('getDeviceId').then(raw => JSON.parse(raw).payload);
    },
    callJsApi(targetDeviceId, capability, payloadJson) {
      return send('callJsApi', {
        targetDeviceId,
        capability,
        payload: payloadJson ? JSON.parse(payloadJson) : null,
        source: { kind: 'frontend' }
      });
    },
    callComponentJsApi(componentId, targetDeviceId, capability, payloadJson) {
      return send('callJsApi', {
        targetDeviceId,
        capability,
        payload: payloadJson ? JSON.parse(payloadJson) : null,
        source: { kind: 'component', componentId }
      });
    },
    callPluginJsApi(pluginId, targetDeviceId, capability, payloadJson) {
      return send('callJsApi', {
        targetDeviceId,
        capability,
        payload: payloadJson ? JSON.parse(payloadJson) : null,
        source: { kind: 'plugin', pluginId }
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
    startWindowDrag() {
      return send('window.dragStart');
    },
    startWindowResize(edge) {
      return send('window.resizeStart', { payload: edge });
    },
    closeWindow() {
      return send('window.close');
    },
    setShellTheme(theme) {
      return send('window.theme', { payload: theme });
    }
  };

  window.fetch = () => Promise.reject(new Error('OneDesk blocks direct frontend networking'));
  window.XMLHttpRequest = function () {
    throw new Error('OneDesk blocks direct frontend networking');
  };
  window.WebSocket = function () {
    throw new Error('OneDesk blocks direct frontend networking');
  };
  window.EventSource = function () {
    throw new Error('OneDesk blocks direct frontend networking');
  };
  navigator.sendBeacon = () => {
    throw new Error('OneDesk blocks direct frontend networking');
  };
  document.addEventListener('click', event => {
    const anchor = event.target?.closest?.('a[href]');
    if (!anchor) return;
    const href = anchor.getAttribute('href') || '';
    if (/^(https?:|wss?:)/i.test(href)) {
      event.preventDefault();
      throw new Error('OneDesk blocks direct frontend navigation');
    }
  }, true);
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

    private sealed record PendingPackageImport(
        string Token,
        string Kind,
        string Path,
        IReadOnlyDictionary<string, IReadOnlyList<string>> SourceKeys);

    private sealed record PackageInspection(
        string Token,
        string Kind,
        string Name,
        string PackagePath,
        IReadOnlyList<PermissionDeclaration> Permissions,
        IReadOnlyList<DependencyDefinition> PluginDependencies,
        IReadOnlyDictionary<string, IReadOnlyList<string>> SourceKeys);

    private sealed record ComponentFilesPayload(
        string Id,
        IReadOnlyDictionary<string, string> Files);

    private sealed record ResourceCopyPayload(
        string ResourceId,
        string TargetId);
}
