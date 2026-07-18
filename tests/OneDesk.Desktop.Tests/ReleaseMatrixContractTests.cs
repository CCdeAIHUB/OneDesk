using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class ReleaseMatrixContractTests
{
    [Fact]
    public void ReleaseWorkflow_CoversRequiredDesktopArchitecturesAndMobileShells()
    {
        // 场景：平台声明不能只写在文档或 csproj 中，每个要求的目标都必须进入可执行发布流水线。
        var workflow = File.ReadAllText(Path.Combine(FindWorkspaceRoot(), ".github", "workflows", "release.yml"));
        foreach (var rid in new[] { "win-x64", "win-arm64", "osx-x64", "osx-arm64", "linux-x64", "linux-arm64" })
        {
            Assert.Contains($"rid: {rid}", workflow, StringComparison.Ordinal);
        }
        Assert.Contains(":app:assembleDebug", workflow, StringComparison.Ordinal);
        Assert.Contains("xcodebuild", workflow, StringComparison.Ordinal);
        Assert.Contains("CODE_SIGNING_ALLOWED=NO", workflow, StringComparison.Ordinal);
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
