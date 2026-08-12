namespace AstroCraft.Core.Rendering;

internal readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor From(byte r, byte g, byte b) => new(r, g, b);

    public static RgbColor Lerp(RgbColor a, RgbColor b, float t)
    {
        t = System.Math.Clamp(t, 0f, 1f);
        return new RgbColor(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    public RgbColor Darken(float factor) =>
        new(
            Clamp((byte)(R * factor)),
            Clamp((byte)(G * factor)),
            Clamp((byte)(B * factor)));

    public RgbColor Lighten(float factor) =>
        new(
            Clamp((byte)(R + (255 - R) * factor)),
            Clamp((byte)(G + (255 - G) * factor)),
            Clamp((byte)(B + (255 - B) * factor)));

    public RgbColor Offset(int delta) =>
        new(Clamp((byte)(R + delta)), Clamp((byte)(G + delta)), Clamp((byte)(B + delta)));

    private static byte Clamp(byte value) => value;
}

public static class TextureAtlasGenerator
{
    public const int TileSize = 16;
    public const int TilesPerRow = 8;
    public const int AtlasSize = TileSize * TilesPerRow;
    public const int TileCount = 64;

    public static byte[] GenerateRgbaPixels()
    {
        byte[] pixels = new byte[AtlasSize * AtlasSize * 4];
        for (int tile = 0; tile < TileCount; tile++)
        {
            int tileX = tile % TilesPerRow;
            int tileY = tile / TilesPerRow;
            FillTile(pixels, tileX, tileY, tile);
        }

        return pixels;
    }

    private static void FillTile(byte[] pixels, int tileX, int tileY, int tile)
    {
        for (int y = 0; y < TileSize; y++)
        {
            for (int x = 0; x < TileSize; x++)
            {
                RgbColor pixel = SampleTile(tile, x, y);
                int atlasX = tileX * TileSize + x;
                int atlasY = tileY * TileSize + y;
                int index = (atlasY * AtlasSize + atlasX) * 4;
                pixels[index] = pixel.R;
                pixels[index + 1] = pixel.G;
                pixels[index + 2] = pixel.B;
                pixels[index + 3] = SampleTileAlpha(tile, x, y);
            }
        }

        // Preserve hard pixel clusters but enforce seamless wrap (copy edges, no blur-average).
        if (tile is 1 or 2 or 3 or 4 or 8 or 26 or 38 or 39 or 40 or 55 or 56 or 57 or 58 or 59 or 60 or 61)
        {
            bool wrapVertical = tile is not (26 or 51 or 44 or 46);
            EnforceTilePeriodicity(pixels, tileX, tileY, wrapVertical);
        }
        else
        {
            StitchTileEdges(pixels, tileX, tileY);
        }
    }

    private static void EnforceTilePeriodicity(byte[] pixels, int tileX, int tileY, bool wrapVertical)
    {
        int baseX = tileX * TileSize;
        int baseY = tileY * TileSize;

        for (int i = 0; i < TileSize; i++)
        {
            int leftIndex = ((baseY + i) * AtlasSize + baseX) * 4;
            int rightIndex = ((baseY + i) * AtlasSize + baseX + TileSize - 1) * 4;
            for (int c = 0; c < 4; c++)
            {
                pixels[rightIndex + c] = pixels[leftIndex + c];
            }

            if (!wrapVertical)
            {
                continue;
            }

            int topIndex = (baseY * AtlasSize + baseX + i) * 4;
            int bottomIndex = ((baseY + TileSize - 1) * AtlasSize + baseX + i) * 4;
            for (int c = 0; c < 4; c++)
            {
                pixels[bottomIndex + c] = pixels[topIndex + c];
            }
        }
    }

    private static void StitchTileEdges(byte[] pixels, int tileX, int tileY)
    {
        int baseX = tileX * TileSize;
        int baseY = tileY * TileSize;

        for (int i = 0; i < TileSize; i++)
        {
            BlendEdgePixel(pixels, baseX + i, baseY, baseX + i, baseY + TileSize - 1);
            BlendEdgePixel(pixels, baseX + i, baseY + TileSize - 1, baseX + i, baseY);
        }

        for (int i = 0; i < TileSize; i++)
        {
            BlendEdgePixel(pixels, baseX, baseY + i, baseX + TileSize - 1, baseY + i);
            BlendEdgePixel(pixels, baseX + TileSize - 1, baseY + i, baseX, baseY + i);
        }
    }

    private static void BlendEdgePixel(byte[] pixels, int x0, int y0, int x1, int y1)
    {
        int i0 = (y0 * AtlasSize + x0) * 4;
        int i1 = (y1 * AtlasSize + x1) * 4;
        for (int c = 0; c < 3; c++)
        {
            byte blended = (byte)((pixels[i0 + c] + pixels[i1 + c]) / 2);
            pixels[i0 + c] = blended;
            pixels[i1 + c] = blended;
        }
    }

    private static RgbColor SampleTile(int tile, int x, int y) => tile switch
    {
        0 => MissingTile(x, y),
        1 => Stone(x, y),
        2 => Dirt(x, y),
        3 => GrassTop(x, y),
        4 => Sand(x, y),
        5 => Water(x, y),
        6 => Oil(x, y),
        7 => Wood(x, y),
        8 => Leaves(x, y),
        9 => Glass(x, y),
        10 => IronOre(x, y),
        11 => CopperOre(x, y),
        12 => CoalOre(x, y),
        13 => Gravel(x, y),
        14 => Bedrock(x, y),
        15 => Ice(x, y),
        16 => Snow(x, y),
        17 => Concrete(x, y),
        18 => Steel(x, y),
        19 => Bricks(x, y),
        20 => Glowstone(x, y),
        21 => Obsidian(x, y),
        22 => Clay(x, y),
        23 => Moss(x, y),
        24 => Sandstone(x, y),
        25 => Basalt(x, y),
        26 => GrassSide(x, y),
        27 => WoodTop(x, y),
        28 => Lava(x, y),
        29 => FlowerYellow(x, y),
        30 => FlowerBlue(x, y),
        31 => Shrub(x, y),
        32 => BirchLog(x, y),
        33 => BirchLogTop(x, y),
        34 => SpruceLog(x, y),
        35 => SpruceLogTop(x, y),
        36 => JungleLog(x, y),
        37 => JungleLogTop(x, y),
        38 => BirchLeaves(x, y),
        39 => SpruceLeaves(x, y),
        40 => JungleLeaves(x, y),
        41 => Cactus(x, y),
        42 => SnowLayer(x, y),
        43 => PodzolTop(x, y),
        44 => PodzolSide(x, y),
        45 => MyceliumTop(x, y),
        46 => MyceliumSide(x, y),
        47 => Deepslate(x, y),
        48 => PackedIce(x, y),
        49 => RedSand(x, y),
        50 => JungleGrassTop(x, y),
        51 => JungleGrassSide(x, y),
        52 => Shale(x, y),
        53 => Fern(x, y),
        54 => Apple(x, y),
        55 => Cobblestone(x, y),
        56 => Granite(x, y),
        57 => PolishedGranite(x, y),
        58 => Andesite(x, y),
        59 => PolishedAndesite(x, y),
        60 => Diorite(x, y),
        61 => PolishedDiorite(x, y),
        62 => TallGrass(x, y),
        63 => FlowerRed(x, y),
        64 => ShortGrass(x, y),
        _ => MissingTile(x, y),
    };

    private static byte SampleTileAlpha(int tile, int x, int y)
    {
        if (tile is 8 or 38 or 39 or 40)
        {
            return LeavesAlpha(x, y, tile);
        }

        if (tile == 53 || tile is 29 or 30 or 31 or 62 or 63 or 64)
        {
            return PlantAlpha(x, y, tile);
        }

        return 255;
    }

    private static RgbColor MissingTile(int x, int y) =>
        ((x + y) & 1) == 0 ? RgbColor.From(200, 40, 40) : RgbColor.From(140, 20, 20);

    private static RgbColor Stone(int x, int y)
    {
        float macro = CombinedClumpNoise(x * 0.85f, y * 0.85f, 1);
        float medium = ClumpNoise(x, y, 2, 2);
        float fine = HashF(x, y, 3);
        float mix = macro * 0.44f + medium * 0.36f + fine * 0.2f;

        RgbColor[] palette =
        [
            RgbColor.From(48, 58, 68),
            RgbColor.From(78, 88, 98),
            RgbColor.From(108, 118, 130),
            RgbColor.From(142, 152, 164),
            RgbColor.From(178, 188, 200),
            RgbColor.From(210, 218, 228),
            RgbColor.From(232, 238, 246),
        ];
        RgbColor color = QuantizePalette(mix, palette);

        float crack = TileNoise(x * 2.1f + 0.6f, y * 2.1f + 1.4f, 6);
        if (crack < 0.09f)
        {
            color = RgbColor.From(48, 52, 58);
        }
        else if (crack > 0.93f)
        {
            color = RgbColor.From(198, 202, 210);
        }

        int h = Hash(x, y, 11);
        if (h % 19 == 0)
        {
            color = RgbColor.From(68, 72, 78);
        }
        else if (h % 23 == 0)
        {
            color = RgbColor.From(202, 206, 214);
        }
        else if (h % 29 == 0)
        {
            color = RgbColor.From(102, 106, 114);
        }

        float speckle = TileNoise(x * 3.4f, y * 3.4f, 7);
        if (speckle < 0.14f)
        {
            color = RgbColor.From(72, 76, 82);
        }
        else if (speckle > 0.86f)
        {
            color = RgbColor.From(176, 180, 188);
        }

        return ApplyMicroDither(color, x, y, 10, 7);
    }

    private static RgbColor Dirt(int x, int y)
    {
        float macro = CombinedClumpNoise(x * 1.1f, y * 1.1f, 10);
        float grain = ClumpNoise(x, y, 2, 14);
        float fine = HashF(x, y, 15);
        float mix = macro * 0.46f + grain * 0.34f + fine * 0.2f;

        RgbColor[] palette =
        [
            RgbColor.From(68, 42, 22),
            RgbColor.From(98, 62, 32),
            RgbColor.From(128, 82, 42),
            RgbColor.From(158, 102, 54),
            RgbColor.From(186, 124, 66),
        ];
        RgbColor color = QuantizePalette(mix, palette);

        float clump = ClumpNoise(x, y, 3, 16);
        if (clump < 0.22f)
        {
            color = RgbColor.From(68, 42, 22);
        }
        else if (clump > 0.8f)
        {
            color = RgbColor.From(178, 118, 62);
        }

        int h = Hash(x, y, 17);
        if (h % 5 == 0)
        {
            color = RgbColor.From(78, 48, 26);
        }
        else if (h % 9 == 0)
        {
            color = RgbColor.From(168, 108, 58);
        }
        else if (h % 13 == 0)
        {
            color = RgbColor.From(88, 56, 28);
        }

        return ApplyMicroDither(color, x, y, 16, 7);
    }

    private static RgbColor GrassTop(int x, int y)
    {
        float coarse = ClumpNoise(x, y, 4, 20) * 0.5f + ClumpNoise(x, y, 3, 21) * 0.5f;
        float medium = ClumpNoise(x, y, 2, 22);
        float fine = TileNoise(x * 2.8f, y * 2.8f, 23) * 0.6f + HashF(x, y, 24) * 0.4f;
        float mix = coarse * 0.4f + medium * 0.38f + fine * 0.22f;

        // Natural mid-green palette: green channel matches the previous neon values (keeps
        // GrassTop_IsGreenerThanDirt margins intact) while red/blue are lifted to cut saturation
        // (peak chroma 0.75 -> ~0.59) so grass reads as muted turf instead of neon lime.
        RgbColor bright = RgbColor.From(150, 208, 86);
        RgbColor mid = RgbColor.From(100, 178, 74);
        RgbColor dark = RgbColor.From(64, 138, 58);
        RgbColor shadow = RgbColor.From(46, 108, 44);
        RgbColor[] palette = [shadow, dark, mid, bright];
        RgbColor color = QuantizePalette(mix, palette);

        float patch = ClumpNoise(x, y, 3, 25);
        if (patch > 0.72f)
        {
            color = bright;
        }
        else if (patch < 0.24f)
        {
            color = dark;
        }

        float microPatch = ClumpNoise(x, y, 2, 26);
        if (microPatch > 0.78f)
        {
            color = bright;
        }
        else if (microPatch < 0.2f)
        {
            color = dark;
        }

        int h = Hash(x, y, 27);
        if (h % 11 == 0)
        {
            color = bright;
        }
        else if (h % 15 == 0)
        {
            color = dark;
        }
        else if (h % 19 == 0)
        {
            color = mid;
        }

        return ApplyMicroDither(color, x, y, 29, 3);
    }

    private static RgbColor GrassSide(int x, int y)
    {
        RgbColor dirt = Dirt(x, y);
        float edgeWave = MacroNoise(x * 0.42f, 0f, 300);
        int grassDepth = 2 + (int)(edgeWave * 2.2f);
        grassDepth = System.Math.Clamp(grassDepth, 2, 4);

        // Horizontal fringe only: shallow top band, not deep green columns.
        RgbColor grassFringe = GrassFringeColor(x);

        if (y < grassDepth)
        {
            return grassFringe;
        }

        if (y == grassDepth)
        {
            return RgbColor.Lerp(grassFringe, dirt, 0.72f);
        }

        return dirt;
    }

    private static RgbColor GrassFringeColor(int x)
    {
        RgbColor center = GrassTop(x, 0);
        RgbColor left = GrassTop(WrapTile(x - 1), 0);
        RgbColor right = GrassTop(WrapTile(x + 1), 0);
        RgbColor smoothed = RgbColor.Lerp(
            RgbColor.Lerp(left, right, 0.5f),
            center,
            0.52f);
        return RgbColor.Lerp(smoothed, RgbColor.From(96, 158, 64), 0.22f);
    }

    private static RgbColor Sand(int x, int y)
    {
        float ripplePhase = y * 0.72f + TileNoise(x * 0.38f, y * 0.18f, 30) * 2.8f;
        float ripple = MathF.Sin(ripplePhase) * 0.5f + 0.5f;
        float grain = CombinedClumpNoise(x * 0.9f, y * 0.9f, 31);
        float mix = ripple * 0.42f + grain * 0.58f;

        RgbColor[] palette =
        [
            RgbColor.From(184, 164, 122),
            RgbColor.From(206, 188, 148),
            RgbColor.From(222, 206, 166),
            RgbColor.From(236, 222, 182),
            RgbColor.From(246, 234, 196),
        ];
        RgbColor color = QuantizePalette(mix, palette);

        int rippleBand = (int)MathF.Floor(ripplePhase / 1.6f) % 4;
        if (rippleBand == 0)
        {
            color = RgbColor.From(184, 164, 122);
        }
        else if (rippleBand == 2)
        {
            color = RgbColor.From(236, 222, 182);
        }

        int h = Hash(x, y, 34);
        if (h % 22 == 0)
        {
            color = RgbColor.From(176, 156, 116);
        }
        else if (h % 27 == 0)
        {
            color = RgbColor.From(242, 228, 188);
        }

        return ApplyMicroDither(color, x, y, 35, 6);
    }

    private static RgbColor Water(int x, int y)
    {
        float wave = MathF.Sin((x + y * 0.5f) * 0.9f + Noise(x * 0.3f, y * 0.3f, 40) * 4f);
        float shimmer = Noise(x * 0.25f, y * 0.25f + wave * 0.5f, 41);
        RgbColor deep = RgbColor.From(42, 98, 178);
        RgbColor shallow = RgbColor.From(72, 138, 212);
        RgbColor color = RgbColor.Lerp(deep, shallow, (wave + 1f) * 0.35f + shimmer * 0.25f);
        if (Hash(x, y, 42) % 14 == 0)
        {
            color = color.Lighten(0.22f);
        }

        return color;
    }

    private static RgbColor Oil(int x, int y)
    {
        float swirl = Noise(x * 0.2f + y * 0.1f, y * 0.25f, 50);
        float gloss = MathF.Sin((x - y) * 0.55f + swirl * 3f);
        RgbColor baseColor = RgbColor.From(34, 42, 36);
        RgbColor color = gloss > 0.35f ? baseColor.Lighten(0.22f) : baseColor;
        if (swirl > 0.72f)
        {
            color = color.Darken(0.78f);
        }

        return color.Offset((Hash(x, y, 51) % 3) - 1);
    }

    // Tile 7 is bark for log sides; tile 27 is end grain for top/bottom (BlockRegistry 27/7/27).
    private static RgbColor Wood(int x, int y)
    {
        float macro = ClumpNoise(x, y, 4, 60);
        float warp = ClumpNoise(x, y, 2, 61) * 2.4f + macro * 1.2f;
        float ringPhase = y * 0.92f + warp;
        int ringBand = (int)MathF.Floor(ringPhase / 1.35f) % 5;
        float grain = CombinedClumpNoise(x * 0.9f, y * 0.9f, 62);
        float groove = ClumpNoise(x, y, 2, 63);
        float knotField = ClumpNoise(x, y, 3, 64);

        RgbColor[] palette =
        [
            RgbColor.From(58, 38, 20),
            RgbColor.From(82, 56, 32),
            RgbColor.From(112, 82, 48),
            RgbColor.From(142, 104, 62),
            RgbColor.From(176, 132, 78),
        ];
        RgbColor color = QuantizePalette(grain, palette);

        if (ringBand is 0 or 2)
        {
            color = palette[1];
        }
        else if (ringBand == 4)
        {
            color = palette[4];
        }

        if (groove > 0.72f)
        {
            color = palette[0];
        }
        else if (groove < 0.22f)
        {
            color = palette[4];
        }

        if (knotField < 0.18f)
        {
            float knotDist = MathF.Abs(x - (7.5f + ClumpNoise(y, 0, 2, 65) * 4f));
            if (knotDist < 1.6f)
            {
                color = palette[0];
            }
        }

        int h = Hash(x, y, 66);
        if (h % 11 == 0)
        {
            color = palette[0];
        }
        else if (h % 17 == 0)
        {
            color = palette[4];
        }
        else if (h % 19 == 0)
        {
            color = RgbColor.From(96, 118, 68);
        }

        return ApplyMicroDither(color, x, y, 67, 3);
    }

    private static RgbColor WoodTop(int x, int y)
    {
        float centerX = (TextureAtlasGenerator.TileSize - 1) * 0.5f;
        float centerY = (TextureAtlasGenerator.TileSize - 1) * 0.5f;
        float dx = x - centerX;
        float dy = y - centerY;
        float radius = MathF.Sqrt(dx * dx + dy * dy);
        float warp = ClumpNoise(dx, dy, 2, 81) * 1.4f;
        int ringBand = (int)MathF.Floor((radius + warp) * 1.42f / 1.1f) % 5;
        float grain = CombinedClumpNoise(x * 0.85f, y * 0.85f, 82);
        float speckle = ClumpNoise(x, y, 2, 83);

        RgbColor[] palette =
        [
            RgbColor.From(72, 48, 26),
            RgbColor.From(96, 68, 38),
            RgbColor.From(128, 92, 54),
            RgbColor.From(158, 116, 72),
            RgbColor.From(188, 142, 90),
        ];
        RgbColor color = QuantizePalette(grain, palette);

        color = palette[System.Math.Clamp(ringBand, 0, palette.Length - 1)];

        if (speckle > 0.74f)
        {
            color = palette[4];
        }
        else if (speckle < 0.22f)
        {
            color = palette[0];
        }

        if (radius > centerX - 1.5f)
        {
            color = palette[0];
        }
        else if (radius < 2.2f)
        {
            color = palette[0];
        }

        int h = Hash(x, y, 84);
        if (h % 8 == 0)
        {
            color = palette[0];
        }
        else if (h % 13 == 0)
        {
            color = palette[4];
        }

        return ApplyMicroDither(color, x, y, 85, 3);
    }

    private static RgbColor Leaves(int x, int y) =>
        SampleLeaves(x, y, 0, [
            RgbColor.From(58, 94, 46),
            RgbColor.From(72, 112, 54),
            RgbColor.From(96, 148, 64),
            RgbColor.From(124, 184, 78),
            RgbColor.From(152, 210, 92),
            RgbColor.From(170, 222, 102),
        ]);

    private static byte LeavesAlpha(int x, int y, int tile = 8)
    {
        int seedOffset = tile switch
        {
            38 => 90,
            39 => 100,
            40 => 110,
            _ => 0,
        };

        float coarse = ClumpNoise(x, y, 3, 78 + seedOffset) * 0.5f + ClumpNoise(x, y, 4, 79 + seedOffset) * 0.5f;
        float medium = ClumpNoise(x, y, 2, 80 + seedOffset);
        float fine = HashF(x, y, 81 + seedOffset);
        float holeField = coarse * 0.42f + medium * 0.38f + fine * 0.2f;

        float microHole = ClumpNoise(x, y, 1, 82 + seedOffset);
        if (holeField < 0.24f || (holeField < 0.38f && microHole < 0.28f))
        {
            return 0;
        }

        if (holeField > 0.76f && medium > 0.58f)
        {
            return 255;
        }

        if (holeField > 0.58f)
        {
            return (byte)(220 + Hash(x, y, 83 + seedOffset) % 35);
        }

        return (byte)(148 + Hash(x, y, 84 + seedOffset) % 72);
    }

    private static byte PlantAlpha(int x, int y, int tile)
    {
        int seedOffset = tile switch
        {
            29 => 10,
            30 => 20,
            31 => 30,
            53 => 0,
            62 => 40,
            63 => 50,
            _ => 0,
        };

        float shape = TileNoise(x * 0.5f + seedOffset * 0.1f, y * 0.5f, 250 + seedOffset);
        if (shape < 0.32f)
        {
            return 0;
        }

        return (byte)(180 + Hash(WrapTile(x), WrapTile(y), 251 + seedOffset) % 60);
    }

    private static byte FernAlpha(int x, int y) => PlantAlpha(x, y, 53);

    private static RgbColor Glass(int x, int y)
    {
        float n = Noise(x * 0.4f, y * 0.4f, 80);
        RgbColor baseColor = RgbColor.From(176, 214, 232);
        RgbColor color = baseColor.Lighten(n * 0.15f);
        if (x + y < 6 || Hash(x, y, 81) % 17 == 0)
        {
            color = color.Lighten(0.2f);
        }

        return color;
    }

    private static RgbColor IronOre(int x, int y) =>
        OreOnStone(x, y, 90, RgbColor.From(242, 220, 186), RgbColor.From(198, 176, 148), 0.12f);

    private static RgbColor CopperOre(int x, int y) =>
        OreOnStone(x, y, 100, RgbColor.From(248, 138, 62), RgbColor.From(204, 98, 42), 0.11f);

    private static RgbColor CoalOre(int x, int y) =>
        OreOnStone(x, y, 110, RgbColor.From(72, 72, 78), RgbColor.From(34, 34, 40), 0.11f);

    private static RgbColor OreOnStone(
        int x,
        int y,
        int seed,
        RgbColor oreLight,
        RgbColor oreDark,
        float oreThreshold)
    {
        RgbColor stoneColor = Stone(x, y);
        float blob = Noise(x * 0.48f, y * 0.48f, seed);
        float blob2 = Noise(x * 0.92f + 2.1f, y * 0.92f, seed + 2);
        float fine = Noise(x * 1.35f, y * 1.35f, seed + 1);
        bool oreVein = blob > 1f - oreThreshold && blob2 > 0.38f;
        bool oreSpeck = fine > 0.94f && Hash(x, y, seed + 3) % 3 == 0;
        if (oreVein || oreSpeck)
        {
            float blend = oreVein
                ? System.Math.Clamp((blob - (1f - oreThreshold)) * 5.5f + blob2 * 0.35f, 0.45f, 1f)
                : 0.55f;
            RgbColor oreColor = RgbColor.Lerp(oreDark, oreLight, blob2);
            RgbColor color = RgbColor.Lerp(stoneColor, oreColor, blend);
            if (Hash(x, y, seed + 4) % 13 == 0)
            {
                color = color.Lighten(0.18f);
            }

            if (seed == 110 && fine > 0.965f && Hash(x, y, seed + 5) % 9 == 0)
            {
                color = RgbColor.Lerp(color, RgbColor.From(196, 52, 38), 0.62f);
            }
            else if (seed == 110 && fine > 0.955f && Hash(x, y, seed + 6) % 13 == 0)
            {
                color = RgbColor.Lerp(color, RgbColor.From(52, 188, 78), 0.58f);
            }

            return color;
        }

        return stoneColor;
    }

    private static RgbColor Gravel(int x, int y)
    {
        int cellX = x / 4;
        int cellY = y / 4;
        int pebble = Hash(cellX, cellY, 120) % 5;
        RgbColor[] pebbles =
        [
            RgbColor.From(128, 126, 124),
            RgbColor.From(108, 106, 104),
            RgbColor.From(148, 144, 140),
            RgbColor.From(96, 94, 92),
            RgbColor.From(136, 132, 128),
        ];
        RgbColor color = pebbles[pebble];
        int localX = x % 4;
        int localY = y % 4;
        if (localX == 0 || localY == 0 || Hash(x, y, 121) % 9 == 0)
        {
            color = color.Darken(0.82f);
        }

        return color.Offset((Hash(x, y, 122) % 5) - 2);
    }

    private static RgbColor Bedrock(int x, int y)
    {
        float crack = Noise(x * 0.8f, y * 0.8f, 130);
        RgbColor baseColor = RgbColor.From(44, 44, 48);
        RgbColor color = crack > 0.62f ? baseColor.Lighten(0.1f) : baseColor;
        if (crack < 0.18f)
        {
            color = color.Darken(0.65f);
        }

        if ((x + y * 3) % 5 == 0 && Hash(x, y, 131) % 4 == 0)
        {
            color = color.Darken(0.55f);
        }

        return color;
    }

    private static RgbColor Ice(int x, int y)
    {
        float crystal = Noise(x * 0.5f, y * 0.5f, 140);
        RgbColor baseColor = RgbColor.From(168, 220, 244);
        RgbColor color = crystal > 0.65f ? baseColor.Lighten(0.12f) : baseColor.Darken(0.95f);
        if ((x - y) % 4 == 0)
        {
            color = color.Lighten(0.08f);
        }

        return color;
    }

    private static RgbColor Snow(int x, int y)
    {
        float n = Noise(x * 0.6f, y * 0.6f, 150);
        RgbColor baseColor = RgbColor.From(240, 248, 252);
        return n > 0.55f ? baseColor : baseColor.Darken(0.96f);
    }

    private static RgbColor Concrete(int x, int y)
    {
        float n = Noise(x * 0.35f, y * 0.35f, 160);
        RgbColor baseColor = RgbColor.From(148, 156, 168);
        return n > 0.5f ? baseColor.Lighten(0.04f) : baseColor.Darken(0.94f);
    }

    private static RgbColor Steel(int x, int y)
    {
        float brush = MathF.Sin(y * 1.1f + Noise(x * 0.2f, y * 0.15f, 170) * 2f);
        RgbColor baseColor = RgbColor.From(112, 124, 140);
        RgbColor color = brush > 0f ? baseColor.Lighten(0.1f) : baseColor.Darken(0.9f);
        return color.Offset((Hash(x, y, 171) % 3) - 1);
    }

    private static RgbColor Bricks(int x, int y)
    {
        const int brickH = 4;
        const int brickW = 8;
        int row = y / brickH;
        int offset = (row & 1) * (brickW / 2);
        int bx = (x + offset) % brickW;
        int by = y % brickH;
        bool mortar = bx == 0 || by == 0;
        RgbColor mortarColor = RgbColor.From(148, 140, 132);
        if (mortar)
        {
            return mortarColor;
        }

        float n = Noise(x * 0.4f, y * 0.4f, 180);
        RgbColor brick = n > 0.5f ? RgbColor.From(148, 72, 60) : RgbColor.From(132, 64, 54);
        return brick.Offset((Hash(x, y, 181) % 5) - 2);
    }

    private static RgbColor Glowstone(int x, int y)
    {
        float glow = Noise(x * 0.45f, y * 0.45f, 190);
        RgbColor baseColor = RgbColor.From(220, 210, 140);
        RgbColor color = glow > 0.55f ? baseColor.Lighten(0.2f) : baseColor.Darken(0.92f);
        if (Hash(x, y, 191) % 10 == 0)
        {
            color = color.Lighten(0.35f);
        }

        return color;
    }

    private static RgbColor Lava(int x, int y)
    {
        float flow = TileNoise(x * 0.42f + y * 0.18f, y * 0.38f, 192);
        float crack = TileNoise(x * 1.6f, y * 1.6f, 193);
        RgbColor hot = RgbColor.From(255, 238, 72);
        RgbColor mid = RgbColor.From(255, 118, 24);
        RgbColor dark = RgbColor.From(212, 42, 8);
        RgbColor color = flow > 0.58f ? RgbColor.Lerp(mid, hot, (flow - 0.58f) / 0.42f)
            : flow < 0.28f ? RgbColor.Lerp(dark, mid, flow / 0.28f)
            : mid;
        if (crack < 0.14f)
        {
            color = hot;
        }

        return ApplyMicroDither(color, x, y, 194, 3);
    }

    private static RgbColor Obsidian(int x, int y)
    {
        float shine = Noise(x * 0.3f, y * 0.3f, 200);
        RgbColor baseColor = RgbColor.From(28, 16, 40);
        RgbColor color = shine > 0.7f ? baseColor.Lighten(0.15f) : baseColor;
        return color.Offset((Hash(x, y, 201) % 3) - 1);
    }

    private static RgbColor Clay(int x, int y)
    {
        float n = Noise(x * 0.4f, y * 0.4f, 210);
        RgbColor baseColor = RgbColor.From(160, 166, 176);
        return n > 0.5f ? baseColor.Lighten(0.05f) : baseColor.Darken(0.93f);
    }

    private static RgbColor Moss(int x, int y)
    {
        RgbColor dirt = Dirt(x, y);
        float moss = Noise(x * 0.5f, y * 0.5f, 220);
        RgbColor mossGreen = RgbColor.From(72, 116, 56);
        if (moss > 0.45f || Hash(x, y, 221) % 6 == 0)
        {
            return RgbColor.Lerp(dirt, mossGreen, System.Math.Clamp(moss * 1.2f, 0.4f, 0.85f));
        }

        return dirt;
    }

    private static RgbColor Sandstone(int x, int y)
    {
        int band = y / 3;
        float n = Noise(x * 0.35f, y * 0.2f, 230);
        RgbColor light = RgbColor.From(220, 200, 148);
        RgbColor dark = RgbColor.From(196, 172, 116);
        RgbColor color = (band & 1) == 0 ? RgbColor.Lerp(dark, light, n) : RgbColor.Lerp(light, dark, n);
        return color.Offset((Hash(x, y, 231) % 4) - 2);
    }

    private static RgbColor Basalt(int x, int y)
    {
        float column = Noise(x * 0.25f, y * 0.6f, 240);
        RgbColor baseColor = RgbColor.From(56, 60, 68);
        RgbColor color = column > 0.55f ? baseColor.Lighten(0.08f) : baseColor.Darken(0.88f);
        if (Hash(x, y, 241) % 11 == 0)
        {
            color = color.Darken(0.75f);
        }

        return color;
    }

    private static RgbColor BirchLog(int x, int y)
    {
        RgbColor oak = Wood(x, y);
        return RgbColor.Lerp(oak, RgbColor.From(228, 220, 196), 0.62f);
    }

    private static RgbColor BirchLogTop(int x, int y)
    {
        RgbColor oak = WoodTop(x, y);
        return RgbColor.Lerp(oak, RgbColor.From(220, 210, 184), 0.55f);
    }

    private static RgbColor SpruceLog(int x, int y)
    {
        RgbColor oak = Wood(x, y);
        return RgbColor.Lerp(oak, RgbColor.From(58, 42, 28), 0.58f);
    }

    private static RgbColor SpruceLogTop(int x, int y)
    {
        RgbColor oak = WoodTop(x, y);
        return RgbColor.Lerp(oak, RgbColor.From(72, 52, 34), 0.5f);
    }

    private static RgbColor JungleLog(int x, int y)
    {
        float stripe = MathF.Sin(y * 0.7f + TileNoise(x * 0.2f, y * 0.1f, 260) * 2f);
        RgbColor light = RgbColor.From(118, 88, 52);
        RgbColor dark = RgbColor.From(72, 52, 30);
        RgbColor color = stripe > 0f ? light : dark;
        return ApplyMicroDither(color, x, y, 261, 3);
    }

    private static RgbColor JungleLogTop(int x, int y) =>
        ApplyMicroDither(RgbColor.Lerp(WoodTop(x, y), RgbColor.From(96, 68, 38), 0.45f), x, y, 262, 3);

    // Leaves palettes below only ever raise the red (and, where untested, blue) channel relative
    // to the original neon values. Green/blue stay fixed for birch/spruce so the distinct-palette
    // test deltas (birchGreen/spruceGreen/spruceBlue/birchBlue) are unaffected while saturation drops.
    private static RgbColor BirchLeaves(int x, int y) =>
        SampleLeaves(x, y, 90, [
            RgbColor.From(148, 188, 78),
            RgbColor.From(166, 208, 88),
            RgbColor.From(184, 224, 98),
            RgbColor.From(200, 236, 108),
            RgbColor.From(216, 244, 118),
            RgbColor.From(232, 250, 128),
        ]);

    private static RgbColor SpruceLeaves(int x, int y) =>
        SampleLeaves(x, y, 100, [
            RgbColor.From(46, 78, 72),
            RgbColor.From(54, 96, 88),
            RgbColor.From(62, 114, 102),
            RgbColor.From(70, 132, 118),
            RgbColor.From(78, 150, 132),
            RgbColor.From(86, 166, 146),
        ]);

    private static RgbColor JungleLeaves(int x, int y) =>
        SampleLeaves(x, y, 110, [
            RgbColor.From(34, 62, 42),
            RgbColor.From(40, 78, 48),
            RgbColor.From(46, 94, 52),
            RgbColor.From(52, 110, 56),
            RgbColor.From(58, 126, 60),
            RgbColor.From(64, 142, 64),
        ]);

    private static RgbColor SampleLeaves(int x, int y, int seed, RgbColor[] palette)
    {
        float coarse = ClumpNoise(x, y, 4, 70 + seed) * 0.5f + ClumpNoise(x, y, 3, 71 + seed) * 0.5f;
        float medium = ClumpNoise(x, y, 2, 72 + seed);
        float fine = HashF(x, y, 73 + seed) * 0.55f + ClumpNoise(x, y, 1, 74 + seed) * 0.45f;
        float mix = coarse * 0.4f + medium * 0.38f + fine * 0.22f;
        RgbColor color = QuantizePalette(mix, palette);

        float patch = ClumpNoise(x, y, 3, 75 + seed);
        if (patch > 0.74f)
        {
            color = palette[^1];
        }
        else if (patch < 0.22f)
        {
            color = palette[0];
        }

        float microPatch = ClumpNoise(x, y, 2, 76 + seed);
        if (microPatch > 0.78f)
        {
            color = palette[^1];
        }
        else if (microPatch < 0.18f)
        {
            color = palette[1];
        }

        int h = Hash(x, y, 77 + seed);
        if (h % 9 == 0)
        {
            color = palette[^1];
        }
        else if (h % 13 == 0)
        {
            color = palette[0];
        }
        else if (h % 17 == 0)
        {
            color = palette[palette.Length / 2];
        }
        else if (h % 21 == 0)
        {
            color = palette[^2];
        }

        return ApplyMicroDither(color, x, y, 78 + seed, 4);
    }

    private static RgbColor Cactus(int x, int y)
    {
        float stripe = MathF.Sin(x * 0.8f + TileNoise(x * 0.15f, y * 0.2f, 270) * 1.5f);
        RgbColor light = RgbColor.From(88, 148, 62);
        RgbColor dark = RgbColor.From(58, 108, 42);
        RgbColor color = stripe > 0.1f ? light : dark;
        if (Hash(x, y, 271) % 9 == 0)
        {
            color = color.Darken(0.7f);
        }

        return ApplyMicroDither(color, x, y, 272, 2);
    }

    private static RgbColor SnowLayer(int x, int y)
    {
        float n = Noise(x * 0.8f, y * 0.8f, 280);
        RgbColor baseColor = RgbColor.From(248, 252, 255);
        return n > 0.5f ? baseColor : baseColor.Darken(0.97f);
    }

    private static RgbColor PodzolTop(int x, int y)
    {
        RgbColor dirt = Dirt(x, y);
        float podzol = Noise(x * 0.45f, y * 0.45f, 290);
        RgbColor brown = RgbColor.From(92, 58, 32);
        return RgbColor.Lerp(dirt, brown, System.Math.Clamp(podzol * 1.1f, 0.55f, 0.9f));
    }

    private static RgbColor PodzolSide(int x, int y)
    {
        RgbColor dirt = Dirt(x, y);
        if (y < 2)
        {
            return PodzolTop(x, y);
        }

        return dirt;
    }

    private static RgbColor MyceliumTop(int x, int y)
    {
        RgbColor dirt = Dirt(x, y);
        float patch = Noise(x * 0.5f, y * 0.5f, 300);
        RgbColor purple = RgbColor.From(148, 112, 168);
        if (patch > 0.42f || Hash(x, y, 301) % 5 == 0)
        {
            return RgbColor.Lerp(dirt, purple, System.Math.Clamp(patch * 1.2f, 0.35f, 0.8f));
        }

        return dirt;
    }

    private static RgbColor MyceliumSide(int x, int y)
    {
        RgbColor dirt = Dirt(x, y);
        if (y < 2)
        {
            return MyceliumTop(x, y);
        }

        return dirt;
    }

    private static RgbColor Deepslate(int x, int y)
    {
        RgbColor stone = Stone(x, y);
        return RgbColor.Lerp(stone, RgbColor.From(48, 52, 62), 0.55f).Darken(0.88f);
    }

    private static RgbColor PackedIce(int x, int y)
    {
        RgbColor ice = Ice(x, y);
        float crystal = TileNoise(x * 0.7f, y * 0.7f, 310);
        return crystal > 0.55f ? ice.Lighten(0.06f) : ice.Darken(0.94f);
    }

    private static RgbColor RedSand(int x, int y)
    {
        RgbColor sand = Sand(x, y);
        return RgbColor.Lerp(sand, RgbColor.From(198, 96, 58), 0.62f);
    }

    private static RgbColor JungleGrassTop(int x, int y)
    {
        RgbColor grass = GrassTop(x, y);
        return RgbColor.Lerp(grass, RgbColor.From(62, 198, 44), 0.32f);
    }

    private static RgbColor JungleGrassSide(int x, int y)
    {
        RgbColor dirt = Dirt(x, y);
        float edgeWave = MacroNoise(x * 0.42f, 0f, 320);
        int grassDepth = System.Math.Clamp(1 + (int)(edgeWave * 1.5f), 1, 3);
        RgbColor fringe = JungleGrassTop(x, 0);
        if (y < grassDepth)
        {
            return fringe;
        }

        if (y == grassDepth)
        {
            return RgbColor.Lerp(fringe, dirt, 0.68f);
        }

        return dirt;
    }

    private static RgbColor Shale(int x, int y)
    {
        int band = y / 2;
        float n = Noise(x * 0.4f, y * 0.3f, 330);
        RgbColor light = RgbColor.From(108, 116, 128);
        RgbColor dark = RgbColor.From(78, 84, 94);
        return (band & 1) == 0 ? RgbColor.Lerp(dark, light, n) : RgbColor.Lerp(light, dark, n);
    }

    private static RgbColor Fern(int x, int y)
    {
        float frond = TileNoise(x * 0.4f, y * 0.6f, 340);
        RgbColor bright = RgbColor.From(72, 148, 52);
        RgbColor dark = RgbColor.From(42, 98, 34);
        if (frond < 0.35f)
        {
            return RgbColor.From(0, 0, 0);
        }

        return RgbColor.Lerp(dark, bright, frond);
    }

    private static RgbColor Apple(int x, int y)
    {
        float dx = x - 7.5f;
        float dy = y - 8.5f;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist > 5.5f)
        {
            return RgbColor.From(0, 0, 0);
        }

        RgbColor red = RgbColor.From(196, 48, 42);
        if (y <= 3 && MathF.Abs(x - 8f) < 1.2f)
        {
            return RgbColor.From(92, 58, 28);
        }

        return dist > 4.5f ? red.Darken(0.85f) : red;
    }

    private static RgbColor TallGrass(int x, int y)
    {
        float blade = TileNoise(x * 0.55f, y * 0.35f, 380);
        if (blade < 0.22f)
        {
            return RgbColor.From(0, 0, 0);
        }

        // Softer, less saturated blade tones (raised R/B) so tall grass reads as natural turf
        // rather than glowing neon lime, matching the muted GrassTop palette.
        RgbColor stem = RgbColor.From(68, 102, 52);
        RgbColor mid = RgbColor.From(108, 156, 78);
        RgbColor tip = RgbColor.From(154, 198, 102);
        float t = y / 15f;
        RgbColor color = t < 0.45f ? RgbColor.Lerp(stem, mid, blade) : RgbColor.Lerp(mid, tip, blade);
        if (Hash(x, y, 381) % 7 == 0)
        {
            color = tip;
        }

        return ApplyMicroDither(color, x, y, 62, 2);
    }

    private static RgbColor ShortGrass(int x, int y)
    {
        float blade = TileNoise(x * 0.7f, y * 0.5f, 420);
        if (blade < 0.35f)
        {
            return RgbColor.From(0, 0, 0);
        }

        RgbColor dark = RgbColor.From(66, 110, 52);
        RgbColor bright = RgbColor.From(140, 188, 86);
        RgbColor color = RgbColor.Lerp(dark, bright, blade);
        if (Hash(x, y, 421) % 5 == 0)
        {
            color = bright;
        }

        return ApplyMicroDither(color, x, y, 64, 1);
    }

    private static RgbColor FlowerRed(int x, int y) => Flower(x, y, RgbColor.From(196, 48, 42), 390);

    private static RgbColor FlowerYellow(int x, int y) => Flower(x, y, RgbColor.From(228, 198, 42), 400);

    private static RgbColor FlowerBlue(int x, int y) => Flower(x, y, RgbColor.From(58, 108, 212), 410);

    private static RgbColor Flower(int x, int y, RgbColor petal, int seed)
    {
        float dx = x - 7.5f;
        float dy = y - 5.5f;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (y > 10)
        {
            float stem = TileNoise(x * 0.4f, y * 0.5f, seed);
            return stem > 0.42f ? RgbColor.From(58, 118, 38) : RgbColor.From(0, 0, 0);
        }

        if (dist > 4.2f)
        {
            return RgbColor.From(0, 0, 0);
        }

        return dist > 3.2f ? petal.Darken(0.82f) : petal;
    }

    private static RgbColor Shrub(int x, int y) =>
        SampleLeaves(x, y, 420, [
            RgbColor.From(42, 88, 34),
            RgbColor.From(58, 108, 42),
            RgbColor.From(72, 128, 48),
            RgbColor.From(88, 148, 54),
            RgbColor.From(102, 168, 60),
            RgbColor.From(118, 186, 66),
        ]);

    private static RgbColor Cobblestone(int x, int y)
    {
        int cellX = x / 3;
        int cellY = y / 3;
        int variant = Hash(cellX, cellY, 350) % 6;
        RgbColor[] blocks =
        [
            RgbColor.From(98, 102, 108),
            RgbColor.From(118, 122, 128),
            RgbColor.From(88, 92, 98),
            RgbColor.From(132, 136, 142),
            RgbColor.From(108, 112, 118),
            RgbColor.From(78, 82, 88),
        ];
        RgbColor color = blocks[variant];
        int localX = x % 3;
        int localY = y % 3;
        if (localX == 0 || localY == 0 || Hash(x, y, 351) % 7 == 0)
        {
            color = color.Darken(0.78f);
        }

        float crack = TileNoise(x * 2.4f, y * 2.4f, 352);
        if (crack < 0.12f)
        {
            color = RgbColor.From(58, 62, 68);
        }

        return ApplyMicroDither(color, x, y, 353, 5);
    }

    private static RgbColor Granite(int x, int y)
    {
        float macro = CombinedClumpNoise(x * 0.9f, y * 0.9f, 360);
        RgbColor[] palette =
        [
            RgbColor.From(168, 118, 108),
            RgbColor.From(188, 132, 122),
            RgbColor.From(148, 102, 94),
            RgbColor.From(204, 148, 136),
            RgbColor.From(128, 88, 82),
        ];
        RgbColor color = QuantizePalette(macro, palette);
        int speckle = Hash(x, y, 361) % 17;
        if (speckle == 0)
        {
            color = RgbColor.From(42, 42, 48);
        }
        else if (speckle == 1)
        {
            color = RgbColor.From(228, 224, 218);
        }

        return ApplyMicroDither(color, x, y, 362, 4);
    }

    private static RgbColor PolishedGranite(int x, int y)
    {
        float n = Noise(x * 0.4f, y * 0.4f, 370);
        RgbColor baseColor = RgbColor.From(178, 128, 118);
        RgbColor color = n > 0.5f ? baseColor.Lighten(0.05f) : baseColor.Darken(0.93f);
        return ApplyMicroDither(color, x, y, 371, 3);
    }

    private static RgbColor Andesite(int x, int y)
    {
        float macro = CombinedClumpNoise(x * 0.85f, y * 0.85f, 380);
        RgbColor[] palette =
        [
            RgbColor.From(118, 122, 128),
            RgbColor.From(138, 142, 148),
            RgbColor.From(98, 102, 108),
            RgbColor.From(158, 162, 168),
            RgbColor.From(88, 92, 98),
        ];
        RgbColor color = QuantizePalette(macro, palette);
        if (Hash(x, y, 381) % 13 == 0)
        {
            color = RgbColor.From(62, 66, 72);
        }

        return ApplyMicroDither(color, x, y, 382, 4);
    }

    private static RgbColor PolishedAndesite(int x, int y)
    {
        float n = Noise(x * 0.38f, y * 0.38f, 390);
        RgbColor baseColor = RgbColor.From(132, 136, 142);
        RgbColor color = n > 0.5f ? baseColor.Lighten(0.04f) : baseColor.Darken(0.94f);
        return ApplyMicroDither(color, x, y, 391, 3);
    }

    private static RgbColor Diorite(int x, int y)
    {
        float macro = CombinedClumpNoise(x * 0.88f, y * 0.88f, 400);
        RgbColor[] palette =
        [
            RgbColor.From(208, 208, 212),
            RgbColor.From(228, 228, 232),
            RgbColor.From(188, 188, 194),
            RgbColor.From(242, 242, 246),
            RgbColor.From(168, 168, 174),
        ];
        RgbColor color = QuantizePalette(macro, palette);
        if (Hash(x, y, 401) % 11 == 0)
        {
            color = RgbColor.From(48, 52, 58);
        }

        return ApplyMicroDither(color, x, y, 402, 4);
    }

    private static RgbColor PolishedDiorite(int x, int y)
    {
        float n = Noise(x * 0.36f, y * 0.36f, 410);
        RgbColor baseColor = RgbColor.From(218, 218, 224);
        RgbColor color = n > 0.5f ? baseColor.Lighten(0.04f) : baseColor.Darken(0.95f);
        return ApplyMicroDither(color, x, y, 411, 3);
    }

    private static int Hash(int x, int y, int seed) =>
        unchecked(x * 374761393 + y * 668265263 + seed * 982451653) & 0x7fffffff;

    private static int WrapTile(int value) =>
        ((value % TileSize) + TileSize) % TileSize;

    private static float HashF(int x, int y, int seed) => Hash(WrapTile(x), WrapTile(y), seed) / (float)0x7fffffff;

    private static float TileNoise(float x, float y, int seed)
    {
        int period = TileSize;
        float px = x * period / TileSize;
        float py = y * period / TileSize;
        int ix = (int)MathF.Floor(px);
        int iy = (int)MathF.Floor(py);
        float fx = px - ix;
        float fy = py - iy;
        float ux = fx * fx * (3f - 2f * fx);
        float uy = fy * fy * (3f - 2f * fy);

        int x0 = Mod(ix, period);
        int y0 = Mod(iy, period);
        int x1 = Mod(ix + 1, period);
        int y1 = Mod(iy + 1, period);

        float a = HashF(x0, y0, seed);
        float b = HashF(x1, y0, seed);
        float c = HashF(x0, y1, seed);
        float d = HashF(x1, y1, seed);
        return Lerp(Lerp(a, b, ux), Lerp(c, d, ux), uy);
    }

    private static float MacroNoise(float x, float y, int seed) =>
        TileNoise(x * 0.2f, y * 0.2f, seed) * 0.4f
        + TileNoise(x * 0.44f, y * 0.44f, seed + 17) * 0.3f
        + TileNoise(x * 0.82f, y * 0.82f, seed + 31) * 0.18f
        + TileNoise(x * 1.35f, y * 1.35f, seed + 47) * 0.12f;

    private static float FineNoise(float x, float y, int seed) =>
        TileNoise(x * 1.1f, y * 1.1f, seed) * 0.36f
        + TileNoise(x * 2.25f, y * 2.25f, seed + 11) * 0.3f
        + TileNoise(x * 4.6f, y * 4.6f, seed + 23) * 0.22f
        + TileNoise(x * 7.2f, y * 7.2f, seed + 37) * 0.12f;

    private static (float X, float Y) DomainWarp(float x, float y, int seed, float strength)
    {
        float wx = TileNoise(x * 0.34f, y * 0.34f, seed) * 2f - 1f;
        float wy = TileNoise(x * 0.34f + 4.7f, y * 0.34f + 2.9f, seed + 1) * 2f - 1f;
        return (x + wx * strength, y + wy * strength);
    }

    private static RgbColor ApplyMicroDither(RgbColor color, int x, int y, int seed, int range) =>
        color.Offset((Hash(WrapTile(x), WrapTile(y), seed) % (range * 2 + 1)) - range);

    private static RgbColor QuantizePalette(float value, RgbColor[] palette)
    {
        value = System.Math.Clamp(value, 0f, 1f);
        int index = System.Math.Min((int)(value * palette.Length), palette.Length - 1);
        return palette[index];
    }

    private static float ClumpNoise(float x, float y, int cellSize, int seed)
    {
        int cx = (int)MathF.Floor(x / cellSize);
        int cy = (int)MathF.Floor(y / cellSize);
        return TileNoise(cx * 1.73f + seed * 0.07f, cy * 1.73f, seed);
    }

    private static float CombinedClumpNoise(float x, float y, int seed) =>
        ClumpNoise(x, y, 4, seed) * 0.48f
        + ClumpNoise(x, y, 2, seed + 11) * 0.32f
        + HashF((int)MathF.Floor(x), (int)MathF.Floor(y), seed + 23) * 0.2f;

    private static (float X, float Y) RotCoords(float x, float y, int seed)
    {
        float angle = HashF(0, 0, seed) * MathF.PI * 0.5f;
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        return (x * cos - y * sin, x * sin + y * cos);
    }

    private static int Mod(int value, int period) => ((value % period) + period) % period;

    private static float Noise(float x, float y, int seed) => TileNoise(x, y, seed);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
