using System.Text.Json;
using OneDesk.Desktop.Domain;
using OneDesk.Desktop.Services;

namespace OneDesk.Desktop.Shell;

public sealed record BridgeRequest(
    string Type,
    string RequestId,
    JsonElement? Payload = null,
    string? TargetDeviceId = null,
    string? Capability = null,
    BridgeSource? Source = null);

public sealed record BridgeSource(
    string? SchemeId,
    string? PageId,
    string? ComponentId,
    string? PluginId,
    string Kind);

public sealed record BridgeResponse(
    string RequestId,
    bool Ok,
    object? Payload = null,
    string? ErrorCode = null,
    string? Message = null)
{
    public static BridgeResponse Success(string requestId, object? payload = null) => new(requestId, true, payload);

    public static BridgeResponse Failure(string requestId, string errorCode, string message) =>
        new(requestId, false, null, errorCode, message);
}

public sealed record PendingPackageImport(
    string Token,
    string Kind,
    string Path,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SourceKeys);

public sealed record PackageInspection(
    string Token,
    string Kind,
    string Name,
    string PackagePath,
    IReadOnlyList<PermissionDeclaration> Permissions,
    IReadOnlyList<DependencyDefinition> PluginDependencies,
    IReadOnlyList<string> MissingPluginIds,
    IReadOnlyList<PluginVersionConflict> PluginConflicts,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SourceKeys);

public sealed record ConfirmWorkspaceImportPayload(
    string Token,
    IReadOnlyList<string> GrantedCapabilities,
    IReadOnlyDictionary<string, PluginVersionChoice> PluginChoices);

public sealed record ComponentFilesPayload(string Id, IReadOnlyDictionary<string, string> Files);

public sealed record ResourceCopyPayload(string ResourceId, string TargetId);

public sealed record PluginFrontendRuntimeDescriptor(string PluginId, string Name, string SessionId, string Source);

public sealed record PluginFrontendJsApiPayload(
    string SessionId,
    string TargetDeviceId,
    string Capability,
    JsonElement Payload);

public sealed record PluginFrontendBackendPayload(string SessionId, string Method, JsonElement? Parameters);

public sealed record PluginSettingsPayload(string PluginId, JsonElement? Settings);
