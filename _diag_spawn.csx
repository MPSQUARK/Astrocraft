using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.World;
using AstroCraft.Core.World.Generation;

ProceduralWorldGenerator gen = new(42);
BlockRegistry reg = BlockRegistry.CreateDefault();
GameWorld world = new(reg, gen);

for (int r = 0; r <= 64; r += 8) {
  for (int a = 0; a < 360; a += 45) {
    int x = (int)(r * Math.Cos(a * Math.PI / 180));
    int z = (int)(r * Math.Sin(a * Math.PI / 180));
    var biome = gen.GetBiome(x, z);
    int h = gen.ComputeSurfaceHeight(x, z);
    world.EnsureChunksAround(x, z, 2);
    var surf = world.GetBlock(x, h-1, z);
    int trees = 0;
    for (int dx=-8; dx<=8; dx++)
      for (int dz=-8; dz<=8; dz++)
        for (int y=GameConstants.SeaLevel; y<GameConstants.WorldHeight; y++)
          if (world.GetBlock(x+dx,y,z+dz) is BlockId.Wood or BlockId.Leaves) trees++;
    if (biome == Biome.Forest && surf == BlockId.Grass && trees > 5)
      Console.WriteLine($"({x},{z}) biome={biome} h={h} surf={surf} trees={trees}");
  }
}
