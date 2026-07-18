using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OneDesk.Desktop.Services;
using OneDesk.Desktop.Shell;
using OneDesk.Desktop.Storage;
using Xilium.CefGlue.Avalonia;

namespace OneDesk.Desktop;

public sealed class MainWindow : Window
{
    private readonly AvaloniaCefBrowser _browser;
    private readonly StructuredLogStore _logs;
    private readonly DeviceRegistry _devices;

    public MainWindow(
        DesktopNativeBridge nativeBridge,
        AvaloniaDesktopShellPlatform platform,
        FrontendNetworkPolicy networkPolicy,
        StructuredLogStore logs,
        DeviceRegistry devices)
    {
        _logs = logs;
        _devices = devices;
        platform.Attach(this);

        Title = "OneDesk";
        Width = 1200;
        Height = 780;
        MinWidth = 1120;
        MinHeight = 720;
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.Transparent,
        ];
        Background = Brushes.Transparent;
        Icon = TrayIconFactory.CreateOneDeskIcon();

        var frontendRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var indexPath = Path.Combine(frontendRoot, "index.html");
        if (!File.Exists(indexPath)) throw new FileNotFoundException("未找到桌面前端入口文件", indexPath);

        networkPolicy.BlockDirectFrontendNetworking = true;
        _browser = new AvaloniaCefBrowser
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            RequestHandler = new CefNetworkRequestHandler(networkPolicy, frontendRoot),
        };
        _browser.RegisterJavascriptObject(nativeBridge, "OneDeskNative");
        _browser.LoadError += (_, eventArgs) => LogBrowserError("CEF 页面加载失败", eventArgs.ErrorText);
        _browser.JavascriptUncaughException += (_, eventArgs) => LogBrowserError("CEF JavaScript 未捕获异常", eventArgs.Message);
        Content = _browser;
        Opened += (_, _) => _browser.Address = new Uri(indexPath).AbsoluteUri;
    }

    private void LogBrowserError(string message, string error)
    {
        _logs.Append(_devices.DesktopIdentity.DeviceId, "Error", "Chromium", message, new Dictionary<string, object?>
        {
            ["error"] = error,
            ["address"] = _browser.Address,
        });
    }
}
