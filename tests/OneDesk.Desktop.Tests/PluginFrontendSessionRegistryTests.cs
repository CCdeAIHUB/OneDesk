using OneDesk.Desktop.Services;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class PluginFrontendSessionRegistryTests
{
    [Fact]
    public void RotatingPluginSessionInvalidatesPreviousIdentity()
    {
        // 场景：同一插件重新加载后，旧 iframe 不能继续借用已经轮换的宿主身份。
        var registry = new PluginFrontendSessionRegistry();
        var first = registry.Create("plugin.sample");
        var second = registry.Create("plugin.sample");

        Assert.False(registry.TryResolve(first, out _));
        Assert.True(registry.TryResolve(second, out var pluginId));
        Assert.Equal("plugin.sample", pluginId);
    }

    [Fact]
    public void RevokingPluginRemovesFrontendIdentity()
    {
        // 场景：插件被删除后，仍存活的沙箱页面必须立即失去调用宿主的资格。
        var registry = new PluginFrontendSessionRegistry();
        var session = registry.Create("plugin.sample");

        registry.Revoke("plugin.sample");

        Assert.False(registry.TryResolve(session, out _));
    }
}
