using System.Buffers.Binary;
using System.IO.Compression;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.World;

namespace AstroCraft.Core.Networking;

public static class ChunkDataCodec
{
    private const byte FormatRaw = 0;
    private const byte FormatRle = 1;
    private const byte FormatDeflate = 2;

    public static byte[] Encode(Chunk chunk)
    {
        ReadOnlySpan<BlockId> blocks = chunk.Blocks;
        int blockCount = blocks.Length;

        int runCount = CountRuns(blocks);
        int rleBytes = 1 + runCount * 4;
        int rawBytes = blockCount * 2;
        bool useRle = rleBytes < rawBytes;
        byte innerFormat = useRle ? FormatRle : FormatRaw;
        int innerLength = useRle ? rleBytes : rawBytes;

        byte[] inner = new byte[innerLength];
        if (useRle)
        {
            WriteRle(blocks, inner);
        }
        else
        {
            WriteRaw(blocks, inner);
        }

        byte[]? deflated = TryDeflate(inner);
        if (deflated is not null && deflated.Length + 1 < inner.Length)
        {
            byte[] buffer = new byte[10 + deflated.Length];
            WriteHeader(buffer, chunk.Position.X, chunk.Position.Z, FormatDeflate);
            buffer[9] = innerFormat;
            deflated.CopyTo(buffer.AsSpan(10));
            return buffer;
        }

        byte[] uncompressed = new byte[9 + innerLength];
        WriteHeader(uncompressed, chunk.Position.X, chunk.Position.Z, innerFormat);
        inner.CopyTo(uncompressed.AsSpan(9));
        return uncompressed;
    }

    public static BlockId[] Decode(ReadOnlySpan<byte> payload)
    {
        int blockCount = GameConstants.ChunkSizeX * GameConstants.ChunkSizeY * GameConstants.ChunkSizeZ;
        byte format = payload[8];
        if (format == FormatDeflate)
        {
            byte innerFormat = payload[9];
            byte[] inflated = Inflate(payload[10..]);
            return innerFormat == FormatRle
                ? DecodeRle(inflated, blockCount)
                : DecodeRaw(inflated, blockCount);
        }

        ReadOnlySpan<byte> data = payload[9..];
        return format == FormatRle ? DecodeRle(data, blockCount) : DecodeRaw(data, blockCount);
    }

    private static void WriteHeader(byte[] buffer, int chunkX, int chunkZ, byte format)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0), chunkX);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), chunkZ);
        buffer[8] = format;
    }

    private static byte[]? TryDeflate(ReadOnlySpan<byte> source)
    {
        using MemoryStream output = new();
        using (DeflateStream deflate = new(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(source);
        }

        return output.Length < source.Length ? output.ToArray() : null;
    }

    private static byte[] Inflate(ReadOnlySpan<byte> source)
    {
        using MemoryStream input = new(source.ToArray());
        using DeflateStream deflate = new(input, CompressionMode.Decompress);
        using MemoryStream output = new();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    private static int CountRuns(ReadOnlySpan<BlockId> blocks)
    {
        if (blocks.Length == 0)
        {
            return 0;
        }

        int runs = 1;
        for (int i = 1; i < blocks.Length; i++)
        {
            if (blocks[i] != blocks[i - 1])
            {
                runs++;
            }
        }

        return runs;
    }

    private static void WriteRle(ReadOnlySpan<BlockId> blocks, Span<byte> destination)
    {
        int offset = 0;
        BlockId current = blocks[0];
        ushort runLength = 1;
        for (int i = 1; i < blocks.Length; i++)
        {
            if (blocks[i] == current && runLength < ushort.MaxValue)
            {
                runLength++;
                continue;
            }

            WriteRun(destination, ref offset, runLength, current);
            current = blocks[i];
            runLength = 1;
        }

        WriteRun(destination, ref offset, runLength, current);
    }

    private static void WriteRun(Span<byte> destination, ref int offset, ushort runLength, BlockId blockId)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], runLength);
        offset += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], (ushort)blockId);
        offset += 2;
    }

    private static void WriteRaw(ReadOnlySpan<BlockId> blocks, Span<byte> destination)
    {
        int offset = 0;
        for (int i = 0; i < blocks.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], (ushort)blocks[i]);
            offset += 2;
        }
    }

    private static BlockId[] DecodeRle(ReadOnlySpan<byte> data, int blockCount)
    {
        BlockId[] blocks = new BlockId[blockCount];
        int blockIndex = 0;
        int offset = 0;
        while (offset + 4 <= data.Length && blockIndex < blockCount)
        {
            ushort runLength = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            offset += 2;
            BlockId blockId = (BlockId)BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            offset += 2;

            int end = System.Math.Min(blockCount, blockIndex + runLength);
            for (int i = blockIndex; i < end; i++)
            {
                blocks[i] = blockId;
            }

            blockIndex = end;
        }

        if (blockIndex < blockCount)
        {
            Array.Fill(blocks, BlockId.Air, blockIndex, blockCount - blockIndex);
        }

        return blocks;
    }

    private static BlockId[] DecodeRaw(ReadOnlySpan<byte> data, int blockCount)
    {
        BlockId[] blocks = new BlockId[blockCount];
        for (int i = 0; i < blockCount; i++)
        {
            blocks[i] = (BlockId)BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 2)..]);
        }

        return blocks;
    }
}
