using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class MobileFrontendRenderingContractTests
{
    [Fact]
    public void PageTransitionsKeepTheIncomingPageBehindTheOutgoingPage()
    {
        // 场景：切换页面时新旧页面必须同时渲染，禁止先卸载旧页面再显示新页面而露出白底。
        var appSource = File.ReadAllText(Path.Combine(FindWorkspaceRoot(), "frontends", "mobile", "src", "App.vue"));

        Assert.DoesNotContain("mode=\"out-in\"", appSource, StringComparison.Ordinal);
        Assert.Contains(":style=\"pageStageStyle\"", appSource, StringComparison.Ordinal);
        Assert.Contains("class=\"page-surface", appSource, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualComponentFillsItsGridCellWithoutOuterMargin()
    {
        // 场景：组件内容必须贴合格子边框，组件配置不能给 100% 尺寸的根节点增加外边距并造成偏移。
        var rendererSource = File.ReadAllText(Path.Combine(FindWorkspaceRoot(), "frontends", "mobile", "src", "schemeRenderer.ts"));

        Assert.DoesNotContain("margin: `${Math.max(0, config.base?.margin ?? 0)}px`", rendererSource, StringComparison.Ordinal);
        Assert.Contains("boxSizing: \"border-box\"", rendererSource, StringComparison.Ordinal);
    }

    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "pnpm-workspace.yaml")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("OneDesk workspace root was not found.");
    }
}
