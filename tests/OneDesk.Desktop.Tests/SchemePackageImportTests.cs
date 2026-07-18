using System.IO.Compression;
using System.Text.Json;
using OneDesk.Desktop.Services;
using OneDesk.Desktop.Storage;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class SchemePackageImportTests
{
    [Fact]
    public void ComponentImportInstallsEntityAndDependentActionsByManifestIdentity()
    {
        // 场景：组件包文件名与组件 ID 不同时，必须按清单 ID 安装，并同时提交依赖动作。
        var root = Path.Combine(Path.GetTempPath(), $"onedesk-package-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(root, "package");
        var packagePath = Path.Combine(root, "renamed-package.zip");
        Directory.CreateDirectory(Path.Combine(packageRoot, "actions"));
        File.WriteAllText(
            Path.Combine(packageRoot, "onedesk.component.json"),
            JsonSerializer.Serialize(new
            {
                id = "component.real-id",
                name = "测试组件",
                version = "1.0.0",
                editMode = "Visual",
                entryFile = "src/App.vue",
                visualConfigFile = "visual.json",
                actionIds = new[] { "action-one" },
                requestedPermissions = Array.Empty<object>(),
                pluginDependencies = Array.Empty<object>(),
            }));
        File.WriteAllText(Path.Combine(packageRoot, "visual.json"), "{}");
        File.WriteAllText(Path.Combine(packageRoot, "actions", "action-one.json"), "{\"id\":\"action-one\"}");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);

        try
        {
            var paths = new OneDeskDataPaths(Path.Combine(root, "data"));
            var service = new SchemePackageService(paths);
            var result = service.ImportComponent(packagePath);

            Assert.True(result.Ready);
            Assert.Equal(Path.Combine(paths.Components, "component.real-id"), result.DestinationDirectory);
            Assert.True(File.Exists(Path.Combine(paths.Components, "component.real-id", "onedesk.component.json")));
            Assert.True(File.Exists(Path.Combine(paths.Actions, "action-one.json")));
            Assert.False(Directory.Exists(Path.Combine(paths.Components, "renamed-package")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ComponentImportStopsUntilPluginVersionConflictIsResolved()
    {
        var fixture = CreateComponentPackageWithPlugin("1.0.0", "2.0.0");
        try
        {
            var paths = new OneDeskDataPaths(Path.Combine(fixture.Root, "data"));
            var service = new SchemePackageService(paths);

            var result = service.ImportComponent(
                fixture.PackagePath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["plugin.sample"] = "1.0.0",
                });

            Assert.False(result.Ready);
            var conflict = Assert.Single(result.UnresolvedPluginConflicts);
            Assert.Equal("plugin.sample", conflict.Id);
            Assert.Equal("1.0.0", conflict.InstalledVersion);
            Assert.Equal("2.0.0", conflict.PackagedVersion);
            Assert.False(Directory.Exists(Path.Combine(paths.Components, "component.with-plugin")));
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public void ComponentImportCanAtomicallyReplacePluginWithPackagedVersion()
    {
        var fixture = CreateComponentPackageWithPlugin("1.0.0", "2.0.0");
        try
        {
            var paths = new OneDeskDataPaths(Path.Combine(fixture.Root, "data"));
            paths.EnsureCreated();
            var existingPlugin = Path.Combine(paths.Plugins, "plugin.sample");
            Directory.CreateDirectory(existingPlugin);
            File.WriteAllText(Path.Combine(existingPlugin, "onedesk.plugin.json"), "{\"id\":\"plugin.sample\",\"name\":\"旧插件\",\"version\":\"1.0.0\",\"persistent\":false,\"permissions\":[]}");
            var service = new SchemePackageService(paths);

            var result = service.ImportComponent(
                fixture.PackagePath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["plugin.sample"] = "1.0.0",
                },
                new Dictionary<string, PluginVersionChoice>(StringComparer.OrdinalIgnoreCase)
                {
                    ["plugin.sample"] = PluginVersionChoice.UsePackage,
                });

            Assert.True(result.Ready);
            Assert.Equal(new[] { "plugin.sample" }, result.InstalledPluginIds);
            using var pluginDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(existingPlugin, "onedesk.plugin.json")));
            Assert.Equal("2.0.0", pluginDocument.RootElement.GetProperty("version").GetString());
            Assert.True(File.Exists(Path.Combine(paths.Components, "component.with-plugin", "onedesk.component.json")));
            Assert.True(File.Exists(Path.Combine(paths.Actions, "action-one.json")));
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public void DeferredComponentImportRestoresWorkspaceWhenPluginValidationFails()
    {
        // 场景：包内插件宿主校验失败时，组件、动作和插件文件必须作为一个整体回滚。
        var fixture = CreateComponentPackageWithPlugin("1.0.0", "2.0.0");
        try
        {
            var paths = new OneDeskDataPaths(Path.Combine(fixture.Root, "data"));
            paths.EnsureCreated();
            var existingPlugin = Path.Combine(paths.Plugins, "plugin.sample");
            Directory.CreateDirectory(existingPlugin);
            File.WriteAllText(Path.Combine(existingPlugin, "onedesk.plugin.json"), "{\"version\":\"1.0.0\"}");
            var service = new SchemePackageService(paths);

            using (var session = service.BeginImportComponent(
                       fixture.PackagePath,
                       new Dictionary<string, string> { ["plugin.sample"] = "1.0.0" },
                       new Dictionary<string, PluginVersionChoice> { ["plugin.sample"] = PluginVersionChoice.UsePackage }))
            {
                Assert.True(session.Result.Ready);
                Assert.True(Directory.Exists(Path.Combine(paths.Components, "component.with-plugin")));
            }

            Assert.False(Directory.Exists(Path.Combine(paths.Components, "component.with-plugin")));
            Assert.False(File.Exists(Path.Combine(paths.Actions, "action-one.json")));
            using var restoredPlugin = JsonDocument.Parse(File.ReadAllText(Path.Combine(existingPlugin, "onedesk.plugin.json")));
            Assert.Equal("1.0.0", restoredPlugin.RootElement.GetProperty("version").GetString());
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    private static PackageFixture CreateComponentPackageWithPlugin(string installedVersion, string packagedVersion)
    {
        _ = installedVersion;
        var root = Path.Combine(Path.GetTempPath(), $"onedesk-package-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(root, "package");
        var pluginRoot = Path.Combine(packageRoot, "plugins", "plugin.sample");
        Directory.CreateDirectory(Path.Combine(packageRoot, "actions"));
        Directory.CreateDirectory(pluginRoot);
        File.WriteAllText(
            Path.Combine(packageRoot, "onedesk.component.json"),
            JsonSerializer.Serialize(new
            {
                id = "component.with-plugin",
                name = "带插件组件",
                version = "1.0.0",
                editMode = "Visual",
                entryFile = "src/App.vue",
                visualConfigFile = "visual.json",
                actionIds = new[] { "action-one" },
                requestedPermissions = Array.Empty<object>(),
                pluginDependencies = new[] { new { id = "plugin.sample", version = packagedVersion, kind = "plugin" } },
            }));
        File.WriteAllText(Path.Combine(packageRoot, "visual.json"), "{}");
        File.WriteAllText(Path.Combine(packageRoot, "actions", "action-one.json"), "{\"id\":\"action-one\"}");
        File.WriteAllText(
            Path.Combine(pluginRoot, "onedesk.plugin.json"),
            JsonSerializer.Serialize(new
            {
                id = "plugin.sample",
                name = "示例插件",
                version = packagedVersion,
                persistent = false,
                permissions = Array.Empty<object>(),
                selfContained = true,
            }));
        var packagePath = Path.Combine(root, "component.zip");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return new PackageFixture(root, packagePath);
    }

    private sealed record PackageFixture(string Root, string PackagePath);
}
