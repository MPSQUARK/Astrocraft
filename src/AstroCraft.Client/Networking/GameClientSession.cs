using System.Net;
using System.Net.Sockets;
using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;
using AstroCraft.Core.Networking;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Client.World;

namespace AstroCraft.Client.Networking;

public sealed class GameClientSession : IDisposable
{
    private const float ReconcileDistanceThreshold = 2.5f;
    private readonly UdpClient _udp;
    private readonly IPEndPoint _serverEndPoint;
    private readonly BlockRegistry _blockRegistry = BlockRegistry.CreateDefault();
    private readonly PlayerPhysics _physics;
    private readonly GameWorld _world;
    private readonly Dictionary<int, PlayerState> _remotePlayers = new();
    private readonly HashSet<ChunkPosition> _dirtyChunks = new();

    public GameClientSession(string serverAddress, int port, bool flatWorld)
    {
        _serverEndPoint = new IPEndPoint(IPAddress.Parse(serverAddress), port);
        _udp = new UdpClient();
        _udp.Connect(_serverEndPoint);
        _world = new GameWorld(_blockRegistry, new ClientEmptyWorldGenerator()) { IsFlatWorld = flatWorld };
        _physics = new PlayerPhysics(_blockRegistry);
        LocalPlayer = new PlayerState { DisplayName = "Player" };
    }

    public GameWorld World => _world;
    public PlayerState LocalPlayer { get; private set; }
    public IReadOnlyDictionary<int, PlayerState> RemotePlayers => _remotePlayers;
    public bool IsConnected { get; private set; }
    public int LocalPlayerId { get; private set; }
    public int ServerTick { get; private set; }
    public IReadOnlyCollection<ChunkPosition> DirtyChunks => _dirtyChunks;

    public void SendHello(string playerName)
    {
        LocalPlayer.DisplayName = playerName;
        byte[] packet = NetworkSerializer.WriteClientHello(playerName);
        _udp.Send(packet);
    }

    public void SendInput(PlayerInput input)
    {
        if (!IsConnected)
        {
            return;
        }

        if (input.HotbarSelection >= 0 && input.HotbarSelection < GameConstants.HotbarSize)
        {
            LocalPlayer.Inventory.SelectedHotbarIndex = input.HotbarSelection;
        }

        byte[] packet = NetworkSerializer.WritePlayerInput(LocalPlayerId, input);
        _udp.Send(packet);
        PredictLocalPlayer(input);
    }

    public void Poll()
    {
        while (_udp.Available > 0)
        {
            IPEndPoint? remote = null;
            byte[] data = _udp.Receive(ref remote);
            HandlePacket(data);
        }
    }

    public void ClearDirtyChunks() => _dirtyChunks.Clear();

    public void Dispose()
    {
        if (IsConnected)
        {
            byte[] disconnect = [(byte)MessageType.Disconnect];
            try
            {
                _udp.Send(disconnect);
            }
            catch (SocketException)
            {
            }
        }

        _udp.Dispose();
    }

    private void HandlePacket(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        MessageType type = NetworkSerializer.ReadMessageType(data);
        ReadOnlySpan<byte> payload = data[1..];

        switch (type)
        {
            case MessageType.ServerWelcome:
                HandleServerWelcome(payload);
                break;
            case MessageType.StateDelta:
                HandleStateDelta(payload);
                break;
            case MessageType.ChunkData:
                HandleChunkData(payload);
                break;
            case MessageType.BlockChanged:
                HandleBlockChanged(payload);
                break;
        }
    }

    private void HandleServerWelcome(ReadOnlySpan<byte> payload)
    {
        (int playerId, int tick, Vector3 spawn) = ClientMessageReader.ReadServerWelcome(payload);
        PlayerState player = new()
        {
            PlayerId = playerId,
            DisplayName = LocalPlayer.DisplayName,
        };
        player.ResetToSpawn(spawn);
        LocalPlayer = player;
        LocalPlayerId = playerId;
        ServerTick = tick;
        IsConnected = true;
    }

    private void HandleStateDelta(ReadOnlySpan<byte> payload)
    {
        (int tick, IReadOnlyList<PlayerStateSnapshot> players) = ClientMessageReader.ReadStateDelta(payload);
        ServerTick = tick;

        foreach (PlayerStateSnapshot snapshot in players)
        {
            if (snapshot.PlayerId == LocalPlayerId)
            {
                ReconcileLocalPlayer(snapshot);
                continue;
            }

            ApplyRemoteSnapshot(snapshot);
        }
    }

    private void HandleChunkData(ReadOnlySpan<byte> payload)
    {
        (ChunkPosition position, BlockId[] blocks) = ClientMessageReader.ReadChunkData(payload);
        Chunk chunk = _world.GetOrCreateChunk(position);
        int index = 0;

        for (int localZ = 0; localZ < GameConstants.ChunkSizeZ; localZ++)
        {
            for (int localY = 0; localY < GameConstants.ChunkSizeY; localY++)
            {
                for (int localX = 0; localX < GameConstants.ChunkSizeX; localX++)
                {
                    chunk.SetBlock(localX, localY, localZ, blocks[index++]);
                }
            }
        }

        chunk.IsDirty = true;
        _dirtyChunks.Add(position);
    }

    private void HandleBlockChanged(ReadOnlySpan<byte> payload)
    {
        (int x, int y, int z, BlockId blockId) = NetworkSerializer.ReadBlockChanged(payload);
        if (!_world.TrySetBlock(x, y, z, blockId))
        {
            return;
        }

        ChunkPosition chunkPosition = ChunkPosition.FromBlock(x, z);
        if (_world.TryGetChunk(chunkPosition, out Chunk chunk))
        {
            chunk.IsDirty = true;
            _dirtyChunks.Add(chunkPosition);
        }
    }

    private void PredictLocalPlayer(PlayerInput input)
    {
        if (LocalPlayer.Survival.IsDead)
        {
            return;
        }

        _physics.Simulate(LocalPlayer, _world, input, (float)GameConstants.TickDurationSeconds);
    }

    private void ReconcileLocalPlayer(PlayerStateSnapshot snapshot)
    {
        LocalPlayer.Survival.Health = snapshot.Health;
        LocalPlayer.Survival.Oxygen = snapshot.Oxygen;
        LocalPlayer.Survival.Hunger = snapshot.Hunger;

        float distance = Vector3.Distance(LocalPlayer.Position, snapshot.Position);
        if (distance > ReconcileDistanceThreshold)
        {
            LocalPlayer.Position = snapshot.Position;
            LocalPlayer.Velocity = snapshot.Velocity;
            LocalPlayer.YawRadians = snapshot.YawRadians;
            LocalPlayer.PitchRadians = snapshot.PitchRadians;
            LocalPlayer.IsOnGround = snapshot.IsOnGround;
            return;
        }

        LocalPlayer.Velocity = snapshot.Velocity;
        LocalPlayer.IsOnGround = snapshot.IsOnGround;
    }

    private void ApplyRemoteSnapshot(PlayerStateSnapshot snapshot)
    {
        if (!_remotePlayers.TryGetValue(snapshot.PlayerId, out PlayerState? player))
        {
            player = new PlayerState { PlayerId = snapshot.PlayerId };
            _remotePlayers[snapshot.PlayerId] = player;
        }

        player.Position = snapshot.Position;
        player.Velocity = snapshot.Velocity;
        player.YawRadians = snapshot.YawRadians;
        player.PitchRadians = snapshot.PitchRadians;
        player.IsOnGround = snapshot.IsOnGround;
        player.Survival.Health = snapshot.Health;
        player.Survival.Oxygen = snapshot.Oxygen;
        player.Survival.Hunger = snapshot.Hunger;
    }
}
