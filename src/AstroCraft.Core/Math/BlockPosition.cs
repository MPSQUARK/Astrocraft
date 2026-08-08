namespace AstroCraft.Core.Math;

public readonly record struct BlockPosition(int X, int Y, int Z)
{
    public static BlockPosition FromWorld(float x, float y, float z) =>
        new((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z));

    public BlockPosition Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    public float DistanceSquaredTo(BlockPosition other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        float dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}
