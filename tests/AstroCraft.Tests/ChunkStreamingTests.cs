using AstroCraft.Client.Networking;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Hosting;
using AstroCraft.Core.Math;
using AstroCraft.Core.Networking;
using AstroCraft.Core.World;

namespace AstroCraft.Tests;

[Collection("NetworkIntegration")]
public class ChunkStreamingTests
{
    [Fact]
    public async Task Client_ReceivesChunksOnRequest()
    {
        const int gamePort = 27999;
        using GameServerHost host = new("Chunk Debug", gamePort, seed: 5, flatWorld: true, enableDiscovery: false);
        host.Start();
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(2)));
        await Task.Delay(200);

        using GameClientSession session = new("127.0.0.1", gamePort, flatWorldHint: true);
        session.SendHello("ChunkTester");

        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            session.Poll();
            session.TickChunkStreaming(force: true);
            session.ProcessPendingChunks(int.MaxValue);
            if (session.LoadedChunkCount > 0)
            {
                break;
            }

            await Task.Delay(25);
        }

        Assert.True(session.LoadedChunkCount > 0, $"loaded={session.LoadedChunkCount}, pending={session.PendingChunkCount}");
    }

    [Fact]
    public void ChunkViewSelector_ForwardCone_LoadsMoreInFrontThanBehind()
    {
        HashSet<ChunkPosition> interest = new();
        ChunkViewSelector.CollectInterestChunks(0, 0, 0f, interest);

        int north = interest.Count(position => position.Z > 0);
        int south = interest.Count(position => position.Z < 0);

        Assert.True(north > south);
        Assert.Contains(new ChunkPosition(0, 0), interest);
        Assert.True(interest.Count < ChunkViewSelector.EstimateMaxInterestChunks());
    }

    [Fact]
    public void ChunkDataCodec_DeflateRoundTrips()
    {
        BlockId[] blocks = new BlockId[GameConstants.ChunkSizeX * GameConstants.ChunkSizeY * GameConstants.ChunkSizeZ];
        Array.Fill(blocks, BlockId.Stone);
        for (int i = 0; i < blocks.Length; i += 17)
        {
            blocks[i] = BlockId.Grass;
        }

        Chunk chunk = new(new ChunkPosition(3, 5));
        for (int index = 0; index < blocks.Length; index++)
        {
            int localX = index % GameConstants.ChunkSizeX;
            int localZ = index / (GameConstants.ChunkSizeX * GameConstants.ChunkSizeY);
            int localY = (index / GameConstants.ChunkSizeX) % GameConstants.ChunkSizeY;
            chunk.SetBlock(localX, localY, localZ, blocks[index]);
        }

        byte[] encoded = ChunkDataCodec.Encode(chunk);
        BlockId[] decoded = ChunkDataCodec.Decode(encoded);
        Assert.Equal(blocks, decoded);
        Assert.True(encoded[8] is 0 or 1 or 2);
    }
}
