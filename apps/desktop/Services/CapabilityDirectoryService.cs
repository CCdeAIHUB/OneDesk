namespace OneDesk.Desktop.Services;

public sealed class CapabilityDirectoryService
{
    private static readonly IReadOnlyDictionary<string, string> LegacyAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["file.readPrivate"] = "file.private.read",
            ["file.writePrivate"] = "file.private.write",
            ["file.deletePrivate"] = "file.private.delete",
            ["file.readExternal"] = "file.external.read",
            ["file.writeExternal"] = "file.external.write",
            ["file.deleteExternal"] = "file.external.delete",
            ["sensor.motion"] = "sensor.accelerometer",
        };
    private readonly IReadOnlyList<CapabilityDefinition> _capabilities;

    public CapabilityDirectoryService()
    {
        _capabilities = BuildCatalog();
    }

    public IReadOnlyList<CapabilityDefinition> All() => _capabilities;

    public CapabilityDefinition? Find(string capabilityId)
    {
        var normalized = NormalizeId(capabilityId);
        return _capabilities.FirstOrDefault(capability => capability.Id == normalized);
    }

    public static string NormalizeId(string capabilityId) =>
        LegacyAliases.TryGetValue(capabilityId, out var canonical) ? canonical : capabilityId;

    public bool IsHighRisk(string capabilityId)
    {
        return Find(capabilityId)?.HighRisk == true;
    }

    public IReadOnlyList<CapabilityCategory> Categories()
    {
        return _capabilities
            .GroupBy(capability => new { capability.Category, capability.CategoryName })
            .OrderBy(group => group.Key.Category)
            .Select(group => new CapabilityCategory(
                group.Key.Category,
                group.Key.CategoryName,
                group.Any(capability => capability.HighRisk),
                group.OrderBy(capability => capability.Id).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<CapabilityDefinition> BuildCatalog()
    {
        static CapabilityDefinition C(
            string id,
            string category,
            string categoryName,
            string name,
            string description,
            bool highRisk,
            bool desktop = true,
            bool android = true,
            bool ios = true,
            string desktopNote = "支持",
            string androidNote = "支持",
            string iosNote = "支持")
        {
            return new CapabilityDefinition(
                id,
                category,
                categoryName,
                name,
                description,
                highRisk,
                new CapabilitySupport(desktop, desktopNote),
                new CapabilitySupport(android, androidNote),
                new CapabilitySupport(ios, iosNote));
        }

        return
        [
            C("device.identity", "device", "设备", "设备身份", "读取 OneDesk 设备 ID、名称、平台和架构。", false),
            C("device.platform", "device", "设备", "平台信息", "读取操作系统、版本和 CPU 架构。", false),
            C("device.display.list", "device", "设备", "显示器列表", "读取显示器尺寸、方向和缩放信息。", false),
            C("device.power.status", "device", "设备", "电源状态", "读取电池、电源和充电状态。", false),
            C("device.vibrate", "device", "设备", "设备震动", "触发移动设备震动反馈。", false, desktop: false, desktopNote: "桌面端通常无震动器"),
            C("file.private.read", "file", "文件", "读取私有文件", "读取组件或插件私有目录内文件。", false),
            C("file.private.write", "file", "文件", "写入私有文件", "写入组件或插件私有目录内文件。", false),
            C("file.private.delete", "file", "文件", "删除私有文件", "删除组件或插件私有目录内文件。", false),
            C("file.external.read", "file", "文件", "读取外部文件", "读取用户明确授权的外部文件。", true),
            C("file.external.write", "file", "文件", "修改外部文件", "写入用户明确授权的外部文件。", true),
            C("file.external.delete", "file", "文件", "删除外部文件", "删除用户明确授权的外部文件。", true),
            C("clipboard.read", "clipboard", "剪贴板", "读取剪贴板", "读取系统剪贴板文本。", true),
            C("clipboard.write", "clipboard", "剪贴板", "写入剪贴板", "写入系统剪贴板文本。", true),
            C("notification.inApp", "notification", "通知", "应用内通知", "请求 OneDesk 显示应用内提示。", false),
            C("notification.native", "notification", "通知", "系统通知", "调用宿主系统通知能力。", false),
            C("input.hotkey.register", "input", "输入控制", "注册快捷键", "注册系统级快捷键。", true, android: false, ios: false, androidNote: "Android 普通应用不支持全局快捷键", iosNote: "iOS 不支持全局快捷键"),
            C("input.hotkey.unregister", "input", "输入控制", "注销快捷键", "注销已注册的系统级快捷键。", true, android: false, ios: false, androidNote: "Android 普通应用不支持全局快捷键", iosNote: "iOS 不支持全局快捷键"),
            C("input.keyboardMouseSimulation", "input", "输入控制", "模拟键鼠", "模拟键盘、鼠标或触摸输入。", true, android: false, ios: false, androidNote: "Android 普通应用不允许全局输入注入", iosNote: "iOS 不允许全局输入注入"),
            C("process.launch", "process", "进程", "启动应用", "启动程序、应用或已授权 URI。", true, ios: false, iosNote: "iOS 仅允许受控 URL Scheme"),
            C("process.list", "process", "进程", "读取进程列表", "读取系统进程基础信息。", true, android: false, ios: false, androidNote: "Android 限制读取其他应用进程", iosNote: "iOS 不允许"),
            C("process.control", "process", "进程", "控制进程", "终止、切换或控制进程。", true, android: false, ios: false, androidNote: "Android 普通应用不允许", iosNote: "iOS 不允许"),
            C("shell.execute", "shell", "系统命令", "执行命令", "执行宿主系统 shell 命令。", true, android: false, ios: false, androidNote: "Android 普通应用不提供系统 shell", iosNote: "iOS 不允许"),
            C("memory.read", "memory", "内存", "读取内存", "读取目标进程内存。", true, android: false, ios: false, androidNote: "Android 普通应用不支持", iosNote: "iOS 不允许"),
            C("memory.write", "memory", "内存", "修改内存", "修改目标进程内存。", true, android: false, ios: false, androidNote: "Android 普通应用不支持", iosNote: "iOS 不允许"),
            C("network.access", "network", "网络", "网络访问", "由原生壳子代发受权限管理的网络请求。", true),
            C("sensor.accelerometer", "sensor", "传感器", "加速度传感器", "读取加速度并支持晃动、倾斜触发。", true, desktop: false, desktopNote: "桌面设备通常无该传感器"),
            C("sensor.gyroscope", "sensor", "传感器", "陀螺仪", "读取设备旋转速度。", true, desktop: false, desktopNote: "桌面设备通常无该传感器"),
            C("sensor.orientation", "sensor", "传感器", "设备方向", "读取设备姿态和方向变化。", true, desktop: false, desktopNote: "桌面设备通常无该传感器"),
            C("camera.access", "camera", "摄像头", "访问摄像头", "在用户授权后访问摄像头。", true),
            C("microphone.access", "microphone", "麦克风", "访问麦克风", "在用户授权后访问麦克风。", true),
            C("screen.capture", "screen", "屏幕", "屏幕截图", "在用户授权后截取屏幕或窗口。", true),
            C("screen.record", "screen", "屏幕", "屏幕录制", "在用户授权后录制屏幕或窗口。", true),
            C("credential.access", "credential", "凭据", "安全凭据", "访问系统凭据库或平台安全存储。", true),
            C("plugin.invoke", "plugin", "插件", "调用插件", "调用桌面端插件 JSON-RPC 方法。", true, android: true, ios: true, androidNote: "经桌面网关调用", iosNote: "经桌面网关调用"),
            C("scheme.active.get", "scheme", "方案", "读取活动方案", "读取当前设备已缓存并应用的方案。", false),
            C("scheme.page.switch", "scheme", "方案", "切换页面", "切换当前方案页面。", false),
            C("scheme.cache.status", "scheme", "方案", "缓存状态", "读取方案版本、哈希和缓存状态。", false),
            C("log.write", "log", "日志", "写入日志", "写入结构化运行日志。", false),
        ];
    }
}
