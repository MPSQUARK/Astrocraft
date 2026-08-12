using AstroCraft.Core.Blocks;
using AstroCraft.Core.World;

namespace AstroCraft.Core.Rendering;

public readonly record struct BlockVertex(
    float X, float Y, float Z,
    float U, float V,
    byte TextureIndex,
    float Nx, float Ny, float Nz,
    float Ao,
    float WindWeight = 0f);

public readonly record struct ChunkMeshData(BlockVertex[] Opaque, BlockVertex[] Transparent);

// NOTE: All detail levels mesh every exposed face of every non-air block; none of them
// skip whole voxels. Lower detail levels only disable per-vertex ambient occlusion /
// block-light sampling to save CPU time on distant chunks. Skipping voxels here previously
// caused hollow terrain shells (see-through ground/cliffs) at distance, since a block can be
// covered from above yet still exposed on its sides or bottom (caves, overhangs, cliffs).
public enum ChunkMeshDetail
{
    Full,
    NoAmbientOcclusion,
    SurfaceColumns,
}

public static class ChunkMeshBuilder
{
    private const float BlockLightRadius = 6f;
    private const float BlockLightRadiusSq = BlockLightRadius * BlockLightRadius;
    private static readonly BlockRegistry s_blockRegistry = BlockRegistry.CreateDefault();

    private static readonly (int Dx, int Dy, int Dz)[] FaceDirections =
    [
        (1, 0, 0),
        (-1, 0, 0),
        (0, 1, 0),
        (0, -1, 0),
        (0, 0, 1),
        (0, 0, -1),
    ];

    // Per-face, per-vertex corner offsets (side1, side2, corner) relative to the solid block.
    private static readonly (int S1X, int S1Y, int S1Z, int S2X, int S2Y, int S2Z, int CX, int CY, int CZ)[][] FaceVertexAoOffsets =
    [
        // +X
        [
            (0, -1, 0, 0, 0, -1, 0, -1, -1),
            (0, 1, 0, 0, 0, -1, 0, 1, -1),
            (0, 1, 0, 0, 0, 1, 0, 1, 1),
            (0, -1, 0, 0, 0, 1, 0, -1, 1),
        ],
        // -X
        [
            (0, -1, 0, 0, 0, -1, 0, -1, -1),
            (0, 1, 0, 0, 0, -1, 0, 1, -1),
            (0, 1, 0, 0, 0, 1, 0, 1, 1),
            (0, -1, 0, 0, 0, 1, 0, -1, 1),
        ],
        // +Y
        [
            (-1, 0, 0, 0, 0, -1, -1, 0, -1),
            (-1, 0, 0, 0, 0, 1, -1, 0, 1),
            (1, 0, 0, 0, 0, 1, 1, 0, 1),
            (1, 0, 0, 0, 0, -1, 1, 0, -1),
        ],
        // -Y
        [
            (-1, 0, 0, 0, 0, 1, -1, 0, 1),
            (-1, 0, 0, 0, 0, -1, -1, 0, -1),
            (1, 0, 0, 0, 0, -1, 1, 0, -1),
            (1, 0, 0, 0, 0, 1, 1, 0, 1),
        ],
        // +Z
        [
            (-1, 0, 0, 0, -1, 0, -1, -1, 0),
            (1, 0, 0, 0, -1, 0, 1, -1, 0),
            (1, 0, 0, 0, 1, 0, 1, 1, 0),
            (-1, 0, 0, 0, 1, 0, -1, 1, 0),
        ],
        // -Z
        [
            (1, 0, 0, 0, -1, 0, 1, -1, 0),
            (-1, 0, 0, 0, -1, 0, -1, -1, 0),
            (-1, 0, 0, 0, 1, 0, -1, 1, 0),
            (1, 0, 0, 0, 1, 0, 1, 1, 0),
        ],
    ];

    public static BlockVertex[] BuildMesh(Chunk chunk, GameWorld world, ChunkMeshDetail detail = ChunkMeshDetail.Full) =>
        Combine(BuildMeshes(chunk, world, detail));

    public static ChunkMeshData BuildMeshes(Chunk chunk, GameWorld world, ChunkMeshDetail detail = ChunkMeshDetail.Full)
    {
        List<BlockVertex> opaqueVertices = new(capacity: 4096);
        List<BlockVertex> transparentVertices = new(capacity: 512);
        int baseX = chunk.Position.X * GameConstants.ChunkSizeX;
        int baseZ = chunk.Position.Z * GameConstants.ChunkSizeZ;
        bool skipLighting = detail != ChunkMeshDetail.Full;
        bool skipCrossPlants = true;

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

                    int worldX = baseX + localX;
                    int worldZ = baseZ + localZ;

                    BlockDefinition definition = world.BlockRegistry.Get(blockId);
                    BlockAxis axis = chunk.GetBlockAxis(localX, localY, localZ);
                    List<BlockVertex> target = definition.IsTransparent ? transparentVertices : opaqueVertices;
                    if (definition.RenderShape == BlockRenderShape.CrossPlant)
                    {
                        if (!skipCrossPlants)
                        {
                            AddCrossPlant(target, world, definition, worldX, localY, worldZ, skipLighting);
                        }

                        continue;
                    }

                    AddBlockFaces(target, world, definition, worldX, localY, worldZ, axis, skipLighting);
                }
            }
        }

        return new ChunkMeshData(opaqueVertices.ToArray(), transparentVertices.ToArray());
    }

    public static ChunkMeshData BuildMeshes(ChunkMeshBuildSnapshot snapshot, ChunkMeshDetail detail = ChunkMeshDetail.Full)
    {
        List<BlockVertex> opaqueVertices = new(capacity: 4096);
        List<BlockVertex> transparentVertices = new(capacity: 512);
        int baseX = snapshot.Center.X * GameConstants.ChunkSizeX;
        int baseZ = snapshot.Center.Z * GameConstants.ChunkSizeZ;
        bool skipLighting = detail != ChunkMeshDetail.Full;
        bool skipCrossPlants = true;
        BlockRegistry registry = snapshot.BlockRegistry;

        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localY = 0; localY < GameConstants.ChunkSizeY; localY++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    BlockId blockId = snapshot.GetBlock(baseX + localX, localY, baseZ + localZ);
                    if (blockId == BlockId.Air)
                    {
                        continue;
                    }

                    int worldX = baseX + localX;
                    int worldZ = baseZ + localZ;

                    BlockDefinition definition = registry.Get(blockId);
                    BlockAxis axis = snapshot.GetBlockAxis(worldX, localY, worldZ);
                    List<BlockVertex> target = definition.IsTransparent ? transparentVertices : opaqueVertices;
                    if (definition.RenderShape == BlockRenderShape.CrossPlant)
                    {
                        if (!skipCrossPlants)
                        {
                            AddCrossPlant(target, snapshot, definition, worldX, localY, worldZ, skipLighting);
                        }

                        continue;
                    }

                    AddBlockFaces(target, snapshot, definition, worldX, localY, worldZ, axis, skipLighting);
                }
            }
        }

        return new ChunkMeshData(opaqueVertices.ToArray(), transparentVertices.ToArray());
    }

    private static BlockVertex[] Combine(ChunkMeshData meshData)
    {
        if (meshData.Transparent.Length == 0)
        {
            return meshData.Opaque;
        }

        if (meshData.Opaque.Length == 0)
        {
            return meshData.Transparent;
        }

        BlockVertex[] combined = new BlockVertex[meshData.Opaque.Length + meshData.Transparent.Length];
        meshData.Opaque.CopyTo(combined, 0);
        meshData.Transparent.CopyTo(combined, meshData.Opaque.Length);
        return combined;
    }

    private static void AddCrossPlant(
        List<BlockVertex> vertices,
        GameWorld world,
        BlockDefinition definition,
        int worldX,
        int worldY,
        int worldZ,
        bool skipLighting)
    {
        byte texture = definition.TextureTop;
        float height = definition.PlantHeight;
        float ao = skipLighting ? 1f : EncodeVertexLighting(0.92f, ComputeNearbyBlockLight(world, worldX, worldY, worldZ));
        AddCrossPlantQuads(vertices, worldX, worldY, worldZ, height, texture, ao);
    }

    private static void AddCrossPlant(
        List<BlockVertex> vertices,
        ChunkMeshBuildSnapshot snapshot,
        BlockDefinition definition,
        int worldX,
        int worldY,
        int worldZ,
        bool skipLighting)
    {
        byte texture = definition.TextureTop;
        float height = definition.PlantHeight;
        float ao = skipLighting ? 1f : EncodeVertexLighting(0.92f, ComputeNearbyBlockLight(snapshot, worldX, worldY, worldZ));
        AddCrossPlantQuads(vertices, worldX, worldY, worldZ, height, texture, ao);
    }

    private static void AddCrossPlantQuads(
        List<BlockVertex> vertices,
        int x,
        int y,
        int z,
        float height,
        byte texture,
        float ao)
    {
        float top = y + height;
        const float inset = 0.12f;

        AddPlantQuad(vertices, y, texture, ao,
            (x + inset, y, z + inset), (x + 1f - inset, y, z + 1f - inset),
            (x + 1f - inset, top, z + 1f - inset), (x + inset, top, z + inset));

        AddPlantQuad(vertices, y, texture, ao,
            (x + 1f - inset, y, z + inset), (x + inset, y, z + 1f - inset),
            (x + inset, top, z + 1f - inset), (x + 1f - inset, top, z + inset));
    }

    private static void AddPlantQuad(
        List<BlockVertex> vertices,
        int blockY,
        byte texture,
        float ao,
        (float X, float Y, float Z) bottomLeft,
        (float X, float Y, float Z) bottomRight,
        (float X, float Y, float Z) topRight,
        (float X, float Y, float Z) topLeft)
    {
        float nx = topRight.X - bottomLeft.X;
        float nz = topRight.Z - bottomLeft.Z;
        float len = MathF.Sqrt(nx * nx + nz * nz);
        if (len > 0.001f)
        {
            nx /= len;
            nz /= len;
        }

        void AddVert(float vx, float vy, float vz, float u, float v)
        {
            float wind = ComputeWindWeight(texture, blockY, vy);
            vertices.Add(new BlockVertex(vx, vy, vz, u, v, texture, nx, 0f, nz, ao, wind));
        }

        AddVert(bottomLeft.X, bottomLeft.Y, bottomLeft.Z, 0f, 1f);
        AddVert(bottomRight.X, bottomRight.Y, bottomRight.Z, 1f, 1f);
        AddVert(topRight.X, topRight.Y, topRight.Z, 1f, 0f);
        AddVert(bottomLeft.X, bottomLeft.Y, bottomLeft.Z, 0f, 1f);
        AddVert(topRight.X, topRight.Y, topRight.Z, 1f, 0f);
        AddVert(topLeft.X, topLeft.Y, topLeft.Z, 0f, 0f);
    }

    private static bool ShouldRenderFace(BlockRegistry registry, BlockId self, BlockId neighbor)
    {
        if (neighbor == BlockId.Air)
        {
            return true;
        }

        BlockDefinition neighborDef = registry.Get(neighbor);
        if (!neighborDef.IsSolid)
        {
            return true;
        }

        return registry.IsTransparent(neighbor);
    }

    private static void AddBlockFaces(
        List<BlockVertex> vertices,
        GameWorld world,
        BlockDefinition definition,
        int worldX,
        int worldY,
        int worldZ,
        BlockAxis axis,
        bool skipLighting)
    {
        BlockRegistry registry = world.BlockRegistry;
        for (int face = 0; face < FaceDirections.Length; face++)
        {
            (int dx, int dy, int dz) = FaceDirections[face];
            BlockId neighbor = world.GetBlock(worldX + dx, worldY + dy, worldZ + dz);
            if (!ShouldRenderFace(registry, definition.Id, neighbor))
            {
                continue;
            }

            byte texture = ResolveFaceTexture(definition, face, axis);
            AddFace(vertices, world, worldX, worldY, worldZ, face, texture, skipLighting);
        }
    }

    private static void AddBlockFaces(
        List<BlockVertex> vertices,
        ChunkMeshBuildSnapshot snapshot,
        BlockDefinition definition,
        int worldX,
        int worldY,
        int worldZ,
        BlockAxis axis,
        bool skipLighting)
    {
        BlockRegistry registry = snapshot.BlockRegistry;
        for (int face = 0; face < FaceDirections.Length; face++)
        {
            (int dx, int dy, int dz) = FaceDirections[face];
            BlockId neighbor = snapshot.GetBlock(worldX + dx, worldY + dy, worldZ + dz);
            if (!ShouldRenderFace(registry, definition.Id, neighbor))
            {
                continue;
            }

            byte texture = ResolveFaceTexture(definition, face, axis);
            AddFace(vertices, snapshot, worldX, worldY, worldZ, face, texture, skipLighting);
        }
    }

    private static byte ResolveFaceTexture(BlockDefinition definition, int faceIndex, BlockAxis axis)
    {
        if (definition.PlacementOrientation != BlockPlacementOrientation.AxisAligned)
        {
            return faceIndex switch
            {
                2 => definition.TextureTop,
                3 => definition.TextureBottom,
                _ => definition.TextureSide,
            };
        }

        return axis switch
        {
            BlockAxis.X => faceIndex switch
            {
                0 => definition.TextureTop,
                1 => definition.TextureBottom,
                _ => definition.TextureSide,
            },
            BlockAxis.Z => faceIndex switch
            {
                4 => definition.TextureTop,
                5 => definition.TextureBottom,
                _ => definition.TextureSide,
            },
            _ => faceIndex switch
            {
                2 => definition.TextureTop,
                3 => definition.TextureBottom,
                _ => definition.TextureSide,
            },
        };
    }

    private static bool IsOccluding(GameWorld world, int x, int y, int z)
    {
        BlockId block = world.GetBlock(x, y, z);
        return block != BlockId.Air && !world.BlockRegistry.IsTransparent(block);
    }

    private static bool IsOccluding(ChunkMeshBuildSnapshot snapshot, int x, int y, int z)
    {
        BlockId block = snapshot.GetBlock(x, y, z);
        return block != BlockId.Air && !snapshot.BlockRegistry.IsTransparent(block);
    }

    private static float ComputeVertexAo(
        GameWorld world,
        int blockX,
        int blockY,
        int blockZ,
        int s1X, int s1Y, int s1Z,
        int s2X, int s2Y, int s2Z,
        int cX, int cY, int cZ)
    {
        bool side1 = IsOccluding(world, blockX + s1X, blockY + s1Y, blockZ + s1Z);
        bool side2 = IsOccluding(world, blockX + s2X, blockY + s2Y, blockZ + s2Z);
        bool corner = IsOccluding(world, blockX + cX, blockY + cY, blockZ + cZ);

        if (side1 && side2)
        {
            return 0.32f;
        }

        int solidCount = (side1 ? 1 : 0) + (side2 ? 1 : 0) + (corner ? 1 : 0);
        return solidCount switch
        {
            0 => 1.0f,
            1 => 0.78f,
            2 => 0.55f,
            _ => 0.32f,
        };
    }

    private static float ComputeVertexAo(
        ChunkMeshBuildSnapshot snapshot,
        int blockX,
        int blockY,
        int blockZ,
        int s1X, int s1Y, int s1Z,
        int s2X, int s2Y, int s2Z,
        int cX, int cY, int cZ)
    {
        bool side1 = IsOccluding(snapshot, blockX + s1X, blockY + s1Y, blockZ + s1Z);
        bool side2 = IsOccluding(snapshot, blockX + s2X, blockY + s2Y, blockZ + s2Z);
        bool corner = IsOccluding(snapshot, blockX + cX, blockY + cY, blockZ + cZ);

        if (side1 && side2)
        {
            return 0.32f;
        }

        int solidCount = (side1 ? 1 : 0) + (side2 ? 1 : 0) + (corner ? 1 : 0);
        return solidCount switch
        {
            0 => 1.0f,
            1 => 0.78f,
            2 => 0.55f,
            _ => 0.32f,
        };
    }

    private static float ComputeNearbyBlockLight(GameWorld world, int worldX, int worldY, int worldZ)
    {
        float light = 0f;
        int minX = worldX - (int)BlockLightRadius;
        int maxX = worldX + (int)BlockLightRadius;
        int minY = System.Math.Max(0, worldY - (int)BlockLightRadius);
        int maxY = System.Math.Min(GameConstants.WorldHeight - 1, worldY + (int)BlockLightRadius);
        int minZ = worldZ - (int)BlockLightRadius;
        int maxZ = worldZ + (int)BlockLightRadius;

        for (int sampleZ = minZ; sampleZ <= maxZ; sampleZ++)
        {
            for (int sampleY = minY; sampleY <= maxY; sampleY++)
            {
                for (int sampleX = minX; sampleX <= maxX; sampleX++)
                {
                    BlockId block = world.GetBlock(sampleX, sampleY, sampleZ);
                    float emission = s_blockRegistry.GetLightEmission(block);
                    if (emission <= 0f)
                    {
                        continue;
                    }

                    float dx = sampleX - worldX;
                    float dy = sampleY - worldY;
                    float dz = sampleZ - worldZ;
                    float distanceSq = dx * dx + dy * dy + dz * dz;
                    if (distanceSq > BlockLightRadiusSq)
                    {
                        continue;
                    }

                    float falloff = 1f - MathF.Sqrt(distanceSq) / BlockLightRadius;
                    light = System.Math.Max(light, emission * falloff);
                }
            }
        }

        return System.Math.Clamp(light, 0f, 0.45f);
    }

    private static float ComputeNearbyBlockLight(ChunkMeshBuildSnapshot snapshot, int worldX, int worldY, int worldZ)
    {
        float light = 0f;
        int minX = worldX - (int)BlockLightRadius;
        int maxX = worldX + (int)BlockLightRadius;
        int minY = System.Math.Max(0, worldY - (int)BlockLightRadius);
        int maxY = System.Math.Min(GameConstants.WorldHeight - 1, worldY + (int)BlockLightRadius);
        int minZ = worldZ - (int)BlockLightRadius;
        int maxZ = worldZ + (int)BlockLightRadius;

        for (int sampleZ = minZ; sampleZ <= maxZ; sampleZ++)
        {
            for (int sampleY = minY; sampleY <= maxY; sampleY++)
            {
                for (int sampleX = minX; sampleX <= maxX; sampleX++)
                {
                    BlockId block = snapshot.GetBlock(sampleX, sampleY, sampleZ);
                    float emission = s_blockRegistry.GetLightEmission(block);
                    if (emission <= 0f)
                    {
                        continue;
                    }

                    float dx = sampleX - worldX;
                    float dy = sampleY - worldY;
                    float dz = sampleZ - worldZ;
                    float distanceSq = dx * dx + dy * dy + dz * dz;
                    if (distanceSq > BlockLightRadiusSq)
                    {
                        continue;
                    }

                    float falloff = 1f - MathF.Sqrt(distanceSq) / BlockLightRadius;
                    light = System.Math.Max(light, emission * falloff);
                }
            }
        }

        return System.Math.Clamp(light, 0f, 0.45f);
    }

    private static float EncodeVertexLighting(float ao, float blockLight) =>
        System.Math.Clamp(ao, 0.32f, 1f) + blockLight;

    private static bool IsFoliageWindTexture(byte texture) =>
        texture is 8 or 38 or 39 or 40 or 29 or 30 or 31 or 53 or 62 or 63 or 64;

    private static float ComputeWindWeight(byte texture, int blockY, float vertexY) =>
        IsFoliageWindTexture(texture)
            ? System.Math.Clamp(vertexY - blockY, 0f, 1f)
            : 0f;

    private static void AddFace(List<BlockVertex> vertices, GameWorld world, int x, int y, int z, int faceIndex, byte texture, bool skipLighting)
    {
        float ao0;
        float ao1;
        float ao2;
        float ao3;
        if (skipLighting)
        {
            ao0 = ao1 = ao2 = ao3 = 1f;
        }
        else
        {
            var aoOffsets = FaceVertexAoOffsets[faceIndex];
            float blockLight = ComputeNearbyBlockLight(world, x, y, z);
            ao0 = EncodeVertexLighting(ComputeVertexAo(world, x, y, z, aoOffsets[0].S1X, aoOffsets[0].S1Y, aoOffsets[0].S1Z, aoOffsets[0].S2X, aoOffsets[0].S2Y, aoOffsets[0].S2Z, aoOffsets[0].CX, aoOffsets[0].CY, aoOffsets[0].CZ), blockLight);
            ao1 = EncodeVertexLighting(ComputeVertexAo(world, x, y, z, aoOffsets[1].S1X, aoOffsets[1].S1Y, aoOffsets[1].S1Z, aoOffsets[1].S2X, aoOffsets[1].S2Y, aoOffsets[1].S2Z, aoOffsets[1].CX, aoOffsets[1].CY, aoOffsets[1].CZ), blockLight);
            ao2 = EncodeVertexLighting(ComputeVertexAo(world, x, y, z, aoOffsets[2].S1X, aoOffsets[2].S1Y, aoOffsets[2].S1Z, aoOffsets[2].S2X, aoOffsets[2].S2Y, aoOffsets[2].S2Z, aoOffsets[2].CX, aoOffsets[2].CY, aoOffsets[2].CZ), blockLight);
            ao3 = EncodeVertexLighting(ComputeVertexAo(world, x, y, z, aoOffsets[3].S1X, aoOffsets[3].S1Y, aoOffsets[3].S1Z, aoOffsets[3].S2X, aoOffsets[3].S2Y, aoOffsets[3].S2Z, aoOffsets[3].CX, aoOffsets[3].CY, aoOffsets[3].CZ), blockLight);
        }

        switch (faceIndex)
        {
            case 0:
                AddQuad(vertices, y, (x + 1, y, z), (x + 1, y + 1, z), (x + 1, y + 1, z + 1), (x + 1, y, z + 1), texture, 1f, 0f, 0f, ao0, ao1, ao2, ao3);
                break;
            case 1:
                AddQuad(vertices, y, (x, y, z), (x, y, z + 1), (x, y + 1, z + 1), (x, y + 1, z), texture, -1f, 0f, 0f, ao0, ao1, ao2, ao3);
                break;
            case 2:
                AddQuad(vertices, y, (x, y + 1, z), (x, y + 1, z + 1), (x + 1, y + 1, z + 1), (x + 1, y + 1, z), texture, 0f, 1f, 0f, ao0, ao1, ao2, ao3);
                break;
            case 3:
                AddQuad(vertices, y, (x, y, z + 1), (x, y, z), (x + 1, y, z), (x + 1, y, z + 1), texture, 0f, -1f, 0f, ao0, ao1, ao2, ao3);
                break;
            case 4:
                AddQuad(vertices, y, (x, y, z + 1), (x + 1, y, z + 1), (x + 1, y + 1, z + 1), (x, y + 1, z + 1), texture, 0f, 0f, 1f, ao0, ao1, ao2, ao3);
                break;
            case 5:
                AddQuad(vertices, y, (x + 1, y, z), (x, y, z), (x, y + 1, z), (x + 1, y + 1, z), texture, 0f, 0f, -1f, ao0, ao1, ao2, ao3);
                break;
        }
    }

    private static void AddFace(List<BlockVertex> vertices, ChunkMeshBuildSnapshot snapshot, int x, int y, int z, int faceIndex, byte texture, bool skipLighting)
    {
        float ao0;
        float ao1;
        float ao2;
        float ao3;
        if (skipLighting)
        {
            ao0 = ao1 = ao2 = ao3 = 1f;
        }
        else
        {
            var aoOffsets = FaceVertexAoOffsets[faceIndex];
            float blockLight = ComputeNearbyBlockLight(snapshot, x, y, z);
            ao0 = EncodeVertexLighting(ComputeVertexAo(snapshot, x, y, z, aoOffsets[0].S1X, aoOffsets[0].S1Y, aoOffsets[0].S1Z, aoOffsets[0].S2X, aoOffsets[0].S2Y, aoOffsets[0].S2Z, aoOffsets[0].CX, aoOffsets[0].CY, aoOffsets[0].CZ), blockLight);
            ao1 = EncodeVertexLighting(ComputeVertexAo(snapshot, x, y, z, aoOffsets[1].S1X, aoOffsets[1].S1Y, aoOffsets[1].S1Z, aoOffsets[1].S2X, aoOffsets[1].S2Y, aoOffsets[1].S2Z, aoOffsets[1].CX, aoOffsets[1].CY, aoOffsets[1].CZ), blockLight);
            ao2 = EncodeVertexLighting(ComputeVertexAo(snapshot, x, y, z, aoOffsets[2].S1X, aoOffsets[2].S1Y, aoOffsets[2].S1Z, aoOffsets[2].S2X, aoOffsets[2].S2Y, aoOffsets[2].S2Z, aoOffsets[2].CX, aoOffsets[2].CY, aoOffsets[2].CZ), blockLight);
            ao3 = EncodeVertexLighting(ComputeVertexAo(snapshot, x, y, z, aoOffsets[3].S1X, aoOffsets[3].S1Y, aoOffsets[3].S1Z, aoOffsets[3].S2X, aoOffsets[3].S2Y, aoOffsets[3].S2Z, aoOffsets[3].CX, aoOffsets[3].CY, aoOffsets[3].CZ), blockLight);
        }

        switch (faceIndex)
        {
            case 0:
                AddQuad(vertices, y, (x + 1, y, z), (x + 1, y + 1, z), (x + 1, y + 1, z + 1), (x + 1, y, z + 1), texture, 1f, 0f, 0f, ao0, ao1, ao2, ao3);
                break;
            case 1:
                AddQuad(vertices, y, (x, y, z), (x, y, z + 1), (x, y + 1, z + 1), (x, y + 1, z), texture, -1f, 0f, 0f, ao0, ao1, ao2, ao3);
                break;
            case 2:
                AddQuad(vertices, y, (x, y + 1, z), (x, y + 1, z + 1), (x + 1, y + 1, z + 1), (x + 1, y + 1, z), texture, 0f, 1f, 0f, ao0, ao1, ao2, ao3);
                break;
            case 3:
                AddQuad(vertices, y, (x, y, z + 1), (x, y, z), (x + 1, y, z), (x + 1, y, z + 1), texture, 0f, -1f, 0f, ao0, ao1, ao2, ao3);
                break;
            case 4:
                AddQuad(vertices, y, (x, y, z + 1), (x + 1, y, z + 1), (x + 1, y + 1, z + 1), (x, y + 1, z + 1), texture, 0f, 0f, 1f, ao0, ao1, ao2, ao3);
                break;
            case 5:
                AddQuad(vertices, y, (x + 1, y, z), (x, y, z), (x, y + 1, z), (x + 1, y + 1, z), texture, 0f, 0f, -1f, ao0, ao1, ao2, ao3);
                break;
        }
    }

    private static void AddQuad(
        List<BlockVertex> vertices,
        int blockY,
        (int X, int Y, int Z) a,
        (int X, int Y, int Z) b,
        (int X, int Y, int Z) c,
        (int X, int Y, int Z) d,
        byte texture,
        float nx,
        float ny,
        float nz,
        float aoA,
        float aoB,
        float aoC,
        float aoD)
    {
        vertices.Add(new BlockVertex(a.X, a.Y, a.Z, 0, 0, texture, nx, ny, nz, aoA, ComputeWindWeight(texture, blockY, a.Y)));
        vertices.Add(new BlockVertex(b.X, b.Y, b.Z, 0, 1, texture, nx, ny, nz, aoB, ComputeWindWeight(texture, blockY, b.Y)));
        vertices.Add(new BlockVertex(c.X, c.Y, c.Z, 1, 1, texture, nx, ny, nz, aoC, ComputeWindWeight(texture, blockY, c.Y)));
        vertices.Add(new BlockVertex(a.X, a.Y, a.Z, 0, 0, texture, nx, ny, nz, aoA, ComputeWindWeight(texture, blockY, a.Y)));
        vertices.Add(new BlockVertex(c.X, c.Y, c.Z, 1, 1, texture, nx, ny, nz, aoC, ComputeWindWeight(texture, blockY, c.Y)));
        vertices.Add(new BlockVertex(d.X, d.Y, d.Z, 1, 0, texture, nx, ny, nz, aoD, ComputeWindWeight(texture, blockY, d.Y)));
    }
}
