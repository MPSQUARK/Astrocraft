using AstroCraft.Core.Crafting;

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

    public bool IsEdible(BlockId id) => Get(id).IsEdible;

    public BlockId GetDrop(BlockId brokenBlock)
    {
        StackKey stack = GetDropStack(brokenBlock);
        return stack.ItemId != ItemId.None ? BlockId.Air : stack.BlockId;
    }

    public StackKey GetDropStack(BlockId brokenBlock)
    {
        BlockDefinition definition = Get(brokenBlock);
        if (!definition.DropsItem)
        {
            return StackKey.Empty;
        }

        if (definition.DropItemId != ItemId.None)
        {
            return StackKey.Item(definition.DropItemId);
        }

        BlockId drop = definition.DropBlockId == BlockId.Air ? brokenBlock : definition.DropBlockId;
        return StackKey.Block(drop);
    }

    public float GetLightEmission(BlockId id) => Get(id).LightEmission;

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
        yield return Block(BlockId.Stone, "stone", true, false, 1.5f, 1, 1, 1, drop: BlockId.Cobblestone);
        yield return Block(BlockId.Dirt, "dirt", true, false, 0.5f, 2, 2, 2, drop: BlockId.Dirt);
        yield return Block(BlockId.Grass, "grass", true, false, 0.6f, 3, 26, 2, drop: BlockId.Dirt);
        yield return Block(BlockId.Sand, "sand", true, false, 0.5f, 4, 4, 4, drop: BlockId.Sand);
        yield return Fluid(BlockId.Water, "water", 5);
        yield return Fluid(BlockId.Oil, "oil", 6);
        yield return AxisBlock(BlockId.Wood, "wood", true, false, 2f, 27, 7, 27);
        yield return Block(BlockId.Leaves, "leaves", true, false, 0.2f, 8, 8, 8, dropsItem: false);
        yield return Block(BlockId.Glass, "glass", true, true, 0.3f, 9, 9, 9);
        yield return Block(BlockId.IronOre, "iron_ore", true, false, 3f, 10, 1, 1, drop: BlockId.IronOre);
        yield return Block(BlockId.CopperOre, "copper_ore", true, false, 3f, 11, 1, 1, drop: BlockId.CopperOre);
        yield return Block(BlockId.CoalOre, "coal_ore", true, false, 3f, 12, 1, 1, dropItem: ItemId.Coal);
        yield return Block(BlockId.Gravel, "gravel", true, false, 0.6f, 13, 13, 13);
        yield return Block(BlockId.Bedrock, "bedrock", true, false, -1f, 14, 14, 14, breakable: false);
        yield return Block(BlockId.Ice, "ice", true, true, 0.5f, 15, 15, 15);
        yield return Block(BlockId.Snow, "snow", true, false, 0.1f, 16, 16, 16);
        yield return Block(BlockId.Concrete, "concrete", true, false, 1.8f, 17, 17, 17);
        yield return Block(BlockId.Steel, "steel", true, false, 5f, 18, 18, 18);
        yield return Block(BlockId.Bricks, "bricks", true, false, 2f, 19, 19, 19);
        yield return Block(BlockId.Glowstone, "glowstone", true, false, 0.3f, 20, 20, 20, lightEmission: 1.0f);
        yield return Block(BlockId.Obsidian, "obsidian", true, false, 50f, 21, 21, 21);
        yield return Block(BlockId.Clay, "clay", true, false, 0.6f, 22, 22, 22);
        yield return Block(BlockId.Moss, "moss", true, false, 0.2f, 23, 23, 23);
        yield return Block(BlockId.Sandstone, "sandstone", true, false, 0.8f, 24, 24, 24);
        yield return Block(BlockId.Basalt, "basalt", true, false, 1.8f, 25, 25, 25);
        yield return Fluid(BlockId.Lava, "lava", 28, lightEmission: 0.92f);
        yield return AxisBlock(BlockId.BirchLog, "birch_log", true, false, 2f, 33, 32, 33);
        yield return AxisBlock(BlockId.SpruceLog, "spruce_log", true, false, 2f, 35, 34, 35);
        yield return AxisBlock(BlockId.JungleLog, "jungle_log", true, false, 2.2f, 37, 36, 37);
        yield return Block(BlockId.BirchLeaves, "birch_leaves", true, false, 0.2f, 38, 38, 38, dropsItem: false);
        yield return Block(BlockId.SpruceLeaves, "spruce_leaves", true, false, 0.2f, 39, 39, 39, dropsItem: false);
        yield return Block(BlockId.JungleLeaves, "jungle_leaves", true, false, 0.2f, 40, 40, 40, dropsItem: false);
        yield return Block(BlockId.Cactus, "cactus", true, false, 0.4f, 41, 41, 41);
        yield return Block(BlockId.SnowLayer, "snow_layer", true, false, 0.05f, 42, 42, 42);
        yield return Block(BlockId.Podzol, "podzol", true, false, 0.6f, 43, 44, 2, drop: BlockId.Dirt);
        yield return Block(BlockId.Mycelium, "mycelium", true, false, 0.6f, 45, 46, 2, drop: BlockId.Dirt);
        yield return Block(BlockId.Deepslate, "deepslate", true, false, 2f, 47, 47, 47, drop: BlockId.Deepslate);
        yield return Block(BlockId.PackedIce, "packed_ice", true, false, 0.5f, 48, 48, 48);
        yield return Block(BlockId.RedSand, "red_sand", true, false, 0.5f, 49, 49, 49);
        yield return Block(BlockId.JungleGrass, "jungle_grass", true, false, 0.6f, 50, 51, 2, drop: BlockId.Dirt);
        yield return Block(BlockId.Shale, "shale", true, false, 1.6f, 52, 52, 52);
        yield return Plant(BlockId.Fern, "fern", 0.1f, 53, dropsItem: false, plantHeight: 0.88f);
        yield return Food(BlockId.Apple, "apple", 54, 4f, 2.4f);
        yield return Block(BlockId.Cobblestone, "cobblestone", true, false, 2f, 55, 55, 55);
        yield return Block(BlockId.Granite, "granite", true, false, 1.5f, 56, 56, 56);
        yield return Block(BlockId.PolishedGranite, "polished_granite", true, false, 1.5f, 57, 57, 57);
        yield return Block(BlockId.Andesite, "andesite", true, false, 1.5f, 58, 58, 58);
        yield return Block(BlockId.PolishedAndesite, "polished_andesite", true, false, 1.5f, 59, 59, 59);
        yield return Block(BlockId.Diorite, "diorite", true, false, 1.5f, 60, 60, 60);
        yield return Block(BlockId.PolishedDiorite, "polished_diorite", true, false, 1.5f, 61, 61, 61);
        yield return Plant(BlockId.TallGrass, "tall_grass", 0.1f, 62, plantHeight: 0.92f);
        yield return Plant(BlockId.ShortGrass, "short_grass", 0.05f, 64, dropsItem: false, plantHeight: 0.52f);
        yield return Plant(BlockId.FlowerRed, "flower_red", 0.1f, 63, plantHeight: 0.42f);
        yield return Plant(BlockId.FlowerYellow, "flower_yellow", 0.1f, 29, plantHeight: 0.40f);
        yield return Plant(BlockId.FlowerBlue, "flower_blue", 0.1f, 30, plantHeight: 0.40f);
        yield return Plant(BlockId.Shrub, "shrub", 0.2f, 31, dropsItem: false, plantHeight: 0.68f);
        yield return Block(BlockId.Planks, "planks", true, false, 2f, 32, 32, 32, drop: BlockId.Planks);
        yield return Block(BlockId.CraftingTable, "crafting_table", true, false, 2.5f, 33, 33, 33, drop: BlockId.CraftingTable);
        yield return Block(BlockId.Furnace, "furnace", true, false, 3.5f, 34, 34, 34, drop: BlockId.Furnace);
        yield return PlaceableBlock(BlockId.Torch, "torch", 20, 20, 20, lightEmission: 0.55f, drop: BlockId.Torch);
    }

    private static BlockDefinition AxisBlock(
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
        PlacementOrientation = BlockPlacementOrientation.AxisAligned,
    };

    private static BlockDefinition Block(
        BlockId id,
        string name,
        bool solid,
        bool transparent,
        float hardness,
        byte top,
        byte side,
        byte bottom,
        bool breakable = true,
        BlockId drop = BlockId.Air,
        ItemId dropItem = ItemId.None,
        bool dropsItem = true,
        float lightEmission = 0f) => new()
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
        DropBlockId = drop,
        DropItemId = dropItem,
        DropsItem = dropsItem,
        LightEmission = lightEmission,
    };

    private static BlockDefinition PlaceableBlock(
        BlockId id,
        string name,
        byte top,
        byte side,
        byte bottom,
        BlockId drop = BlockId.Air,
        float lightEmission = 0f) => new()
    {
        Id = id,
        Name = name,
        IsSolid = false,
        IsTransparent = true,
        IsBreakable = true,
        Hardness = 0f,
        BlocksOxygen = false,
        ProvidesOxygen = false,
        TextureTop = top,
        TextureSide = side,
        TextureBottom = bottom,
        PlacementOrientation = BlockPlacementOrientation.AxisAligned,
        IsPlaceable = true,
        DropBlockId = drop,
        LightEmission = lightEmission,
    };

    private static BlockDefinition Fluid(BlockId id, string name, byte texture, float lightEmission = 0f) => new()
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
        LightEmission = lightEmission,
    };

    private static BlockDefinition Plant(
        BlockId id,
        string name,
        float hardness,
        byte texture,
        bool breakable = true,
        bool dropsItem = true,
        BlockId drop = BlockId.Air,
        float plantHeight = 1f) => new()
    {
        Id = id,
        Name = name,
        IsSolid = false,
        IsTransparent = false,
        IsBreakable = breakable,
        Hardness = hardness,
        BlocksOxygen = false,
        ProvidesOxygen = false,
        TextureTop = texture,
        TextureSide = texture,
        TextureBottom = texture,
        DropBlockId = drop,
        DropsItem = dropsItem,
        RenderShape = BlockRenderShape.CrossPlant,
        PlantHeight = plantHeight,
    };

    private static BlockDefinition Food(BlockId id, string name, byte texture, float hungerRestore, float saturationRestore) => new()
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
        IsEdible = true,
        HungerRestore = hungerRestore,
        SaturationRestore = saturationRestore,
    };
}
