namespace OneDesk.Desktop.Services;

public enum DeviceKind
{
    Desktop,
    Mobile
}

public sealed record DeviceIdentity(
    string DeviceId,
    string DisplayName,
    DeviceKind Kind,
    string Platform,
    string Architecture);

public sealed record TrustedSource(
    string? SchemeId,
    string? PageId,
    string? ComponentId,
    string? PluginId,
    string Kind);

public sealed record JsApiRequest(
    string RequestId,
    string TargetDeviceId,
    TrustedSource Source,
    string Capability,
    object? Payload);

public sealed record JsApiResult(bool Ok, string? ErrorCode, string? Message, object? Payload)
{
    public static JsApiResult Success(object? payload = null) => new(true, null, null, payload);
    public static JsApiResult Error(string code, string message) => new(false, code, message, null);
}

public sealed record PermissionDeclaration(
    string Category,
    string Capability,
    bool HighRisk,
    string Description);

public sealed record CapabilityDefinition(
    string Id,
    string Category,
    string CategoryName,
    string Name,
    string Description,
    bool HighRisk,
    CapabilitySupport Desktop,
    CapabilitySupport Android,
    CapabilitySupport Ios);

public sealed record CapabilitySupport(bool Supported, string Note);

public sealed record CapabilityCategory(
    string Id,
    string Name,
    bool HighRisk,
    IReadOnlyList<CapabilityDefinition> Capabilities);

public sealed record StructuredLogRecord(
    string LogId,
    DateTimeOffset CreatedAt,
    string SourceDeviceId,
    string Level,
    string Category,
    string Message,
    IReadOnlyDictionary<string, object?> Context);

public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    bool Persistent,
    IReadOnlyList<PermissionDeclaration> Permissions,
    PluginFrontendDefinition? Frontend = null,
    PluginBackendDefinition? Backend = null,
    IReadOnlyDictionary<string, object?>? SettingsSchema = null,
    bool SelfContained = true);

public sealed record PluginFrontendDefinition(string Entry);

public sealed record PluginBackendDefinition(
    string Protocol,
    bool Persistent,
    IReadOnlyList<PluginPlatformArtifact> Artifacts);

public sealed record PluginPlatformArtifact(
    string Platform,
    string Architecture,
    string Path,
    IReadOnlyList<string> Command);
