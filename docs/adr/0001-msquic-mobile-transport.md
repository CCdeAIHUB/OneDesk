# ADR: 桌面与移动端使用 MsQuic 双向流

## 背景

OneDesk 已确认桌面与移动端必须使用 MsQuic。项目早期曾使用无连接 UDP JSON 数据报，本 ADR 要求正式实现必须具备 QUIC 的 TLS 1.3、可靠流、连接生命周期、拥塞控制和双向推送语义。

## 决策

- 桌面端使用 .NET `System.Net.Quic`。Windows 运行时由 MsQuic 提供底层 QUIC 实现。
- Android 使用固定版本 MsQuic 2.5.8，通过 NDK/JNI 暴露最小传输接口。
- 应用层采用有长度前缀的 UTF-8 JSON 信封；每条请求拥有 `messageId`，响应使用 `correlationId`，服务器事件使用独立双向流。
- 每个移动设备维持一条长期 QUIC 连接。配对、重连、方案分块、资源、日志、心跳、JSAPI 和推送确认复用该连接，不再依赖短生命周期 UDP 端口。
- 首次配对由六位验证码完成应用层认证并换取长期信任凭据；后续连接必须携带长期凭据。
- 传输层只负责连接、流和字节消息。配对、权限、缓存、日志、方案和 JSAPI 业务由网关处理器负责。

## 原因

- 满足已经确认的 MsQuic 技术选型。
- 可靠流避免自行实现数据报分块重传和端点漂移处理。
- 长连接天然支持桌面向移动端推送方案和 JSAPI 请求。
- 独立传输接口便于单元测试并隔离 Android/JNI 与业务代码。

## 替代方案

- 保留 UDP JSON：不满足需求，拒绝。
- 使用 Cronet/HTTP3：不能保证底层为已确认的 MsQuic，拒绝。
- Android 使用其他 QUIC 实现：会造成双实现差异，拒绝。

## 影响范围

- `apps/desktop/Services` 的网关传输与连接注册。
- `apps/mobile/android` 的 NDK/JNI、连接状态机和网关客户端。
- 桌面集成测试、Android 单元/仪器测试、构建与发布产物。

## 风险

- MsQuic 对 Android 属于上游的非正式支持平台，需要固定 NDK、ABI 和版本并由本项目自行验证。
- 自签名 TLS 身份必须和应用层长期信任凭据一起管理，不能静默跳过身份失败。
- 旧 UDP 客户端与新 QUIC 服务端不兼容；本版本没有旧协议兼容分支。

## 协议演进

本 ADR 不保留 UDP fallback。协议字段演进通过信封 `protocolVersion` 和兼容测试完成。
