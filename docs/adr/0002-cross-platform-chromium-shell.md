# ADR: 跨平台桌面壳使用 Avalonia 与 CefGlue

## 背景

OneDesk 要求 Windows、macOS、Linux 的 x64 与 ARM64 桌面版本都加载同一份 Vue 3 文件前端，并明确要求 Chromium 内核。Avalonia 原生 WebView 在 macOS 和 Linux 使用 WebKit，不能满足内核约束；商业 Chromium 组件也不符合项目的免费商用要求。

## 决策

- Windows 正式产物继续使用已验证的 WebView2 壳。
- macOS、Linux 与通用 Avalonia 壳使用 CefGlue `120.6099.211`，底层为 CEF/Chromium。
- x64 与 ARM64 使用 CefGlue 对应架构包，发布时按 RID 单独解析，禁止把两套原生二进制混入同一产物。
- 浏览器只允许加载应用 `wwwroot` 下的 `file://` 资源；HTTP、HTTPS、WebSocket 与其他远程请求在浏览器请求处理器和注入脚本两层拦截。
- Vue 前端只调用宿主注入的 `OneDeskNative`，所有网络、文件、插件和设备能力继续经过桌面壳服务。

## 原因

- CefGlue 与 CEF 可免费商用，许可证分别为 MIT 与 BSD。
- 官方提供 Avalonia 控件和 Windows、macOS、Linux 的 x64/ARM64 稳定包。
- 保持 Chromium 语义一致，同时不推翻已经稳定运行的 Windows WebView2 壳。

## 替代方案

- Avalonia NativeWebView：macOS/Linux 不是 Chromium，拒绝。
- DotNetBrowser：商业授权，拒绝。
- Electron：需要重写 C# 壳进程边界并扩大安装体积，拒绝。

## 影响范围

- `apps/desktop` 的启动、窗口、浏览器安全策略、JS 桥和发布矩阵。
- macOS/Linux 发行包需要携带 CEF 原生文件。

## 风险

- CEF 原生包体积较大。
- Linux ARM64 的发行版兼容性必须在对应系统 CI 或实机持续验证。

## 未来演进

本版本不提供 WebKit 降级；CEF 初始化失败时返回明确错误并终止壳启动。
