using OneDesk.Desktop.Shell;

namespace OneDesk.Windows;

/// <summary>
/// 把共享桥接需要的窗口和文件选择能力映射到 WinForms，不在这里承载任何业务规则。
/// </summary>
internal sealed class WinFormsDesktopShellPlatform(MainForm owner) : IDesktopShellPlatform
{
    public Task<string?> PickFileAsync(DesktopFileKind kind, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return owner.InvokeOnUiAsync(() =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = kind switch
                {
                    DesktopFileKind.Media => "添加媒体资源",
                    DesktopFileKind.ComponentPackage => "选择组件包",
                    DesktopFileKind.PagePackage => "选择页面包",
                    DesktopFileKind.SchemePackage => "选择方案包",
                    DesktopFileKind.PluginPackage => "选择插件包",
                    _ => "选择文件",
                },
                Filter = Filter(kind),
                CheckFileExists = true,
                Multiselect = false,
            };
            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        });
    }

    public Task MinimizeAsync() => owner.MinimizeShellAsync();

    public Task<bool> ToggleMaximizeAsync() => owner.ToggleMaximizeShellAsync();

    public Task StartDragAsync() => owner.StartWindowDragAsync();

    public Task StartResizeAsync(string edge) => owner.StartWindowResizeAsync(edge);

    public Task MoveByAsync(double deltaX, double deltaY) => owner.MoveWindowByAsync(deltaX, deltaY);

    public Task CloseToTrayAsync() => owner.CloseToTrayAsync();

    public Task SetThemeAsync(string theme) => owner.SetShellThemeAsync(theme);

    public void Notify(string title, string message) => owner.ShowNativeNotification(title, message);

    private static string Filter(DesktopFileKind kind) => kind switch
    {
        DesktopFileKind.Media => "图片与视频 (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp;*.mp4;*.webm;*.mov;*.mkv;*.avi)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp;*.mp4;*.webm;*.mov;*.mkv;*.avi|所有文件 (*.*)|*.*",
        DesktopFileKind.ComponentPackage => "OneDesk 组件包 (*.zip;*.onedesk-component)|*.zip;*.onedesk-component",
        DesktopFileKind.PagePackage => "OneDesk 页面包 (*.zip;*.onedesk-page)|*.zip;*.onedesk-page",
        DesktopFileKind.SchemePackage => "OneDesk 方案包 (*.zip;*.onedesk-scheme)|*.zip;*.onedesk-scheme",
        DesktopFileKind.PluginPackage => "OneDesk 插件包 (*.zip;*.onedesk-plugin)|*.zip;*.onedesk-plugin",
        _ => "所有文件 (*.*)|*.*",
    };
}
