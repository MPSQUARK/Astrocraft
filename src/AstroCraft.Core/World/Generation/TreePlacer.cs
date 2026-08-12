using AstroCraft.Core.Blocks;



namespace AstroCraft.Core.World.Generation;



internal enum TreeKind : byte

{

    Oak = 0,

    Birch = 1,

    Spruce = 2,

    Jungle = 3,

}



internal static class TreePlacer

{

    internal static bool IsLogBlock(BlockId block) =>

        block is BlockId.Wood or BlockId.BirchLog or BlockId.SpruceLog or BlockId.JungleLog;



    internal static bool IsLeavesBlock(BlockId block) =>

        block is BlockId.Leaves or BlockId.BirchLeaves or BlockId.SpruceLeaves or BlockId.JungleLeaves;



    internal static BlockId GetLogBlock(TreeKind kind) => kind switch

    {

        TreeKind.Birch => BlockId.BirchLog,

        TreeKind.Spruce => BlockId.SpruceLog,

        TreeKind.Jungle => BlockId.JungleLog,

        _ => BlockId.Wood,

    };



    internal static BlockId GetLeavesBlock(TreeKind kind) => kind switch

    {

        TreeKind.Birch => BlockId.BirchLeaves,

        TreeKind.Spruce => BlockId.SpruceLeaves,

        TreeKind.Jungle => BlockId.JungleLeaves,

        _ => BlockId.Leaves,

    };



    internal static void GetTreeProfile(TreeKind kind, int seed, out int trunkHeight, out int clearanceRadius, out int verticalExtent)

    {

        switch (kind)

        {

            case TreeKind.Birch:

                trunkHeight = 7 + ((seed >> 4) & 3);

                clearanceRadius = 3;

                verticalExtent = trunkHeight + 4;

                break;

            case TreeKind.Spruce:

                trunkHeight = 8 + ((seed >> 6) & 3);

                int maxConeRadius = 2 + (seed & 1);

                clearanceRadius = maxConeRadius + 1;

                verticalExtent = trunkHeight + 2;

                break;

            case TreeKind.Jungle:

                trunkHeight = 10 + ((seed >> 5) & 5);

                int jungleRadius = 4 + (seed & 1);

                clearanceRadius = jungleRadius + 1;

                verticalExtent = trunkHeight + jungleRadius + 2;

                break;

            default:

                trunkHeight = 4 + ((seed >> 8) & 3);

                int canopyRadius = 3 + (seed & 1);

                clearanceRadius = canopyRadius + 1;

                verticalExtent = trunkHeight + canopyRadius + 2;

                break;

        }

    }



    public static bool CanPlaceTree(Chunk chunk, int localX, int baseY, int localZ, TreeKind kind, int seed)

    {

        if (baseY <= GameConstants.SeaLevel)

        {

            return false;

        }



        BlockId ground = chunk.GetBlock(localX, baseY - 1, localZ);

        if (ground is not (BlockId.Grass or BlockId.Dirt or BlockId.Moss or BlockId.JungleGrass or BlockId.Podzol))

        {

            return false;

        }



        if (chunk.GetBlock(localX, baseY, localZ) != BlockId.Air)

        {

            return false;

        }



        GetTreeProfile(kind, seed, out _, out int clearanceRadius, out int verticalExtent);



        for (int dx = -clearanceRadius; dx <= clearanceRadius; dx++)

        {

            for (int dz = -clearanceRadius; dz <= clearanceRadius; dz++)

            {

                if (dx * dx + dz * dz > clearanceRadius * clearanceRadius)

                {

                    continue;

                }



                int x = localX + dx;

                int z = localZ + dz;

                if (x < 0 || x >= GameConstants.ChunkSizeX || z < 0 || z >= GameConstants.ChunkSizeZ)

                {

                    continue;

                }



                for (int y = baseY; y < baseY + verticalExtent; y++)

                {

                    if (y < 0 || y >= GameConstants.WorldHeight)

                    {

                        continue;

                    }



                    BlockId block = chunk.GetBlock(x, y, z);

                    if (IsLogBlock(block) || IsLeavesBlock(block))

                    {

                        return false;

                    }

                }

            }

        }



        return true;

    }



    public static void PlaceTree(Chunk chunk, int localX, int baseY, int localZ, TreeKind kind, int seed)

    {

        switch (kind)

        {

            case TreeKind.Birch:

                PlaceBirch(chunk, localX, baseY, localZ, seed);

                break;

            case TreeKind.Spruce:

                PlaceSpruce(chunk, localX, baseY, localZ, seed);

                break;

            case TreeKind.Jungle:

                PlaceJungle(chunk, localX, baseY, localZ, seed);

                break;

            default:

                PlaceOak(chunk, localX, baseY, localZ, seed);

                break;

        }

    }



    private static void PlaceOak(Chunk chunk, int localX, int baseY, int localZ, int seed)

    {

        int trunkHeight = 4 + ((seed >> 8) & 3);

        BuildTrunk(chunk, localX, baseY, localZ, trunkHeight, TreeKind.Oak);



        int canopyRadius = 3 + (seed & 1);

        int centerY = baseY + trunkHeight - 1;



        for (int dy = -canopyRadius; dy <= canopyRadius; dy++)

        {

            int y = centerY + dy;

            float verticalDistance = System.MathF.Abs(dy) / (canopyRadius + 0.5f);

            int layerRadius = (int)System.MathF.Round(canopyRadius * (1f - verticalDistance * 0.4f));

            layerRadius = System.Math.Clamp(layerRadius, 1, canopyRadius);

            PlaceLeafDisc(chunk, localX, localZ, baseY, trunkHeight, y, layerRadius, TreeKind.Oak);

        }

    }



    private static void PlaceBirch(Chunk chunk, int localX, int baseY, int localZ, int seed)

    {

        int trunkHeight = 7 + ((seed >> 4) & 3);

        BuildTrunk(chunk, localX, baseY, localZ, trunkHeight, TreeKind.Birch);



        int canopyRadius = 2;

        int crownCenterY = baseY + trunkHeight;



        for (int dy = -1; dy <= 2; dy++)

        {

            int y = crownCenterY + dy;

            int layerRadius = dy switch

            {

                -1 => 1,

                0 => canopyRadius,

                1 => canopyRadius - 1,

                _ => 1,

            };

            PlaceLeafDisc(chunk, localX, localZ, baseY, trunkHeight, y, layerRadius, TreeKind.Birch);

        }

    }



    private static void PlaceSpruce(Chunk chunk, int localX, int baseY, int localZ, int seed)

    {

        int trunkHeight = 8 + ((seed >> 6) & 3);

        BuildTrunk(chunk, localX, baseY, localZ, trunkHeight, TreeKind.Spruce);



        int maxRadius = 2 + (seed & 1);

        int foliageBottom = baseY + trunkHeight / 4;

        int foliageTop = baseY + trunkHeight;

        int foliageHeight = foliageTop - foliageBottom;

        BlockId leaves = GetLeavesBlock(TreeKind.Spruce);



        for (int y = foliageBottom; y <= foliageTop; y++)

        {

            float progress = foliageHeight > 0 ? (float)(y - foliageBottom) / foliageHeight : 1f;

            int radius = (int)System.MathF.Round(maxRadius * (1f - progress));

            if (radius <= 0)

            {

                if (y > baseY + trunkHeight - 1 && y < GameConstants.WorldHeight)

                {

                    chunk.SetBlock(localX, y, localZ, leaves);

                }



                continue;

            }



            PlaceLeafDisc(chunk, localX, localZ, baseY, trunkHeight, y, radius, TreeKind.Spruce);

        }

    }



    private static void PlaceJungle(Chunk chunk, int localX, int baseY, int localZ, int seed)

    {

        int trunkHeight = 10 + ((seed >> 5) & 5);

        BuildTrunk(chunk, localX, baseY, localZ, trunkHeight, TreeKind.Jungle);



        int canopyRadius = 4 + (seed & 1);

        int centerY = baseY + trunkHeight;



        for (int dy = -2; dy <= canopyRadius; dy++)

        {

            int y = centerY + dy;

            float verticalDistance = System.MathF.Abs(dy) / (canopyRadius + 1f);

            int layerRadius = (int)System.MathF.Round(canopyRadius * (1f - verticalDistance * 0.35f));

            layerRadius = System.Math.Clamp(layerRadius, 1, canopyRadius);

            PlaceLeafDisc(chunk, localX, localZ, baseY, trunkHeight, y, layerRadius, TreeKind.Jungle);

        }

    }



    private static void BuildTrunk(Chunk chunk, int localX, int baseY, int localZ, int trunkHeight, TreeKind kind)

    {

        BlockId log = GetLogBlock(kind);

        for (int dy = 0; dy < trunkHeight; dy++)

        {

            chunk.SetBlock(localX, baseY + dy, localZ, log);

        }

    }



    private static void PlaceLeafDisc(

        Chunk chunk,

        int localX,

        int localZ,

        int baseY,

        int trunkHeight,

        int y,

        int layerRadius,

        TreeKind kind)

    {

        if (y < baseY + 1 || y >= GameConstants.WorldHeight)

        {

            return;

        }



        BlockId log = GetLogBlock(kind);

        BlockId leaves = GetLeavesBlock(kind);



        for (int dx = -layerRadius; dx <= layerRadius; dx++)

        {

            for (int dz = -layerRadius; dz <= layerRadius; dz++)

            {

                if (dx * dx + dz * dz > layerRadius * layerRadius + 0.25f)

                {

                    continue;

                }



                if (dx == 0 && dz == 0 && y < baseY + trunkHeight)

                {

                    continue;

                }



                int leafX = localX + dx;

                int leafZ = localZ + dz;

                if (leafX < 0 || leafX >= GameConstants.ChunkSizeX

                    || leafZ < 0 || leafZ >= GameConstants.ChunkSizeZ)

                {

                    continue;

                }



                if (chunk.GetBlock(leafX, y, leafZ) == log)

                {

                    continue;

                }



                chunk.SetBlock(leafX, y, leafZ, leaves);

            }

        }

    }

}

