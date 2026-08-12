using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;

namespace AstroCraft.Core.World;

public sealed class Chunk
{
    private readonly BlockId[] _blocks = new BlockId[GameConstants.ChunkSizeX * GameConstants.ChunkSizeY * GameConstants.ChunkSizeZ];
    private readonly byte[] _blockAxes = new byte[GameConstants.ChunkSizeX * GameConstants.ChunkSizeY * GameConstants.ChunkSizeZ];
    public ChunkPosition Position { get; }
    public bool IsDirty { get; set; } = true;

    public Chunk(ChunkPosition position)
    {
        Position = position;
        Array.Fill(_blocks, BlockId.Air);
    }

    public BlockId GetBlock(int localX, int localY, int localZ)
    {
        ValidateLocal(localX, localY, localZ);
        return _blocks[Index(localX, localY, localZ)];
    }

    public void SetBlock(int localX, int localY, int localZ, BlockId blockId, BlockAxis axis = BlockAxis.Y)
    {
        ValidateLocal(localX, localY, localZ);
        int index = Index(localX, localY, localZ);
        _blocks[index] = blockId;
        _blockAxes[index] = blockId == BlockId.Air ? (byte)BlockAxis.Y : (byte)axis;
        IsDirty = true;
    }

    public BlockAxis GetBlockAxis(int localX, int localY, int localZ)
    {
        ValidateLocal(localX, localY, localZ);
        return (BlockAxis)_blockAxes[Index(localX, localY, localZ)];
    }

    public ReadOnlySpan<BlockId> Blocks => _blocks;

    public void CopyBlocksTo(Span<BlockId> destination)
    {
        if (destination.Length < _blocks.Length)
        {
            throw new ArgumentException("Destination span is too small.", nameof(destination));
        }

        _blocks.CopyTo(destination);
    }

    private static void ValidateLocal(int localX, int localY, int localZ)
    {
        if (localX is < 0 or >= GameConstants.ChunkSizeX
            || localY is < 0 or >= GameConstants.ChunkSizeY
            || localZ is < 0 or >= GameConstants.ChunkSizeZ)
        {
            throw new ArgumentOutOfRangeException(nameof(localX), "Block coordinates are outside the chunk.");
        }
    }

    private static int Index(int localX, int localY, int localZ) =>
        localX + GameConstants.ChunkSizeX * (localY + GameConstants.ChunkSizeY * localZ);
}
