using AstroCraft.Core.World.Generation;
var g = new ProceduralWorldGenerator(42);
foreach (var (x,z) in new[] {(0,0),(8,8),(15,0),(0,15),(32,32),(48,48)})
    Console.WriteLine($"({x},{z}) = {g.ComputeSurfaceHeight(x,z)}");
