namespace OneDesk.Desktop.Services;

public sealed class CapabilityDirectoryService
{
    private readonly IReadOnlyList<CapabilityDefinition> _capabilities;

    public CapabilityDirectoryService()
    {
        _capabilities = BuildCatalog();
    }

    public IReadOnlyList<CapabilityDefinition> All() => _capabilities;

    public CapabilityDefinition? Find(string capabilityId)
    {
        return _capabilities.FirstOrDefault(capability => capability.Id == capabilityId);
    }

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
            C("device.identity", "device", "设备", "读取设备身份", "读取 OneDesk 分配的设备 ID、名称、平台和架构。", false),
            C("device.list", "device", "设备", "读取设备列表", "读取当前桌面端已知设备与在线状态。", false, android: false, ios: false, androidNote: "移动端需经桌面网关读取", iosNote: "移动端需经桌面网关读取"),
            C("device.vibrate", "device", "设备", "设备震动", "触发移动设备震动反馈。", false, desktop: false, desktopNote: "桌面端无震动硬件"),
            C("capability.list", "capability", "能力目录", "读取能力目录", "读取 OneDesk JSAPI 能力分类、平台支持和高危标记。", false),
            C("log.write", "log", "日志", "写入日志", "写入结构化运行日志。", false),
            C("log.read", "log", "日志", "读取日志", "读取最近结构化日志。", false, android: false, ios: false, androidNote: "移动端仅保存断联日志", iosNote: "移动端仅保存断联日志"),
            C("notification.inApp", "notification", "通知", "应用内通知", "请求前端显示应用内提示。", false),
            C("notification.native", "notification", "通知", "系统通知", "调用宿主系统通知能力。", false),
            C("file.readPrivate", "file", "文件", "读取私有文件", "读取组件或插件私有目录内文件。", false),
            C("file.writePrivate", "file", "文件", "写入私有文件", "写入组件或插件私有目录内文件。", false),
            C("file.deletePrivate", "file", "文件", "删除私有文件", "删除组件或插件私有目录内文件。", false),
            C("file.readExternal", "file", "文件", "读取外部文件", "读取用户授权路径中的外部文件。", true),
            C("file.writeExternal", "file", "文件", "修改外部文件", "写入或覆盖用户授权路径中的外部文件。", true),
            C("file.deleteExternal", "file", "文件", "删除外部文件", "删除用户授权路径中的外部文件。", true),
            C("clipboard.read", "clipboard", "剪贴板", "读取剪贴板", "读取系统剪贴板文本。", true),
            C("clipboard.write", "clipboard", "剪贴板", "写入剪贴板", "写入系统剪贴板文本。", true),
            C("process.list", "process", "进程", "读取进程列表", "读取系统进程基础信息。", true, android: false, ios: false, androidNote: "Android 受系统限制", iosNote: "iOS 不允许"),
            C("process.control", "process", "进程", "控制进程", "启动、终止或切换进程。", true, android: false, ios: false, androidNote: "Android 受系统限制", iosNote: "iOS 不允许"),
            C("memory.read", "memory", "内存", "读取内存", "读取进程内存。", true, android: false, ios: false, androidNote: "Android 普通应用不支持", iosNote: "iOS 不允许"),
            C("memory.write", "memory", "内存", "修改内存", "修改进程内存。", true, android: false, ios: false, androidNote: "Android 普通应用不支持", iosNote: "iOS 不允许"),
            C("input.keyboardMouseSimulation", "input", "输入控制", "模拟键鼠", "模拟键盘、鼠标或触摸输入。", true, android: false, ios: false, androidNote: "Android 普通应用受限制", iosNote: "iOS 不允许"),
            C("network.access", "network", "网络", "网络访问", "由壳子代发网络请求或打开受控连接。", true),
            C("plugin.invoke", "plugin", "插件", "调用插件", "调用桌面端插件 JSON-RPC 方法。", false, android: false, ios: false, androidNote: "移动端经桌面网关调用", iosNote: "移动端经桌面网关调用"),
            C("plugin.manage", "plugin", "插件", "管理插件", "安装、启用、停用或卸载插件。", true, android: false, ios: false, androidNote: "插件只在桌面端运行", iosNote: "插件只在桌面端运行"),
            C("screen.capture", "screen", "屏幕", "屏幕截图", "截取屏幕或窗口画面。", true),
            C("screen.record", "screen", "屏幕", "屏幕录制", "录制屏幕或窗口画面。", true),
            C("camera.access", "sensor", "传感器", "摄像头", "访问摄像头画面。", true),
            C("microphone.access", "sensor", "传感器", "麦克风", "访问麦克风音频。", true),
            C("sensor.motion", "sensor", "传感器", "运动传感器", "读取晃动、倾斜、方向变化等传感器事件。", false, desktop: false, desktopNote: "桌面端通常无移动传感器"),
            C("credential.access", "credential", "凭据", "访问凭据", "访问系统凭据、密钥链或安全存储。", true),
            C("shell.execute", "shell", "系统命令", "执行命令", "执行宿主系统 shell 命令。", true, android: false, ios: false, androidNote: "Android 普通应用不支持", iosNote: "iOS 不允许"),
            C("background.persistent", "background", "后台", "常驻后台", "允许插件或能力在后台持续运行。", true),
            C("crossDevice.sensitiveJsApi", "crossDevice", "跨设备", "敏感跨设备调用", "跨设备调用文件、传感器、剪贴板等敏感 JSAPI。", true)
        ];
    }
}
