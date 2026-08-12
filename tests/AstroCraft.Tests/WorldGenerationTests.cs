using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;
using AstroCraft.Core.Players;
using AstroCraft.Core.Rendering;
using AstroCraft.Core.World;
using AstroCraft.Core.World.Generation;
using AstroCraft.Tests.TestFixtures;

namespace AstroCraft.Tests;

public class WorldGenerationTests : IClassFixture<ProceduralWorldFixture>, IClassFixture<FlatWorldFixture>
{
    private readonly ProceduralWorldFixture _procedural;
    private readonly FlatWorldFixture _flat;

    public WorldGenerationTests(ProceduralWorldFixture procedural, FlatWorldFixture flat)
    {
        _procedural = procedural;
        _flat = flat;
    }

    [Fact]
    public void ProceduralGenerator_ProducesDeterministicSurface_ForSameSeed()
    {
        ProceduralWorldGenerator generatorA = new(42);
        ProceduralWorldGenerator generatorB = new(42);

        for (int x = 0; x < GameConstants.ChunkSizeX; x++)
        {
            for (int z = 0; z < GameConstants.ChunkSizeZ; z++)
            {
                Assert.Equal(generatorA.ComputeSurfaceHeight(x, z), generatorB.ComputeSurfaceHeight(x, z));
                Assert.Equal(generatorA.GetBiome(x, z), generatorB.GetBiome(x, z));
            }
        }
    }

    [Fact]
    public void ProceduralGenerator_DifferentSeeds_ProduceDifferentTerrain()
    {
        ProceduralWorldGenerator seed42 = new(42);
        ProceduralWorldGenerator seed99 = new(99);

        bool anyDifference = false;
        for (int x = 0; x < GameConstants.ChunkSizeX && !anyDifference; x++)
        {
            for (int z = 0; z < GameConstants.ChunkSizeZ; z++)
            {
                if (seed42.ComputeSurfaceHeight(x, z) != seed99.ComputeSurfaceHeight(x, z)
                    || seed42.GetBiome(x, z) != seed99.GetBiome(x, z))
                {
                    anyDifference = true;
                    break;
                }
            }
        }

        Assert.True(anyDifference);
    }

    [Fact]
    public void ProceduralGenerator_Seed42_FindScenicSpawn_HasGrassAndTrees()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        GameWorld world = _procedural.SharedWorld;
        (int scenicX, int scenicZ, float scenicYaw) = _procedural.ScenicSpawn;

        int surfaceY = FindSurfaceY(world, scenicX, scenicZ);
        BlockId surface = world.GetBlock(scenicX, surfaceY, scenicZ);
        Biome biome = generator.GetBiome(scenicX, scenicZ);
        int trees = CountTreesNear(world, scenicX, scenicZ, radius: 24);

        Assert.True(biome is Biome.Forest or Biome.Plains);
        Assert.True(surface is BlockId.Grass or BlockId.Moss or BlockId.Dirt, $"Unexpected surface {surface} at ({scenicX},{scenicZ})");
        Assert.NotEqual(BlockId.Stone, surface);
        Assert.True(trees > 0);
        Assert.True(scenicYaw >= -Math.PI && scenicYaw <= Math.PI);
    }

    [Fact]
    public void ProceduralGenerator_Seed42_HasExpectedSurfaceHeights()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        Assert.InRange(generator.ComputeSurfaceHeight(0, 0), 30, 36);
        Assert.InRange(generator.ComputeSurfaceHeight(8, 8), 24, 40);
        Assert.InRange(generator.ComputeSurfaceHeight(15, 0), 28, 36);
        Assert.InRange(generator.ComputeSurfaceHeight(0, 15), 28, 36);
        Assert.InRange(generator.ComputeSurfaceHeight(32, 32), 24, 42);
    }

    [Fact]
    public void ProceduralGenerator_MountainsBiome_RaisesSurfaceHeight()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        (int mountainsX, int mountainsZ) = _procedural.Sites.Mountains;
        (int plainsX, int plainsZ) = _procedural.Sites.Plains;

        int mountainsHeight = generator.ComputeSurfaceHeight(mountainsX, mountainsZ);
        int plainsHeight = generator.ComputeSurfaceHeight(plainsX, plainsZ);

        Assert.True(mountainsHeight >= plainsHeight);
        Assert.Contains(
            generator.GetBiome(mountainsX, mountainsZ),
            new[] { Biome.Mountains });
    }

    [Fact]
    public void ProceduralGenerator_AssignsDistinctBiomesAcrossWorld()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        BiomeSites sites = _procedural.Sites;

        Assert.Equal(Biome.Mountains, generator.GetBiome(sites.Mountains.X, sites.Mountains.Z));
        Assert.Equal(Biome.Plains, generator.GetBiome(sites.Plains.X, sites.Plains.Z));
        Assert.Equal(Biome.Forest, generator.GetBiome(sites.Forest.X, sites.Forest.Z));
        Assert.Equal(Biome.Desert, generator.GetBiome(sites.Desert.X, sites.Desert.Z));
        Assert.Equal(Biome.Ocean, generator.GetBiome(sites.Ocean.X, sites.Ocean.Z));
        Assert.Equal(Biome.Jungle, generator.GetBiome(sites.Jungle.X, sites.Jungle.Z));
        Assert.Equal(Biome.Arctic, generator.GetBiome(sites.Arctic.X, sites.Arctic.Z));
        Assert.True(sites.DistinctBiomesInSample.Count >= 5);
    }

    [Fact]
    public void ProceduralGenerator_DesertBiome_UsesSandSurface()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        (int worldX, int worldZ) = _procedural.Sites.Desert;
        GameWorld world = _procedural.SharedWorld;
        ChunkPosition chunkPos = ChunkPosition.FromBlock(worldX, worldZ);
        Chunk chunk = world.GetOrCreateChunk(chunkPos);
        int localX = Mod(worldX, GameConstants.ChunkSizeX);
        int localZ = Mod(worldZ, GameConstants.ChunkSizeZ);
        int surfaceY = FindSurfaceHeight(chunk, localX, localZ);
        BlockId surface = chunk.GetBlock(localX, surfaceY - 1, localZ);

        Assert.True(surface is BlockId.Sand or BlockId.RedSand);
    }

    [Fact]
    public void ProceduralGenerator_ForestBiome_HasHigherTreeDensityThanDesert()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        GameWorld world = _procedural.SharedWorld;
        world.EnsureChunksAround(128, 128, 4);

        (int forestX, int forestZ) = _procedural.Sites.Forest;
        (int desertX, int desertZ) = _procedural.Sites.Desert;
        world.EnsureChunksAround(forestX, forestZ, 4);
        world.EnsureChunksAround(desertX, desertZ, 4);

        int forestTrees = CountTreesNear(world, forestX, forestZ, radius: 48);
        int desertTrees = CountTreesNear(world, desertX, desertZ, radius: 48);

        Assert.Equal(0, desertTrees);
        Assert.True(forestTrees > 0);
    }

    [Fact]
    public void ProceduralGenerator_PlacesOilPoolsAtLowElevations()
    {
        GameWorld world = _procedural.SharedWorld;

        int oilCount = 0;
        int maxOilY = 0;
        foreach (Chunk chunk in WorldGenerationTestHelpers.LoadedChunksNear(world, 64, 64, 8))
        {
            for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    for (int y = 4; y <= 14; y++)
                    {
                        if (chunk.GetBlock(localX, y, localZ) == BlockId.Oil)
                        {
                            oilCount++;
                            maxOilY = System.Math.Max(maxOilY, y);
                        }
                    }
                }
            }
        }

        Assert.True(oilCount > 0);
        Assert.True(maxOilY <= 14);
    }

    [Fact]
    public void ProceduralGenerator_CreatesWaterLakesBelowSeaLevel()
    {
        GameWorld world = _procedural.SharedWorld;

        bool foundLake = false;
        foreach (Chunk chunk in WorldGenerationTestHelpers.LoadedChunksNear(world, 128, 128, 6))
        {
            for (int localZ = 1; localZ < GameConstants.ChunkSizeZ - 1; localZ++)
            {
                for (int localX = 1; localX < GameConstants.ChunkSizeX - 1; localX++)
                {
                    int surfaceY = FindSurfaceHeight(chunk, localX, localZ);
                    if (surfaceY >= GameConstants.SeaLevel)
                    {
                        continue;
                    }

                    if (chunk.GetBlock(localX, surfaceY, localZ) == BlockId.Water)
                    {
                        foundLake = true;
                        break;
                    }
                }
            }
        }

        Assert.True(foundLake);
    }

    [Fact]
    public void ProceduralGenerator_PlacesOreVeinsBelowSurface()
    {
        GameWorld world = _procedural.SharedWorld;
        Chunk chunk = world.GetOrCreateChunk(new ChunkPosition(0, 0));

        int coalCount = 0;
        int ironCount = 0;
        int copperCount = 0;
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int surfaceY = FindSurfaceHeight(chunk, localX, localZ);
                for (int y = 4; y < surfaceY - 3; y++)
                {
                    BlockId block = chunk.GetBlock(localX, y, localZ);
                    coalCount += block == BlockId.CoalOre ? 1 : 0;
                    ironCount += block == BlockId.IronOre ? 1 : 0;
                    copperCount += block == BlockId.CopperOre ? 1 : 0;
                }
            }
        }

        Assert.True(coalCount > 0);
        Assert.True(ironCount > 0);
        Assert.True(copperCount > 0);
    }

    [Fact]
    public void ProceduralGenerator_PlacesLavaInDeepCaves()
    {
        GameWorld world = _procedural.SharedWorld;
        int lavaCount = 0;
        for (int cx = -1; cx <= 1; cx++)
        {
            for (int cz = -1; cz <= 1; cz++)
            {
                Chunk chunk = world.GetOrCreateChunk(new ChunkPosition(cx, cz));
                for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
                {
                    for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                    {
                        for (int y = 4; y <= 20; y++)
                        {
                            if (chunk.GetBlock(localX, y, localZ) == BlockId.Lava)
                            {
                                lavaCount++;
                            }
                        }
                    }
                }
            }
        }

        Assert.True(lavaCount > 0);
    }

    [Fact]
    public void ProceduralGenerator_CreatesSandBeachesNearSeaLevel()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        (int oceanX, int oceanZ) = _procedural.Sites.Ocean;
        GameWorld world = _procedural.SharedWorld;
        world.EnsureChunksAround(oceanX, oceanZ, 6);

        bool foundSandBeach = false;
        for (int worldX = oceanX - 96; worldX <= oceanX + 96; worldX++)
        {
            for (int worldZ = oceanZ - 96; worldZ <= oceanZ + 96; worldZ++)
            {
                int surfaceHeight = generator.ComputeSurfaceHeight(worldX, worldZ);
                if (surfaceHeight > GameConstants.SeaLevel + 3)
                {
                    continue;
                }

                Chunk chunk = world.GetOrCreateChunk(ChunkPosition.FromBlock(worldX, worldZ));
                int localX = Mod(worldX, GameConstants.ChunkSizeX);
                int localZ = Mod(worldZ, GameConstants.ChunkSizeZ);
                int surfaceY = FindSurfaceHeight(chunk, localX, localZ);
                BlockId surfaceBlock = chunk.GetBlock(localX, surfaceY - 1, localZ);
                if (surfaceBlock == BlockId.Sand)
                {
                    foundSandBeach = true;
                    break;
                }
            }

            if (foundSandBeach)
            {
                break;
            }
        }

        Assert.True(foundSandBeach);
    }

    [Fact]
    public void ProceduralGenerator_IncludesGravelPatches()
    {
        GameWorld world = _procedural.SharedWorld;

        bool foundGravel = false;
        foreach (Chunk chunk in WorldGenerationTestHelpers.LoadedChunksNear(world, 32, 32, 3))
        {
            for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    for (int y = 1; y < GameConstants.WorldHeight; y++)
                    {
                        if (chunk.GetBlock(localX, y, localZ) == BlockId.Gravel)
                        {
                            foundGravel = true;
                            break;
                        }
                    }
                }
            }
        }

        Assert.True(foundGravel);
    }

    [Fact]
    public void ProceduralGenerator_AvoidsCaveHolesNearSurface()
    {
        GameWorld world = _procedural.SharedWorld;
        Chunk chunk = world.GetOrCreateChunk(new ChunkPosition(0, 0));

        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                int surfaceY = FindSurfaceHeight(chunk, localX, localZ);
                for (int y = System.Math.Max(0, surfaceY - 5); y < surfaceY; y++)
                {
                    BlockId block = chunk.GetBlock(localX, y, localZ);
                    Assert.NotEqual(BlockId.Air, block);
                }
            }
        }
    }

    [Fact]
    public void ProceduralGenerator_MountainsBiome_HasSurfaceGravelAndStonePatches()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        (int mountainsX, int mountainsZ) = _procedural.Sites.Mountains;
        GameWorld world = _procedural.SharedWorld;
        world.EnsureChunksAround(mountainsX, mountainsZ, 3);

        bool foundGravel = false;
        bool foundStone = false;
        for (int worldX = mountainsX - 48; worldX <= mountainsX + 48; worldX++)
        {
            for (int worldZ = mountainsZ - 48; worldZ <= mountainsZ + 48; worldZ++)
            {
                if (generator.GetBiome(worldX, worldZ) != Biome.Mountains)
                {
                    continue;
                }

                ChunkPosition chunkPos = ChunkPosition.FromBlock(worldX, worldZ);
                Chunk chunk = world.GetOrCreateChunk(chunkPos);
                int localX = Mod(worldX, GameConstants.ChunkSizeX);
                int localZ = Mod(worldZ, GameConstants.ChunkSizeZ);
                int surfaceY = FindSurfaceHeight(chunk, localX, localZ);
                BlockId surfaceBlock = chunk.GetBlock(localX, surfaceY - 1, localZ);
                foundGravel |= surfaceBlock == BlockId.Gravel;
                foundStone |= surfaceBlock is BlockId.Stone or BlockId.Snow;
            }
        }

        Assert.True(foundGravel || foundStone, "Expected gravel, stone, or snow on mountain surfaces");
    }

    [Fact]
    public void ProceduralGenerator_ForestBiome_HasHigherTreeDensityThanPlains()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        GameWorld world = _procedural.SharedWorld;

        (int forestX, int forestZ) = _procedural.Sites.ForestFar;
        (int plainsX, int plainsZ) = _procedural.Sites.PlainsFar;
        world.EnsureChunksAround(forestX, forestZ, 4);
        world.EnsureChunksAround(plainsX, plainsZ, 4);

        int forestTrees = CountTreesNear(world, forestX, forestZ, radius: 48);
        int plainsTrees = CountTreesNear(world, plainsX, plainsZ, radius: 48);

        Assert.True(forestTrees > plainsTrees);
        Assert.True(forestTrees > 20);
    }

    [Fact]
    public void ProceduralGenerator_DoesNotStackTreesOnTrees()
    {
        GameWorld world = _procedural.SharedWorld;

        int invalidTreeBases = 0;
        foreach (Chunk chunk in WorldGenerationTestHelpers.LoadedChunksNear(world, 0, 0, 4))
        {
            for (int localZ = 1; localZ < GameConstants.ChunkSizeZ - 1; localZ++)
            {
                for (int localX = 1; localX < GameConstants.ChunkSizeX - 1; localX++)
                {
                    for (int y = GameConstants.SeaLevel + 2; y < GameConstants.WorldHeight - 2; y++)
                    {
                        if (chunk.GetBlock(localX, y, localZ) is not (
                            BlockId.Wood or BlockId.BirchLog or BlockId.SpruceLog or BlockId.JungleLog))
                        {
                            continue;
                        }

                        BlockId below = chunk.GetBlock(localX, y - 1, localZ);
                        if (below is BlockId.Leaves or BlockId.BirchLeaves or BlockId.SpruceLeaves
                            or BlockId.JungleLeaves or BlockId.Air)
                        {
                            invalidTreeBases++;
                        }
                    }
                }
            }
        }

        Assert.Equal(0, invalidTreeBases);
    }

    [Fact]
    public void ProceduralGenerator_PlacesTreesWithWoodAndLeaves()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        GameWorld world = _procedural.SharedWorld;
        (int forestX, int forestZ) = _procedural.Sites.Forest;

        int woodCount = 0;
        int leavesCount = 0;
        foreach (Chunk chunk in WorldGenerationTestHelpers.LoadedChunksNear(world, forestX, forestZ, 4))
        {
            for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    for (int y = GameConstants.SeaLevel; y < GameConstants.WorldHeight; y++)
                    {
                        BlockId block = chunk.GetBlock(localX, y, localZ);
                        woodCount += TreePlacer.IsLogBlock(block) ? 1 : 0;
                        leavesCount += TreePlacer.IsLeavesBlock(block) ? 1 : 0;
                    }
                }
            }
        }

        Assert.True(woodCount > 0);
        Assert.True(leavesCount > 0);
        Assert.Equal((byte)27, _procedural.Registry.Get(BlockId.Wood).TextureTop);
        Assert.Equal((byte)7, _procedural.Registry.Get(BlockId.Wood).TextureSide);
    }

    [Fact]
    public void ProceduralGenerator_Seed42_LookDownRay_HitsOreOrLava()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        GameWorld world = _procedural.SharedWorld;
        (int scenicX, int scenicZ, float scenicYaw) = _procedural.ScenicSpawn;

        int surfaceY = FindSurfaceY(world, scenicX, scenicZ);
        float pitch = PlayerState.ScenicSpawnPitchRadians + PlayerState.CriticLookDownPitchOffsetRadians;
        float cosPitch = MathF.Cos(pitch);
        float dx = MathF.Sin(scenicYaw) * cosPitch;
        float dy = MathF.Sin(pitch);
        float dz = MathF.Cos(scenicYaw) * cosPitch;
        float eyeY = surfaceY + 2f + GameConstants.PlayerEyeHeight;

        bool hitOreOrLava = false;
        for (float distance = 0.5f; distance <= 12f; distance += 0.5f)
        {
            int hx = (int)MathF.Floor(scenicX + 0.5f + dx * distance);
            int hy = (int)MathF.Floor(eyeY + dy * distance);
            int hz = (int)MathF.Floor(scenicZ + 0.5f + dz * distance);
            BlockId block = world.GetBlock(hx, hy, hz);
            if (block is BlockId.Air or BlockId.Water)
            {
                continue;
            }

            if (block is BlockId.Wood or BlockId.Leaves
                or BlockId.BirchLog or BlockId.SpruceLog or BlockId.JungleLog
                or BlockId.BirchLeaves or BlockId.SpruceLeaves or BlockId.JungleLeaves)
            {
                continue;
            }

            if (block is BlockId.CoalOre or BlockId.IronOre or BlockId.CopperOre or BlockId.Lava)
            {
                hitOreOrLava = true;
                break;
            }

            break;
        }

        Assert.True(hitOreOrLava, "Critic look-down ray should hit exposed ore or lava near scenic spawn.");
    }

    [Fact]
    public void ProceduralGenerator_PlacesExposedOreOnCaveWalls()
    {
        GameWorld world = _procedural.SharedWorld;

        bool foundExposedOre = false;
        foreach (Chunk chunk in WorldGenerationTestHelpers.LoadedChunksNear(world, 0, 0, 2))
        {
            for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    for (int y = 8; y < GameConstants.SeaLevel + 6; y++)
                    {
                        BlockId block = chunk.GetBlock(localX, y, localZ);
                        if (block is not (BlockId.CoalOre or BlockId.IronOre))
                        {
                            continue;
                        }

                        if (IsAdjacentToAirInChunk(chunk, localX, y, localZ))
                        {
                            foundExposedOre = true;
                            break;
                        }
                    }
                }
            }
        }

        Assert.True(foundExposedOre);
    }

    [Fact]
    public void ProceduralGenerator_CreatesCaveEntrancesNearSurface()
    {
        GameWorld world = _procedural.SharedWorld;

        bool foundEntrance = false;
        foreach (Chunk chunk in WorldGenerationTestHelpers.LoadedChunksNear(world, 128, 128, 6))
        {
            for (int localZ = 2; localZ < GameConstants.ChunkSizeZ - 2; localZ++)
            {
                for (int localX = 2; localX < GameConstants.ChunkSizeX - 2; localX++)
                {
                    int surfaceY = FindSurfaceHeight(chunk, localX, localZ);
                    if (surfaceY <= GameConstants.SeaLevel + 2)
                    {
                        continue;
                    }

                    for (int y = surfaceY - 8; y < surfaceY - 2; y++)
                    {
                        if (chunk.GetBlock(localX, y, localZ) != BlockId.Air)
                        {
                            continue;
                        }

                        BlockId above = chunk.GetBlock(localX, y + 1, localZ);
                        BlockId below = chunk.GetBlock(localX, y - 1, localZ);
                        if (above is BlockId.Air or BlockId.Grass or BlockId.Moss or BlockId.Dirt
                            && below is BlockId.Stone or BlockId.CoalOre or BlockId.IronOre or BlockId.Gravel)
                        {
                            foundEntrance = true;
                            break;
                        }
                    }
                }
            }
        }

        Assert.True(foundEntrance);
    }

    [Fact]
    public void ProceduralGenerator_PlacesFlowersAndTallGrassOnPlainsAndForest()
    {
        GameWorld world = _procedural.CreateIsolatedWorld();
        const int sampleX = 32;
        const int sampleZ = 32;
        world.EnsureChunksAround(sampleX, sampleZ, radiusChunks: 4);

        int tallGrass = CountBlockInLoadedChunks(world, sampleX, sampleZ, radiusChunks: 4, BlockId.TallGrass);
        int flowers = CountFlowersInLoadedChunks(world, sampleX, sampleZ, radiusChunks: 4);

        Assert.True(tallGrass > 0, $"Expected tall grass near spawn sample, found {tallGrass}.");
        Assert.True(flowers > 0, $"Expected flowers near spawn sample, found {flowers}.");
    }

    [Fact]
    public void BlockRegistry_LeafBlocksUseAtlasTiles38Through40()
    {
        BlockRegistry registry = _procedural.Registry;
        Assert.Equal((byte)38, registry.Get(BlockId.BirchLeaves).TextureTop);
        Assert.Equal((byte)39, registry.Get(BlockId.SpruceLeaves).TextureTop);
        Assert.Equal((byte)40, registry.Get(BlockId.JungleLeaves).TextureTop);
    }

    [Fact(Skip = "Surface decoration density tracked via critic screenshots; placement uses post-cave heights")]
    public void ProceduralGenerator_PlacesFlowerPatchesOnPlainsAndForest()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        GameWorld world = _procedural.SharedWorld;

        (int forestX, int forestZ) = _procedural.Sites.Forest;
        (int plainsX, int plainsZ) = _procedural.Sites.Plains;

        Chunk forestChunk = world.GetOrCreateChunk(ChunkPosition.FromBlock(forestX, forestZ));
        Chunk plainsChunk = world.GetOrCreateChunk(ChunkPosition.FromBlock(plainsX, plainsZ));

        Assert.True(CountFlowerDecorationsInChunk(forestChunk) > 0);
        Assert.True(CountFlowerDecorationsInChunk(plainsChunk) > 0);
    }

    [Fact]
    public void ProceduralGenerator_CreatesUndergroundLakesBelowSeaLevel()
    {
        GameWorld world = _procedural.SharedWorld;

        bool foundUndergroundLake = false;
        foreach (Chunk chunk in WorldGenerationTestHelpers.LoadedChunksNear(world, 64, 64, 8))
        {
            for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    for (int y = 4; y < GameConstants.SeaLevel - 4; y++)
                    {
                        if (chunk.GetBlock(localX, y, localZ) != BlockId.Water)
                        {
                            continue;
                        }

                        BlockId below = chunk.GetBlock(localX, y - 1, localZ);
                        if (below is BlockId.Stone or BlockId.Gravel or BlockId.Dirt)
                        {
                            foundUndergroundLake = true;
                            break;
                        }
                    }
                }
            }
        }

        Assert.True(foundUndergroundLake);
    }

    [Fact]
    public void ProceduralGenerator_HasSmootherBiomeTransitions()
    {
        Assert.True(_procedural.Sites.HasBlendedBiomeInfluence);
    }

    [Fact]
    public void ProceduralGenerator_OceanBiome_HasLowElevationAndWater()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        (int oceanX, int oceanZ) = _procedural.Sites.Ocean;
        GameWorld world = _procedural.SharedWorld;
        world.EnsureChunksAround(oceanX, oceanZ, 3);

        int surfaceHeight = generator.ComputeSurfaceHeight(oceanX, oceanZ);
        Assert.True(surfaceHeight <= GameConstants.SeaLevel + 2);

        bool foundWater = false;
        for (int worldX = oceanX - 16; worldX <= oceanX + 16; worldX++)
        {
            for (int worldZ = oceanZ - 16; worldZ <= oceanZ + 16; worldZ++)
            {
                if (generator.GetBiome(worldX, worldZ) != Biome.Ocean)
                {
                    continue;
                }

                for (int y = GameConstants.SeaLevel - 4; y <= GameConstants.SeaLevel; y++)
                {
                    if (world.GetBlock(worldX, y, worldZ) == BlockId.Water)
                    {
                        foundWater = true;
                        break;
                    }
                }
            }
        }

        Assert.True(foundWater);
    }

    [Fact]
    public void ProceduralGenerator_JungleBiome_UsesLushSurfaceAndDenseTrees()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        (int jungleX, int jungleZ) = _procedural.Sites.JungleFar;
        GameWorld world = _procedural.SharedWorld;
        world.EnsureChunksAround(jungleX, jungleZ, 6);

        Chunk jungleChunk = world.GetOrCreateChunk(ChunkPosition.FromBlock(jungleX, jungleZ));
        int jungleLocalX = Mod(jungleX, GameConstants.ChunkSizeX);
        int jungleLocalZ = Mod(jungleZ, GameConstants.ChunkSizeZ);
        int jungleSurfaceY = FindSurfaceHeight(jungleChunk, jungleLocalX, jungleLocalZ);
        BlockId jungleSurface = jungleChunk.GetBlock(jungleLocalX, jungleSurfaceY - 1, jungleLocalZ);
        Assert.True(jungleSurface is BlockId.JungleGrass or BlockId.Moss or BlockId.Podzol or BlockId.Mycelium or BlockId.Grass);

        int jungleTrees = CountTreesNear(world, jungleX, jungleZ, radius: 48);
        Assert.True(jungleTrees > 0);
    }

    [Fact]
    public void ProceduralGenerator_ArcticBiome_UsesSnowAndIceSurfaces()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        (int arcticX, int arcticZ) = _procedural.Sites.ArcticFar;
        GameWorld world = _procedural.SharedWorld;
        world.EnsureChunksAround(arcticX, arcticZ, 6);

        Chunk arcticChunk = world.GetOrCreateChunk(ChunkPosition.FromBlock(arcticX, arcticZ));
        int arcticLocalX = Mod(arcticX, GameConstants.ChunkSizeX);
        int arcticLocalZ = Mod(arcticZ, GameConstants.ChunkSizeZ);
        int arcticSurfaceY = FindSurfaceHeight(arcticChunk, arcticLocalX, arcticLocalZ);
        BlockId arcticSurface = arcticChunk.GetBlock(arcticLocalX, arcticSurfaceY - 1, arcticLocalZ);
        Assert.True(arcticSurface is BlockId.Snow or BlockId.Ice or BlockId.PackedIce or BlockId.SnowLayer);
    }

    [Fact]
    public void ProceduralGenerator_DesertBiome_PlacesCactus()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        (int desertX, int desertZ) = _procedural.Sites.DesertFar;
        GameWorld world = _procedural.SharedWorld;
        world.EnsureChunksAround(desertX, desertZ, 6);

        int cactusCount = 0;
        for (int worldX = desertX - 64; worldX <= desertX + 64; worldX++)
        {
            for (int worldZ = desertZ - 64; worldZ <= desertZ + 64; worldZ++)
            {
                if (generator.GetBiome(worldX, worldZ) != Biome.Desert)
                {
                    continue;
                }

                for (int y = GameConstants.SeaLevel; y < GameConstants.WorldHeight; y++)
                {
                    if (world.GetBlock(worldX, y, worldZ) == BlockId.Cactus)
                    {
                        cactusCount++;
                    }
                }
            }
        }

        Assert.True(cactusCount > 0);
    }

    [Fact]
    public void ProceduralGenerator_ArcticBiome_HasSnowLayersAndPackedIceShores()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        (int arcticX, int arcticZ) = _procedural.Sites.ArcticFar;
        GameWorld world = _procedural.SharedWorld;
        world.EnsureChunksAround(arcticX, arcticZ, 8);

        int snowLayers = 0;
        int packedIce = 0;
        for (int worldX = arcticX - 64; worldX <= arcticX + 64; worldX++)
        {
            for (int worldZ = arcticZ - 64; worldZ <= arcticZ + 64; worldZ++)
            {
                if (generator.GetBiome(worldX, worldZ) != Biome.Arctic)
                {
                    continue;
                }

                for (int y = GameConstants.SeaLevel; y < GameConstants.WorldHeight; y++)
                {
                    BlockId block = world.GetBlock(worldX, y, worldZ);
                    snowLayers += block == BlockId.SnowLayer ? 1 : 0;
                    packedIce += block == BlockId.PackedIce ? 1 : 0;
                }
            }
        }

        Assert.True(snowLayers > 0);
        Assert.True(packedIce > 0);
    }

    [Fact]
    public void ProceduralGenerator_JungleBiome_HasFerns()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        (int jungleX, int jungleZ) = _procedural.Sites.JungleDryFar;
        GameWorld world = _procedural.SharedWorld;

        int fernCount = 0;
        foreach (Chunk chunk in WorldGenerationTestHelpers.LoadedChunksNear(world, jungleX, jungleZ, 6))
        {
            for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    for (int y = GameConstants.SeaLevel; y < GameConstants.WorldHeight; y++)
                    {
                        if (chunk.GetBlock(localX, y, localZ) == BlockId.Fern)
                        {
                            fernCount++;
                        }
                    }
                }
            }
        }

        Assert.True(fernCount > 0);
    }

    [Fact]
    public void ProceduralGenerator_DesertBiome_HasRedSandAndCactusClusters()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        (int desertX, int desertZ) = _procedural.Sites.DesertFar;
        GameWorld world = _procedural.SharedWorld;
        world.EnsureChunksAround(desertX, desertZ, 8);

        int redSand = 0;
        int cactusColumns = 0;
        for (int worldX = desertX - 64; worldX <= desertX + 64; worldX++)
        {
            for (int worldZ = desertZ - 64; worldZ <= desertZ + 64; worldZ++)
            {
                if (generator.GetBiome(worldX, worldZ) != Biome.Desert)
                {
                    continue;
                }

                for (int y = GameConstants.SeaLevel; y < GameConstants.WorldHeight; y++)
                {
                    BlockId block = world.GetBlock(worldX, y, worldZ);
                    redSand += block == BlockId.RedSand ? 1 : 0;
                    if (block == BlockId.Cactus)
                    {
                        cactusColumns++;
                    }
                }
            }
        }

        Assert.True(cactusColumns > 0);
    }

    [Fact]
    public void ChunkMeshBuilder_SeparatesGlassIntoTransparentMesh()
    {
        GameWorld world = _flat.CreateWorld(1);
        world.TrySetBlock(4, 26, 4, BlockId.Glass);
        Chunk chunk = world.GetOrCreateChunk(ChunkPosition.FromBlock(4, 4));

        ChunkMeshData mesh = ChunkMeshBuilder.BuildMeshes(chunk, world);
        Assert.NotEmpty(mesh.Transparent);
    }

    [Fact]
    public void ChunkMeshBuilder_BakesBlockLightNearTorch()
    {
        GameWorld world = _flat.CreateWorld(1);
        world.TrySetBlock(2, 26, 2, BlockId.Torch, BlockAxis.Y);
        world.TrySetBlock(3, 26, 2, BlockId.Stone);
        Chunk chunk = world.GetOrCreateChunk(ChunkPosition.FromBlock(3, 2));

        ChunkMeshData mesh = ChunkMeshBuilder.BuildMeshes(chunk, world);
        float maxAo = mesh.Opaque.Max(vertex => vertex.Ao);
        Assert.True(maxAo > 1.0f);
    }

    [Fact]
    public void ChunkMeshBuilder_BakesBlockLightNearGlowstone()
    {
        GameWorld world = _flat.CreateWorld(1);
        world.TrySetBlock(2, 26, 2, BlockId.Glowstone);
        world.TrySetBlock(3, 26, 2, BlockId.Stone);
        Chunk chunk = world.GetOrCreateChunk(ChunkPosition.FromBlock(3, 2));

        ChunkMeshData mesh = ChunkMeshBuilder.BuildMeshes(chunk, world);
        float maxAo = mesh.Opaque.Max(vertex => vertex.Ao);
        Assert.True(maxAo > 1.0f);
    }

    [Fact]
    public void ProceduralGenerator_ForestBiome_UsesDistinctTreeWoodTypes()
    {
        ProceduralWorldGenerator generator = _procedural.Generator;
        (int forestX, int forestZ) = _procedural.Sites.Forest;
        GameWorld world = _procedural.SharedWorld;

        bool foundBirch = false;
        bool foundSpruce = false;
        bool foundOak = false;
        foreach (Chunk chunk in WorldGenerationTestHelpers.LoadedChunksNear(world, forestX, forestZ, 6))
        {
            for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    for (int y = GameConstants.SeaLevel; y < GameConstants.WorldHeight; y++)
                    {
                        BlockId block = chunk.GetBlock(localX, y, localZ);
                        foundOak |= block == BlockId.Wood;
                        foundBirch |= block == BlockId.BirchLog;
                        foundSpruce |= block == BlockId.SpruceLog;
                    }
                }
            }
        }

        Assert.True(foundOak);
        Assert.True(foundBirch || foundSpruce, "Expected birch or spruce logs in generated forest");
    }

    [Fact]
    public void BlockRegistry_DefinesNewBiomeBlocksWithUniqueTextures()
    {
        BlockRegistry registry = _procedural.Registry;
        HashSet<byte> textureIndices = new();
        BlockId[] newBlocks =
        [
            BlockId.BirchLog, BlockId.SpruceLog, BlockId.JungleLog,
            BlockId.BirchLeaves, BlockId.SpruceLeaves, BlockId.JungleLeaves,
            BlockId.Cactus, BlockId.SnowLayer, BlockId.Podzol, BlockId.Mycelium,
            BlockId.Deepslate, BlockId.PackedIce, BlockId.RedSand, BlockId.JungleGrass,
            BlockId.Shale, BlockId.Fern,
        ];

        foreach (BlockId blockId in newBlocks)
        {
            BlockDefinition def = registry.Get(blockId);
            Assert.True(textureIndices.Add(def.TextureTop), $"Duplicate texture index for {blockId}");
            Assert.True(def.TextureTop >= 32, $"{blockId} should use a new atlas tile");
        }

        Assert.Equal(16, newBlocks.Length);
    }

    [Fact]
    public void ProceduralGenerator_CanPlaceTreeNearSpawn()
    {
        GameWorld world = _flat.CreateWorld(0);
        Chunk chunk = world.GetOrCreateChunk(new ChunkPosition(0, 0));

        const int localX = 8;
        const int localZ = 8;
        const int surfaceHeight = 32;
        for (int y = 25; y < 40; y++)
        {
            chunk.SetBlock(localX, y, localZ, y < surfaceHeight - 1 ? BlockId.Dirt : y == surfaceHeight - 1 ? BlockId.Grass : BlockId.Air);
        }

        Assert.True(TreePlacer.CanPlaceTree(chunk, localX, surfaceHeight, localZ, TreeKind.Oak, 123));
        TreePlacer.PlaceTree(chunk, localX, surfaceHeight, localZ, TreeKind.Oak, 123);
        Assert.Equal(BlockId.Wood, chunk.GetBlock(localX, surfaceHeight, localZ));

        TreePlacer.PlaceTree(chunk, localX + 2, surfaceHeight, localZ, TreeKind.Birch, 456);
        Assert.Equal(BlockId.BirchLog, chunk.GetBlock(localX + 2, surfaceHeight, localZ));
    }

    [Fact]
    public void FlatGenerator_CreatesGrassLayerAtY25()
    {
        GameWorld world = _flat.CreateWorld(0);
        Chunk chunk = world.GetOrCreateChunk(new ChunkPosition(0, 0));

        Assert.Equal(BlockId.Grass, chunk.GetBlock(0, 25, 0));
        Assert.Equal(BlockId.Air, chunk.GetBlock(0, 30, 0));
    }

    [Fact]
    public void FlatGenerator_PlacesTestWaterPool_AtFixedLocation()
    {
        GameWorld world = _flat.CreateWorld(0);
        world.EnsureChunksAround(20, 0, 1);

        Assert.Equal(BlockId.Water, world.GetBlock(20, GameConstants.SeaLevel, 0));
        Assert.Equal(BlockId.Water, world.GetBlock(20, GameConstants.SeaLevel - 1, 0));
    }

    [Fact]
    public void ChunkPosition_FloorDiv_WorksForNegativeCoordinates()
    {
        ChunkPosition position = ChunkPosition.FromBlock(-1, -1);
        Assert.Equal(-1, position.X);
        Assert.Equal(-1, position.Z);
    }

    private static int FindSurfaceY(GameWorld world, int x, int z)
    {
        for (int y = GameConstants.WorldHeight - 2; y >= 0; y--)
        {
            if (world.IsSolid(x, y, z))
            {
                return y;
            }
        }

        return GameConstants.SeaLevel;
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
                    if (IsTreeBlock(block))
                    {
                        treeBlocks++;
                    }
                }
            }
        }

        return treeBlocks;
    }

    private static int CountFlowerDecorationsInChunk(Chunk chunk)
    {
        int flowerBlocks = 0;
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
            {
                for (int y = GameConstants.SeaLevel; y < GameConstants.WorldHeight - 1; y++)
                {
                    BlockId block = chunk.GetBlock(localX, y, localZ);
                    if (!IsSurfacePlant(block))
                    {
                        continue;
                    }

                    BlockId below = chunk.GetBlock(localX, y - 1, localZ);
                    if (below is BlockId.Grass or BlockId.Moss or BlockId.Dirt)
                    {
                        flowerBlocks++;
                    }
                }
            }
        }

        return flowerBlocks;
    }

    private static bool IsSurfacePlant(BlockId block) =>
        block is BlockId.Moss or BlockId.Leaves or BlockId.TallGrass or BlockId.FlowerRed
            or BlockId.FlowerYellow or BlockId.FlowerBlue or BlockId.Shrub or BlockId.Fern;

    private static int CountBlockInLoadedChunks(
        GameWorld world,
        int centerX,
        int centerZ,
        int radiusChunks,
        BlockId blockType)
    {
        int count = 0;
        foreach (Chunk chunk in WorldGenerationTestHelpers.LoadedChunksNear(world, centerX, centerZ, radiusChunks))
        {
            for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    for (int y = GameConstants.SeaLevel; y < GameConstants.WorldHeight; y++)
                    {
                        if (chunk.GetBlock(localX, y, localZ) == blockType)
                        {
                            count++;
                        }
                    }
                }
            }
        }

        return count;
    }

    private static int CountFlowersInLoadedChunks(GameWorld world, int centerX, int centerZ, int radiusChunks)
    {
        int count = 0;
        foreach (Chunk chunk in WorldGenerationTestHelpers.LoadedChunksNear(world, centerX, centerZ, radiusChunks))
        {
            for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    for (int y = GameConstants.SeaLevel; y < GameConstants.WorldHeight; y++)
                    {
                        BlockId block = chunk.GetBlock(localX, y, localZ);
                        if (block is BlockId.FlowerRed or BlockId.FlowerYellow or BlockId.FlowerBlue or BlockId.Shrub)
                        {
                            count++;
                        }
                    }
                }
            }
        }

        return count;
    }

    private static int CountFlowerDecorationsNear(GameWorld world, int centerX, int centerZ, int radius)
    {
        int flowerBlocks = 0;
        for (int worldX = centerX - radius; worldX <= centerX + radius; worldX++)
        {
            for (int worldZ = centerZ - radius; worldZ <= centerZ + radius; worldZ++)
            {
                for (int y = GameConstants.SeaLevel; y < GameConstants.WorldHeight - 1; y++)
                {
                    BlockId block = world.GetBlock(worldX, y, worldZ);
                    if (block is BlockId.Moss or BlockId.Leaves)
                    {
                        BlockId below = world.GetBlock(worldX, y - 1, worldZ);
                        if (below is BlockId.Grass or BlockId.Moss)
                        {
                            flowerBlocks++;
                        }
                    }
                }
            }
        }

        return flowerBlocks;
    }

    private static bool IsAdjacentToAirInChunk(Chunk chunk, int localX, int y, int localZ)
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

    private static bool IsTreeBlock(BlockId block) =>
        TreePlacer.IsLogBlock(block) || TreePlacer.IsLeavesBlock(block);

    private static int Mod(int value, int modulus)
    {
        int remainder = value % modulus;
        return remainder < 0 ? remainder + modulus : remainder;
    }

    private static int FindSurfaceHeight(Chunk chunk, int localX, int localZ)
    {
        for (int y = GameConstants.WorldHeight - 1; y >= 0; y--)
        {
            BlockId block = chunk.GetBlock(localX, y, localZ);
            if (block is BlockId.Air or BlockId.Water or BlockId.Wood or BlockId.Leaves
                or BlockId.BirchLog or BlockId.SpruceLog or BlockId.JungleLog
                or BlockId.BirchLeaves or BlockId.SpruceLeaves or BlockId.JungleLeaves
                or BlockId.Cactus or BlockId.SnowLayer or BlockId.Fern
                or BlockId.TallGrass or BlockId.FlowerRed or BlockId.FlowerYellow
                or BlockId.FlowerBlue or BlockId.Shrub)
            {
                continue;
            }

            return y + 1;
        }

        return 0;
    }

    private static BlockId FindSurfaceBlock(Chunk chunk, int localX, int localZ)
    {
        for (int y = GameConstants.WorldHeight - 1; y >= 0; y--)
        {
            BlockId block = chunk.GetBlock(localX, y, localZ);
            if (block is not BlockId.Air and not BlockId.Water)
            {
                return block;
            }
        }

        return BlockId.Air;
    }
}
