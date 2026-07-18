using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop.Services;

public sealed class DesktopSchemeCapabilityProvider : IDesktopCapabilityProvider
{
    private readonly OneDeskRepository _repository;
    private readonly JsonFileStore _store;
    private readonly string _runtimeStatePath;

    public DesktopSchemeCapabilityProvider(OneDeskRepository repository, JsonFileStore store, OneDeskDataPaths paths)
    {
        _repository = repository;
        _store = store;
        _runtimeStatePath = Path.Combine(paths.Cache, "desktop-scheme-runtime.json");
    }

    public IReadOnlySet<string> CapabilityIds => DesktopCapabilityContracts.Scheme;

    public Task<JsApiResult> ExecuteAsync(JsApiRequest request, CancellationToken cancellationToken = default) => request.Capability switch
    {
        "scheme.active.get" => GetActiveAsync(cancellationToken),
        "scheme.page.switch" => SwitchPageAsync(request, cancellationToken),
        "scheme.cache.status" => GetCacheStatusAsync(cancellationToken),
        _ => Task.FromResult(JsApiResult.Error("CapabilityPlatformHandlerMissing", "方案运行时未注册该能力。")),
    };

    private async Task<JsApiResult> GetActiveAsync(CancellationToken cancellationToken)
    {
        var active = await _repository.GetActiveSchemeAsync(cancellationToken);
        if (active is null) return JsApiResult.Success(new { active = false });
        var scheme = await _repository.GetSchemeAsync(active.SchemeId, cancellationToken);
        var runtime = await _store.LoadAsync<DesktopSchemeRuntimeState>(_runtimeStatePath, cancellationToken);
        return JsApiResult.Success(new
        {
            active = scheme is not null,
            active.SchemeId,
            active.AppliedAt,
            currentPageId = runtime?.SchemeId == active.SchemeId ? runtime.PageId : scheme?.PageIds.FirstOrDefault(),
            scheme,
        });
    }

    private async Task<JsApiResult> SwitchPageAsync(JsApiRequest request, CancellationToken cancellationToken)
    {
        var active = await _repository.GetActiveSchemeAsync(cancellationToken);
        if (active is null) return JsApiResult.Error("SchemeNotApplied", "桌面端当前没有活动方案。");
        var scheme = await _repository.GetSchemeAsync(active.SchemeId, cancellationToken);
        if (scheme is null || scheme.PageIds.Count == 0) return JsApiResult.Error("SchemePageMissing", "活动方案没有可切换页面。");
        var runtime = await _store.LoadAsync<DesktopSchemeRuntimeState>(_runtimeStatePath, cancellationToken);
        var currentPageId = runtime?.SchemeId == scheme.Id && scheme.PageIds.Contains(runtime.PageId, StringComparer.OrdinalIgnoreCase)
            ? runtime.PageId
            : scheme.PageIds[0];
        var requestedPageId = ReadString(request.Payload, "pageId", "");
        string nextPageId;
        if (!string.IsNullOrWhiteSpace(requestedPageId))
        {
            if (!scheme.PageIds.Contains(requestedPageId, StringComparer.OrdinalIgnoreCase))
                return JsApiResult.Error("SchemePageNotFound", "目标页面不属于当前活动方案。");
            nextPageId = requestedPageId;
        }
        else
        {
            var direction = ReadString(request.Payload, "direction", "next");
            var index = scheme.PageIds.ToList().FindIndex(pageId => string.Equals(pageId, currentPageId, StringComparison.OrdinalIgnoreCase));
            nextPageId = string.Equals(direction, "previous", StringComparison.OrdinalIgnoreCase)
                ? scheme.PageIds[(index - 1 + scheme.PageIds.Count) % scheme.PageIds.Count]
                : scheme.PageIds[(index + 1) % scheme.PageIds.Count];
        }
        var next = new DesktopSchemeRuntimeState(scheme.Id, nextPageId, DateTimeOffset.UtcNow);
        await _store.SaveAsync(_runtimeStatePath, next, cancellationToken);
        return JsApiResult.Success(next);
    }

    private async Task<JsApiResult> GetCacheStatusAsync(CancellationToken cancellationToken)
    {
        var active = await _repository.GetActiveSchemeAsync(cancellationToken);
        if (active is null) return JsApiResult.Success(new { cached = false });
        var scheme = await _repository.GetSchemeAsync(active.SchemeId, cancellationToken);
        if (scheme is null) return JsApiResult.Success(new { cached = false, active.SchemeId });
        var canonical = JsonSerializer.Serialize(scheme);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return JsApiResult.Success(new { cached = true, schemeId = scheme.Id, scheme.Version, hash, active.AppliedAt });
    }

    private static string ReadString(object? payload, string key, string fallback) =>
        payload is JsonElement { ValueKind: JsonValueKind.Object } element &&
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
}

public sealed record DesktopSchemeRuntimeState(string SchemeId, string PageId, DateTimeOffset UpdatedAt);
