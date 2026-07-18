using System.Runtime.InteropServices;
using OneDesk.Desktop.Services;
using OneDesk.Desktop.Storage;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class PluginHostServiceTests
{
    [Fact]
    public async Task FailedPersistentUpgradeKeepsPreviousRegistration()
    {
        // 场景：新版本常驻插件缺少当前平台产物时，旧版本注册不能提前被移除。
        var root = Path.Combine(Path.GetTempPath(), $"onedesk-plugin-host-{Guid.NewGuid():N}");
        var paths = new OneDeskDataPaths(root);
        using var host = new PluginHostService(new StructuredLogStore(paths));
        var oldManifest = Manifest("example.plugin", "1.0.0");
        await host.RegisterManifestAsync(oldManifest, root, TestContext.Current.CancellationToken);

        var currentPlatform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var currentArchitecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var brokenUpgrade = Manifest(
            "example.plugin",
            "2.0.0",
            new PluginBackendDefinition(
                "json-rpc",
                true,
                [new PluginPlatformArtifact(currentPlatform, currentArchitecture, "missing-plugin", [])]));

        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                host.RegisterManifestAsync(brokenUpgrade, root, TestContext.Current.CancellationToken));

            var installed = Assert.Single(host.InstalledPlugins);
            Assert.Equal("1.0.0", installed.Version);
        }
        finally
        {
            host.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static PluginManifest Manifest(string id, string version, PluginBackendDefinition? backend = null) =>
        new(id, id, version, backend?.Persistent ?? false, [], null, backend);
}
