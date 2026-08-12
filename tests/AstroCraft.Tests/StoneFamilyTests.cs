using AstroCraft.Core.Blocks;

namespace AstroCraft.Tests;

public class StoneFamilyTests
{
    [Fact]
    public void BlockRegistry_GetDrop_StoneYieldsCobble()
    {
        BlockRegistry registry = BlockRegistry.CreateDefault();
        Assert.Equal(BlockId.Cobblestone, registry.GetDrop(BlockId.Stone));
    }

    [Fact]
    public void BlockRegistry_DefinesStoneFamilyBlocks()
    {
        BlockRegistry registry = BlockRegistry.CreateDefault();

        BlockId[] stoneFamily =
        [
            BlockId.Cobblestone,
            BlockId.Granite,
            BlockId.PolishedGranite,
            BlockId.Andesite,
            BlockId.PolishedAndesite,
            BlockId.Diorite,
            BlockId.PolishedDiorite,
        ];

        HashSet<byte> textureIndices = new();
        foreach (BlockId blockId in stoneFamily)
        {
            BlockDefinition definition = registry.Get(blockId);
            Assert.False(string.IsNullOrWhiteSpace(definition.Name));
            Assert.True(definition.IsSolid);
            Assert.True(definition.IsBreakable);
            Assert.True(definition.DropsItem);
            Assert.Equal(blockId, registry.GetDrop(blockId));
            Assert.NotEqual((byte)0, definition.TextureTop);
            Assert.Equal(definition.TextureTop, definition.TextureSide);
            Assert.Equal(definition.TextureTop, definition.TextureBottom);
            Assert.True(textureIndices.Add(definition.TextureTop), $"Duplicate texture index for {blockId}");
        }
    }
}
