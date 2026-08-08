using AstroCraft.Core.Blocks;
using AstroCraft.Core.World;

namespace AstroCraft.Core.Rendering;

public readonly record struct BlockVertex(float X, float Y, float Z, float U, float V, byte TextureIndex);

public static class ChunkMeshBuilder
{
    private static readonly (int Dx, int Dy, int Dz)[] FaceDirections =
    [
        (1, 0, 0),
        (-1, 0, 0),
        (0, 1, 0),
        (0, -1, 0),
        (0, 0, 1),
        (0, 0, -1),
    ];

    public static BlockVertex[] BuildMesh(Chunk chunk, GameWorld world)
    {
        List<BlockVertex> vertices = new();
        int baseX = chunk.Position.X * GameConstants.ChunkSizeX;
        int baseZ = chunk.Position.Z * GameConstants.ChunkSizeZ;

        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localY = 0; localY < GameConstants.ChunkSizeY; localY++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    BlockId blockId = chunk.GetBlock(localX, localY, localZ);
                    if (blockId == BlockId.Air)
                    {
                        continue;
                    }

                    BlockDefinition definition = world.BlockRegistry.Get(blockId);
                    int worldX = baseX + localX;
                    int worldZ = baseZ + localZ;
                    AddBlockFaces(vertices, world, definition, worldX, localY, worldZ);
                }
            }
        }

        return vertices.ToArray();
    }

    private static void AddBlockFaces(
        List<BlockVertex> vertices,
        GameWorld world,
        BlockDefinition definition,
        int worldX,
        int worldY,
        int worldZ)
    {
        for (int face = 0; face < FaceDirections.Length; face++)
        {
            (int dx, int dy, int dz) = FaceDirections[face];
            BlockId neighbor = world.GetBlock(worldX + dx, worldY + dy, worldZ + dz);
            if (!world.BlockRegistry.IsTransparent(neighbor) && neighbor != BlockId.Air)
            {
                continue;
            }

            byte texture = face switch
            {
                2 => definition.TextureTop,
                3 => definition.TextureBottom,
                _ => definition.TextureSide,
            };
            AddFace(vertices, worldX, worldY, worldZ, face, texture);
        }
    }

    private static void AddFace(List<BlockVertex> vertices, int x, int y, int z, int faceIndex, byte texture)
    {
        switch (faceIndex)
        {
            case 0:
                AddQuad(vertices, (x + 1, y, z), (x + 1, y + 1, z), (x + 1, y + 1, z + 1), (x + 1, y, z + 1), texture);
                break;
            case 1:
                AddQuad(vertices, (x, y, z), (x, y, z + 1), (x, y + 1, z + 1), (x, y + 1, z), texture);
                break;
            case 2:
                AddQuad(vertices, (x, y + 1, z), (x, y + 1, z + 1), (x + 1, y + 1, z + 1), (x + 1, y + 1, z), texture);
                break;
            case 3:
                AddQuad(vertices, (x, y, z + 1), (x, y, z), (x + 1, y, z), (x + 1, y, z + 1), texture);
                break;
            case 4:
                AddQuad(vertices, (x, y, z + 1), (x + 1, y, z + 1), (x + 1, y + 1, z + 1), (x, y + 1, z + 1), texture);
                break;
            case 5:
                AddQuad(vertices, (x + 1, y, z), (x, y, z), (x, y + 1, z), (x + 1, y + 1, z), texture);
                break;
        }
    }

    private static void AddQuad(
        List<BlockVertex> vertices,
        (int X, int Y, int Z) a,
        (int X, int Y, int Z) b,
        (int X, int Y, int Z) c,
        (int X, int Y, int Z) d,
        byte texture)
    {
        vertices.Add(new BlockVertex(a.X, a.Y, a.Z, 0, 0, texture));
        vertices.Add(new BlockVertex(b.X, b.Y, b.Z, 0, 1, texture));
        vertices.Add(new BlockVertex(c.X, c.Y, c.Z, 1, 1, texture));
        vertices.Add(new BlockVertex(a.X, a.Y, a.Z, 0, 0, texture));
        vertices.Add(new BlockVertex(c.X, c.Y, c.Z, 1, 1, texture));
        vertices.Add(new BlockVertex(d.X, d.Y, d.Z, 1, 0, texture));
    }
}
