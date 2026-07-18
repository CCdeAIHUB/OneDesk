namespace OneDesk.Desktop.Shell;

public enum DesktopFileKind
{
    Media,
    ComponentPackage,
    PagePackage,
    SchemePackage,
    PluginPackage,
}

/// <summary>
/// 窗口、托盘和系统文件选择都属于平台壳职责，业务分发器只能依赖此接口。
/// </summary>
public interface IDesktopShellPlatform
{
    Task<string?> PickFileAsync(DesktopFileKind kind, CancellationToken cancellationToken = default);
    Task MinimizeAsync();
    Task<bool> ToggleMaximizeAsync();
    Task StartDragAsync();
    Task StartResizeAsync(string edge);
    Task MoveByAsync(double deltaX, double deltaY);
    Task CloseToTrayAsync();
    Task SetThemeAsync(string theme);
    void Notify(string title, string message);
}
