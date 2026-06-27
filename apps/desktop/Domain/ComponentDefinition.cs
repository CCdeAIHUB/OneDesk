namespace OneDesk.Desktop.Domain;

public enum ComponentEditMode
{
    Visual,
    Code
}

public sealed record ComponentDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required ComponentEditMode EditMode { get; init; }
    public required string EntryFile { get; init; }
    public string? VisualConfigFile { get; init; }
    public required IReadOnlyList<string> ActionIds { get; init; }
    public required IReadOnlyList<PermissionGrant> RequestedPermissions { get; init; }
    public required IReadOnlyList<DependencyDefinition> PluginDependencies { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record VisualComponentConfig
{
    public required string BackgroundKind { get; init; }
    public required string BackgroundValue { get; init; }
    public required string PressedStyle { get; init; }
    public required int BorderRadius { get; init; }
    public required IReadOnlyList<VisualTextLayer> TextLayers { get; init; }
    public required IReadOnlyList<VisualImageLayer> ImageLayers { get; init; }
}

public sealed record VisualTextLayer
{
    public required string Text { get; init; }
    public required string FontFamily { get; init; }
    public required int FontSize { get; init; }
    public required string Color { get; init; }
    public required string Position { get; init; }
}

public sealed record VisualImageLayer
{
    public required string Source { get; init; }
    public required string Size { get; init; }
    public required string Position { get; init; }
    public required int Margin { get; init; }
}
