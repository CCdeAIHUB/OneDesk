using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class WindowsShellArchitectureTests
{
    [Fact]
    public void WebViewBridge_DelegatesBusinessRequestsToSharedDispatcher()
    {
        // 场景：Windows 壳不得重新维护一份工作区和插件分发 switch，否则平台行为会再次分叉。
        var root = FindWorkspaceRoot();
        var chromium = File.ReadAllText(Path.Combine(root, "apps", "desktop-windows", "MainForm.Chromium.cs"));
        var main = File.ReadAllText(Path.Combine(root, "apps", "desktop-windows", "MainForm.cs"));

        Assert.Contains("_bridgeDispatcher.DispatchAsync(request)", chromium, StringComparison.Ordinal);
        Assert.DoesNotContain("workspace.saveComponent", chromium, StringComparison.Ordinal);
        Assert.DoesNotContain("plugin.confirmImport", chromium, StringComparison.Ordinal);
        Assert.DoesNotContain("_repository", main, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleSaveComponentAsync", main, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleConfirmPluginImportAsync", main, StringComparison.Ordinal);
    }

    [Fact]
    public void WebView_CoversWholeClientAreaAndLeavesCornerClippingToDwm()
    {
        // 场景：前端与壳子分别留边或裁圆角会在四角形成空白缝隙。
        var root = FindWorkspaceRoot();
        var window = File.ReadAllText(Path.Combine(root, "apps", "desktop-windows", "MainForm.Window.cs"));

        Assert.Contains("_browser.Bounds = ClientRectangle", window, StringComparison.Ordinal);
        Assert.Contains("DwmwaWindowCornerPreference", window, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientSize.Width - 2", window, StringComparison.Ordinal);
    }

    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "pnpm-workspace.yaml")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new DirectoryNotFoundException("未找到 OneDesk 工作区根目录");
    }
}
