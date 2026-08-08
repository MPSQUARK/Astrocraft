using AstroCraft.Core.World;
using AstroCraft.Core.World.Generation;

namespace AstroCraft.Client.World;

public sealed class ClientEmptyWorldGenerator : IWorldGenerator
{
    public void GenerateChunk(GameWorld world, Chunk chunk) => chunk.IsDirty = false;
}
