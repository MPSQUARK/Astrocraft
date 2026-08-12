using AstroCraft.Core.Blocks;
using AstroCraft.Core.World;
using AstroCraft.Core.World.Generation;

namespace AstroCraft.Tests.TestFixtures;

/// <summary>
/// Shared block registry and flat world generator for tests that need a simple terrain setup.
/// Each test should call <see cref="CreateWorld"/> for an isolated world instance.
/// </summary>
public sealed class FlatWorldFixture
{
    public BlockRegistry Registry { get; } = BlockRegistry.CreateDefault();

    private readonly FlatWorldGenerator _generator = new();

    public GameWorld CreateWorld(int chunkRadius = 2)
    {
        GameWorld world = new(Registry, _generator);
        if (chunkRadius > 0)
        {
            world.EnsureChunksAround(0, 0, chunkRadius);
        }

        return world;
    }
}
