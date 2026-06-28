namespace OneDesk.Desktop.Domain;

public sealed record MediaResourceDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string FileName { get; init; }
    public required string Extension { get; init; }
    public required long SizeBytes { get; init; }
    public required string FileUri { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record MediaResourceCopyResult
{
    public required string ResourceId { get; init; }
    public required string RelativePath { get; init; }
    public required string FileUri { get; init; }
}
