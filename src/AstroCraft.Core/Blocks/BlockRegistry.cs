namespace AstroCraft.Core.Blocks;

public sealed class BlockRegistry
{
    private readonly BlockDefinition[] _definitions = new BlockDefinition[256];

    public BlockRegistry(IEnumerable<BlockDefinition> definitions)
    {
        foreach (BlockDefinition definition in definitions)
        {
            _definitions[(int)definition.Id] = definition;
        }

        _definitions[(int)BlockId.Air] = CreateAirFallback();
    }

    public BlockDefinition Get(BlockId id) => _definitions[(int)id];

    public bool IsSolid(BlockId id) => Get(id).IsSolid;

    public bool IsTransparent(BlockId id) => Get(id).IsTransparent;

    public bool IsBreakable(BlockId id) => Get(id).IsBreakable;

    public static BlockRegistry CreateDefault() => new(DefaultDefinitions());

    private static BlockDefinition CreateAirFallback() => new()
    {
        Id = BlockId.Air,
        Name = "air",
        IsSolid = false,
        IsTransparent = true,
        IsBreakable = false,
        Hardness = 0,
        BlocksOxygen = false,
        ProvidesOxygen = false,
        TextureTop = 0,
        TextureSide = 0,
        TextureBottom = 0,
    };

    private static IEnumerable<BlockDefinition> DefaultDefinitions()
    {
        yield return Block(BlockId.Stone, "stone", true, false, 1.5f, 1, 1, 1);
        yield return Block(BlockId.Dirt, "dirt", true, false, 0.5f, 2, 2, 2);
        yield return Block(BlockId.Grass, "grass", true, false, 0.6f, 3, 2, 2);
        yield return Block(BlockId.Sand, "sand", true, false, 0.5f, 4, 4, 4);
        yield return Fluid(BlockId.Water, "water", 5);
        yield return Fluid(BlockId.Oil, "oil", 6);
        yield return Block(BlockId.Wood, "wood", true, false, 2f, 7, 7, 7);
        yield return Block(BlockId.Leaves, "leaves", true, true, 0.2f, 8, 8, 8);
        yield return Block(BlockId.Glass, "glass", true, true, 0.3f, 9, 9, 9);
        yield return Block(BlockId.IronOre, "iron_ore", true, false, 3f, 10, 1, 1);
        yield return Block(BlockId.CopperOre, "copper_ore", true, false, 3f, 11, 1, 1);
        yield return Block(BlockId.CoalOre, "coal_ore", true, false, 3f, 12, 1, 1);
        yield return Block(BlockId.Gravel, "gravel", true, false, 0.6f, 13, 13, 13);
        yield return Block(BlockId.Bedrock, "bedrock", true, false, -1f, 14, 14, 14, breakable: false);
        yield return Block(BlockId.Ice, "ice", true, true, 0.5f, 15, 15, 15);
        yield return Block(BlockId.Snow, "snow", true, false, 0.1f, 16, 16, 16);
        yield return Block(BlockId.Concrete, "concrete", true, false, 1.8f, 17, 17, 17);
        yield return Block(BlockId.Steel, "steel", true, false, 5f, 18, 18, 18);
        yield return Block(BlockId.Bricks, "bricks", true, false, 2f, 19, 19, 19);
        yield return Block(BlockId.Glowstone, "glowstone", true, false, 0.3f, 20, 20, 20);
        yield return Block(BlockId.Obsidian, "obsidian", true, false, 50f, 21, 21, 21);
        yield return Block(BlockId.Clay, "clay", true, false, 0.6f, 22, 22, 22);
        yield return Block(BlockId.Moss, "moss", true, false, 0.2f, 23, 23, 23);
        yield return Block(BlockId.Sandstone, "sandstone", true, false, 0.8f, 24, 24, 24);
        yield return Block(BlockId.Basalt, "basalt", true, false, 1.8f, 25, 25, 25);
    }

    private static BlockDefinition Block(
        BlockId id,
        string name,
        bool solid,
        bool transparent,
        float hardness,
        byte top,
        byte side,
        byte bottom,
        bool breakable = true) => new()
    {
        Id = id,
        Name = name,
        IsSolid = solid,
        IsTransparent = transparent,
        IsBreakable = breakable,
        Hardness = hardness,
        BlocksOxygen = solid,
        ProvidesOxygen = false,
        TextureTop = top,
        TextureSide = side,
        TextureBottom = bottom,
    };

    private static BlockDefinition Fluid(BlockId id, string name, byte texture) => new()
    {
        Id = id,
        Name = name,
        IsSolid = false,
        IsTransparent = true,
        IsBreakable = false,
        Hardness = 0,
        BlocksOxygen = false,
        ProvidesOxygen = false,
        TextureTop = texture,
        TextureSide = texture,
        TextureBottom = texture,
    };
}
