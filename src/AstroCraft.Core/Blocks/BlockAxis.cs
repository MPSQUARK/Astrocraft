using System.Numerics;

namespace AstroCraft.Core.Blocks;

public enum BlockAxis : byte
{
    Y = 0,
    X = 1,
    Z = 2,
}

public enum BlockPlacementOrientation
{
    None = 0,
    AxisAligned = 1,
}

public static class BlockAxisExtensions
{
    public static BlockAxis FromPlacementFace(Vector3 faceNormal)
    {
        float absX = MathF.Abs(faceNormal.X);
        float absY = MathF.Abs(faceNormal.Y);
        float absZ = MathF.Abs(faceNormal.Z);

        if (absY >= absX && absY >= absZ)
        {
            return BlockAxis.Y;
        }

        if (absX >= absZ)
        {
            return BlockAxis.X;
        }

        return BlockAxis.Z;
    }

    public static BlockAxis Next(BlockAxis axis) => axis switch
    {
        BlockAxis.Y => BlockAxis.X,
        BlockAxis.X => BlockAxis.Z,
        _ => BlockAxis.Y,
    };
}
