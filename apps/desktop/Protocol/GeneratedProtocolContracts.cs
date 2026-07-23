// 此文件由 packages/protocol/schema/onedesk.protocol.json 生成，请勿手工修改。
// schema-sha256: d9643bed89f2dde0c2a7f1a781c1ff539cc0f45c9f90495b5c9597b44c0de77b
using System.Text.Json;

namespace OneDesk.Desktop.Transport;

public static class OneDeskProtocol
{
    public const int Version = 1;
    public const string SchemaSha256 = "d9643bed89f2dde0c2a7f1a781c1ff539cc0f45c9f90495b5c9597b44c0de77b";
}

public enum GatewayMessageType
{
    Request,
    Response,
    Event
}

public enum ProtocolDeviceKind
{
    Desktop,
    Mobile
}

public enum ProtocolSourceKind
{
    Component,
    Plugin,
    System
}

public sealed record ProtocolDeviceIdentity(
    string DeviceId,
    string DisplayName,
    ProtocolDeviceKind Kind,
    string Platform,
    string Architecture
);

public sealed record ProtocolTrustedSource(
    string? SchemeId,
    string? PageId,
    string? ComponentId,
    string? PluginId,
    ProtocolSourceKind Kind
);

public sealed record PairingRequestContract(
    string VerificationCode,
    string ClientNonce,
    string? StableDeviceKey,
    ProtocolDeviceIdentity MobileIdentity
);

public sealed record PairingResponseContract(
    ProtocolDeviceIdentity DesktopIdentity,
    ProtocolDeviceIdentity AssignedMobileIdentity,
    string TrustCredential,
    long CredentialExpiresAtUnixMs
);

public sealed record TrustedConnectRequestContract(
    string TrustCredential,
    string ClientNonce,
    ProtocolDeviceIdentity MobileIdentity
);

public sealed record JsApiRequestContract(
    string RequestId,
    string TargetDeviceId,
    ProtocolTrustedSource Source,
    string Capability,
    JsonElement Payload
);

public sealed record JsApiErrorContract(
    string Code,
    string Message,
    bool HighRisk
);

public sealed record JsApiResponseContract(
    string RequestId,
    bool Ok,
    JsApiErrorContract? Error,
    JsonElement? Payload
);

public sealed record SchemeDescriptorContract(
    string Version,
    string Hash,
    long TotalBytes,
    bool HasScheme
);

public sealed record LogRecordContract(
    string LogId,
    long CreatedAtUnixMs,
    string SourceDeviceId,
    string Level,
    string Category,
    string Message,
    JsonElement Context
);

public sealed record MobileGatewayEnvelope(
    int ProtocolVersion,
    string MessageType,
    string MessageId,
    string? CorrelationId,
    JsonElement Payload
);
