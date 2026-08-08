namespace AstroCraft.Core.Rendering;

internal readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor From(byte r, byte g, byte b) => new(r, g, b);

    public RgbColor Darken(float factor) =>
        new(
            Clamp((byte)(R * factor)),
            Clamp((byte)(G * factor)),
            Clamp((byte)(B * factor)));

    public RgbColor Vary(int x, int y)
    {
        int delta = ((x * 13) + (y * 7)) % 20 - 10;
        return new RgbColor(Clamp((byte)(R + delta)), Clamp((byte)(G + delta)), Clamp((byte)(B + delta)));
    }

    private static byte Clamp(byte value) => value;
}

public static class TextureAtlasGenerator
{
    public const int TileSize = 16;
    public const int TilesPerRow = 8;
    public const int AtlasSize = TileSize * TilesPerRow;

    public static byte[] GenerateRgbaPixels()
    {
        byte[] pixels = new byte[AtlasSize * AtlasSize * 4];
        for (int tile = 0; tile < 32; tile++)
        {
            RgbColor baseColor = TileColor(tile);
            int tileX = tile % TilesPerRow;
            int tileY = tile / TilesPerRow;
            FillTile(pixels, tileX, tileY, baseColor);
        }

        return pixels;
    }

    private static void FillTile(byte[] pixels, int tileX, int tileY, RgbColor baseColor)
    {
        for (int y = 0; y < TileSize; y++)
        {
            for (int x = 0; x < TileSize; x++)
            {
                bool border = x == 0 || y == 0 || x == TileSize - 1 || y == TileSize - 1;
                RgbColor pixel = border ? baseColor.Darken(0.75f) : baseColor.Vary(x, y);
                int atlasX = tileX * TileSize + x;
                int atlasY = tileY * TileSize + y;
                int index = (atlasY * AtlasSize + atlasX) * 4;
                pixels[index] = pixel.R;
                pixels[index + 1] = pixel.G;
                pixels[index + 2] = pixel.B;
                pixels[index + 3] = 255;
            }
        }
    }

    private static RgbColor TileColor(int tile) => tile switch
    {
        1 => RgbColor.From(90, 98, 110),
        2 => RgbColor.From(74, 58, 42),
        3 => RgbColor.From(58, 120, 72),
        4 => RgbColor.From(176, 160, 108),
        5 => RgbColor.From(36, 92, 168),
        6 => RgbColor.From(48, 40, 28),
        7 => RgbColor.From(92, 68, 44),
        8 => RgbColor.From(44, 110, 64),
        9 => RgbColor.From(170, 210, 230),
        10 => RgbColor.From(120, 108, 100),
        11 => RgbColor.From(96, 132, 148),
        12 => RgbColor.From(48, 48, 52),
        13 => RgbColor.From(130, 128, 126),
        14 => RgbColor.From(24, 24, 28),
        15 => RgbColor.From(168, 220, 240),
        16 => RgbColor.From(228, 236, 244),
        17 => RgbColor.From(148, 156, 168),
        18 => RgbColor.From(112, 124, 140),
        19 => RgbColor.From(132, 72, 60),
        20 => RgbColor.From(220, 210, 140),
        21 => RgbColor.From(36, 20, 48),
        22 => RgbColor.From(118, 126, 138),
        23 => RgbColor.From(52, 96, 64),
        24 => RgbColor.From(168, 150, 108),
        25 => RgbColor.From(56, 60, 68),
        _ => RgbColor.From(200, 40, 40),
    };
}
