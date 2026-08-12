using AstroCraft.Core.Math;

namespace AstroCraft.Core.Networking;

/// <summary>
/// Minecraft-style chunk interest: tight ring around the player plus a forward cone.
/// </summary>
public static class ChunkViewSelector
{
    public static void CollectInterestChunks(
        int centerBlockX,
        int centerBlockZ,
        float yawRadians,
        ICollection<ChunkPosition> destination)
    {
        ChunkPosition center = ChunkPosition.FromBlock(centerBlockX, centerBlockZ);
        float forwardX = MathF.Sin(yawRadians);
        float forwardZ = MathF.Cos(yawRadians);

        int forwardRadius = GameConstants.DefaultViewDistanceChunks;
        int nearRadius = GameConstants.NearChunkRadius;
        int rearRadius = GameConstants.RearChunkRadius;

        for (int dz = -forwardRadius; dz <= forwardRadius; dz++)
        {
            for (int dx = -forwardRadius; dx <= forwardRadius; dx++)
            {
                int chebyshev = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz));
                if (chebyshev <= nearRadius)
                {
                    destination.Add(new ChunkPosition(center.X + dx, center.Z + dz));
                    continue;
                }

                if (chebyshev > forwardRadius)
                {
                    continue;
                }

                float allowedRadius = ResolveDirectionalRadius(dx, dz, forwardX, forwardZ, forwardRadius, rearRadius);
                if (chebyshev <= allowedRadius)
                {
                    destination.Add(new ChunkPosition(center.X + dx, center.Z + dz));
                }
            }
        }
    }

    public static int EstimateMaxInterestChunks()
    {
        int forwardRadius = GameConstants.DefaultViewDistanceChunks;
        return (forwardRadius * 2 + 1) * (forwardRadius * 2 + 1);
    }

    private static float ResolveDirectionalRadius(
        int dx,
        int dz,
        float forwardX,
        float forwardZ,
        int forwardRadius,
        int rearRadius)
    {
        if (dx == 0 && dz == 0)
        {
            return forwardRadius;
        }

        float offsetX = dx + System.Math.Sign(dx) * 0.5f;
        float offsetZ = dz + System.Math.Sign(dz) * 0.5f;
        float length = MathF.Sqrt(offsetX * offsetX + offsetZ * offsetZ);
        if (length < 0.001f)
        {
            return forwardRadius;
        }

        float dot = (offsetX / length) * forwardX + (offsetZ / length) * forwardZ;
        float forwardBias = (dot + 1f) * 0.5f;
        return rearRadius + (forwardRadius - rearRadius) * forwardBias;
    }
}
