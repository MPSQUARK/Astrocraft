namespace AstroCraft.Core.Math;

public readonly record struct ChunkPosition(int X, int Z)
{
    public static ChunkPosition FromBlock(int blockX, int blockZ) =>
        new(FloorDiv(blockX, GameConstants.ChunkSizeX), FloorDiv(blockZ, GameConstants.ChunkSizeZ));

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        if (remainder != 0 && ((remainder < 0) ^ (divisor < 0)))
        {
            quotient--;
        }

        return quotient;
    }
}
