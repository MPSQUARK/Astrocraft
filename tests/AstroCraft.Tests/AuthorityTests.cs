using System.Numerics;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Players;
using AstroCraft.Core.Server;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Core.World.Generation;

namespace AstroCraft.Tests;

public class AuthorityTests
{
    [Fact]
    public void Server_RejectsDistantClientPositionClaim()
    {
        GameServer server = new(seed: 1, flatWorld: true);
        System.Net.IPEndPoint endpoint = new(System.Net.IPAddress.Loopback, 12345);
        int playerId = server.ConnectClient(endpoint, "Tester");
        ConnectedClient client = server.Clients.Single(c => c.PlayerId == playerId);
        client.Player.Position = new Vector3(10f, 30f, 10f);

        Vector3 cheatPosition = new(100f, 30f, 100f);
        Assert.False(server.ValidateClientPositionClaim(playerId, cheatPosition));
    }

    [Fact]
    public void Server_AcceptsNearbyClientPositionClaim()
    {
        GameServer server = new(seed: 1, flatWorld: true);
        System.Net.IPEndPoint endpoint = new(System.Net.IPAddress.Loopback, 12346);
        int playerId = server.ConnectClient(endpoint, "Tester");
        ConnectedClient client = server.Clients.Single(c => c.PlayerId == playerId);
        Vector3 position = client.Player.Position;

        Assert.True(server.ValidateClientPositionClaim(playerId, position + new Vector3(0.5f, 0f, 0f)));
    }

    [Fact]
    public void BlockPlacement_IsServerSideOnly()
    {
        BlockRegistry registry = BlockRegistry.CreateDefault();
        GameWorld world = new(registry, new FlatWorldGenerator());
        GameServer server = new(seed: 1, flatWorld: true);
        System.Net.IPEndPoint endpoint = new(System.Net.IPAddress.Loopback, 12347);
        int playerId = server.ConnectClient(endpoint, "Builder");
        ConnectedClient client = server.Clients.Single(c => c.PlayerId == playerId);
        client.Player.Inventory.Hotbar[0].BlockId = BlockId.Concrete;
        client.Player.Inventory.Hotbar[0].Count = 64;

        PlayerInput input = new(0f, 0f, 0f, 0f, false, false, false, false, true, 0);
        server.QueueInput(endpoint, input);
        server.Tick();

        bool anyConcrete = false;
        foreach (var chunk in server.World.LoadedChunks)
        {
            foreach (BlockId block in chunk.Blocks)
            {
                if (block == BlockId.Concrete)
                {
                    anyConcrete = true;
                    break;
                }
            }
        }

        Assert.False(anyConcrete);
    }
}
