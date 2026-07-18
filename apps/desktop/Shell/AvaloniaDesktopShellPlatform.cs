using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;

namespace OneDesk.Desktop.Shell;

/// <summary>
/// Avalonia 平台适配只保存窗口手势和调用系统选择器，不包含工作区业务逻辑。
/// </summary>
public sealed class AvaloniaDesktopShellPlatform : IDesktopShellPlatform
{
    private Window? _window;
    private PointerPressedEventArgs? _lastPointerPress;

    public void Attach(Window window)
    {
        _window = window;
        window.PointerPressed += (_, eventArgs) => _lastPointerPress = eventArgs;
    }

    public async Task<string?> PickFileAsync(DesktopFileKind kind, CancellationToken cancellationToken = default)
    {
        var window = WindowOrThrow();
        var files = await Dispatcher.UIThread.InvokeAsync(() => window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = kind switch
            {
                DesktopFileKind.Media => "添加媒体资源",
                DesktopFileKind.ComponentPackage => "选择组件包",
                DesktopFileKind.PagePackage => "选择页面包",
                DesktopFileKind.SchemePackage => "选择方案包",
                _ => "选择插件包",
            },
            AllowMultiple = false,
            FileTypeFilter = [FileType(kind)],
        }));
        cancellationToken.ThrowIfCancellationRequested();
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public Task MinimizeAsync() => OnUiAsync(() => WindowOrThrow().WindowState = WindowState.Minimized);

    public async Task<bool> ToggleMaximizeAsync()
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = WindowOrThrow();
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return window.WindowState == WindowState.Maximized;
        });
    }

    public Task StartDragAsync() => OnUiAsync(() =>
    {
        if (_lastPointerPress is null) throw new InvalidOperationException("WindowDragPointerMissing");
        WindowOrThrow().BeginMoveDrag(_lastPointerPress);
    });

    public Task StartResizeAsync(string edge) => OnUiAsync(() =>
    {
        if (_lastPointerPress is null) throw new InvalidOperationException("WindowResizePointerMissing");
        WindowOrThrow().BeginResizeDrag(ParseEdge(edge), _lastPointerPress);
    });

    public Task MoveByAsync(double deltaX, double deltaY) => OnUiAsync(() =>
    {
        var window = WindowOrThrow();
        window.Position = new PixelPoint(window.Position.X + (int)Math.Round(deltaX), window.Position.Y + (int)Math.Round(deltaY));
    });

    public Task CloseToTrayAsync() => OnUiAsync(() => WindowOrThrow().Hide());

    public Task SetThemeAsync(string theme) => OnUiAsync(() =>
    {
        Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
    });

    public void Notify(string title, string message)
    {
        // Avalonia 没有统一的气泡通知接口；系统通知由平台 JSAPI provider 处理，此接口只保留壳边界。
    }

    public void ShowWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var window = WindowOrThrow();
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        });
    }

    private Window WindowOrThrow() => _window ?? throw new InvalidOperationException("DesktopWindowNotAttached");

    private static Task OnUiAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    private static WindowEdge ParseEdge(string edge) => edge.ToLowerInvariant() switch
    {
        "left" => WindowEdge.West,
        "right" => WindowEdge.East,
        "top" => WindowEdge.North,
        "bottom" => WindowEdge.South,
        "top-left" => WindowEdge.NorthWest,
        "top-right" => WindowEdge.NorthEast,
        "bottom-left" => WindowEdge.SouthWest,
        "bottom-right" => WindowEdge.SouthEast,
        _ => throw new ArgumentOutOfRangeException(nameof(edge), "未知窗口缩放方向"),
    };

    private static FilePickerFileType FileType(DesktopFileKind kind) => kind switch
    {
        DesktopFileKind.Media => new FilePickerFileType("图片与视频")
        {
            Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp", "*.mp4", "*.webm", "*.mov", "*.mkv", "*.avi"],
        },
        DesktopFileKind.ComponentPackage => new FilePickerFileType("OneDesk 组件包") { Patterns = ["*.zip", "*.onedesk-component"] },
        DesktopFileKind.PagePackage => new FilePickerFileType("OneDesk 页面包") { Patterns = ["*.zip", "*.onedesk-page"] },
        DesktopFileKind.SchemePackage => new FilePickerFileType("OneDesk 方案包") { Patterns = ["*.zip", "*.onedesk-scheme"] },
        _ => new FilePickerFileType("OneDesk 插件包") { Patterns = ["*.zip", "*.onedesk-plugin"] },
    };
}
