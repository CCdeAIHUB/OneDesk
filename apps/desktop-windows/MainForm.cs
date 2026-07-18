using System.Drawing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.WinForms;
using OneDesk.Desktop.Services;
using OneDesk.Desktop.Shell;
using OneDesk.Desktop.Storage;

namespace OneDesk.Windows;

/// <summary>
/// Windows 主窗口只负责组合平台能力。工作区、权限、插件和导入导出统一由共享分发器处理。
/// </summary>
public sealed partial class MainForm : Form
{
    private static readonly Size BaseInitialWindowSize = new(1200, 780);

    private WebView2? _browser;
    private ServiceProvider? _services;
    private DesktopBridgeDispatcher? _bridgeDispatcher;
    private WindowsDesktopCapabilityProvider? _windowsCapabilityProvider;
    private QuicGatewayService? _gateway;
    private DeviceRegistry? _devices;
    private StructuredLogStore? _logs;
    private Label? _loadingLabel;
    private NotifyIcon? _notifyIcon;
    private bool _allowExit;
    private bool _isDarkTheme;

    public MainForm()
    {
        AppDiagnostics.Write("MainForm constructor entered.");
        Text = "OneDesk";
        StartPosition = FormStartPosition.CenterScreen;
        var dpiScale = DeviceDpi / 96d;
        Size = new Size(
            (int)Math.Round(BaseInitialWindowSize.Width * dpiScale),
            (int)Math.Round(BaseInitialWindowSize.Height * dpiScale));
        MinimumSize = new Size(
            (int)Math.Round(1120 * dpiScale),
            (int)Math.Round(720 * dpiScale));
        BackColor = Color.Black;
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);

        _loadingLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "OneDesk 正在加载...",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(54, 65, 82),
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Point),
        };
        Controls.Add(_loadingLabel);

        Shown += OnFirstShownAsync;
        FormClosing += MainForm_FormClosing;
    }

    private async void OnFirstShownAsync(object? sender, EventArgs eventArgs)
    {
        Shown -= OnFirstShownAsync;
        AppDiagnostics.Write("MainForm shown.");
        EnsureInitialWindowBounds();
        ApplyDwmTheme(false);
        EnsureTrayIcon();
        await InitializeServicesAsync();
        await InitializeChromiumAsync();
    }

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
        collection.AddSingleton<PluginFrontendSessionRegistry>();
        collection.AddSingleton<DesktopCredentialVault>();
        collection.AddSingleton<IDesktopCapabilityProvider, PortableDesktopCapabilityProvider>();
        collection.AddSingleton<IDesktopCapabilityProvider, DesktopSchemeCapabilityProvider>();

        _windowsCapabilityProvider = new WindowsDesktopCapabilityProvider(this, ShowNativeNotification, ShowInAppNotification);
        collection.AddSingleton<IDesktopCapabilityProvider>(_windowsCapabilityProvider);
        collection.AddSingleton<JsApiRouter>();
        collection.AddSingleton<FrontendNetworkPolicy>();
        collection.AddSingleton<PortableDesktopSettingsService>();

        var platform = new WinFormsDesktopShellPlatform(this);
        collection.AddSingleton(platform);
        collection.AddSingleton<IDesktopShellPlatform>(platform);
        collection.AddSingleton<DesktopBridgeDispatcher>();

        _services = collection.BuildServiceProvider();
        _devices = _services.GetRequiredService<DeviceRegistry>();
        _logs = _services.GetRequiredService<StructuredLogStore>();
        _gateway = _services.GetRequiredService<QuicGatewayService>();
        _gateway.AttachJsApiRouter(_services.GetRequiredService<JsApiRouter>());
        _bridgeDispatcher = _services.GetRequiredService<DesktopBridgeDispatcher>();
        await _bridgeDispatcher.LoadInstalledPluginsAsync();

        var settings = await _services.GetRequiredService<PortableDesktopSettingsService>().LoadAsync();
        await _gateway.StartAsync(settings.GatewayPort);
        AppDiagnostics.Write("Service initialization completed.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loadingLabel?.Dispose();
            if (_notifyIcon is not null) _notifyIcon.Visible = false;
            _notifyIcon?.ContextMenuStrip?.Dispose();
            _notifyIcon?.Dispose();
            _windowsCapabilityProvider?.Dispose();
            _browser?.Dispose();
            _services?.Dispose();
        }

        base.Dispose(disposing);
    }
}
