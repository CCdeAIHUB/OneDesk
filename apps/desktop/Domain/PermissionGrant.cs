namespace OneDesk.Desktop.Domain;

public sealed record PermissionGrant
{
    public required string Category { get; init; }
    public required string Capability { get; init; }
    public required bool HighRisk { get; init; }
    public required string Description { get; init; }
    public bool Granted { get; init; } = true;
}
