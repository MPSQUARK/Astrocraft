using AstroCraft.Core.Blocks;
using AstroCraft.Core.Rendering;

namespace AstroCraft.Tests;

public sealed class TextureAtlasGeneratorTests
{
    [Fact]
    public void GenerateRgbaPixels_MatchesAtlasDimensions()
    {
        byte[] pixels = TextureAtlasGenerator.GenerateRgbaPixels();
        int expectedLength = TextureAtlasGenerator.AtlasSize * TextureAtlasGenerator.AtlasSize * 4;
        Assert.Equal(expectedLength, pixels.Length);
    }

    [Fact]
    public void Constants_MatchBlockRegistryTileIndices()
    {
        BlockRegistry registry = BlockRegistry.CreateDefault();

        Assert.Equal(16, TextureAtlasGenerator.TileSize);
        Assert.Equal(64, TextureAtlasGenerator.TileCount);

        Assert.Equal((byte)1, registry.Get(BlockId.Stone).TextureTop);
        Assert.Equal((byte)2, registry.Get(BlockId.Dirt).TextureTop);
        Assert.Equal((byte)3, registry.Get(BlockId.Grass).TextureTop);
        Assert.Equal((byte)26, registry.Get(BlockId.Grass).TextureSide);
        Assert.Equal((byte)2, registry.Get(BlockId.Grass).TextureBottom);
        Assert.Equal((byte)27, registry.Get(BlockId.Wood).TextureTop);
        Assert.Equal((byte)7, registry.Get(BlockId.Wood).TextureSide);
        Assert.Equal((byte)27, registry.Get(BlockId.Wood).TextureBottom);
        Assert.Equal((byte)8, registry.Get(BlockId.Leaves).TextureTop);
        Assert.Equal((byte)38, registry.Get(BlockId.BirchLeaves).TextureTop);
        Assert.Equal((byte)39, registry.Get(BlockId.SpruceLeaves).TextureTop);
        Assert.Equal((byte)40, registry.Get(BlockId.JungleLeaves).TextureTop);
    }

    [Fact]
    public void BirchSpruceJungleLeaves_HaveDistinctPalettes()
    {
        byte[] pixels = TextureAtlasGenerator.GenerateRgbaPixels();
        double birchGreen = AverageChannel(pixels, 38, channelOffset: 1);
        double spruceGreen = AverageChannel(pixels, 39, channelOffset: 1);
        double jungleGreen = AverageChannel(pixels, 40, channelOffset: 1);
        double birchBlue = AverageChannel(pixels, 38, channelOffset: 2);
        double spruceBlue = AverageChannel(pixels, 39, channelOffset: 2);
        double jungleBlue = AverageChannel(pixels, 40, channelOffset: 2);

        Assert.True(birchGreen > spruceGreen + 20, "Birch leaves should be lighter green than spruce.");
        Assert.True(spruceBlue > birchBlue + 8, "Spruce leaves should read as blue-green.");
        Assert.True(jungleGreen < birchGreen - 25, "Jungle leaves should be deeper green than birch.");
        Assert.True(jungleGreen < spruceGreen - 10, "Jungle leaves should be deeper green than spruce.");
    }

    [Fact]
    public void GrassTop_IsGreenerThanDirt()
    {
        byte[] pixels = TextureAtlasGenerator.GenerateRgbaPixels();
        double grassGreen = AverageChannel(pixels, 3, channelOffset: 1);
        double dirtGreen = AverageChannel(pixels, 2, channelOffset: 1);
        Assert.True(grassGreen > dirtGreen + 30);
        Assert.True(grassGreen > 150, "Grass top should read as vibrant green.");
    }

    [Fact]
    public void GrassSide_HasGreenOverhangAboveDirt()
    {
        byte[] pixels = TextureAtlasGenerator.GenerateRgbaPixels();
        double topGreen = AverageRowChannel(pixels, 26, row: 0, channelOffset: 1);
        double bottomGreen = AverageRowChannel(pixels, 26, row: 15, channelOffset: 1);
        double topRed = AverageRowChannel(pixels, 26, row: 0, channelOffset: 0);
        double bottomRed = AverageRowChannel(pixels, 26, row: 15, channelOffset: 0);
        Assert.True(topGreen > bottomGreen + 15);
        Assert.True(bottomRed > topRed + 10);
    }

    [Fact]
    public void GrassSide_FringeUsesHorizontalGrassOnly()
    {
        byte[] pixels = TextureAtlasGenerator.GenerateRgbaPixels();
        for (int x = 0; x < TextureAtlasGenerator.TileSize; x++)
        {
            byte fringeGreen = GetTilePixel(pixels, 26, x, 0, channelOffset: 1);
            for (int y = 1; y < 8; y++)
            {
                byte green = GetTilePixel(pixels, 26, x, y, channelOffset: 1);
                if (green < fringeGreen - 12)
                {
                    break;
                }

                Assert.InRange(System.Math.Abs(green - fringeGreen), 0, 4);
            }
        }
    }

    [Fact]
    public void LeavesTile_HasSemiTransparentPixels()
    {
        byte[] pixels = TextureAtlasGenerator.GenerateRgbaPixels();
        int lowAlpha = 0;
        int highAlpha = 0;
        for (int y = 0; y < TextureAtlasGenerator.TileSize; y++)
        {
            for (int x = 0; x < TextureAtlasGenerator.TileSize; x++)
            {
                byte alpha = GetTilePixel(pixels, 8, x, y, channelOffset: 3);
                if (alpha < 200)
                {
                    lowAlpha++;
                }
                else if (alpha == 255)
                {
                    highAlpha++;
                }
            }
        }

        Assert.True(lowAlpha > 8);
        Assert.True(highAlpha < TextureAtlasGenerator.TileSize * TextureAtlasGenerator.TileSize);
    }

    [Fact]
    public void StoneTile_EdgesMatchForSeamlessTiling()
    {
        byte[] pixels = TextureAtlasGenerator.GenerateRgbaPixels();
        const int tile = 1;
        for (int i = 0; i < TextureAtlasGenerator.TileSize; i++)
        {
            byte leftR = GetTilePixel(pixels, tile, 0, i, 0);
            byte rightR = GetTilePixel(pixels, tile, TextureAtlasGenerator.TileSize - 1, i, 0);
            byte topG = GetTilePixel(pixels, tile, i, 0, 1);
            byte bottomG = GetTilePixel(pixels, tile, i, TextureAtlasGenerator.TileSize - 1, 1);
            Assert.Equal(leftR, rightR);
            Assert.Equal(topG, bottomG);
        }
    }

    [Fact]
    public void SandAndGrassTop_EdgesMatchForSeamlessTiling()
    {
        byte[] pixels = TextureAtlasGenerator.GenerateRgbaPixels();
        foreach (int tile in new[] { 3, 4 })
        {
            for (int i = 0; i < TextureAtlasGenerator.TileSize; i++)
            {
                byte leftR = GetTilePixel(pixels, tile, 0, i, 0);
                byte rightR = GetTilePixel(pixels, tile, TextureAtlasGenerator.TileSize - 1, i, 0);
                byte topG = GetTilePixel(pixels, tile, i, 0, 1);
                byte bottomG = GetTilePixel(pixels, tile, i, TextureAtlasGenerator.TileSize - 1, 1);
                Assert.Equal(leftR, rightR);
                Assert.Equal(topG, bottomG);
            }
        }
    }

    [Fact]
    public void ProceduralTiles_AreNotFlatSolids()
    {
        byte[] pixels = TextureAtlasGenerator.GenerateRgbaPixels();
        foreach (int tile in new[] { 1, 2, 3, 4, 7, 8, 10, 11, 12, 26 })
        {
            Assert.True(CountDistinctRgb(pixels, tile) > 12, $"Tile {tile} lacks variation.");
        }
    }

    private static double AverageChannel(byte[] pixels, int tile, int channelOffset)
    {
        double sum = 0;
        for (int y = 0; y < TextureAtlasGenerator.TileSize; y++)
        {
            for (int x = 0; x < TextureAtlasGenerator.TileSize; x++)
            {
                sum += GetTilePixel(pixels, tile, x, y, channelOffset);
            }
        }

        return sum / (TextureAtlasGenerator.TileSize * TextureAtlasGenerator.TileSize);
    }

    private static double AverageRowChannel(byte[] pixels, int tile, int row, int channelOffset)
    {
        double sum = 0;
        for (int x = 0; x < TextureAtlasGenerator.TileSize; x++)
        {
            sum += GetTilePixel(pixels, tile, x, row, channelOffset);
        }

        return sum / TextureAtlasGenerator.TileSize;
    }

    private static int CountDistinctRgb(byte[] pixels, int tile)
    {
        HashSet<int> colors = new();
        for (int y = 0; y < TextureAtlasGenerator.TileSize; y++)
        {
            for (int x = 0; x < TextureAtlasGenerator.TileSize; x++)
            {
                int r = GetTilePixel(pixels, tile, x, y, 0);
                int g = GetTilePixel(pixels, tile, x, y, 1);
                int b = GetTilePixel(pixels, tile, x, y, 2);
                colors.Add((r << 16) | (g << 8) | b);
            }
        }

        return colors.Count;
    }

    private static byte GetTilePixel(byte[] pixels, int tile, int x, int y, int channelOffset)
    {
        int tileX = tile % TextureAtlasGenerator.TilesPerRow;
        int tileY = tile / TextureAtlasGenerator.TilesPerRow;
        int atlasX = tileX * TextureAtlasGenerator.TileSize + x;
        int atlasY = tileY * TextureAtlasGenerator.TileSize + y;
        int index = (atlasY * TextureAtlasGenerator.AtlasSize + atlasX) * 4 + channelOffset;
        return pixels[index];
    }
}
