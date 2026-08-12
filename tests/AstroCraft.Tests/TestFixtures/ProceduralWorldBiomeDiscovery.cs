using AstroCraft.Core;
using AstroCraft.Core.World.Generation;

namespace AstroCraft.Tests.TestFixtures;

internal sealed record BiomeSites(
    (int X, int Z) Mountains,
    (int X, int Z) Plains,
    (int X, int Z) Forest,
    (int X, int Z) Desert,
    (int X, int Z) Ocean,
    (int X, int Z) Jungle,
    (int X, int Z) Arctic,
    (int X, int Z) ForestFar,
    (int X, int Z) PlainsFar,
    (int X, int Z) DesertFar,
    (int X, int Z) JungleFar,
    (int X, int Z) JungleDryFar,
    (int X, int Z) ArcticFar,
    bool HasBlendedBiomeInfluence,
    HashSet<Biome> DistinctBiomesInSample);

internal static class ProceduralWorldBiomeDiscovery
{
    public static BiomeSites Discover(ProceduralWorldGenerator generator)
    {
        return new BiomeSites(
            Mountains: FindBiomeCoordinates(generator, Biome.Mountains),
            Plains: FindBiomeCoordinates(generator, Biome.Plains),
            Forest: FindBiomeCoordinates(generator, Biome.Forest),
            Desert: FindBiomeCoordinates(generator, Biome.Desert),
            Ocean: FindBiomeCoordinates(generator, Biome.Ocean, requireAboveSeaLevel: false),
            Jungle: FindBiomeCoordinates(generator, Biome.Jungle),
            Arctic: FindBiomeCoordinates(generator, Biome.Arctic),
            ForestFar: FindBiomeCoordinates(generator, Biome.Forest, minDistanceFromSpawn: 80),
            PlainsFar: FindBiomeCoordinates(generator, Biome.Plains, minDistanceFromSpawn: 80),
            DesertFar: FindBiomeCoordinates(generator, Biome.Desert, minDistanceFromSpawn: 80),
            JungleFar: FindBiomeCoordinates(generator, Biome.Jungle, minDistanceFromSpawn: 80),
            JungleDryFar: FindDryBiomeCoordinates(generator, Biome.Jungle, minDistanceFromSpawn: 80),
            ArcticFar: FindBiomeCoordinates(generator, Biome.Arctic, minDistanceFromSpawn: 80),
            HasBlendedBiomeInfluence: DiscoverBlendedBiomeInfluence(generator),
            DistinctBiomesInSample: DiscoverDistinctBiomes(generator));
    }

    private static bool DiscoverBlendedBiomeInfluence(ProceduralWorldGenerator generator)
    {
        for (int worldX = 0; worldX < 1024; worldX += 8)
        {
            for (int worldZ = 0; worldZ < 1024; worldZ += 8)
            {
                float desert = generator.GetBiomeInfluence(worldX, worldZ, Biome.Desert);
                float plains = generator.GetBiomeInfluence(worldX, worldZ, Biome.Plains);
                float forest = generator.GetBiomeInfluence(worldX, worldZ, Biome.Forest);
                float mountains = generator.GetBiomeInfluence(worldX, worldZ, Biome.Mountains);
                float jungle = generator.GetBiomeInfluence(worldX, worldZ, Biome.Jungle);

                if ((desert > 0.08f && plains > 0.08f)
                    || (forest > 0.08f && plains > 0.08f)
                    || (mountains > 0.08f && plains > 0.08f)
                    || (jungle > 0.08f && forest > 0.08f))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static HashSet<Biome> DiscoverDistinctBiomes(ProceduralWorldGenerator generator)
    {
        HashSet<Biome> biomes = new();
        for (int worldX = 0; worldX < 4096; worldX += 8)
        {
            for (int worldZ = 0; worldZ < 4096; worldZ += 8)
            {
                biomes.Add(generator.GetBiome(worldX, worldZ));
            }
        }

        return biomes;
    }

    private static (int X, int Z) FindBiomeCoordinates(
        ProceduralWorldGenerator generator,
        Biome target,
        bool requireAboveSeaLevel = true,
        int minDistanceFromSpawn = 0)
    {
        for (int worldX = 0; worldX < 4096; worldX += 4)
        {
            for (int worldZ = 0; worldZ < 4096; worldZ += 4)
            {
                if (worldX * worldX + worldZ * worldZ < minDistanceFromSpawn * minDistanceFromSpawn)
                {
                    continue;
                }

                if (generator.GetBiome(worldX, worldZ) != target)
                {
                    continue;
                }

                int surfaceHeight = generator.ComputeSurfaceHeight(worldX, worldZ);
                if (target == Biome.Ocean || !requireAboveSeaLevel || surfaceHeight > GameConstants.SeaLevel - 6)
                {
                    return (worldX, worldZ);
                }
            }
        }

        throw new InvalidOperationException($"Could not find biome {target}.");
    }

    private static (int X, int Z) FindDryBiomeCoordinates(
        ProceduralWorldGenerator generator,
        Biome target,
        int minDistanceFromSpawn = 0)
    {
        for (int worldX = 0; worldX < 4096; worldX += 4)
        {
            for (int worldZ = 0; worldZ < 4096; worldZ += 4)
            {
                if (worldX * worldX + worldZ * worldZ < minDistanceFromSpawn * minDistanceFromSpawn)
                {
                    continue;
                }

                if (generator.GetBiome(worldX, worldZ) != target)
                {
                    continue;
                }

                int surfaceHeight = generator.ComputeSurfaceHeight(worldX, worldZ);
                if (surfaceHeight > GameConstants.SeaLevel)
                {
                    return (worldX, worldZ);
                }
            }
        }

        throw new InvalidOperationException($"Could not find dry biome {target}.");
    }
}
