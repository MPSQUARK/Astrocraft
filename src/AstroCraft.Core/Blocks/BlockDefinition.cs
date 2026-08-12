using AstroCraft.Core.Crafting;

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
    public BlockPlacementOrientation PlacementOrientation { get; init; } = BlockPlacementOrientation.None;
    public bool IsEdible { get; init; }
    public float HungerRestore { get; init; }
    public float SaturationRestore { get; init; }
    public BlockId DropBlockId { get; init; } = BlockId.Air;
    public ItemId DropItemId { get; init; } = ItemId.None;
    public bool DropsItem { get; init; } = true;
    public bool IsPlaceable { get; init; }
    public float LightEmission { get; init; }
    public BlockRenderShape RenderShape { get; init; } = BlockRenderShape.Cube;
    public float PlantHeight { get; init; } = 1f;
}
