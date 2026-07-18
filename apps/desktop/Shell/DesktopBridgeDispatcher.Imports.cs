using System.IO.Compression;
using System.Text.Json;
using OneDesk.Desktop.Domain;
using OneDesk.Desktop.Services;

namespace OneDesk.Desktop.Shell;

public sealed partial class DesktopBridgeDispatcher
{
    private async Task<BridgeResponse> InspectWorkspaceImportAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var kind = ReadString(request, "kind");
        var fileKind = kind switch
        {
            "Component" => DesktopFileKind.ComponentPackage,
            "Page" => DesktopFileKind.PagePackage,
            "Scheme" => DesktopFileKind.SchemePackage,
            _ => (DesktopFileKind?)null,
        };
        if (fileKind is null) return InvalidPayload(request);
        var path = await _platform.PickFileAsync(fileKind.Value, cancellationToken);
        if (string.IsNullOrWhiteSpace(path)) return BridgeResponse.Failure(request.RequestId, "UserCancelled", "已取消导入");
        var inspection = InspectWorkspacePackage(kind!, path);
        var token = Guid.NewGuid().ToString("N");
        _pendingImports[token] = new PendingPackageImport(token, kind!, path, inspection.SourceKeys);
        return BridgeResponse.Success(request.RequestId, inspection with { Token = token });
    }

    private async Task<BridgeResponse> ConfirmWorkspaceImportAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var payload = DeserializePayload<ConfirmWorkspaceImportPayload>(request);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Token) || !_pendingImports.TryRemove(payload.Token, out var pending))
        {
            return BridgeResponse.Failure(request.RequestId, "ImportSessionExpired", "导入会话不存在或已过期");
        }

        using var importSession = pending.Kind switch
        {
            "Component" => _packages.BeginImportComponent(pending.Path, InstalledPluginVersions(), payload.PluginChoices),
            "Page" => _packages.BeginImportPage(pending.Path, InstalledPluginVersions(), payload.PluginChoices),
            "Scheme" => _packages.BeginImportScheme(pending.Path, InstalledPluginVersions(), payload.PluginChoices),
            _ => throw new InvalidOperationException("UnsupportedImportKind"),
        };
        var result = importSession.Result;
        if (!result.Ready)
        {
            var message = result.MissingPluginIds.Count > 0
                ? $"缺少插件依赖：{string.Join("、", result.MissingPluginIds)}"
                : $"尚未处理插件版本冲突：{string.Join("、", result.UnresolvedPluginConflicts.Select(conflict => conflict.Id))}";
            return BridgeResponse.Failure(request.RequestId, "DependencyMissingOrConflict", message) with { Payload = result };
        }

        var preparedPlugins = new List<PluginHostService.PreparedPluginRegistration>();
        try
        {
            foreach (var pluginId in result.InstalledPluginIds)
            {
                preparedPlugins.Add(await PrepareInstalledPluginAsync(pluginId, cancellationToken));
            }
            // 所有插件都通过握手后才进入不可取消提交段，防止依赖集合只切换一半。
            foreach (var prepared in preparedPlugins) await prepared.CommitAsync(CancellationToken.None);
            importSession.Complete();
            foreach (var pluginId in result.InstalledPluginIds) _pluginSessions.Revoke(pluginId);
        }
        finally
        {
            foreach (var prepared in preparedPlugins) await prepared.DisposeAsync();
        }

        GrantImportedPermissions(payload.GrantedCapabilities, pending.SourceKeys);
        return BridgeResponse.Success(request.RequestId, result);
    }

    private PackageInspection InspectWorkspacePackage(string kind, string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var permissions = new List<PermissionDeclaration>();
        var sourceKeys = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var dependencies = new List<DependencyDefinition>();
        var packagedPlugins = new Dictionary<string, PluginManifest>(StringComparer.OrdinalIgnoreCase);
        var title = Path.GetFileName(packagePath);

        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            if (entry.FullName.EndsWith("onedesk.component.json", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = entry.Open();
                var component = JsonSerializer.Deserialize<ComponentDefinition>(stream, JsonOptions);
                if (component is not null)
                {
                    title = component.Name;
                    var declarations = component.RequestedPermissions
                        .Select(permission => new PermissionDeclaration(permission.Category, permission.Capability, permission.HighRisk, permission.Description))
                        .ToArray();
                    permissions.AddRange(declarations);
                    sourceKeys[$"component:{component.Id}"] = declarations.Select(item => item.Capability).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    dependencies.AddRange(component.PluginDependencies);
                }
            }
            else if (entry.FullName.EndsWith("onedesk.scheme.json", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = entry.Open();
                var scheme = JsonSerializer.Deserialize<SchemeDefinition>(stream, JsonOptions);
                if (scheme is not null)
                {
                    title = scheme.Name;
                    dependencies.AddRange(scheme.PluginDependencies);
                }
            }
            else if (entry.FullName.EndsWith("onedesk.plugin.json", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = entry.Open();
                var plugin = JsonSerializer.Deserialize<PluginManifest>(stream, JsonOptions);
                if (plugin is not null) packagedPlugins[plugin.Id] = plugin;
            }
        }

        var distinctPermissions = permissions
            .GroupBy(permission => permission.Capability, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(permission => permission.Category, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var pluginDependencies = dependencies
            .Where(dependency => string.Equals(dependency.Kind, "plugin", StringComparison.OrdinalIgnoreCase))
            .GroupBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var installed = InstalledPluginVersions();
        var missing = pluginDependencies
            .Where(dependency => !installed.ContainsKey(dependency.Id) && !packagedPlugins.ContainsKey(dependency.Id))
            .Select(dependency => dependency.Id)
            .ToArray();
        var conflicts = pluginDependencies.Select(dependency =>
            {
                installed.TryGetValue(dependency.Id, out var installedVersion);
                packagedPlugins.TryGetValue(dependency.Id, out var packaged);
                return new PluginVersionConflict(
                    dependency.Id,
                    dependency.Version,
                    installedVersion,
                    packaged?.Version,
                    installedVersion is not null,
                    packaged is not null);
            })
            .Where(conflict =>
                conflict.InstalledVersion is not null && !string.Equals(conflict.InstalledVersion, conflict.RequiredVersion, StringComparison.OrdinalIgnoreCase) ||
                conflict.InstalledVersion is null && conflict.PackagedVersion is not null && !string.Equals(conflict.PackagedVersion, conflict.RequiredVersion, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return new PackageInspection("", kind, title, packagePath, distinctPermissions, pluginDependencies, missing, conflicts, sourceKeys);
    }

    private async Task<PluginHostService.PreparedPluginRegistration> PrepareInstalledPluginAsync(string pluginId, CancellationToken cancellationToken)
    {
        var pluginRoot = Path.Combine(_paths.Plugins, pluginId);
        var manifestPath = Path.Combine(pluginRoot, "onedesk.plugin.json");
        var manifest = JsonSerializer.Deserialize<PluginManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonOptions)
            ?? throw new InvalidDataException($"插件清单格式不正确：{pluginId}");
        return await _plugins.PrepareManifestAsync(manifest, pluginRoot, cancellationToken);
    }

    private void GrantImportedPermissions(
        IReadOnlyList<string> grantedCapabilities,
        IReadOnlyDictionary<string, IReadOnlyList<string>> sourceKeys)
    {
        var granted = grantedCapabilities.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (sourceKey, capabilities) in sourceKeys)
        {
            foreach (var capability in capabilities.Where(granted.Contains)) _permissions.Grant(sourceKey, capability);
        }
    }
}
