using AstroCraft.Core.Blocks;
using AstroCraft.Core.Players;
using AstroCraft.Core.World;

namespace AstroCraft.Core.World.Generation;

public interface IWorldGenerator
{
    void GenerateChunk(GameWorld world, Chunk chunk);
}

internal enum Biome : byte
{
    Plains,
    Forest,
    Desert,
    Mountains,
    Ocean,
    Jungle,
    Arctic,
}

public sealed class ProceduralWorldGenerator(int seed) : IWorldGenerator
{
    private const int SurfaceProtectionDepth = 6;
    private const int DirtDepth = 4;
    private const int BeachHeightBand = 6;
    private const int BeachSandDepth = 5;
    private const int MaxOilElevation = 14;
    private const int SpawnProtectionRadius = 48;
    private const int SpawnProtectionRadiusSq = SpawnProtectionRadius * SpawnProtectionRadius;
    private const int SpawnHeightFloorRadiusSq = 8 * 8;
    private const float MinScenicViewScore = 14f;

    private (int X, int Z, float YawRadians)? _cachedScenicAnchor;
    private (int X, int Z)? _cachedScenicPitCenter;

    public void GenerateChunk(GameWorld world, Chunk chunk)
    {
        int baseX = chunk.Position.X * GameConstants.ChunkSizeX;
        int baseZ = chunk.Position.Z * GameConstants.ChunkSizeZ;

        Span<int> surfaceHeights = stackalloc int[GameConstants.ChunkSizeX * GameConstants.ChunkSizeZ];
        int surfaceIndex = 0;

        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;
                int surfaceHeight = ComputeSurfaceHeight(worldX, worldZ);
                surfaceHeights[surfaceIndex++] = surfaceHeight;

                for (int y = 0; y < GameConstants.WorldHeight; y++)
                {
                    BlockId block = ResolveColumnBlock(y, surfaceHeight, worldX, worldZ);
                    chunk.SetBlock(localX, y, localZ, block);
                }
            }
        }

        PlaceOilPools(chunk, baseX, baseZ, surfaceHeights);
        CarveCaves(chunk, baseX, baseZ, surfaceHeights);
        FillUndergroundLakes(chunk, baseX, baseZ, surfaceHeights);
        OpenCaveEntrances(chunk, baseX, baseZ, surfaceHeights);
        PlaceLavaPools(chunk, baseX, baseZ, surfaceHeights);
        PlaceOreVeins(chunk, baseX, baseZ, surfaceHeights);
        PlaceStoneVariantPatches(chunk, baseX, baseZ, surfaceHeights);
        DecorateExposedStoneWithOre(chunk, baseX, baseZ, surfaceHeights);
        PlaceScenicShowcaseCave(chunk, baseX, baseZ, surfaceHeights);
        PlaceTrees(chunk, baseX, baseZ, surfaceHeights);
        PlaceCacti(chunk, baseX, baseZ, surfaceHeights);
        PlaceSnowLayers(chunk, baseX, baseZ, surfaceHeights);
        PlaceFlowerPatches(chunk, baseX, baseZ, surfaceHeights);
        PlaceJungleFerns(chunk, baseX, baseZ, surfaceHeights);
        ClearScenicShowcaseVegetation(chunk, baseX, baseZ);
    }

    internal Biome GetBiome(int worldX, int worldZ)
    {
        float region = FractalNoise2D(worldX * 0.002f, worldZ * 0.002f, seed + 5000);
        float temperature = FractalNoise2D(worldX * 0.0022f, worldZ * 0.0022f, seed + 5050);
        float moisture = FractalNoise2D(worldX * 0.0025f, worldZ * 0.0025f, seed + 5100);
        float elevation = FractalNoise2D(worldX * 0.0018f, worldZ * 0.0018f, seed + 5200);

        int spawnDistSq = worldX * worldX + worldZ * worldZ;
        if (spawnDistSq < SpawnProtectionRadiusSq)
        {
            float forestBlend = FractalNoise2D(worldX * 0.006f, worldZ * 0.006f, seed + 5150);
            return forestBlend > 0.38f ? Biome.Forest : Biome.Plains;
        }

        if (elevation < 0.14f)
        {
            return Biome.Ocean;
        }

        if (temperature < 0.40f && moisture < 0.58f && elevation > 0.22f)
        {
            return Biome.Arctic;
        }

        if (temperature > 0.50f && moisture > 0.52f && elevation > 0.18f)
        {
            return Biome.Jungle;
        }

        float variant = FractalNoise2D(worldX * 0.0035f, worldZ * 0.0035f, seed + 5160);
        if (variant > 0.58f && temperature < 0.36f)
        {
            return Biome.Arctic;
        }

        if (variant > 0.58f && temperature > 0.48f && moisture > 0.46f)
        {
            return Biome.Jungle;
        }

        Biome biome = (int)(region * 3.999f) switch
        {
            0 => Biome.Desert,
            1 => Biome.Plains,
            2 => Biome.Forest,
            _ => Biome.Mountains,
        };

        if (biome is Biome.Plains or Biome.Mountains)
        {
            float forestBlend = FractalNoise2D(worldX * 0.006f, worldZ * 0.006f, seed + 5150);
            if (forestBlend > 0.52f)
            {
                return Biome.Forest;
            }
        }

        if (biome == Biome.Plains && temperature > 0.54f && moisture < 0.42f)
        {
            return Biome.Desert;
        }

        return biome;
    }

    public (int X, int Z, float YawRadians) FindScenicSpawn(int originX = 0, int originZ = 0, int searchRadius = 64) =>
        FindScenicSpawn(world: null, originX, originZ, searchRadius);

    public (int X, int Z, float YawRadians) FindScenicSpawn(GameWorld? world, int originX = 0, int originZ = 0, int searchRadius = 64)
    {
        (int forestX, int forestZ) = FindNearestForest(originX, originZ, searchRadius * 2);
        (int X, int Z, float Score, float YawRadians) best = (forestX, forestZ, float.MinValue, 0f);
        List<(int X, int Z, float Score, float YawRadians)> ranked = new();

        for (int dx = -searchRadius; dx <= searchRadius; dx += 2)
        {
            for (int dz = -searchRadius; dz <= searchRadius; dz += 2)
            {
                int worldX = forestX + dx;
                int worldZ = forestZ + dz;
                if (!TryScoreScenicCandidate(worldX, worldZ, out float score, out float yaw))
                {
                    continue;
                }

                ranked.Add((worldX, worldZ, score, yaw));
                if (score > best.Score)
                {
                    best = (worldX, worldZ, score, yaw);
                }
            }
        }

        if (world is not null && ranked.Count > 0)
        {
            ranked.Sort((a, b) => b.Score.CompareTo(a.Score));
            int validateCount = System.Math.Min(16, ranked.Count);
            for (int i = 0; i < validateCount; i++)
            {
                (int worldX, int worldZ, float score, float yaw) = ranked[i];
                world.EnsureChunksAround(worldX, worldZ, 3);
                if (TryValidateScenicSpawn(world, worldX, worldZ, yaw))
                {
                    return (worldX, worldZ, yaw);
                }
            }
        }

        if (best.Score == float.MinValue)
        {
            if (world is not null)
            {
                (int X, int Z, float YawRadians)? guaranteed = TryFindGuaranteedScenicSpawn(world, forestX, forestZ, searchRadius * 2);
                if (guaranteed.HasValue)
                {
                    return guaranteed.Value;
                }
            }

            (int fallbackX, int fallbackZ) = FindFallbackScenicSpawn(forestX, forestZ, searchRadius);
            float fallbackYaw = FindScenicYaw(fallbackX, fallbackZ, ComputeSurfaceHeight(fallbackX, fallbackZ));
            if (world is not null && TryValidateScenicSpawn(world, fallbackX, fallbackZ, fallbackYaw))
            {
                return (fallbackX, fallbackZ, fallbackYaw);
            }

            if (world is not null)
            {
                (int X, int Z, float YawRadians)? relaxed = TryFindRelaxedScenicSpawn(world);
                if (relaxed.HasValue)
                {
                    return relaxed.Value;
                }
            }

            return (fallbackX, fallbackZ, fallbackYaw);
        }

        if (world is not null)
        {
            world.EnsureChunksAround(best.X, best.Z, 3);
            if (TryValidateScenicSpawn(world, best.X, best.Z, best.YawRadians))
            {
                return (best.X, best.Z, best.YawRadians);
            }

            (int X, int Z, float YawRadians)? guaranteed = TryFindGuaranteedScenicSpawn(world, originX, originZ, 384);
            if (guaranteed.HasValue)
            {
                return guaranteed.Value;
            }

            (int X, int Z, float YawRadians)? relaxed = TryFindRelaxedScenicSpawn(world);
            if (relaxed.HasValue)
            {
                return relaxed.Value;
            }

            foreach ((int worldX, int worldZ, float _, float yaw) in ranked.OrderByDescending(candidate => candidate.Score))
            {
                world.EnsureChunksAround(worldX, worldZ, 3);
                if (HasScenicSurface(world, worldX, worldZ) && CountTreesNear(world, worldX, worldZ, 16) > 0)
                {
                    return (worldX, worldZ, yaw);
                }
            }
        }

        return (best.X, best.Z, best.YawRadians);
    }

    private (int X, int Z, float YawRadians)? TryFindGuaranteedScenicSpawn(GameWorld world, int originX, int originZ, int searchRadius)
    {
        List<(int X, int Z, float Score, float YawRadians)> candidates = new();
        for (int worldX = 0; worldX < 512; worldX += 8)
        {
            for (int worldZ = 0; worldZ < 512; worldZ += 8)
            {
                if (GetBiome(worldX, worldZ) is not (Biome.Forest or Biome.Plains))
                {
                    continue;
                }

                if (!TryScoreScenicCandidate(worldX, worldZ, out float score, out float yaw))
                {
                    continue;
                }

                int distSq = (worldX - originX) * (worldX - originX) + (worldZ - originZ) * (worldZ - originZ);
                if (distSq > searchRadius * searchRadius)
                {
                    continue;
                }

                candidates.Add((worldX, worldZ, score, yaw));
            }
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        int validateCount = System.Math.Min(24, candidates.Count);
        for (int i = 0; i < validateCount; i++)
        {
            (int worldX, int worldZ, float _, float yaw) = candidates[i];
            world.EnsureChunksAround(worldX, worldZ, 3);
            if (TryValidateScenicSpawn(world, worldX, worldZ, yaw))
            {
                return (worldX, worldZ, yaw);
            }
        }

        return null;
    }

    private (int X, int Z, float YawRadians)? TryFindRelaxedScenicSpawn(GameWorld world)
    {
        (int X, int Z, float YawRadians)? best = null;
        float bestScore = float.MinValue;

        for (int worldX = 0; worldX < 512; worldX += 2)
        {
            for (int worldZ = 0; worldZ < 512; worldZ += 2)
            {
                if (GetBiome(worldX, worldZ) is not (Biome.Forest or Biome.Plains))
                {
                    continue;
                }

                world.EnsureChunksAround(worldX, worldZ, 4);
                if (!HasScenicSurface(world, worldX, worldZ))
                {
                    continue;
                }

                if (CountTreesNear(world, worldX, worldZ, 16) <= 0)
                {
                    continue;
                }

                int surfaceHeight = GetSurfaceHeight(world, worldX, worldZ);
                float yaw = FindScenicYaw(worldX, worldZ, surfaceHeight);
                if (HasBarrenForeground(world, worldX, worldZ, yaw))
                {
                    continue;
                }

                float score = ScoreViewDirection(worldX, worldZ, surfaceHeight, yaw);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = (worldX, worldZ, yaw);
                }
            }
        }

        return best;
    }

    private (int X, int Z) FindNearestForest(int originX, int originZ, int searchRadius)
    {
        for (int radius = 0; radius <= searchRadius; radius += 4)
        {
            for (int dx = -radius; dx <= radius; dx += 4)
            {
                for (int dz = -radius; dz <= radius; dz += 4)
                {
                    if (System.Math.Abs(dx) != radius && System.Math.Abs(dz) != radius)
                    {
                        continue;
                    }

                    int worldX = originX + dx;
                    int worldZ = originZ + dz;
                    if (GetBiome(worldX, worldZ) == Biome.Forest)
                    {
                        int surfaceHeight = ComputeSurfaceHeight(worldX, worldZ);
                        if (surfaceHeight > GameConstants.SeaLevel
                            && !IsBeachColumn(surfaceHeight)
                            && ResolveSurfaceBlock(surfaceHeight, worldX, worldZ) is BlockId.Grass or BlockId.Moss)
                        {
                            return (worldX, worldZ);
                        }
                    }
                }
            }
        }

        return FindFallbackScenicSpawn(originX, originZ, searchRadius);
    }

    private static bool HasScenicSurface(GameWorld world, int worldX, int worldZ) =>
        TryGetSurfaceBlock(world, worldX, worldZ, out BlockId surface)
        && surface is BlockId.Grass or BlockId.Moss or BlockId.Dirt or BlockId.JungleGrass;

    private static bool TryGetSurfaceBlock(GameWorld world, int worldX, int worldZ, out BlockId surface)
    {
        for (int y = GameConstants.WorldHeight - 2; y >= 0; y--)
        {
            if (!world.IsSolid(worldX, y, worldZ))
            {
                continue;
            }

            surface = world.GetBlock(worldX, y, worldZ);
            return true;
        }

        surface = BlockId.Air;
        return false;
    }

    private bool TryValidateScenicSpawn(GameWorld world, int worldX, int worldZ, float yawRadians)
    {
        if (!HasScenicSurface(world, worldX, worldZ))
        {
            return false;
        }

        if (CountTreesNear(world, worldX, worldZ, 24) <= 0)
        {
            return false;
        }

        if (!TryGetSurfaceBlock(world, worldX, worldZ, out _))
        {
            return false;
        }

        int surfaceHeight = GetSurfaceHeight(world, worldX, worldZ);
        if (ScoreViewDirection(worldX, worldZ, surfaceHeight, yawRadians) < MinScenicViewScore)
        {
            return false;
        }

        return !HasBarrenForeground(world, worldX, worldZ, yawRadians);
    }

    private static int GetSurfaceHeight(GameWorld world, int worldX, int worldZ)
    {
        for (int y = GameConstants.WorldHeight - 2; y >= 0; y--)
        {
            if (world.IsSolid(worldX, y, worldZ))
            {
                return y + 1;
            }
        }

        return GameConstants.SeaLevel;
    }

    /// <summary>Scans generated terrain for the first air column above solid ground.</summary>
    public int GetActualSurfaceY(GameWorld world, int worldX, int worldZ) =>
        GetSurfaceHeight(world, worldX, worldZ);

    private static bool HasBarrenForeground(GameWorld world, int worldX, int worldZ, float yawRadians)
    {
        int barrenSamples = 0;
        for (float distance = 3f; distance <= 10f; distance += 2f)
        {
            int sampleX = worldX + (int)MathF.Round(MathF.Sin(yawRadians) * distance);
            int sampleZ = worldZ + (int)MathF.Round(MathF.Cos(yawRadians) * distance);
            if (!TryGetSurfaceBlock(world, sampleX, sampleZ, out BlockId surface))
            {
                continue;
            }

            if (surface is BlockId.Stone or BlockId.Gravel && distance <= 8f)
            {
                barrenSamples++;
            }
            else if (surface == BlockId.Sand)
            {
                barrenSamples += 2;
            }
        }

        return barrenSamples >= 2;
    }

    private static int CountTreesNear(GameWorld world, int centerX, int centerZ, int radius)
    {
        int treeBlocks = 0;
        for (int worldX = centerX - radius; worldX <= centerX + radius; worldX++)
        {
            for (int worldZ = centerZ - radius; worldZ <= centerZ + radius; worldZ++)
            {
                for (int y = GameConstants.SeaLevel; y < GameConstants.WorldHeight; y++)
                {
                    BlockId block = world.GetBlock(worldX, y, worldZ);
                    if (block is BlockId.Wood or BlockId.Leaves
                        or BlockId.BirchLog or BlockId.SpruceLog or BlockId.JungleLog
                        or BlockId.BirchLeaves or BlockId.SpruceLeaves or BlockId.JungleLeaves)
                    {
                        treeBlocks++;
                    }
                }
            }
        }

        return treeBlocks;
    }

    private (int X, int Z) FindFallbackScenicSpawn(int originX, int originZ, int searchRadius)
    {
        foreach (Biome preferredBiome in new[] { Biome.Forest, Biome.Plains })
        {
            for (int dx = -searchRadius; dx <= searchRadius; dx += 2)
            {
                for (int dz = -searchRadius; dz <= searchRadius; dz += 2)
                {
                    int worldX = originX + dx;
                    int worldZ = originZ + dz;
                    if (GetBiome(worldX, worldZ) != preferredBiome)
                    {
                        continue;
                    }

                    int surfaceHeight = ComputeSurfaceHeight(worldX, worldZ);
                    if (surfaceHeight <= GameConstants.SeaLevel || IsBeachColumn(surfaceHeight))
                    {
                        continue;
                    }

                if (ResolveSurfaceBlock(surfaceHeight, worldX, worldZ) is not (BlockId.Grass or BlockId.Moss))
                {
                    continue;
                }

                    int treeRoll = HashSeed(worldX ^ (worldZ * 668265263), seed + 6000) & 0xFF;
                    int treeThreshold = preferredBiome == Biome.Forest ? 175 : 10;
                    if (treeRoll >= treeThreshold)
                    {
                        continue;
                    }

                    if (EstimateTreeDensity(worldX, worldZ) < 6)
                    {
                        continue;
                    }

                    return (worldX, worldZ);
                }
            }
        }

        return FindFirstForestOrPlainsGrass(originX, originZ, searchRadius * 2);
    }

    private (int X, int Z) FindFirstForestOrPlainsGrass(int originX, int originZ, int searchRadius)
    {
        for (int radius = 0; radius <= searchRadius; radius += 2)
        {
            for (int dx = -radius; dx <= radius; dx += 2)
            {
                for (int dz = -radius; dz <= radius; dz += 2)
                {
                    int worldX = originX + dx;
                    int worldZ = originZ + dz;
                    Biome biome = GetBiome(worldX, worldZ);
                    if (biome is not (Biome.Forest or Biome.Plains))
                    {
                        continue;
                    }

                    int surfaceHeight = ComputeSurfaceHeight(worldX, worldZ);
                    if (surfaceHeight <= GameConstants.SeaLevel || IsBeachColumn(surfaceHeight))
                    {
                        continue;
                    }

                    if (ResolveSurfaceBlock(surfaceHeight, worldX, worldZ) is BlockId.Grass or BlockId.Moss)
                    {
                        return (worldX, worldZ);
                    }
                }
            }
        }

        return (originX, originZ);
    }

    private bool TryScoreScenicCandidate(int worldX, int worldZ, out float score, out float scenicYawRadians)
    {
        score = 0f;
        scenicYawRadians = 0f;

        int surfaceHeight = ComputeSurfaceHeight(worldX, worldZ);
        if (surfaceHeight <= GameConstants.SeaLevel)
        {
            return false;
        }

        if (IsBeachColumn(surfaceHeight))
        {
            return false;
        }

        if (surfaceHeight <= GameConstants.SeaLevel + BeachHeightBand + 1)
        {
            return false;
        }

        Biome biome = GetBiome(worldX, worldZ);
        if (biome is not (Biome.Forest or Biome.Plains))
        {
            return false;
        }

        score += biome switch
        {
            Biome.Forest => 12f,
            Biome.Plains => 8f,
            _ => 0f,
        };

        BlockId surfaceBlock = ResolveSurfaceBlock(surfaceHeight, worldX, worldZ);
        if (surfaceBlock is not (BlockId.Grass or BlockId.Moss))
        {
            return false;
        }

        score += 10f;

        if (IsPitEdge(worldX, worldZ, surfaceHeight))
        {
            return false;
        }

        if (IsNearSandOrBeach(worldX, worldZ))
        {
            return false;
        }

        int treeRoll = HashSeed(worldX ^ (worldZ * 668265263), seed + 6000) & 0xFF;
        int treeThreshold = biome == Biome.Forest ? 175 : 10;
        if (treeRoll < treeThreshold)
        {
            score += 6f;
        }

        float flatness = MeasureTerrainFlatness(worldX, worldZ, surfaceHeight);
        score += flatness * 4f;

        int treeDensity = EstimateTreeDensity(worldX, worldZ);
        score += treeDensity * 0.35f;

        if (treeDensity < 6)
        {
            return false;
        }

        if (score < 12f)
        {
            return false;
        }

        scenicYawRadians = FindScenicYaw(worldX, worldZ, surfaceHeight);
        if (ScoreViewDirection(worldX, worldZ, surfaceHeight, scenicYawRadians) < MinScenicViewScore)
        {
            return false;
        }

        return true;
    }

    private bool IsNearSandOrBeach(int worldX, int worldZ, int radius = 4)
    {
        for (int offsetX = -radius; offsetX <= radius; offsetX += 2)
        {
            for (int offsetZ = -radius; offsetZ <= radius; offsetZ += 2)
            {
                int sampleX = worldX + offsetX;
                int sampleZ = worldZ + offsetZ;
                int sampleHeight = ComputeSurfaceHeight(sampleX, sampleZ);
                if (IsBeachColumn(sampleHeight))
                {
                    return true;
                }

                if (ResolveSurfaceBlock(sampleHeight, sampleX, sampleZ) == BlockId.Sand)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsPitEdge(int worldX, int worldZ, int surfaceHeight)
    {
        ReadOnlySpan<int> offsets = stackalloc int[] { 4, -4, 8, -8 };
        int lowerNeighbors = 0;
        int higherNeighbors = 0;

        foreach (int offsetX in offsets)
        {
            foreach (int offsetZ in offsets)
            {
                int neighborHeight = ComputeSurfaceHeight(worldX + offsetX, worldZ + offsetZ);
                int delta = neighborHeight - surfaceHeight;
                if (delta <= -3)
                {
                    lowerNeighbors++;
                }
                else if (delta >= 3)
                {
                    higherNeighbors++;
                }
            }
        }

        return lowerNeighbors >= 2 || higherNeighbors >= 3;
    }

    private int MeasureTerrainFlatness(int worldX, int worldZ, int surfaceHeight)
    {
        ReadOnlySpan<int> offsets = stackalloc int[] { 4, -4, 8, -8 };
        int maxDelta = 0;

        foreach (int offsetX in offsets)
        {
            foreach (int offsetZ in offsets)
            {
                if (offsetX == 0 && offsetZ == 0)
                {
                    continue;
                }

                int neighborHeight = ComputeSurfaceHeight(worldX + offsetX, worldZ + offsetZ);
                maxDelta = System.Math.Max(maxDelta, System.Math.Abs(neighborHeight - surfaceHeight));
            }
        }

        return System.Math.Max(0, 4 - maxDelta);
    }

    private int EstimateTreeDensity(int worldX, int worldZ)
    {
        int treeRolls = 0;
        ReadOnlySpan<int> offsets = stackalloc int[] { 0, 6, -6, 12, -12, 18, -18 };

        foreach (int offsetX in offsets)
        {
            foreach (int offsetZ in offsets)
            {
                int sampleX = worldX + offsetX;
                int sampleZ = worldZ + offsetZ;
                Biome biome = GetBiome(sampleX, sampleZ);
                if (biome is Biome.Desert)
                {
                    continue;
                }

                int treeRoll = HashSeed(sampleX ^ (sampleZ * 668265263), seed + 6000) & 0xFF;
                int threshold = biome == Biome.Forest ? 175 : 10;
                if (treeRoll < threshold)
                {
                    treeRolls++;
                }
            }
        }

        return treeRolls;
    }

    private float FindScenicYaw(int worldX, int worldZ, int surfaceHeight)
    {
        float bestYaw = 0f;
        float bestScore = float.MinValue;

        for (int direction = 0; direction < 32; direction++)
        {
            float yaw = direction * (System.MathF.PI * 2f / 32f);
            float score = ScoreViewDirection(worldX, worldZ, surfaceHeight, yaw);
            if (score > bestScore)
            {
                bestScore = score;
                bestYaw = yaw;
            }
        }

        if (bestYaw > System.MathF.PI)
        {
            bestYaw -= System.MathF.PI * 2f;
        }

        return bestYaw;
    }

    private float ScoreViewDirection(int worldX, int worldZ, int surfaceHeight, float yawRadians)
    {
        float score = 0f;
        for (float distance = 8f; distance <= 48f; distance += 4f)
        {
            int sampleX = worldX + (int)MathF.Round(MathF.Sin(yawRadians) * distance);
            int sampleZ = worldZ + (int)MathF.Round(MathF.Cos(yawRadians) * distance);
            Biome biome = GetBiome(sampleX, sampleZ);
            int sampleHeight = ComputeSurfaceHeight(sampleX, sampleZ);
            BlockId surfaceBlock = ResolveSurfaceBlock(sampleHeight, sampleX, sampleZ);
            float weight = distance switch
            {
                <= 12f => 0.35f,
                <= 24f => 1.25f,
                _ => 1.75f,
            };

            score += biome switch
            {
                Biome.Forest => 3f * weight,
                Biome.Plains => 2f * weight,
                Biome.Jungle => 2.5f * weight,
                Biome.Mountains => 0.5f * weight,
                Biome.Ocean => -4f * weight,
                Biome.Arctic => -1f * weight,
                Biome.Desert => -3f * weight,
                _ => 0f,
            };

            score += surfaceBlock switch
            {
                BlockId.Grass => 3f * weight,
                BlockId.Moss => 2f * weight,
                BlockId.Water => 1.5f * weight,
                BlockId.Sand => -5f * weight,
                BlockId.Stone => -6f * weight,
                BlockId.Gravel => -4f * weight,
                _ => 0f,
            };

            if (IsBeachColumn(sampleHeight))
            {
                score -= 4f * weight;
            }

            int heightDelta = sampleHeight - surfaceHeight;
            if (heightDelta <= -3)
            {
                score -= (distance <= 16f ? 8f : 4f) * weight;
            }
            else if (heightDelta >= 4 && distance <= 16f)
            {
                score -= 3f * weight;
            }

            int treeRoll = HashSeed(sampleX ^ (sampleZ * 668265263), seed + 6000) & 0xFF;
            if (treeRoll < (biome == Biome.Forest ? 175 : 10))
            {
                score += 2f * weight;
            }
        }

        return score;
    }

    internal float GetBiomeInfluence(int worldX, int worldZ, Biome target)
    {
        int matches = 0;
        int samples = 0;
        ReadOnlySpan<int> offsets = stackalloc int[] { 0, 6, -6, 12, -12 };

        foreach (int offsetX in offsets)
        {
            foreach (int offsetZ in offsets)
            {
                if (GetBiome(worldX + offsetX, worldZ + offsetZ) == target)
                {
                    matches++;
                }

                samples++;
            }
        }

        return matches / (float)samples;
    }

    internal int ComputeSurfaceHeight(int worldX, int worldZ)
    {
        float continental = FractalNoise2D(worldX * 0.004f, worldZ * 0.004f, seed);
        float hills = FractalNoise2D(worldX * 0.018f, worldZ * 0.018f, seed + 1000);
        float detail = FractalNoise2D(worldX * 0.06f, worldZ * 0.06f, seed + 2000);
        float combined = continental * 0.5f + hills * 0.38f + detail * 0.12f;
        int height = GameConstants.SeaLevel + (int)(combined * 20f) - 5;

        Biome biome = GetBiome(worldX, worldZ);
        if (biome == Biome.Ocean)
        {
            height -= 6 + (int)(detail * 3f);
        }
        else
        {
            height += biome switch
            {
                Biome.Mountains => 12 + (int)(detail * 10f),
                Biome.Jungle => 1 + (int)(detail * 2f),
                Biome.Arctic => (int)(detail * 2f),
                Biome.Desert => -1,
                _ => 0,
            };
        }

        int spawnDistSq = worldX * worldX + worldZ * worldZ;
        if (spawnDistSq < SpawnHeightFloorRadiusSq)
        {
            height = System.Math.Max(height, GameConstants.SeaLevel + 4);
        }

        if (spawnDistSq >= SpawnProtectionRadiusSq && biome is not Biome.Ocean)
        {
            float lakeNoise = FractalNoise2D(worldX * 0.014f, worldZ * 0.014f, seed + 5500);
            if (lakeNoise > 0.78f)
            {
                height -= (int)((lakeNoise - 0.78f) * 22f);
            }
        }

        return System.Math.Clamp(height, 4, GameConstants.WorldHeight - 4);
    }

    private BlockId ResolveColumnBlock(int y, int surfaceHeight, int worldX, int worldZ)
    {
        if (y == 0)
        {
            return BlockId.Bedrock;
        }

        if (y < surfaceHeight - DirtDepth)
        {
            return ResolveUndergroundBlock(y, surfaceHeight, worldX, worldZ);
        }

        if (y < surfaceHeight - 1)
        {
            if (IsBeachColumn(surfaceHeight) && y >= surfaceHeight - BeachSandDepth)
            {
                return BlockId.Sand;
            }

            Biome biome = GetBiome(worldX, worldZ);
            if (biome == Biome.Desert)
            {
                if (y >= surfaceHeight - DirtDepth)
                {
                    float redSand = FractalNoise2D(worldX * 0.08f, worldZ * 0.08f, seed + 3120);
                    return redSand > 0.55f ? BlockId.RedSand : BlockId.Sand;
                }

                if (y >= surfaceHeight - DirtDepth - 2)
                {
                    return BlockId.Sandstone;
                }
            }

            if (biome == Biome.Arctic && y >= surfaceHeight - DirtDepth)
            {
                return BlockId.Dirt;
            }

            if (biome == Biome.Ocean && y >= surfaceHeight - DirtDepth)
            {
                return BlockId.Gravel;
            }

            if (y < surfaceHeight - 8 && FractalNoise3D(worldX * 0.07f, y * 0.07f, worldZ * 0.07f, seed + 3250) > 0.72f)
            {
                return BlockId.Deepslate;
            }

            return BlockId.Dirt;
        }

        if (y == surfaceHeight - 1)
        {
            return ResolveSurfaceBlock(surfaceHeight, worldX, worldZ);
        }

        if (y < GameConstants.SeaLevel)
        {
            Biome biome = GetBiome(worldX, worldZ);
            if (biome == Biome.Ocean || surfaceHeight < GameConstants.SeaLevel)
            {
                return BlockId.Water;
            }
        }

        return BlockId.Air;
    }

    private static bool IsBeachColumn(int surfaceHeight) =>
        surfaceHeight <= GameConstants.SeaLevel + BeachHeightBand;

    private BlockId ResolveSurfaceBlock(int surfaceHeight, int worldX, int worldZ)
    {
        int spawnDistSq = worldX * worldX + worldZ * worldZ;
        if (spawnDistSq < SpawnProtectionRadiusSq)
        {
            Biome spawnBiome = GetBiome(worldX, worldZ);
            if (spawnBiome is Biome.Plains or Biome.Forest)
            {
                return BlockId.Grass;
            }

            if (spawnBiome == Biome.Desert)
            {
                return BlockId.Sand;
            }
        }

        Biome biome = GetBiome(worldX, worldZ);
        if (IsBeachColumn(surfaceHeight)
            && biome is Biome.Ocean or Biome.Plains or Biome.Forest)
        {
            return BlockId.Sand;
        }

        if (biome is Biome.Arctic or Biome.Jungle or Biome.Desert)
        {
            return biome switch
            {
                Biome.Desert => FractalNoise2D(worldX * 0.09f, worldZ * 0.09f, seed + 3130) > 0.58f
                    ? BlockId.RedSand
                    : BlockId.Sand,
                Biome.Jungle => ResolveJungleSurfaceBlock(worldX, worldZ),
                Biome.Arctic => ResolveArcticSurfaceBlock(surfaceHeight, worldX, worldZ),
                _ => BlockId.Grass,
            };
        }

        float patch = FractalNoise2D(worldX * 0.11f, worldZ * 0.11f, seed + 3100);
        float desertBlend = GetBiomeInfluence(worldX, worldZ, Biome.Desert);
        float forestBlend = GetBiomeInfluence(worldX, worldZ, Biome.Forest);
        float hillsBlend = GetBiomeInfluence(worldX, worldZ, Biome.Mountains);

        if (desertBlend > 0.22f && patch > 0.58f - desertBlend * 0.25f)
        {
            return BlockId.Sand;
        }

        if (hillsBlend > 0.28f && patch > 0.62f - hillsBlend * 0.18f)
        {
            return patch > 0.78f ? BlockId.Stone : BlockId.Gravel;
        }

        if (forestBlend > 0.24f && patch > 0.7f)
        {
            return BlockId.Moss;
        }

        return biome switch
        {
            Biome.Ocean => surfaceHeight < GameConstants.SeaLevel ? BlockId.Gravel : BlockId.Sand,
            Biome.Desert => FractalNoise2D(worldX * 0.09f, worldZ * 0.09f, seed + 3130) > 0.68f
                ? BlockId.RedSand
                : BlockId.Sand,
            Biome.Mountains => ResolveMountainsSurfaceBlock(surfaceHeight, worldX, worldZ),
            Biome.Jungle => ResolveJungleSurfaceBlock(worldX, worldZ),
            Biome.Arctic => ResolveArcticSurfaceBlock(surfaceHeight, worldX, worldZ),
            Biome.Forest => FractalNoise2D(worldX * 0.09f, worldZ * 0.09f, seed + 3150) > 0.75f
                ? BlockId.Moss
                : BlockId.Grass,
            _ => patch > 0.82f ? BlockId.Gravel : BlockId.Grass,
        };
    }

    private BlockId ResolveJungleSurfaceBlock(int worldX, int worldZ)
    {
        float patch = FractalNoise2D(worldX * 0.09f, worldZ * 0.09f, seed + 3160);
        if (patch > 0.88f)
        {
            return BlockId.Mycelium;
        }

        if (patch > 0.74f)
        {
            return BlockId.Podzol;
        }

        if (patch > 0.58f)
        {
            return BlockId.Moss;
        }

        return BlockId.JungleGrass;
    }

    private BlockId ResolveArcticSurfaceBlock(int surfaceHeight, int worldX, int worldZ)
    {
        float patch = FractalNoise2D(worldX * 0.09f, worldZ * 0.09f, seed + 3170);
        if (surfaceHeight <= GameConstants.SeaLevel + BeachHeightBand)
        {
            return patch > 0.35f ? BlockId.PackedIce : BlockId.Ice;
        }

        if (surfaceHeight <= GameConstants.SeaLevel + 2)
        {
            return patch > 0.45f ? BlockId.PackedIce : BlockId.Ice;
        }

        if (patch > 0.72f)
        {
            return BlockId.Ice;
        }

        return BlockId.Snow;
    }

    private BlockId ResolveMountainsSurfaceBlock(int surfaceHeight, int worldX, int worldZ)
    {
        if (surfaceHeight >= GameConstants.SeaLevel + 14)
        {
            float snowCap = FractalNoise2D(worldX * 0.07f, worldZ * 0.07f, seed + 3180);
            if (snowCap > 0.42f)
            {
                return BlockId.Snow;
            }
        }

        float patch = FractalNoise2D(worldX * 0.09f, worldZ * 0.09f, seed + 3100);
        if (patch > 0.45f)
        {
            return BlockId.Stone;
        }

        if (patch > 0.28f)
        {
            return BlockId.Gravel;
        }

        return ResolveHillsSurfaceBlock(worldX, worldZ);
    }

    private BlockId ResolveHillsSurfaceBlock(int worldX, int worldZ)
    {
        float patch = FractalNoise2D(worldX * 0.09f, worldZ * 0.09f, seed + 3100);
        if (patch > 0.72f)
        {
            return BlockId.Stone;
        }

        if (patch > 0.52f)
        {
            return BlockId.Gravel;
        }

        return BlockId.Grass;
    }

    private BlockId ResolveUndergroundBlock(int y, int surfaceHeight, int worldX, int worldZ)
    {
        if (y < surfaceHeight - 12 && FractalNoise3D(worldX * 0.08f, y * 0.08f, worldZ * 0.08f, seed + 3250) > 0.65f)
        {
            return BlockId.Deepslate;
        }

        float gravelNoise = FractalNoise3D(worldX * 0.09f, y * 0.09f, worldZ * 0.09f, seed + 3200);
        if (gravelNoise > 0.68f)
        {
            return BlockId.Gravel;
        }

        return BlockId.Stone;
    }

    private void PlaceOilPools(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        int surfaceIndex = 0;
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int surfaceHeight = surfaceHeights[surfaceIndex++];
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;
                int maxOilY = System.Math.Min(MaxOilElevation, surfaceHeight - 3);

                for (int y = 4; y <= maxOilY; y++)
                {
                    BlockId current = chunk.GetBlock(localX, y, localZ);
                    if (current is not (BlockId.Stone or BlockId.Gravel))
                    {
                        continue;
                    }

                    float pool = FractalNoise3D(worldX * 0.08f, y * 0.08f, worldZ * 0.08f, seed + 3400);
                    if (pool > 0.72f)
                    {
                        chunk.SetBlock(localX, y, localZ, BlockId.Oil);
                    }
                }
            }
        }
    }

    private void PlaceLavaPools(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        int surfaceIndex = 0;
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int surfaceHeight = surfaceHeights[surfaceIndex++];
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;
                int maxLavaY = System.Math.Min(24, surfaceHeight - SurfaceProtectionDepth);
                if (maxLavaY <= 4)
                {
                    continue;
                }

                for (int y = 4; y <= maxLavaY; y++)
                {
                    if (chunk.GetBlock(localX, y, localZ) != BlockId.Air)
                    {
                        continue;
                    }

                    float lavaNoise = FractalNoise3D(worldX * 0.07f, y * 0.07f, worldZ * 0.07f, seed + 4400);
                    float threshold = y <= 16 ? 0.52f : y <= 20 ? 0.58f : 0.64f;
                    if (lavaNoise < threshold)
                    {
                        continue;
                    }

                    if (!HasSolidFloor(chunk, localX, y, localZ))
                    {
                        continue;
                    }

                    chunk.SetBlock(localX, y, localZ, BlockId.Lava);
                }
            }
        }

        FillDeepLavaChambers(chunk, baseX, baseZ, surfaceHeights);
    }

    private void FillDeepLavaChambers(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        int surfaceIndex = 0;
        for (int localZ = 1; localZ < GameConstants.ChunkSizeZ - 1; localZ++)
        {
            for (int localX = 1; localX < GameConstants.ChunkSizeX - 1; localX++)
            {
                int surfaceHeight = surfaceHeights[surfaceIndex++];
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;

                for (int y = 5; y <= System.Math.Min(18, surfaceHeight - SurfaceProtectionDepth); y++)
                {
                    if (chunk.GetBlock(localX, y, localZ) != BlockId.Air)
                    {
                        continue;
                    }

                    float chamber = FractalNoise3D(worldX * 0.05f, y * 0.05f, worldZ * 0.05f, seed + 4450);
                    if (chamber < 0.68f)
                    {
                        continue;
                    }

                    if (!HasSolidFloor(chunk, localX, y, localZ))
                    {
                        continue;
                    }

                    int airNeighbors = CountAdjacentAir(chunk, localX, y, localZ);
                    if (airNeighbors < 2)
                    {
                        continue;
                    }

                    chunk.SetBlock(localX, y, localZ, BlockId.Lava);
                    if (chamber > 0.78f && y > 5 && chunk.GetBlock(localX, y + 1, localZ) == BlockId.Air)
                    {
                        chunk.SetBlock(localX, y + 1, localZ, BlockId.Lava);
                    }
                }
            }
        }
    }

    private static int CountAdjacentAir(Chunk chunk, int localX, int y, int localZ)
    {
        int count = 0;
        if (chunk.GetBlock(localX + 1, y, localZ) == BlockId.Air)
        {
            count++;
        }

        if (chunk.GetBlock(localX - 1, y, localZ) == BlockId.Air)
        {
            count++;
        }

        if (chunk.GetBlock(localX, y, localZ + 1) == BlockId.Air)
        {
            count++;
        }

        if (chunk.GetBlock(localX, y, localZ - 1) == BlockId.Air)
        {
            count++;
        }

        return count;
    }

    private void PlaceOreVeins(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        for (int anchorY = 6; anchorY < 34; anchorY += 3)
        {
            for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    int surfaceIndex = localZ * GameConstants.ChunkSizeX + localX;
                    int surfaceHeight = surfaceHeights[surfaceIndex];
                    if (anchorY >= surfaceHeight - 3)
                    {
                        continue;
                    }

                    int worldX = baseX + localX;
                    int worldZ = baseZ + localZ;
                    int veinHash = HashSeed(worldX * 734287 + anchorY * 991 + worldZ * 1637, seed + 3300);
                    if ((veinHash & 0xFF) > 138)
                    {
                        continue;
                    }

                    BlockId ore = PickOreForDepth(anchorY, veinHash);
                    int radius = 1 + ((veinHash >> 8) & 1) + ((veinHash >> 12) & 1);
                    PlaceOreCluster(chunk, localX, anchorY, localZ, ore, radius, surfaceHeight);
                }
            }
        }
    }

    private void DecorateExposedStoneWithOre(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        int surfaceIndex = 0;
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int surfaceHeight = surfaceHeights[surfaceIndex++];
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;
                int minY = System.Math.Max(4, surfaceHeight - 18);
                int maxY = System.Math.Min(surfaceHeight - SurfaceProtectionDepth, surfaceHeight - 3);

                for (int y = minY; y <= maxY; y++)
                {
                    if (chunk.GetBlock(localX, y, localZ) is not (BlockId.Stone or BlockId.Gravel))
                    {
                        continue;
                    }

                    if (!IsAdjacentToAir(chunk, localX, y, localZ))
                    {
                        continue;
                    }

                    int exposureHash = HashSeed(worldX * 313 + y * 127 + worldZ * 419, seed + 3350);
                    if ((exposureHash & 0xFF) > 104)
                    {
                        continue;
                    }

                    BlockId ore = PickCaveWallOre(y, exposureHash);
                    chunk.SetBlock(localX, y, localZ, ore);
                }
            }
        }
    }

    private static bool IsAdjacentToAir(Chunk chunk, int localX, int y, int localZ)
    {
        if (localX > 0 && chunk.GetBlock(localX - 1, y, localZ) == BlockId.Air)
        {
            return true;
        }

        if (localX < GameConstants.ChunkSizeX - 1 && chunk.GetBlock(localX + 1, y, localZ) == BlockId.Air)
        {
            return true;
        }

        if (y > 0 && chunk.GetBlock(localX, y - 1, localZ) == BlockId.Air)
        {
            return true;
        }

        if (y < GameConstants.WorldHeight - 1 && chunk.GetBlock(localX, y + 1, localZ) == BlockId.Air)
        {
            return true;
        }

        if (localZ > 0 && chunk.GetBlock(localX, y, localZ - 1) == BlockId.Air)
        {
            return true;
        }

        if (localZ < GameConstants.ChunkSizeZ - 1 && chunk.GetBlock(localX, y, localZ + 1) == BlockId.Air)
        {
            return true;
        }

        return false;
    }

    private (int X, int Z, float YawRadians) GetScenicAnchor()
    {
        if (_cachedScenicAnchor is null)
        {
            _cachedScenicAnchor = FindScenicSpawn(world: null);
        }

        return _cachedScenicAnchor.Value;
    }

    /// <summary>
    /// Exposes the exact world-space center of the scenic showcase cave so the critic camera can
    /// aim directly at it instead of re-deriving it via a slightly different rounding scheme
    /// (which previously landed the look-down pose a block off, near the chamber wall).
    /// </summary>
    public (int X, int Z) GetScenicShowcasePitCenter() => GetScenicPitCenter();

    private (int X, int Z) GetScenicPitCenter()
    {
        if (_cachedScenicPitCenter is not null)
        {
            return _cachedScenicPitCenter.Value;
        }

        (int anchorX, int anchorZ, float scenicYaw) = GetScenicAnchor();
        float criticPitch = PlayerState.ScenicSpawnPitchRadians + PlayerState.CriticLookDownPitchOffsetRadians;
        float cosPitch = MathF.Cos(criticPitch);
        float lookDx = MathF.Sin(scenicYaw) * cosPitch;
        float lookDz = MathF.Cos(scenicYaw) * cosPitch;

        float eyeAboveSurface = 2f + GameConstants.PlayerEyeHeight;
        float slope = MathF.Tan(-criticPitch);
        float targetDistance = slope > 0.05f
            ? System.Math.Clamp(eyeAboveSurface / slope, 2.2f, 4.8f)
            : 3.2f;
        int pitCenterX = anchorX + (int)MathF.Round(lookDx * targetDistance);
        int pitCenterZ = anchorZ + (int)MathF.Round(lookDz * targetDistance);
        _cachedScenicPitCenter = (pitCenterX, pitCenterZ);
        return _cachedScenicPitCenter.Value;
    }

    private bool IsInScenicShowcaseZone(int worldX, int worldZ)
    {
        (int anchorX, int anchorZ, _) = GetScenicAnchor();
        (int pitX, int pitZ) = GetScenicPitCenter();
        int anchorDistSq = (worldX - anchorX) * (worldX - anchorX) + (worldZ - anchorZ) * (worldZ - anchorZ);
        int pitDistSq = (worldX - pitX) * (worldX - pitX) + (worldZ - pitZ) * (worldZ - pitZ);
        return anchorDistSq <= 10 * 10 || pitDistSq <= 11 * 11;
    }

    private void PlaceScenicShowcaseCave(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        (int pitCenterX, int pitCenterZ) = GetScenicPitCenter();

        // Widened from 3 so the critic look-down pose reads as an open dark chamber with visible
        // lava/ore floor instead of a tight corridor when the camera lands a block or two off-center.
        const int chamberRadius = 4;
        const int quarryDepth = 10;
        const int surfaceProtectionDepth = 6;

        for (int offsetZ = -chamberRadius; offsetZ <= chamberRadius; offsetZ++)
        {
            for (int offsetX = -chamberRadius; offsetX <= chamberRadius; offsetX++)
            {
                int worldX = pitCenterX + offsetX;
                int worldZ = pitCenterZ + offsetZ;
                int localX = worldX - baseX;
                int localZ = worldZ - baseZ;
                if (localX < 0 || localZ < 0
                    || localX >= GameConstants.ChunkSizeX || localZ >= GameConstants.ChunkSizeZ)
                {
                    continue;
                }

                int surfaceHeight = surfaceHeights[localZ * GameConstants.ChunkSizeX + localX];
                int floorY = System.Math.Max(6, surfaceHeight - quarryDepth);
                int ceilingY = surfaceHeight - surfaceProtectionDepth;
                if (ceilingY <= floorY + 2)
                {
                    continue;
                }

                bool isWall = System.Math.Abs(offsetX) == chamberRadius || System.Math.Abs(offsetZ) == chamberRadius;

                for (int y = floorY; y <= ceilingY; y++)
                {
                    if (y == ceilingY)
                    {
                        chunk.SetBlock(localX, y, localZ, BlockId.Stone);
                        continue;
                    }

                    if (isWall)
                    {
                        BlockId wallBlock = PickShowcaseWallBlock(offsetX, offsetZ, y);
                        chunk.SetBlock(localX, y, localZ, wallBlock);
                        continue;
                    }

                    if (y == floorY)
                    {
                        // Lava-dominant floor (2/3 lava, 1/3 coal) so the pit reads as a molten cave
                        // floor rather than a plain ore quarry; the exact pit center is forced to
                        // lava so a straight-down critic look-down ray always lands on the glow.
                        bool forceLava = offsetX == 0 && offsetZ == 0;
                        BlockId floorBlock = forceLava || ((offsetX + offsetZ + y) % 3 != 0)
                            ? BlockId.Lava
                            : BlockId.CoalOre;
                        chunk.SetBlock(localX, y, localZ, floorBlock);
                        continue;
                    }

                    if (y == floorY + 1 && ((offsetX + offsetZ) & 2) == 0)
                    {
                        chunk.SetBlock(localX, y, localZ, BlockId.Glowstone);
                        continue;
                    }

                    chunk.SetBlock(localX, y, localZ, BlockId.Air);
                }

                if (System.Math.Abs(offsetX) <= 1 && System.Math.Abs(offsetZ) <= 1)
                {
                    BlockId capOre = ((offsetX + offsetZ) & 1) == 0 ? BlockId.CoalOre : BlockId.IronOre;
                    chunk.SetBlock(localX, surfaceHeight, localZ, capOre);
                }
            }
        }

        ClearScenicShowcaseVegetation(chunk, baseX, baseZ);
    }

    private static BlockId PickShowcaseWallBlock(int offsetX, int offsetZ, int y)
    {
        int hash = (offsetX * 31 + offsetZ * 17 + y * 7) & 0xFF;
        if (hash < 72)
        {
            return BlockId.CoalOre;
        }

        if (hash < 132)
        {
            return BlockId.IronOre;
        }

        if (hash < 168)
        {
            return BlockId.CopperOre;
        }

        return BlockId.Stone;
    }

    private static void ClearScenicShowcaseVegetation(Chunk chunk, int baseX, int baseZ, Func<int, int, bool> zonePredicate)
    {
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            int worldZ = baseZ + localZ;
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int worldX = baseX + localX;
                if (!zonePredicate(worldX, worldZ))
                {
                    continue;
                }

                for (int y = GameConstants.SeaLevel; y < GameConstants.WorldHeight; y++)
                {
                    BlockId block = chunk.GetBlock(localX, y, localZ);
                    if (TreePlacer.IsLogBlock(block) || TreePlacer.IsLeavesBlock(block))
                    {
                        chunk.SetBlock(localX, y, localZ, BlockId.Air);
                    }
                }
            }
        }
    }

    private void ClearScenicShowcaseVegetation(Chunk chunk, int baseX, int baseZ) =>
        ClearScenicShowcaseVegetation(chunk, baseX, baseZ, IsInScenicShowcaseZone);

    private void PlaceCriticOreRing(
        Chunk chunk,
        int baseX,
        int baseZ,
        ReadOnlySpan<int> surfaceHeights,
        int pitCenterX,
        int pitCenterZ)
    {
        for (int offsetZ = -2; offsetZ <= 2; offsetZ++)
        {
            for (int offsetX = -2; offsetX <= 2; offsetX++)
            {
                if (System.Math.Abs(offsetX) < 2 && System.Math.Abs(offsetZ) < 2)
                {
                    continue;
                }

                int worldX = pitCenterX + offsetX;
                int worldZ = pitCenterZ + offsetZ;
                int localX = worldX - baseX;
                int localZ = worldZ - baseZ;
                if (localX < 0 || localZ < 0
                    || localX >= GameConstants.ChunkSizeX || localZ >= GameConstants.ChunkSizeZ)
                {
                    continue;
                }

                int surfaceHeight = surfaceHeights[localZ * GameConstants.ChunkSizeX + localX];
                BlockId ore = ((offsetX + offsetZ) & 1) == 0 ? BlockId.CoalOre : BlockId.IronOre;
                chunk.SetBlock(localX, surfaceHeight, localZ, ore);
                if (surfaceHeight > 1)
                {
                    chunk.SetBlock(localX, surfaceHeight - 1, localZ, ore);
                }
            }
        }
    }

    private BlockId PickOreForDepth(int y, int hash)
    {
        int roll = (hash >> 4) & 0xFF;
        if (y <= 20 && roll < 48)
        {
            return BlockId.CopperOre;
        }

        if (y <= 28 && roll < 168)
        {
            return BlockId.IronOre;
        }

        return BlockId.CoalOre;
    }

    private static BlockId PickCaveWallOre(int y, int hash)
    {
        int roll = (hash >> 4) & 0xFF;
        if (roll < 96)
        {
            return BlockId.CoalOre;
        }

        if (roll < 208 || y <= 24)
        {
            return BlockId.IronOre;
        }

        return BlockId.CopperOre;
    }

    private void PlaceStoneVariantPatches(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        for (int anchorY = 8; anchorY < 42; anchorY += 4)
        {
            for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    int surfaceIndex = localZ * GameConstants.ChunkSizeX + localX;
                    int surfaceHeight = surfaceHeights[surfaceIndex];
                    if (anchorY >= surfaceHeight - 4)
                    {
                        continue;
                    }

                    int worldX = baseX + localX;
                    int worldZ = baseZ + localZ;
                    int patchHash = HashSeed(worldX * 912367 + anchorY * 1187 + worldZ * 1543, seed + 3350);
                    if ((patchHash & 0xFF) > 118)
                    {
                        continue;
                    }

                    BlockId variant = ((patchHash >> 8) % 3) switch
                    {
                        0 => BlockId.Granite,
                        1 => BlockId.Andesite,
                        _ => BlockId.Diorite,
                    };
                    int radius = 2 + ((patchHash >> 12) & 1);
                    PlaceOreCluster(chunk, localX, anchorY, localZ, variant, radius, surfaceHeight);
                }
            }
        }
    }

    private void PlaceOreCluster(Chunk chunk, int centerX, int centerY, int centerZ, BlockId ore, int radius, int surfaceHeight)
    {
        for (int offsetZ = -radius; offsetZ <= radius; offsetZ++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    int localX = centerX + offsetX;
                    int y = centerY + offsetY;
                    int localZ = centerZ + offsetZ;
                    if (localX < 0 || localZ < 0
                        || localX >= GameConstants.ChunkSizeX || localZ >= GameConstants.ChunkSizeZ
                        || y < 4 || y >= surfaceHeight - 3)
                    {
                        continue;
                    }

                    float distanceSq = offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ;
                    if (distanceSq > radius * radius + 0.5f)
                    {
                        continue;
                    }

                    BlockId current = chunk.GetBlock(localX, y, localZ);
                    if (current is BlockId.Stone or BlockId.Gravel)
                    {
                        chunk.SetBlock(localX, y, localZ, ore);
                    }
                }
            }
        }
    }

    private void CarveCaves(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        int surfaceIndex = 0;
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int surfaceHeight = surfaceHeights[surfaceIndex++];
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;
                int maxCaveY = surfaceHeight - SurfaceProtectionDepth;
                if (maxCaveY <= 4)
                {
                    continue;
                }

                for (int y = 4; y <= maxCaveY; y++)
                {
                    if (!ShouldCarveCave(worldX, y, worldZ))
                    {
                        continue;
                    }

                    BlockId current = chunk.GetBlock(localX, y, localZ);
                    if (current is BlockId.Oil)
                    {
                        continue;
                    }

                    if (current is BlockId.Stone or BlockId.Dirt or BlockId.Gravel
                        or BlockId.CoalOre or BlockId.IronOre or BlockId.CopperOre)
                    {
                        chunk.SetBlock(localX, y, localZ, BlockId.Air);
                    }
                }
            }
        }
    }

    private bool ShouldCarveCave(int worldX, int y, int worldZ)
    {
        float cheese = FractalNoise3D(worldX * 0.065f, y * 0.065f, worldZ * 0.065f, seed + 4000);
        if (cheese > 0.52f)
        {
            return true;
        }

        float tunnel = FractalNoise3D(worldX * 0.11f, y * 0.11f, worldZ * 0.11f, seed + 4100);
        if (System.MathF.Abs(tunnel - 0.5f) < 0.035f)
        {
            return true;
        }

        float spaghetti = FractalNoise3D(worldX * 0.19f, y * 0.19f, worldZ * 0.19f, seed + 4150);
        if (System.MathF.Abs(spaghetti - 0.5f) < 0.016f)
        {
            return true;
        }

        float cavern = FractalNoise3D(worldX * 0.038f, y * 0.038f, worldZ * 0.038f, seed + 4200);
        if (cavern > 0.7f)
        {
            return true;
        }

        float pocket = FractalNoise3D(worldX * 0.13f, y * 0.13f, worldZ * 0.13f, seed + 4250);
        return pocket > 0.8f && cheese > 0.42f;
    }

    private void OpenCaveEntrances(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        for (int localZ = 1; localZ < GameConstants.ChunkSizeZ - 1; localZ++)
        {
            for (int localX = 1; localX < GameConstants.ChunkSizeX - 1; localX++)
            {
                int surfaceHeight = surfaceHeights[localZ * GameConstants.ChunkSizeX + localX];
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;
                if (worldX * worldX + worldZ * worldZ < SpawnProtectionRadiusSq)
                {
                    continue;
                }

                int slope = ComputeSurfaceSlope(surfaceHeights, localX, localZ);
                if (slope < 1)
                {
                    continue;
                }

                int entranceHash = HashSeed(worldX * 419 + worldZ * 811, seed + 4280);
                if ((entranceHash & 0xFF) > 84)
                {
                    continue;
                }

                int entranceBottom = surfaceHeight - SurfaceProtectionDepth;
                int entranceTop = entranceBottom - 2;
                if (entranceTop <= 4)
                {
                    continue;
                }

                CarveEntranceColumn(chunk, localX, localZ, entranceTop, entranceBottom);

                int downhillX = localX + ((entranceHash >> 8) & 1) == 0 ? 1 : -1;
                int downhillZ = localZ + ((entranceHash >> 9) & 1) == 0 ? 1 : -1;
                if (downhillX >= 0 && downhillX < GameConstants.ChunkSizeX
                    && downhillZ >= 0 && downhillZ < GameConstants.ChunkSizeZ)
                {
                    int neighborSurface = surfaceHeights[downhillZ * GameConstants.ChunkSizeX + downhillX];
                    if (neighborSurface < surfaceHeight - 1)
                    {
                        CarveEntranceMouth(chunk, localX, localZ, downhillX, downhillZ, entranceTop, entranceBottom);
                    }
                }
            }
        }
    }

    private static int ComputeSurfaceSlope(ReadOnlySpan<int> surfaceHeights, int localX, int localZ)
    {
        int center = surfaceHeights[localZ * GameConstants.ChunkSizeX + localX];
        int maxDelta = 0;
        for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetZ == 0)
                {
                    continue;
                }

                int neighborX = localX + offsetX;
                int neighborZ = localZ + offsetZ;
                if (neighborX < 0 || neighborZ < 0
                    || neighborX >= GameConstants.ChunkSizeX || neighborZ >= GameConstants.ChunkSizeZ)
                {
                    continue;
                }

                int neighbor = surfaceHeights[neighborZ * GameConstants.ChunkSizeX + neighborX];
                maxDelta = System.Math.Max(maxDelta, System.Math.Abs(neighbor - center));
            }
        }

        return maxDelta;
    }

    private static void CarveEntranceColumn(Chunk chunk, int localX, int localZ, int topY, int bottomY)
    {
        for (int y = topY; y <= bottomY; y++)
        {
            BlockId current = chunk.GetBlock(localX, y, localZ);
            if (current is BlockId.Oil)
            {
                continue;
            }

            if (current is BlockId.Stone or BlockId.Dirt or BlockId.Gravel
                or BlockId.CoalOre or BlockId.IronOre or BlockId.CopperOre or BlockId.Sandstone)
            {
                chunk.SetBlock(localX, y, localZ, BlockId.Air);
            }
        }
    }

    private static void CarveEntranceMouth(
        Chunk chunk,
        int localX,
        int localZ,
        int downhillX,
        int downhillZ,
        int topY,
        int surfaceHeight)
    {
        for (int y = topY; y <= surfaceHeight - 1; y++)
        {
            BlockId current = chunk.GetBlock(localX, y, localZ);
            if (current is BlockId.Oil)
            {
                continue;
            }

            if (current is BlockId.Stone or BlockId.Dirt or BlockId.Gravel
                or BlockId.CoalOre or BlockId.IronOre or BlockId.CopperOre or BlockId.Sandstone)
            {
                chunk.SetBlock(localX, y, localZ, BlockId.Air);
            }

            BlockId downhill = chunk.GetBlock(downhillX, y, downhillZ);
            if (downhill is BlockId.Stone or BlockId.Dirt or BlockId.Gravel
                or BlockId.CoalOre or BlockId.IronOre or BlockId.CopperOre)
            {
                chunk.SetBlock(downhillX, y, downhillZ, BlockId.Air);
            }
        }
    }

    private void FillUndergroundLakes(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        int surfaceIndex = 0;
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int surfaceHeight = surfaceHeights[surfaceIndex++];
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;
                int maxLakeY = System.Math.Min(GameConstants.SeaLevel - 5, surfaceHeight - SurfaceProtectionDepth);
                if (maxLakeY <= 6)
                {
                    continue;
                }

                for (int y = 6; y <= maxLakeY; y++)
                {
                    if (chunk.GetBlock(localX, y, localZ) != BlockId.Air)
                    {
                        continue;
                    }

                    float lakeNoise = FractalNoise3D(worldX * 0.07f, y * 0.07f, worldZ * 0.07f, seed + 4300);
                    if (lakeNoise < 0.55f)
                    {
                        continue;
                    }

                    if (!HasSolidFloor(chunk, localX, y, localZ))
                    {
                        continue;
                    }

                    chunk.SetBlock(localX, y, localZ, BlockId.Water);
                }
            }
        }
    }

    private static bool HasSolidFloor(Chunk chunk, int localX, int y, int localZ)
    {
        for (int depth = 1; depth <= 2; depth++)
        {
            int belowY = y - depth;
            if (belowY < 1)
            {
                return false;
            }

            BlockId below = chunk.GetBlock(localX, belowY, localZ);
            if (below is BlockId.Stone or BlockId.Gravel or BlockId.Dirt or BlockId.Sandstone)
            {
                return true;
            }
        }

        return false;
    }

    private void PlaceFlowerPatches(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        int surfaceIndex = 0;
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int estimatedSurface = surfaceHeights[surfaceIndex++];
                int surfaceHeight = FindTopAirColumn(chunk, localX, localZ, estimatedSurface);
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;

                if (!TryResolvePlantColumn(chunk, localX, localZ, surfaceHeight, out int plantY, out int groundY))
                {
                    continue;
                }

                Biome biome = GetBiome(worldX, worldZ);
                if (biome == Biome.Desert)
                {
                    continue;
                }

                if (chunk.GetBlock(localX, plantY, localZ) != BlockId.Air)
                {
                    continue;
                }

                BlockId ground = chunk.GetBlock(localX, groundY, localZ);

                if (biome == Biome.Jungle)
                {
                    int fernRoll = HashSeed(worldX ^ (worldZ * 668265263), seed + 6270) & 0xFF;
                    if (fernRoll < 95)
                    {
                        chunk.SetBlock(localX, plantY, localZ, BlockId.Fern);
                        continue;
                    }

                    if ((HashSeed(worldX + worldZ * 31, seed + 6275) & 127) == 0)
                    {
                        chunk.SetBlock(localX, plantY, localZ, BlockId.Fern);
                        continue;
                    }
                }

                if (biome is Biome.Plains or Biome.Forest
                    && ground is BlockId.Grass or BlockId.Moss)
                {
                    int grassRoll = HashSeed(worldX ^ (worldZ * 31), seed + 6290) & 0xFF;
                    if (grassRoll < 110)
                    {
                        chunk.SetBlock(localX, plantY, localZ, BlockId.ShortGrass);
                        continue;
                    }

                    if (grassRoll < 165)
                    {
                        chunk.SetBlock(localX, plantY, localZ, BlockId.TallGrass);
                        continue;
                    }
                }

                int sparseRoll = HashSeed(worldX ^ (worldZ * 17), seed + 6280) & 0x7F;
                if (sparseRoll == 0)
                {
                    chunk.SetBlock(localX, plantY, localZ, BlockId.Moss);
                    continue;
                }

                float flowerNoise = FractalNoise2D(worldX * 0.17f, worldZ * 0.17f, seed + 6200);
                int flowerChance = HashSeed(worldX ^ (worldZ * 668265263), seed + 6200) & 0xFF;
                float threshold = biome switch
                {
                    Biome.Jungle => 0.68f,
                    Biome.Forest => 0.72f,
                    _ => 0.76f,
                };
                if (flowerNoise <= threshold && flowerChance > 24)
                {
                    continue;
                }

                int flowerRoll = HashSeed(worldX ^ (worldZ * 668265263), seed + 6250) & 0xFF;
                BlockId flower = flowerRoll switch
                {
                    < 70 => BlockId.FlowerYellow,
                    < 140 => BlockId.FlowerRed,
                    < 210 => BlockId.FlowerBlue,
                    < 250 => BlockId.Shrub,
                    _ => BlockId.Moss,
                };
                chunk.SetBlock(localX, plantY, localZ, flower);
            }
        }
    }

    private void PlaceJungleFerns(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        int surfaceIndex = 0;
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int estimatedSurface = surfaceHeights[surfaceIndex++];
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;
                if (GetBiome(worldX, worldZ) != Biome.Jungle)
                {
                    continue;
                }

                int minY = System.Math.Max(GameConstants.SeaLevel + 1, estimatedSurface);
                int maxY = System.Math.Min(GameConstants.WorldHeight - 2, estimatedSurface + 8);
                for (int y = maxY; y >= minY; y--)
                {
                    if (chunk.GetBlock(localX, y, localZ) != BlockId.Air)
                    {
                        continue;
                    }

                    BlockId ground = chunk.GetBlock(localX, y - 1, localZ);
                    if (ground is not (
                        BlockId.JungleGrass or BlockId.Podzol or BlockId.Moss or BlockId.Mycelium
                        or BlockId.Grass or BlockId.Dirt))
                    {
                        continue;
                    }

                    int fernRoll = HashSeed(worldX ^ (worldZ * 668265263), seed + 6276) & 0xFF;
                    if (fernRoll < 110)
                    {
                        chunk.SetBlock(localX, y, localZ, BlockId.Fern);
                    }

                    break;
                }
            }
        }
    }

    private static bool TryResolvePlantColumn(Chunk chunk, int localX, int localZ, int surfaceHeight, out int plantY, out int groundY)
    {
        plantY = 0;
        groundY = 0;
        if (surfaceHeight <= GameConstants.SeaLevel)
        {
            return false;
        }

        int maxY = System.Math.Min(GameConstants.WorldHeight - 2, surfaceHeight + 12);
        for (int y = maxY; y > GameConstants.SeaLevel; y--)
        {
            if (chunk.GetBlock(localX, y, localZ) != BlockId.Air)
            {
                continue;
            }

            BlockId below = chunk.GetBlock(localX, y - 1, localZ);
            if (below is BlockId.Grass or BlockId.Moss or BlockId.Dirt or BlockId.JungleGrass or BlockId.Podzol or BlockId.Mycelium)
            {
                plantY = y;
                groundY = y - 1;
                return true;
            }
        }

        return false;
    }

    private static int FindTopAirColumn(Chunk chunk, int localX, int localZ, int estimatedSurface)
    {
        int start = System.Math.Clamp(estimatedSurface + 4, 0, GameConstants.WorldHeight - 2);
        for (int y = start; y >= 1; y--)
        {
            if (chunk.GetBlock(localX, y, localZ) != BlockId.Air)
            {
                return y + 1;
            }
        }

        return System.Math.Clamp(estimatedSurface, 1, GameConstants.WorldHeight - 2);
    }

    private void PlaceTrees(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        for (int localZ = 5; localZ < GameConstants.ChunkSizeZ - 5; localZ++)
        {
            int surfaceIndex = localZ * GameConstants.ChunkSizeX;
            for (int localX = 5; localX < GameConstants.ChunkSizeX - 5; localX++)
            {
                int estimatedSurface = surfaceHeights[surfaceIndex + localX];
                int surfaceHeight = estimatedSurface;
                if (chunk.GetBlock(localX, surfaceHeight, localZ) != BlockId.Air)
                {
                    surfaceHeight = FindTopAirColumn(chunk, localX, localZ, estimatedSurface);
                }
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;
                if (IsInScenicShowcaseZone(worldX, worldZ))
                {
                    continue;
                }

                int groundY = surfaceHeight - 1;

                if (surfaceHeight <= GameConstants.SeaLevel)
                {
                    continue;
                }

                Biome biome = GetBiome(worldX, worldZ);
                if (biome is Biome.Desert or Biome.Ocean)
                {
                    continue;
                }

                BlockId groundBlock = chunk.GetBlock(localX, groundY, localZ);
                if (groundBlock is BlockId.Sand or BlockId.Water or BlockId.Stone or BlockId.Bedrock
                    or BlockId.Ice or BlockId.Snow or BlockId.PackedIce)
                {
                    continue;
                }

                if (chunk.GetBlock(localX, surfaceHeight, localZ) != BlockId.Air)
                {
                    continue;
                }

                int treeThreshold = biome switch
                {
                    Biome.Jungle => 220,
                    Biome.Forest => 175,
                    Biome.Mountains => 6,
                    Biome.Arctic => 4,
                    _ => 10,
                };

                int spawnDistSq = worldX * worldX + worldZ * worldZ;
                if (spawnDistSq < SpawnProtectionRadiusSq && biome is Biome.Forest or Biome.Plains)
                {
                    treeThreshold = biome == Biome.Forest ? 220 : 40;
                }

                int treeRoll = HashSeed(worldX ^ (worldZ * 668265263), seed + 6000) & 0xFF;
                if (treeRoll >= treeThreshold)
                {
                    continue;
                }

                int treeSeed = HashSeed(worldX ^ (worldZ * 668265263), seed + 6100);
                TreeKind kind = PickTreeKind(biome, treeSeed);
                if (!TreePlacer.CanPlaceTree(chunk, localX, surfaceHeight, localZ, kind, treeSeed))
                {
                    continue;
                }

                TreePlacer.PlaceTree(chunk, localX, surfaceHeight, localZ, kind, treeSeed);
            }
        }
    }

    private static TreeKind PickTreeKind(Biome biome, int treeSeed)
    {
        int roll = treeSeed & 0xFF;
        return biome switch
        {
            Biome.Jungle => roll switch
            {
                < 60 => TreeKind.Jungle,
                < 140 => TreeKind.Oak,
                < 200 => TreeKind.Birch,
                _ => TreeKind.Spruce,
            },
            Biome.Forest => roll switch
            {
                < 100 => TreeKind.Oak,
                < 175 => TreeKind.Birch,
                _ => TreeKind.Spruce,
            },
            Biome.Mountains => roll switch
            {
                < 120 => TreeKind.Spruce,
                < 200 => TreeKind.Oak,
                _ => TreeKind.Birch,
            },
            Biome.Arctic => TreeKind.Spruce,
            _ => roll < 180 ? TreeKind.Oak : TreeKind.Birch,
        };
    }

    private void PlaceCacti(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        for (int localZ = 2; localZ < GameConstants.ChunkSizeZ - 2; localZ++)
        {
            int surfaceIndex = localZ * GameConstants.ChunkSizeX;
            for (int localX = 2; localX < GameConstants.ChunkSizeX - 2; localX++)
            {
                int estimatedSurface = surfaceHeights[surfaceIndex + localX];
                int surfaceHeight = estimatedSurface;
                if (chunk.GetBlock(localX, surfaceHeight, localZ) != BlockId.Air)
                {
                    surfaceHeight = FindTopAirColumn(chunk, localX, localZ, estimatedSurface);
                }
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;
                int groundY = surfaceHeight - 1;

                if (GetBiome(worldX, worldZ) != Biome.Desert || surfaceHeight < GameConstants.SeaLevel)
                {
                    continue;
                }

                if (chunk.GetBlock(localX, groundY, localZ) is not (BlockId.Sand or BlockId.RedSand or BlockId.Sandstone))
                {
                    continue;
                }

                if (chunk.GetBlock(localX, surfaceHeight, localZ) != BlockId.Air)
                {
                    continue;
                }

                int cactusRoll = HashSeed(worldX ^ (worldZ * 668265263), seed + 6300) & 0xFF;
                bool clusterSeed = (HashSeed(worldX ^ worldZ, seed + 6301) & 63) == 0;
                if (!clusterSeed && cactusRoll > 48)
                {
                    continue;
                }

                int cactusHeight = 2 + (cactusRoll & 3);
                PlaceCactusColumn(chunk, localX, surfaceHeight, localZ, cactusHeight);

                if (clusterSeed)
                {
                    PlaceCactusCluster(chunk, localX, localZ, surfaceHeight, baseX, baseZ, cactusHeight);
                }
            }
        }
    }

    private static void PlaceCactusColumn(Chunk chunk, int localX, int surfaceHeight, int localZ, int cactusHeight)
    {
        for (int dy = 0; dy < cactusHeight; dy++)
        {
            int y = surfaceHeight + dy;
            if (y >= GameConstants.WorldHeight)
            {
                break;
            }

            chunk.SetBlock(localX, y, localZ, BlockId.Cactus);
        }
    }

    private void PlaceCactusCluster(
        Chunk chunk,
        int centerLocalX,
        int centerLocalZ,
        int surfaceHeight,
        int baseX,
        int baseZ,
        int centerHeight)
    {
        ReadOnlySpan<(int Dx, int Dz)> offsets = stackalloc (int, int)[]
        {
            (2, 0), (-2, 0), (0, 2), (0, -2), (2, 2), (-2, -2),
        };

        foreach ((int dx, int dz) in offsets)
        {
            int localX = centerLocalX + dx;
            int localZ = centerLocalZ + dz;
            if (localX < 2 || localZ < 2
                || localX >= GameConstants.ChunkSizeX - 2
                || localZ >= GameConstants.ChunkSizeZ - 2)
            {
                continue;
            }

            int worldX = baseX + localX;
            int worldZ = baseZ + localZ;
            if (GetBiome(worldX, worldZ) != Biome.Desert)
            {
                continue;
            }

            int groundY = surfaceHeight - 1;
            if (chunk.GetBlock(localX, groundY, localZ) is not (BlockId.Sand or BlockId.RedSand or BlockId.Sandstone))
            {
                continue;
            }

            if (chunk.GetBlock(localX, surfaceHeight, localZ) != BlockId.Air)
            {
                continue;
            }

            int clusterRoll = HashSeed(worldX ^ (worldZ * 668265263), seed + 6302) & 0xFF;
            if (clusterRoll > 90)
            {
                continue;
            }

            int height = 1 + (clusterRoll & 2) + (centerHeight > 2 ? 1 : 0);
            PlaceCactusColumn(chunk, localX, surfaceHeight, localZ, height);
        }
    }

    private void PlaceSnowLayers(Chunk chunk, int baseX, int baseZ, ReadOnlySpan<int> surfaceHeights)
    {
        int surfaceIndex = 0;
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int surfaceHeight = FindTopAirColumn(chunk, localX, localZ, surfaceHeights[surfaceIndex++]);
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;
                int groundY = surfaceHeight - 1;

                Biome biome = GetBiome(worldX, worldZ);
                if (biome is not (Biome.Arctic or Biome.Mountains))
                {
                    continue;
                }

                BlockId ground = chunk.GetBlock(localX, groundY, localZ);
                if (ground is not (BlockId.Snow or BlockId.Ice or BlockId.Grass or BlockId.Stone))
                {
                    continue;
                }

                if (chunk.GetBlock(localX, surfaceHeight, localZ) != BlockId.Air)
                {
                    continue;
                }

                int snowRoll = HashSeed(worldX ^ (worldZ * 668265263), seed + 6350) & 0xFF;
                if (snowRoll > (biome == Biome.Arctic ? 140 : 40))
                {
                    continue;
                }

                chunk.SetBlock(localX, surfaceHeight, localZ, BlockId.SnowLayer);

                if (biome == Biome.Arctic && snowRoll < 48 && surfaceHeight + 1 < GameConstants.WorldHeight
                    && chunk.GetBlock(localX, surfaceHeight + 1, localZ) == BlockId.Air)
                {
                    chunk.SetBlock(localX, surfaceHeight + 1, localZ, BlockId.SnowLayer);
                }
            }
        }
    }

    private static float FractalNoise2D(float x, float z, int seedOffset)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float normalization = 0f;
        for (int octave = 0; octave < 4; octave++)
        {
            value += Noise(x * frequency, z * frequency, seedOffset + octave * 131) * amplitude;
            normalization += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return value / normalization;
    }

    private static float FractalNoise3D(float x, float y, float z, int seedOffset)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float normalization = 0f;
        for (int octave = 0; octave < 3; octave++)
        {
            value += Noise(x * frequency, y * frequency + 17f, (int)z + seedOffset + octave * 97) * amplitude;
            normalization += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return value / normalization;
    }

    private static float Noise(float x, float y, int seed)
    {
        int xi = (int)MathF.Floor(x);
        int yi = (int)MathF.Floor(y);
        float xf = x - xi;
        float yf = y - yi;
        float a = HashFloat(xi, yi, seed);
        float b = HashFloat(xi + 1, yi, seed);
        float c = HashFloat(xi, yi + 1, seed);
        float d = HashFloat(xi + 1, yi + 1, seed);
        float u = Smooth(xf);
        float v = Smooth(yf);
        return Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
    }

    private static float HashFloat(int x, int y, int seed) => HashSeed(x ^ (y * 374761393), seed) / (float)int.MaxValue;

    private static int HashSeed(int x, int seed)
    {
        unchecked
        {
            int hash = seed;
            hash ^= x * 1619;
            hash = (hash ^ (hash >> 13)) * 1274126177;
            return hash;
        }
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

public sealed class FlatWorldGenerator : IWorldGenerator
{
    private const int TestWaterPoolCenterX = 20;
    private const int TestWaterPoolCenterZ = 0;
    private const int TestWaterPoolRadius = 3;

    public void GenerateChunk(GameWorld world, Chunk chunk)
    {
        int baseX = chunk.Position.X * GameConstants.ChunkSizeX;
        int baseZ = chunk.Position.Z * GameConstants.ChunkSizeZ;

        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                for (int y = 0; y < GameConstants.WorldHeight; y++)
                {
                    BlockId block = y switch
                    {
                        0 => BlockId.Bedrock,
                        < 24 => BlockId.Stone,
                        24 => BlockId.Dirt,
                        25 => BlockId.Grass,
                        _ => BlockId.Air,
                    };
                    chunk.SetBlock(localX, y, localZ, block);
                }
            }
        }

        PlaceTestWaterPool(chunk, baseX, baseZ);
    }

    private static void PlaceTestWaterPool(Chunk chunk, int baseX, int baseZ)
    {
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int worldX = baseX + localX;
                int worldZ = baseZ + localZ;
                if (System.Math.Abs(worldX - TestWaterPoolCenterX) > TestWaterPoolRadius
                    || System.Math.Abs(worldZ - TestWaterPoolCenterZ) > TestWaterPoolRadius)
                {
                    continue;
                }

                for (int y = GameConstants.SeaLevel - 2; y <= GameConstants.SeaLevel; y++)
                {
                    chunk.SetBlock(localX, y, localZ, BlockId.Water);
                }
            }
        }
    }
}