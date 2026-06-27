namespace OneDesk.Desktop.Domain;

public sealed record SchemeDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<string> PageIds { get; init; }
    public required PageSwitchDefinition GlobalPrevious { get; init; }
    public required PageSwitchDefinition GlobalNext { get; init; }
    public required IReadOnlyList<PageSwitchEdge> Edges { get; init; }
    public required IReadOnlyList<DependencyDefinition> PluginDependencies { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record PageSwitchDefinition
{
    public required TriggerDefinition Trigger { get; init; }
    public required string Animation { get; init; }
}

public sealed record PageSwitchEdge
{
    public required string FromPageId { get; init; }
    public required string ToPageId { get; init; }
    public required TriggerDefinition Trigger { get; init; }
    public required string Animation { get; init; }
}

public sealed record DependencyDefinition
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Kind { get; init; }
}
