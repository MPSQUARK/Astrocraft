using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Entities;
using AstroCraft.Core.Furnaces;
using AstroCraft.Core.Math;
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
    private readonly RecipeRegistry _recipeRegistry = RecipeRegistry.CreateDefault();
    private readonly GameWorld _world;
    private readonly PlayerPhysics _physics;
    private readonly SurvivalSimulator _survival;
    private readonly BlockInteractionSystem _interaction;
    private readonly SmeltingRecipeRegistry _smeltingRecipes = SmeltingRecipeRegistry.CreateDefault();
    private readonly FurnaceFuelRegistry _furnaceFuels = FurnaceFuelRegistry.CreateDefault();
    private readonly Dictionary<(int X, int Y, int Z), FurnaceState> _furnaces = new();
    private readonly List<FurnaceStateChange> _pendingFurnaceChanges = new();
    private readonly ItemEntityWorld _itemEntities = new();
    private readonly ItemPickupSystem _itemPickup = new();
    private readonly ConcurrentDictionary<int, ConnectedClient> _clients = new();
    private readonly ConcurrentDictionary<string, ConnectedClient> _clientsByEndpoint = new();
    private readonly Dictionary<(int X, int Z), byte[]> _chunkPayloadCache = new();
    private int _nextPlayerId = 1;
    private int _currentTick;
    private readonly int _seed;
    private readonly ProceduralWorldGenerator? _proceduralGenerator;
    private float _timeOfDay;

    public GameServer(int seed, bool flatWorld)
    {
        _seed = seed;
        WorldSeed = seed;
        IWorldGenerator generator = flatWorld
            ? new FlatWorldGenerator()
            : new ProceduralWorldGenerator(seed);
        _proceduralGenerator = generator as ProceduralWorldGenerator;
        _world = new GameWorld(_blockRegistry, generator) { IsFlatWorld = flatWorld };
        _physics = new PlayerPhysics(_blockRegistry);
        _survival = new SurvivalSimulator();
        _interaction = new BlockInteractionSystem(_blockRegistry);
    }

    public GameWorld World => _world;
    public ItemEntityWorld ItemEntities => _itemEntities;
    public int CurrentTick => _currentTick;
    public float TimeOfDay => _timeOfDay;
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
        return playerId;
    }

    public IReadOnlyList<Chunk> FulfillChunkRequests(IReadOnlyList<ChunkPosition> positions, int maxChunks)
    {
        List<Chunk> chunks = new(System.Math.Min(positions.Count, maxChunks));
        foreach (ChunkPosition position in positions)
        {
            if (chunks.Count >= maxChunks)
            {
                break;
            }

            chunks.Add(_world.GetOrCreateChunk(position));
        }

        return chunks;
    }

    public byte[] GetCachedChunkPayload(Chunk chunk)
    {
        (int X, int Z) key = (chunk.Position.X, chunk.Position.Z);
        if (!_chunkPayloadCache.TryGetValue(key, out byte[]? payload))
        {
            payload = ChunkDataCodec.Encode(chunk);
            _chunkPayloadCache[key] = payload;
        }

        return payload;
    }

    public void InvalidateChunkPayloadCache(int chunkX, int chunkZ) =>
        _chunkPayloadCache.Remove((chunkX, chunkZ));

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
        _timeOfDay = (_timeOfDay + (float)(GameConstants.TickDurationSeconds / GameConstants.DayCycleSeconds)) % 1f;
        TickFurnaces();
        _itemEntities.Update(_world, (float)GameConstants.TickDurationSeconds);
        foreach (ConnectedClient client in _clients.Values)
        {
            SimulateClient(client);
            _itemPickup.UpdateMagnet(client.Player, _itemEntities, (float)GameConstants.TickDurationSeconds);
            _itemPickup.TryPickup(client.Player, _itemEntities);
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

    public bool TryCraft(int playerId, string recipeId)
    {
        if (!_clients.TryGetValue(playerId, out ConnectedClient? client))
        {
            return false;
        }

        if (client.Player.Survival.IsDead)
        {
            return false;
        }

        return client.Player.Inventory.TryCraft(recipeId, _recipeRegistry);
    }

    public RecipeRegistry RecipeRegistry => _recipeRegistry;
    public IReadOnlyList<FurnaceStateChange> PendingFurnaceChanges => _pendingFurnaceChanges;

    public void ClearPendingFurnaceChanges() => _pendingFurnaceChanges.Clear();

    public bool TryGetFurnace(int x, int y, int z, out FurnaceState state) =>
        _furnaces.TryGetValue((x, y, z), out state!);

    public FurnaceState GetOrCreateFurnace(int x, int y, int z)
    {
        (int, int, int) key = (x, y, z);
        if (!_furnaces.TryGetValue(key, out FurnaceState? state))
        {
            state = new FurnaceState();
            _furnaces[key] = state;
        }

        return state;
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
        if (!float.IsNaN(input.YawRadians))
        {
            player.YawRadians = input.YawRadians;
        }

        if (!float.IsNaN(input.PitchRadians))
        {
            player.PitchRadians = input.PitchRadians;
        }
        _survival.Update(player, _world, (float)GameConstants.TickDurationSeconds);
        bool ateFood = _survival.TryEatFood(player, _blockRegistry, input);
        if (_interaction.UpdateBreaking(
                player,
                _world,
                input,
                (float)GameConstants.TickDurationSeconds,
                out BlockId brokenBlock,
                out int brokenX,
                out int brokenY,
                out int brokenZ)
            && brokenBlock != BlockId.Air)
        {
            if (brokenBlock == BlockId.Furnace)
            {
                _furnaces.Remove((brokenX, brokenY, brokenZ));
            }

            StackKey drop = _blockRegistry.GetDropStack(brokenBlock);
            if (!drop.IsEmpty)
            {
                _itemEntities.SpawnAtBlock(brokenX, brokenY, brokenZ, drop);
            }
        }

        if (input.RotateBlock)
        {
            _interaction.TryRotateTargetBlock(player, _world);
        }

        if (!ateFood
            && input.PlaceBlock
            && _interaction.TryResolvePlacement(player, _world, out BlockPosition placement)
            && BlockInteractionSystem.IsWithinPlacementReach(player, placement))
        {
            BlockId selected = player.Inventory.SelectedHotbarSlot.BlockId;
            if (_interaction.TryPlaceBlock(player, _world, input) && selected == BlockId.Furnace)
            {
                GetOrCreateFurnace(placement.X, placement.Y, placement.Z);
            }
        }
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

            if (client.Player.Survival.RespawnTicksRemaining > 0)
            {
                client.Player.Survival.RespawnTicksRemaining--;
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
        int offset = (playerId - 1) * 4;
        int x = offset;
        int z = 0;
        if (_proceduralGenerator is not null)
        {
            _world.EnsureChunksAround(x, z, 2);
            int surface = _proceduralGenerator.GetActualSurfaceY(_world, x, z);
            return new Vector3(x + 0.5f, surface + 1.05f, z + 0.5f);
        }

        return new Vector3(x + 0.5f, GameConstants.RespawnY, z + 0.5f);
    }

    private void TickFurnaces()
    {
        foreach (((int x, int y, int z), FurnaceState state) in _furnaces.ToList())
        {
            if (_world.GetBlock(x, y, z) != BlockId.Furnace)
            {
                _furnaces.Remove((x, y, z));
                continue;
            }

            int outputCountBefore = state.Output.Count;
            ItemId outputItemBefore = state.Output.ItemId;
            BlockId outputBlockBefore = state.Output.BlockId;
            if (FurnaceSystem.Tick(state, _smeltingRecipes, _furnaceFuels)
                && state.Output.Count > outputCountBefore
                && (state.Output.ItemId != outputItemBefore || state.Output.BlockId != outputBlockBefore || outputCountBefore == 0))
            {
                _pendingFurnaceChanges.Add(new FurnaceStateChange(
                    x,
                    y,
                    z,
                    state.Output.BlockId,
                    state.Output.ItemId,
                    state.Output.Count));
            }
        }
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
