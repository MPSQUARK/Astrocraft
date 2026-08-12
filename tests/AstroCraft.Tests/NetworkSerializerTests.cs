using System.Buffers.Binary;
using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Entities;
using AstroCraft.Core.Math;
using AstroCraft.Core.Networking;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.Server;
using AstroCraft.Core.World;

namespace AstroCraft.Tests;

public class NetworkSerializerTests
{
    [Fact]
    public void PlayerInput_RoundTripsThroughSerializer()
    {
        PlayerInput original = new(1f, -0.5f, 0.1f, -0.2f, true, false, true, true, false, 3, 1.2f, -0.35f, true);
        byte[] packet = NetworkSerializer.WritePlayerInput(5, original);
        PlayerInput parsed = NetworkSerializer.ReadPlayerInput(packet.AsSpan(5));

        Assert.Equal(original.MoveForward, parsed.MoveForward);
        Assert.Equal(original.MoveRight, parsed.MoveRight);
        Assert.Equal(original.Jump, parsed.Jump);
        Assert.Equal(original.HotbarSelection, parsed.HotbarSelection);
        Assert.Equal(original.UseItem, parsed.UseItem);
        Assert.Equal(original.YawRadians, parsed.YawRadians);
        Assert.Equal(original.PitchRadians, parsed.PitchRadians);
    }

    [Fact]
    public void BlockChanged_RoundTripsThroughSerializer()
    {
        byte[] packet = NetworkSerializer.WriteBlockChanged(10, 20, 30, Core.Blocks.BlockId.Wood, Core.Blocks.BlockAxis.X);
        (int x, int y, int z, Core.Blocks.BlockId block, Core.Blocks.BlockAxis axis) =
            NetworkSerializer.ReadBlockChanged(packet.AsSpan(1));

        Assert.Equal(10, x);
        Assert.Equal(20, y);
        Assert.Equal(30, z);
        Assert.Equal(Core.Blocks.BlockId.Wood, block);
        Assert.Equal(Core.Blocks.BlockAxis.X, axis);
    }

    [Fact]
    public void WriteChunkData_FlatWorldChunk_EncodesAndFitsUdp()
    {
        GameServer server = new(seed: 5, flatWorld: true);
        server.World.EnsureChunksAround(4, 4, 2);
        Chunk chunk = server.World.GetOrCreateChunk(new ChunkPosition(0, 0));
        byte[] packet = NetworkSerializer.WriteChunkData(chunk);
        Assert.True(packet.Length < 60_000, $"chunk packet was {packet.Length} bytes");
    }

    [Fact]
    public void ChunkDataCodec_RoundTripsTerrainChunk()
    {
        Chunk chunk = new(new ChunkPosition(2, -3));
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localY = 0; localY < GameConstants.ChunkSizeY; localY++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    BlockId blockId = localY < GameConstants.ChunkSizeY / 2 ? BlockId.Air : BlockId.Stone;
                    chunk.SetBlock(localX, localY, localZ, blockId);
                }
            }
        }

        byte[] encoded = ChunkDataCodec.Encode(chunk);
        Assert.True(encoded.Length < 4096, $"RLE chunk should be small, was {encoded.Length} bytes");

        BlockId[] decoded = ChunkDataCodec.Decode(encoded);
        Assert.Equal(chunk.Blocks.Length, decoded.Length);
        for (int i = 0; i < chunk.Blocks.Length; i++)
        {
            Assert.Equal(chunk.Blocks[i], decoded[i]);
        }
    }

    [Fact]
    public void WriteChunkData_RoundTripsThroughNetworkPacket()
    {
        Chunk chunk = new(new ChunkPosition(0, 0));
        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localY = 0; localY < GameConstants.ChunkSizeY; localY++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    chunk.SetBlock(localX, localY, localZ, BlockId.Grass);
                }
            }
        }

        byte[] packet = NetworkSerializer.WriteChunkData(chunk);
        ReadOnlySpan<byte> payload = packet.AsSpan(1);
        int chunkX = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int chunkZ = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        BlockId[] blocks = ChunkDataCodec.Decode(payload);

        Assert.Equal(chunk.Position, new ChunkPosition(chunkX, chunkZ));
        Assert.Equal(chunk.Blocks.Length, blocks.Length);
        Assert.All(blocks, block => Assert.Equal(BlockId.Grass, block));
    }
}
