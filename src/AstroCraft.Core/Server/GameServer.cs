using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Networking;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Core.World.Generation;

namespace AstroCraft.Core.Server;

public sealed class ConnectedClient
{
    public required int PlayerId { get; init; }
    public required IPEndPoint EndPoint { get; init; }
    public required PlayerState Player { get; init; }
    public PlayerInput PendingInput { get; set; }
    public DateTime LastHeardUtc { get; set; } = DateTime.UtcNow;
}

public sealed class GameServer
{
    private readonly BlockRegistry _blockRegistry = BlockRegistry.CreateDefault();
    private readonly GameWorld _world;
    private readonly PlayerPhysics _physics;
    private readonly SurvivalSimulator _survival;
    private readonly BlockInteractionSystem _interaction;
    private readonly ConcurrentDictionary<int, ConnectedClient> _clients = new();
    private readonly ConcurrentDictionary<string, ConnectedClient> _clientsByEndpoint = new();
    private int _nextPlayerId = 1;
    private int _currentTick;
    private readonly int _seed;

    public GameServer(int seed, bool flatWorld)
    {
        _seed = seed;
        IWorldGenerator generator = flatWorld
            ? new FlatWorldGenerator()
            : new ProceduralWorldGenerator(seed);
        _world = new GameWorld(_blockRegistry, generator) { IsFlatWorld = flatWorld };
        _physics = new PlayerPhysics(_blockRegistry);
        _survival = new SurvivalSimulator();
        _interaction = new BlockInteractionSystem(_blockRegistry);
    }

    public GameWorld World => _world;
    public int CurrentTick => _currentTick;
    public int WorldSeed { get; }
    public bool IsFlatWorld => _world.IsFlatWorld;
    public IReadOnlyCollection<ConnectedClient> Clients => _clients.Values.ToList();

    public int ConnectClient(IPEndPoint endPoint, string playerName)
    {
        int playerId = Interlocked.Increment(ref _nextPlayerId) - 1;
        Vector3 spawn = FindSpawnPosition(playerId);
        PlayerState player = new()
        {
            PlayerId = playerId,
            DisplayName = playerName,
        };
        player.ResetToSpawn(spawn);
        SeedStarterInventory(player);

        ConnectedClient client = new()
        {
            PlayerId = playerId,
            EndPoint = endPoint,
            Player = player,
        };

        _clients[playerId] = client;
        _clientsByEndpoint[endPoint.ToString()] = client;
        _world.EnsureChunksAround((int)spawn.X, (int)spawn.Z, GameConstants.DefaultViewDistanceChunks);
        return playerId;
    }

    public void DisconnectClient(int playerId)
    {
        if (!_clients.TryRemove(playerId, out ConnectedClient? client))
        {
            return;
        }

        _clientsByEndpoint.TryRemove(client.EndPoint.ToString(), out _);
    }

    public bool TryGetClientByEndpoint(IPEndPoint endPoint, out ConnectedClient client) =>
        _clientsByEndpoint.TryGetValue(endPoint.ToString(), out client!);

    public void QueueInput(IPEndPoint endPoint, PlayerInput input)
    {
        if (!_clientsByEndpoint.TryGetValue(endPoint.ToString(), out ConnectedClient? client))
        {
            return;
        }

        client.PendingInput = input;
        client.LastHeardUtc = DateTime.UtcNow;
        if (input.HotbarSelection >= 0 && input.HotbarSelection < GameConstants.HotbarSize)
        {
            client.Player.Inventory.SelectedHotbarIndex = input.HotbarSelection;
        }
    }

    public void Tick()
    {
        _currentTick++;
        foreach (ConnectedClient client in _clients.Values)
        {
            SimulateClient(client);
        }

        RemoveTimedOutClients();
        RespawnDeadPlayers();
    }

    public bool ValidateClientPositionClaim(int playerId, Vector3 claimedPosition)
    {
        if (!_clients.TryGetValue(playerId, out ConnectedClient? client))
        {
            return false;
        }

        float distance = Vector3.Distance(client.Player.Position, claimedPosition);
        return distance < 2f;
    }

    private void SimulateClient(ConnectedClient client)
    {
        PlayerState player = client.Player;
        if (player.Survival.IsDead)
        {
            return;
        }

        PlayerInput input = client.PendingInput;
        _physics.Simulate(player, _world, input, (float)GameConstants.TickDurationSeconds);
        _survival.Update(player, _world);
        _interaction.UpdateBreaking(player, _world, input);
        _interaction.TryPlaceBlock(player, _world, input);
        _world.EnsureChunksAround((int)player.Position.X, (int)player.Position.Z, GameConstants.DefaultViewDistanceChunks);
    }

    private void RespawnDeadPlayers()
    {
        foreach (ConnectedClient client in _clients.Values)
        {
            if (!client.Player.Survival.IsDead)
            {
                continue;
            }

            Vector3 spawn = FindSpawnPosition(client.PlayerId);
            client.Player.ResetToSpawn(spawn);
        }
    }

    private void RemoveTimedOutClients()
    {
        foreach (ConnectedClient client in _clients.Values)
        {
            if (DateTime.UtcNow - client.LastHeardUtc > TimeSpan.FromSeconds(30))
            {
                DisconnectClient(client.PlayerId);
            }
        }
    }

    private Vector3 FindSpawnPosition(int playerId)
    {
        int offset = playerId * 4;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            int x = offset + attempt * 2;
            int z = offset;
            int surface = FindSurfaceY(x, z);
            Vector3 candidate = new(x + 0.5f, surface + 2f, z + 0.5f);
            if (!_world.IsSolid((int)candidate.X, (int)candidate.Y, (int)candidate.Z))
            {
                return candidate;
            }
        }

        return new Vector3(0.5f, GameConstants.RespawnY, 0.5f);
    }

    private int FindSurfaceY(int x, int z)
    {
        for (int y = GameConstants.WorldHeight - 2; y >= 0; y--)
        {
            if (_world.IsSolid(x, y, z))
            {
                return y;
            }
        }

        return GameConstants.SeaLevel;
    }

    private static void SeedStarterInventory(PlayerState player)
    {
        player.Inventory.Hotbar[0].BlockId = BlockId.Concrete;
        player.Inventory.Hotbar[0].Count = 64;
        player.Inventory.Hotbar[1].BlockId = BlockId.Steel;
        player.Inventory.Hotbar[1].Count = 64;
        player.Inventory.Hotbar[2].BlockId = BlockId.Glowstone;
        player.Inventory.Hotbar[2].Count = 16;
    }
}
