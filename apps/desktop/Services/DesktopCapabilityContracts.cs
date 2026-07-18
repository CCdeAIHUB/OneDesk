namespace OneDesk.Desktop.Services;

/// <summary>
/// 桌面能力声明的可执行处理器清单。契约测试会把它与公开能力目录逐项比对，防止“标记支持但运行时落入默认错误”。
/// </summary>
public static class DesktopCapabilityContracts
{
    public static IReadOnlySet<string> BuiltIn { get; } = Set(
        "device.identity",
        "file.private.read",
        "file.private.write",
        "file.private.delete",
        "notification.inApp",
        "notification.native",
        "process.list",
        "network.access",
        "plugin.invoke",
        "log.write");

    public static IReadOnlySet<string> Portable { get; } = Set(
        "device.platform",
        "file.external.read",
        "file.external.write",
        "file.external.delete",
        "process.launch",
        "process.control",
        "shell.execute",
        "credential.access");

    public static IReadOnlySet<string> Scheme { get; } = Set(
        "scheme.active.get",
        "scheme.page.switch",
        "scheme.cache.status");

    public static IReadOnlySet<string> Windows { get; } = Set(
        "device.display.list",
        "device.power.status",
        "clipboard.read",
        "clipboard.write",
        "notification.inApp",
        "notification.native",
        "input.hotkey.register",
        "input.hotkey.unregister",
        "input.keyboardMouseSimulation",
        "memory.read",
        "memory.write",
        "camera.access",
        "microphone.access",
        "screen.capture",
        "screen.record");

    private static IReadOnlySet<string> Set(params string[] ids) => new HashSet<string>(ids, StringComparer.Ordinal);
}
