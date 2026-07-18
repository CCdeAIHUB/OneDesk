using System.Security.Cryptography;
using System.Text;
using OneDesk.Desktop.Domain;
using OneDesk.Desktop.Services;

namespace OneDesk.Desktop.Shell;

public sealed partial class DesktopBridgeDispatcher
{
    private async Task<BridgeResponse> HandleWorkspaceListAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var components = await _repository.ListComponentsAsync(cancellationToken);
        var actions = await _repository.ListActionsAsync(cancellationToken);
        var pages = await _repository.ListPagesAsync(cancellationToken);
        var schemes = await _repository.ListSchemesAsync(cancellationToken);
        var activeScheme = await _repository.GetActiveSchemeAsync(cancellationToken);
        return BridgeResponse.Success(request.RequestId, new
        {
            components,
            actions,
            pages,
            schemes,
            activeScheme,
            devices = _devices.All(),
        });
    }

    private async Task<BridgeResponse> SaveComponentAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var component = DeserializePayload<ComponentDefinition>(request);
        if (component is null) return InvalidPayload(request);
        await _repository.SaveComponentAsync(component, cancellationToken);
        return BridgeResponse.Success(request.RequestId, component);
    }

    private async Task<BridgeResponse> ReadComponentFilesAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var id = ReadString(request, "id");
        if (string.IsNullOrWhiteSpace(id)) return InvalidPayload(request);
        return BridgeResponse.Success(request.RequestId, await _repository.ReadComponentFilesAsync(id, cancellationToken));
    }

    private async Task<BridgeResponse> SaveComponentFilesAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var payload = DeserializePayload<ComponentFilesPayload>(request);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Id)) return InvalidPayload(request);
        await _repository.SaveComponentFilesAsync(payload.Id, payload.Files, cancellationToken);
        return BridgeResponse.Success(request.RequestId, await _repository.ReadComponentFilesAsync(payload.Id, cancellationToken));
    }

    private BridgeResponse DeleteComponent(BridgeRequest request)
    {
        var id = ReadString(request, "id");
        if (string.IsNullOrWhiteSpace(id)) return InvalidPayload(request);
        _repository.DeleteComponent(id);
        return BridgeResponse.Success(request.RequestId);
    }

    private async Task<BridgeResponse> SaveActionAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var action = DeserializePayload<ActionDefinition>(request);
        if (action is null) return InvalidPayload(request);
        await _repository.SaveActionAsync(action, cancellationToken);
        return BridgeResponse.Success(request.RequestId, action);
    }

    private BridgeResponse DeleteAction(BridgeRequest request)
    {
        var id = ReadString(request, "id");
        if (string.IsNullOrWhiteSpace(id)) return InvalidPayload(request);
        _repository.DeleteAction(id);
        return BridgeResponse.Success(request.RequestId);
    }

    private async Task<BridgeResponse> SavePageAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var page = DeserializePayload<PageDefinition>(request);
        if (page is null) return InvalidPayload(request);
        await _repository.SavePageAsync(page, cancellationToken);
        return BridgeResponse.Success(request.RequestId, page);
    }

    private BridgeResponse DeletePage(BridgeRequest request)
    {
        var id = ReadString(request, "id");
        if (string.IsNullOrWhiteSpace(id)) return InvalidPayload(request);
        _repository.DeletePage(id);
        return BridgeResponse.Success(request.RequestId);
    }

    private async Task<BridgeResponse> SaveSchemeAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var scheme = DeserializePayload<SchemeDefinition>(request);
        if (scheme is null) return InvalidPayload(request);
        await _repository.SaveSchemeAsync(scheme, cancellationToken);
        return BridgeResponse.Success(request.RequestId, scheme);
    }

    private BridgeResponse DeleteScheme(BridgeRequest request)
    {
        var id = ReadString(request, "id");
        if (string.IsNullOrWhiteSpace(id)) return InvalidPayload(request);
        _repository.DeleteScheme(id);
        return BridgeResponse.Success(request.RequestId);
    }

    private async Task<BridgeResponse> ApplySchemeAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var id = ReadString(request, "id");
        if (string.IsNullOrWhiteSpace(id) || await _repository.GetSchemeAsync(id, cancellationToken) is null)
        {
            return BridgeResponse.Failure(request.RequestId, "SchemeNotFound", "方案不存在，无法应用");
        }

        var deviceId = ReadString(request, "deviceId");
        await _repository.ApplySchemeAsync(id, string.IsNullOrWhiteSpace(deviceId) ? null : deviceId, cancellationToken);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BridgeResponse.Success(request.RequestId, new { schemeId = id, deviceId = (string?)null, delivery = "desktop", message = "方案已应用为桌面默认方案" });
        }

        var push = await _gateway.PushSchemeUpdateAsync(deviceId, cancellationToken);
        _logs.Append(_devices.DesktopIdentity.DeviceId, push.Acknowledged ? "Info" : "Warning", "Scheme", "方案已分配到移动设备", new Dictionary<string, object?>
        {
            ["schemeId"] = id,
            ["deviceId"] = deviceId,
            ["online"] = push.Online,
            ["acknowledged"] = push.Acknowledged,
        });
        return BridgeResponse.Success(request.RequestId, new
        {
            schemeId = id,
            deviceId,
            delivery = push.Acknowledged ? "acknowledged" : push.Online ? "unconfirmed" : "pending",
            message = push.Message,
        });
    }

    private async Task<BridgeResponse> ExportAsync(BridgeRequest request, string kind, CancellationToken cancellationToken)
    {
        var id = ReadString(request, "id");
        if (string.IsNullOrWhiteSpace(id)) return InvalidPayload(request);
        var result = kind switch
        {
            "Component" => await _packages.ExportComponentByIdAsync(id, cancellationToken),
            "Page" => await _packages.ExportPageByIdAsync(id, cancellationToken),
            _ => await _packages.ExportSchemeByIdAsync(id, cancellationToken),
        };
        return BridgeResponse.Success(request.RequestId, result);
    }

    private async Task<BridgeResponse> AddResourceAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var path = await _platform.PickFileAsync(DesktopFileKind.Media, cancellationToken);
        if (string.IsNullOrWhiteSpace(path)) return BridgeResponse.Failure(request.RequestId, "UserCancelled", "已取消添加资源");
        return BridgeResponse.Success(request.RequestId, await _repository.AddMediaResourceAsync(path, cancellationToken));
    }

    private BridgeResponse DeleteResource(BridgeRequest request)
    {
        var id = ReadString(request, "id");
        if (string.IsNullOrWhiteSpace(id)) return InvalidPayload(request);
        _repository.DeleteMediaResource(id);
        return BridgeResponse.Success(request.RequestId);
    }

    private async Task<BridgeResponse> CopyResourceAsync(BridgeRequest request, bool component, CancellationToken cancellationToken)
    {
        var payload = DeserializePayload<ResourceCopyPayload>(request);
        if (payload is null || string.IsNullOrWhiteSpace(payload.ResourceId) || string.IsNullOrWhiteSpace(payload.TargetId)) return InvalidPayload(request);
        // 资源选择后必须复制到目标目录，组件和页面不能共享一个可变全局文件。
        var result = component
            ? await _repository.CopyMediaResourceToComponentAsync(payload.ResourceId, payload.TargetId, cancellationToken)
            : await _repository.CopyMediaResourceToPageAsync(payload.ResourceId, payload.TargetId, cancellationToken);
        return BridgeResponse.Success(request.RequestId, result);
    }

    private async Task<BridgeResponse> SchemeCacheManifestAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var active = await _repository.GetActiveSchemeAsync(cancellationToken);
        if (active is null) return BridgeResponse.Success(request.RequestId);
        var scheme = await _repository.GetSchemeAsync(active.SchemeId, cancellationToken);
        var pages = new List<PageDefinition>();
        var components = new List<ComponentDefinition>();
        if (scheme is not null)
        {
            foreach (var pageId in scheme.PageIds)
            {
                var page = await _repository.GetPageAsync(pageId, cancellationToken);
                if (page is null) continue;
                pages.Add(page);
                foreach (var componentId in page.Cells.Select(cell => cell.ComponentId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var definition = await _repository.GetComponentAsync(componentId!, cancellationToken);
                    if (definition is not null) components.Add(definition);
                }
            }
        }
        var json = System.Text.Json.JsonSerializer.Serialize(new { active, scheme, pages, components }, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return BridgeResponse.Success(request.RequestId, new
        {
            activeSchemeId = active.SchemeId,
            active.AppliedAt,
            pageCount = pages.Count,
            componentCount = components.Select(component => component.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            hash,
        });
    }
}
