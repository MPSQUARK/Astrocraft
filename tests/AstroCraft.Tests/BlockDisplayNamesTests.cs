using AstroCraft.Core.Blocks;

namespace AstroCraft.Tests;

public class BlockDisplayNamesTests
{
    [Fact]
    public void GetDisplayName_CoversAllBlockIdValues()
    {
        foreach (BlockId blockId in Enum.GetValues<BlockId>())
        {
            string displayName = BlockDisplayNames.GetDisplayName(blockId);
            Assert.False(string.IsNullOrWhiteSpace(displayName));
        }
    }

    [Theory]
    [InlineData(BlockId.Air, "Air")]
    [InlineData(BlockId.IronOre, "Iron Ore")]
    [InlineData(BlockId.BirchLog, "Birch Log")]
    [InlineData(BlockId.SnowLayer, "Snow Layer")]
    [InlineData(BlockId.Apple, "Apple")]
    public void GetDisplayName_ReturnsExpectedNames(BlockId blockId, string expected)
    {
        Assert.Equal(expected, BlockDisplayNames.GetDisplayName(blockId));
    }
}
