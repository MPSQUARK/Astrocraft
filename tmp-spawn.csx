using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.World;
using AstroCraft.Core.World.Generation;

ProceduralWorldGenerator generator = new(42);
BlockRegistry registry = BlockRegistry.CreateDefault();
GameWorld world = new(registry, generator);
var (x, z, yaw) = generator.FindScenicSpawn(world);
world.EnsureChunksAround(x, z, 4);
int surfaceY = 0;
BlockId surface = BlockId.Air;
for (int y = GameConstants.WorldHeight - 2; y >= 0; y--)
{
    if (world.IsSolid(x, y, z)) { surfaceY = y; surface = world.GetBlock(x, y, z); break; }
}
int trees = 0;
for (int wx = x-24; wx <= x+24; wx++)
for (int wz = z-24; wz <= z+24; wz++)
for (int y = GameConstants.SeaLevel; y < GameConstants.WorldHeight; y++)
{
    var b = world.GetBlock(wx,y,wz);
    if (b is BlockId.Wood or BlockId.Leaves) trees++;
}
Console.WriteLine($"Spawn: ({x}, {z}) surface={surface} y={surfaceY} biome={generator.GetBiome(x,z)} trees={trees} yaw={yaw:F3}");
