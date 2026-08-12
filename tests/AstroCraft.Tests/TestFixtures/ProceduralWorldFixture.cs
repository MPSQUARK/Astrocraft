using AstroCraft.Core.Blocks;
using AstroCraft.Core.World;
using AstroCraft.Core.World.Generation;

namespace AstroCraft.Tests.TestFixtures;

/// <summary>
/// Shared procedural world (seed 42). Backed by a process-wide singleton so multiple test
/// classes can run in parallel while reusing the same generated terrain.
/// </summary>
public sealed class ProceduralWorldFixture
{
    public const int DefaultSeed = 42;

    private static readonly Lazy<SharedState> Shared = new(CreateSharedState);

    public BlockRegistry Registry => Shared.Value.Registry;

    public ProceduralWorldGenerator Generator => Shared.Value.Generator;

    public GameWorld SharedWorld => Shared.Value.SharedWorld;

    internal BiomeSites Sites => Shared.Value.Sites;

    public (int X, int Z, float Yaw) ScenicSpawn => Shared.Value.ScenicSpawn;

    public GameWorld CreateIsolatedWorld() => new(Registry, new ProceduralWorldGenerator(DefaultSeed));

    public GameWorld CreateWorld(int seed) => new(Registry, new ProceduralWorldGenerator(seed));

    private static SharedState CreateSharedState()
    {
        BlockRegistry registry = BlockRegistry.CreateDefault();
        ProceduralWorldGenerator generator = new(DefaultSeed);
        GameWorld world = new(registry, generator);
        BiomeSites sites = ProceduralWorldBiomeDiscovery.Discover(generator);
        (int scenicX, int scenicZ, float scenicYaw) = generator.FindScenicSpawn(world);

        world.EnsureChunksAround(0, 0, 4);
        world.EnsureChunksAround(scenicX, scenicZ, 4);

        return new SharedState(registry, generator, world, sites, (scenicX, scenicZ, scenicYaw));
    }

    private sealed record SharedState(
        BlockRegistry Registry,
        ProceduralWorldGenerator Generator,
        GameWorld SharedWorld,
        BiomeSites Sites,
        (int X, int Z, float Yaw) ScenicSpawn);
}
