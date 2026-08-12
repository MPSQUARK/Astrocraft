using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Math;

namespace AstroCraft.Client.Rendering;

public readonly struct FrustumPlanes
{
    public readonly Vector4 Left;
    public readonly Vector4 Right;
    public readonly Vector4 Bottom;
    public readonly Vector4 Top;
    public readonly Vector4 Near;
    public readonly Vector4 Far;

    public FrustumPlanes(Vector4 left, Vector4 right, Vector4 bottom, Vector4 top, Vector4 near, Vector4 far)
    {
        Left = left;
        Right = right;
        Bottom = bottom;
        Top = top;
        Near = near;
        Far = far;
    }
}

public static class FrustumCuller
{
    public static void GetChunkBounds(ChunkPosition position, out Vector3 min, out Vector3 max)
    {
        float minX = position.X * GameConstants.ChunkSizeX;
        float minZ = position.Z * GameConstants.ChunkSizeZ;
        min = new Vector3(minX, 0f, minZ);
        max = new Vector3(
            minX + GameConstants.ChunkSizeX,
            GameConstants.ChunkSizeY,
            minZ + GameConstants.ChunkSizeZ);
    }

    public static FrustumPlanes ExtractFrustum(Matrix4x4 modelViewProjection) =>
        new(
            NormalizePlane(modelViewProjection.M14 + modelViewProjection.M11, modelViewProjection.M24 + modelViewProjection.M21, modelViewProjection.M34 + modelViewProjection.M31, modelViewProjection.M44 + modelViewProjection.M41),
            NormalizePlane(modelViewProjection.M14 - modelViewProjection.M11, modelViewProjection.M24 - modelViewProjection.M21, modelViewProjection.M34 - modelViewProjection.M31, modelViewProjection.M44 - modelViewProjection.M41),
            NormalizePlane(modelViewProjection.M14 + modelViewProjection.M12, modelViewProjection.M24 + modelViewProjection.M22, modelViewProjection.M34 + modelViewProjection.M32, modelViewProjection.M44 + modelViewProjection.M42),
            NormalizePlane(modelViewProjection.M14 - modelViewProjection.M12, modelViewProjection.M24 - modelViewProjection.M22, modelViewProjection.M34 - modelViewProjection.M32, modelViewProjection.M44 - modelViewProjection.M42),
            NormalizePlane(modelViewProjection.M14 + modelViewProjection.M13, modelViewProjection.M24 + modelViewProjection.M23, modelViewProjection.M34 + modelViewProjection.M33, modelViewProjection.M44 + modelViewProjection.M43),
            NormalizePlane(modelViewProjection.M14 - modelViewProjection.M13, modelViewProjection.M24 - modelViewProjection.M23, modelViewProjection.M34 - modelViewProjection.M33, modelViewProjection.M44 - modelViewProjection.M43));

    public static bool IsChunkVisible(ChunkPosition position, Matrix4x4 modelViewProjection)
    {
        GetChunkBounds(position, out Vector3 min, out Vector3 max);
        return IsAabbVisible(min, max, ExtractFrustum(modelViewProjection));
    }

    public static bool IsChunkVisible(ChunkPosition position, FrustumPlanes planes)
    {
        GetChunkBounds(position, out Vector3 min, out Vector3 max);
        return IsAabbVisible(min, max, planes);
    }

    public static bool IsAabbVisible(Vector3 min, Vector3 max, Matrix4x4 modelViewProjection) =>
        IsAabbVisible(min, max, ExtractFrustum(modelViewProjection));

    public static bool IsAabbVisible(Vector3 min, Vector3 max, FrustumPlanes planes)
    {
        if (!IsPositiveVertexInside(min, max, planes.Left))
        {
            return false;
        }

        if (!IsPositiveVertexInside(min, max, planes.Right))
        {
            return false;
        }

        if (!IsPositiveVertexInside(min, max, planes.Bottom))
        {
            return false;
        }

        if (!IsPositiveVertexInside(min, max, planes.Top))
        {
            return false;
        }

        if (!IsPositiveVertexInside(min, max, planes.Near))
        {
            return false;
        }

        return IsPositiveVertexInside(min, max, planes.Far);
    }

    private static bool IsPositiveVertexInside(Vector3 min, Vector3 max, Vector4 plane)
    {
        Vector3 positive = new(
            plane.X >= 0 ? max.X : min.X,
            plane.Y >= 0 ? max.Y : min.Y,
            plane.Z >= 0 ? max.Z : min.Z);

        return plane.X * positive.X + plane.Y * positive.Y + plane.Z * positive.Z + plane.W >= 0f;
    }

    private static Vector4 NormalizePlane(float x, float y, float z, float w) =>
        Vector4.Normalize(new Vector4(x, y, z, w));
}
