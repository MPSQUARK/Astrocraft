namespace AstroCraft.Core.Blocks;

public sealed class BlockDefinition
{
    public required BlockId Id { get; init; }
    public required string Name { get; init; }
    public required bool IsSolid { get; init; }
    public required bool IsTransparent { get; init; }
    public required bool IsBreakable { get; init; }
    public required float Hardness { get; init; }
    public required bool BlocksOxygen { get; init; }
    public required bool ProvidesOxygen { get; init; }
    public required byte TextureTop { get; init; }
    public required byte TextureSide { get; init; }
    public required byte TextureBottom { get; init; }
}
