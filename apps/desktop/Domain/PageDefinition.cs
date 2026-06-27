namespace OneDesk.Desktop.Domain;

public sealed record PageDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required int Rows { get; init; }
    public required int Columns { get; init; }
    public required GridSpacing Spacing { get; init; }
    public required string BackgroundKind { get; init; }
    public required string BackgroundValue { get; init; }
    public required IReadOnlyList<GridCellDefinition> Cells { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record GridSpacing
{
    public required int Padding { get; init; }
    public required int RowGap { get; init; }
    public required int ColumnGap { get; init; }
}

public sealed record GridCellDefinition
{
    public required string Id { get; init; }
    public required int Row { get; init; }
    public required int Column { get; init; }
    public required int RowSpan { get; init; }
    public required int ColumnSpan { get; init; }
    public string? ComponentId { get; init; }
    public required CellStyleDefinition Style { get; init; }
}

public sealed record CellStyleDefinition
{
    public required int BorderRadius { get; init; }
    public required string OutlineColor { get; init; }
    public required int OutlineWidth { get; init; }
    public required string OutlineStyle { get; init; }
}
