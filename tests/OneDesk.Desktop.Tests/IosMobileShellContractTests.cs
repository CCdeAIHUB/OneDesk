using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class IosMobileShellContractTests
{
    [Fact]
    public void GeneratedIosCapabilityCatalog_MatchesCanonicalCatalog()
    {
        // 场景：能力目录新增或调整高危标记时，iOS 不能继续使用旧的手写集合。
        var root = FindWorkspaceRoot();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "packages", "protocol", "capabilities.json")));
        var expected = catalog.RootElement.GetProperty("capabilities")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("id").GetString()!,
                item => item.GetProperty("risk").GetString() == "high",
                StringComparer.Ordinal);
        var generated = File.ReadAllText(Path.Combine(root, "apps", "mobile", "ios", "GeneratedCapabilityCatalog.swift"));
        var actual = Regex.Matches(generated, "\\.init\\(id: \\\"(?<id>[^\\\"]+)\\\", category: \\\"[^\\\"]+\\\", highRisk: (?<risk>true|false)\\)")
            .ToDictionary(
                match => match.Groups["id"].Value,
                match => bool.Parse(match.Groups["risk"].Value),
                StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void XcodeProject_ContainsEveryIosRuntimeSource()
    {
        // 场景：新增运行模块若未加入 Xcode Sources，源码存在也不能算可交付实现。
        var iosRoot = Path.Combine(FindWorkspaceRoot(), "apps", "mobile", "ios");
        var project = File.ReadAllText(Path.Combine(iosRoot, "OneDesk.xcodeproj", "project.pbxproj"));
        var sourceNames = Directory.EnumerateFiles(iosRoot, "*.swift", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(iosRoot, "*.cpp", SearchOption.AllDirectories))
            .Select(Path.GetFileName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var sourceName in sourceNames)
        {
            Assert.Contains($"{sourceName} in Sources", project, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void IosTransport_UsesMsQuicWithoutUdpFallback()
    {
        // 场景：iOS 传输不得因平台构建困难而静默降级为 UDP 或 Network.framework。
        var iosRoot = Path.Combine(FindWorkspaceRoot(), "apps", "mobile", "ios");
        var native = File.ReadAllText(Path.Combine(iosRoot, "Native", "onedesk_msquic.cpp"));
        var build = File.ReadAllText(Path.Combine(iosRoot, "scripts", "prepare-ios-dependencies.sh"));

        Assert.Contains("MsQuicOpen2", native, StringComparison.Ordinal);
        Assert.Contains("cmake/toolchains/ios.cmake", build, StringComparison.Ordinal);
        Assert.DoesNotContain("Network.framework", native, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SOCK_DGRAM", native, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IosDeviceTriggers_FeedTheSharedMobileActionRuntime()
    {
        // 场景：iOS 设备动作不能只出现在编辑器目录中，必须由原生传感器发给移动前端。
        var iosRoot = Path.Combine(FindWorkspaceRoot(), "apps", "mobile", "ios");
        var monitor = File.ReadAllText(Path.Combine(iosRoot, "Triggers", "DeviceTriggerMonitor.swift"));
        var webView = File.ReadAllText(Path.Combine(iosRoot, "Web", "OneDeskWebView.swift"));

        Assert.Contains("CMMotionManager", monitor, StringComparison.Ordinal);
        Assert.Contains("orientation-change", monitor, StringComparison.Ordinal);
        Assert.Contains("tilt-left", monitor, StringComparison.Ordinal);
        Assert.Contains("__oneDeskHandleDeviceTrigger", webView, StringComparison.Ordinal);
        Assert.Contains("deviceTriggers.stop()", webView, StringComparison.Ordinal);
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
