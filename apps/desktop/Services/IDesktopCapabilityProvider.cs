namespace OneDesk.Desktop.Services;

/// <summary>
/// 平台能力通过提供器接入路由，核心路由只负责身份、权限和目标设备，不直接依赖 Win32、Cocoa 或 Linux API。
/// </summary>
public interface IDesktopCapabilityProvider
{
    IReadOnlySet<string> CapabilityIds { get; }

    Task<JsApiResult> ExecuteAsync(JsApiRequest request, CancellationToken cancellationToken = default);
}
