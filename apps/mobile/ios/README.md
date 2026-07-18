# OneDesk iOS 客户端

该目录包含 SwiftUI + WKWebView 移动壳、MsQuic 原生适配、扫码配对、长期信任、断联日志、原子方案缓存和 JSAPI 路由的正式源码。

## 构建

需要 macOS、Xcode 16、CMake、pnpm，以及已经初始化的 `third_party/msquic` 子模块。

```bash
open apps/mobile/ios/OneDesk.xcodeproj
```

Xcode 的“准备前端与 MsQuic”构建阶段会先构建 `frontends/mobile`，再为当前真机或模拟器架构编译 MsQuic 静态库。也可以单独执行：

```bash
apps/mobile/ios/scripts/prepare-ios-dependencies.sh
```

## 安全边界

- Vue 入口只从应用包内的 `file://` 加载。
- CSP、注入脚本和 WKNavigationDelegate 共同阻止前端直接联网。
- 长期信任凭据只保存在 Keychain，不返回给前端。
- 不支持的 iOS 能力返回 `CapabilityNotSupported`，不会伪造成功。
