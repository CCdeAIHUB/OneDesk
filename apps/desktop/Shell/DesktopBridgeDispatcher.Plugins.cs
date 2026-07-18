using System.IO.Compression;
using System.Text.Json;
using OneDesk.Desktop.Services;

namespace OneDesk.Desktop.Shell;

public sealed partial class DesktopBridgeDispatcher
{
    public async Task LoadInstalledPluginsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var pluginRoot in Directory.EnumerateDirectories(_paths.Plugins, "*", SearchOption.TopDirectoryOnly))
        {
            var manifestPath = Path.Combine(pluginRoot, "onedesk.plugin.json");
            try
            {
                if (!File.Exists(manifestPath)) throw new InvalidDataException("插件目录缺少 onedesk.plugin.json");
                var manifest = JsonSerializer.Deserialize<PluginManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonOptions)
                    ?? throw new InvalidDataException("插件清单格式不正确");
                await _plugins.RegisterManifestAsync(manifest, pluginRoot, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // 单个损坏插件不能阻断桌面端启动，但必须形成可诊断的结构化记录。
                _logs.Append(_devices.DesktopIdentity.DeviceId, "Error", "Plugin", "启动时加载插件失败", new Dictionary<string, object?>
                {
                    ["packageDirectory"] = pluginRoot,
                    ["error"] = error.Message,
                });
            }
        }
    }

    private async Task<BridgeResponse> ListFrontendPluginsAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var runtimes = new List<PluginFrontendRuntimeDescriptor>();
        foreach (var plugin in _plugins.InstalledPlugins.Where(plugin => plugin.Frontend is not null))
        {
            var pluginRoot = Path.GetFullPath(Path.Combine(_paths.Plugins, plugin.Id));
            var entryPath = Path.GetFullPath(Path.Combine(pluginRoot, plugin.Frontend!.Entry));
            if (!entryPath.StartsWith(pluginRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(entryPath))
            {
                return BridgeResponse.Failure(request.RequestId, "PluginFrontendEntryInvalid", $"插件前端入口无效：{plugin.Id}");
            }
            if (new FileInfo(entryPath).Length > 4 * 1024 * 1024)
            {
                return BridgeResponse.Failure(request.RequestId, "PluginFrontendEntryTooLarge", $"插件前端入口超过 4 MiB：{plugin.Id}");
            }
            runtimes.Add(new PluginFrontendRuntimeDescriptor(
                plugin.Id,
                plugin.Name,
                _pluginSessions.Create(plugin.Id),
                await File.ReadAllTextAsync(entryPath, cancellationToken)));
        }
        return BridgeResponse.Success(request.RequestId, runtimes);
    }

    private async Task<BridgeResponse> CallFrontendPluginJsApiAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var payload = DeserializePayload<PluginFrontendJsApiPayload>(request);
        if (payload is null || !_pluginSessions.TryResolve(payload.SessionId, out var pluginId) || string.IsNullOrWhiteSpace(payload.Capability))
        {
            return BridgeResponse.Failure(request.RequestId, "PluginFrontendIdentityInvalid", "前端插件会话无效或已过期");
        }
        var target = string.IsNullOrWhiteSpace(payload.TargetDeviceId) ? _devices.DesktopIdentity.DeviceId : payload.TargetDeviceId;
        var result = await _jsApiRouter.RouteAsync(new JsApiRequest(
            request.RequestId,
            target,
            new TrustedSource(null, null, null, pluginId, "plugin"),
            payload.Capability,
            payload.Payload), cancellationToken);
        return new BridgeResponse(request.RequestId, result.Ok, result.Payload, result.ErrorCode, result.Message);
    }

    private async Task<BridgeResponse> InvokeFrontendPluginBackendAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var payload = DeserializePayload<PluginFrontendBackendPayload>(request);
        if (payload is null || !_pluginSessions.TryResolve(payload.SessionId, out var pluginId) || string.IsNullOrWhiteSpace(payload.Method))
        {
            return BridgeResponse.Failure(request.RequestId, "PluginFrontendIdentityInvalid", "前端插件会话无效或已过期");
        }
        return BridgeResponse.Success(request.RequestId, await _plugins.InvokeAsync(pluginId, payload.Method, payload.Parameters, cancellationToken));
    }

    private async Task<BridgeResponse> InspectPluginImportAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var path = await _platform.PickFileAsync(DesktopFileKind.PluginPackage, cancellationToken);
        if (string.IsNullOrWhiteSpace(path)) return BridgeResponse.Failure(request.RequestId, "UserCancelled", "已取消插件导入");
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.Entries.FirstOrDefault(item => item.FullName.EndsWith("onedesk.plugin.json", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return BridgeResponse.Failure(request.RequestId, "PluginManifestMissing", "插件包缺少 onedesk.plugin.json");
        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<PluginManifest>(stream, JsonOptions);
        if (manifest is null) return BridgeResponse.Failure(request.RequestId, "InvalidPluginManifest", "插件清单格式不正确");
        var token = Guid.NewGuid().ToString("N");
        var sources = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [$"plugin:{manifest.Id}"] = manifest.Permissions.Select(permission => permission.Capability).ToArray(),
        };
        _pendingImports[token] = new PendingPackageImport(token, "Plugin", path, sources);
        return BridgeResponse.Success(request.RequestId, new PackageInspection(token, "Plugin", manifest.Name, path, manifest.Permissions, [], [], [], sources));
    }

    private async Task<BridgeResponse> ConfirmPluginImportAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var token = ReadString(request, "token");
        if (string.IsNullOrWhiteSpace(token) || !_pendingImports.TryRemove(token, out var pending) || pending.Kind != "Plugin")
        {
            return BridgeResponse.Failure(request.RequestId, "ImportSessionExpired", "插件导入会话不存在或已过期");
        }
        var manifest = await InstallPluginPackageAsync(pending.Path, cancellationToken);
        var granted = request.Payload is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("grantedCapabilities", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray()
            : [];
        GrantImportedPermissions(granted, pending.SourceKeys);
        return BridgeResponse.Success(request.RequestId, manifest);
    }

    private async Task<PluginManifest> InstallPluginPackageAsync(string packagePath, CancellationToken cancellationToken)
    {
        var extractionRoot = Path.Combine(_paths.Temp, $"plugin-extract-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(_paths.Temp, $"plugin-backup-{Guid.NewGuid():N}");
        try
        {
            SchemePackageService.SafeExtractPackage(packagePath, extractionRoot);
            var manifestPath = Directory.EnumerateFiles(extractionRoot, "onedesk.plugin.json", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidDataException("插件包缺少 onedesk.plugin.json");
            var manifest = JsonSerializer.Deserialize<PluginManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonOptions)
                ?? throw new InvalidDataException("插件清单格式不正确");
            var packageRoot = Path.GetDirectoryName(manifestPath) ?? extractionRoot;
            PluginHostService.ValidateManifest(manifest, packageRoot);
            var pluginRoot = Path.Combine(_paths.Plugins, manifest.Id);

            // 文件切换后先完成新进程握手；任一步失败，using 会恢复旧文件和旧插件注册。
            using var transaction = FileInstallTransaction.Begin(
                [FileInstallOperation.Directory(packageRoot, pluginRoot)],
                backupRoot);
            await using var prepared = await _plugins.PrepareManifestAsync(manifest, pluginRoot, cancellationToken);
            await prepared.CommitAsync(cancellationToken);
            transaction.Complete();
            _pluginSessions.Revoke(manifest.Id);
            return manifest;
        }
        finally
        {
            if (Directory.Exists(extractionRoot)) Directory.Delete(extractionRoot, recursive: true);
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, recursive: true);
        }
    }

    private async Task<BridgeResponse> SubmitPluginSettingsAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var pluginId = ReadString(request, "pluginId");
        if (string.IsNullOrWhiteSpace(pluginId)) return InvalidPayload(request);
        var settings = request.Payload is { ValueKind: JsonValueKind.Object } payload && payload.TryGetProperty("settings", out var value)
            ? value.Clone()
            : default(JsonElement?);
        return BridgeResponse.Success(request.RequestId, await _plugins.SubmitSettingsAsync(pluginId, settings, cancellationToken));
    }

    private async Task<BridgeResponse> DeletePluginAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var pluginId = ReadString(request, "id");
        if (string.IsNullOrWhiteSpace(pluginId)) return InvalidPayload(request);
        var removed = await _plugins.RemoveAsync(pluginId, cancellationToken);
        if (!removed) return BridgeResponse.Failure(request.RequestId, "PluginNotFound", "插件不存在");
        _pluginSessions.Revoke(pluginId);
        return BridgeResponse.Success(request.RequestId, new { pluginId });
    }
}
