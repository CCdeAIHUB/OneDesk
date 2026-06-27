namespace OneDesk.Desktop.Domain;

public sealed record ActionDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required TriggerDefinition Trigger { get; init; }
    public required IReadOnlyList<JsApiInvocationDefinition> Invocations { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record TriggerDefinition
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string DisplayName { get; init; }
    public required int FingerCount { get; init; }
    public bool PlatformLimited { get; init; }
}

public sealed record JsApiInvocationDefinition
{
    public required string TargetDeviceId { get; init; }
    public required string Capability { get; init; }
    public required Dictionary<string, object?> Parameters { get; init; }
}
