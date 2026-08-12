using System.Net;
using System.Net.Sockets;
using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Furnaces;
using AstroCraft.Core.Math;
using AstroCraft.Core.Networking;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Client.World;
using AstroCraft.Core.Entities;
using AstroCraft.Client.Audio;
using AstroCraft.Client.Effects;

namespace AstroCraft.Client.Networking;

public sealed class GameClientSession : IDisposable
{
    private const float ReconcileDistanceThreshold = 6f;
    private const float ReconcileNudgeThreshold = 0.45f;
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(30);
    private readonly UdpClient _udp;
    private readonly object _udpGate = new();
    private readonly IPEndPoint _serverEndPoint;
    private readonly BlockRegistry _blockRegistry = BlockRegistry.CreateDefault();
    private readonly PlayerPhysics _physics;
    private readonly BlockInteractionSystem _interaction;
    private readonly SurvivalSimulator _survival = new();
    private readonly ItemEntityWorld _itemEntities = new();
    private readonly ItemPickupSystem _itemPickup = new();
    private readonly GameSound _sounds = new();
    private readonly BlockBreakEffects _breakEffects = new();
    private readonly Dictionary<(int X, int Y, int Z), PredictedBlockChange> _pendingBlockPredictions = new();
    private readonly Dictionary<(int X, int Y, int Z), InventorySlot> _furnaceOutputs = new();
    private GameWorld _world;
    private readonly Dictionary<int, PlayerState> _remotePlayers = new();
    private readonly HashSet<ChunkPosition> _dirtyChunks = new();
    private readonly HashSet<ChunkPosition> _receivedChunkSnapshots = new();
    private readonly Dictionary<ChunkPosition, List<PendingServerBlockChange>> _pendingSnapshotBlockChanges = new();
    private readonly Queue<byte[]> _preWelcomeChunkPackets = new();
    private readonly ClientInboundPacketQueue _inboundPackets = new();
    private readonly ClientChunkIngestPipeline _chunkPipeline = new();
    private readonly ClientChunkInterestTracker _chunkInterest = new();
    private ChunkPosition _lastStreamChunkCenter = new(int.MinValue, int.MinValue);
    private double _chunkStreamAccumulator;
    private const double ChunkStreamIntervalSeconds = 0.15;
    private static readonly TimeSpan StaleChunkRequestTimeout = TimeSpan.FromSeconds(2.5);
    private bool _worldReady;
    private DateTime _lastServerPacketUtc = DateTime.UtcNow;
    private float _footstepAccumulator;
    private int _reconnectAttempts;
    private DateTime _nextReconnectUtc = DateTime.MinValue;

    public GameClientSession(string serverAddress, int port, bool flatWorldHint)
    {
        _serverEndPoint = new IPEndPoint(IPAddress.Parse(serverAddress), port);
        _udp = new UdpClient();
        _udp.Client.ReceiveBufferSize = 8 * 1024 * 1024;
        _udp.Connect(_serverEndPoint);
        _world = CreateWorld(seed: 0, flatWorldHint);
        _physics = new PlayerPhysics(_blockRegistry);
        _interaction = new BlockInteractionSystem(_blockRegistry);
        LocalPlayer = new PlayerState { DisplayName = "Player" };
        FlatWorldHint = flatWorldHint;
    }

    public GameWorld World => _world;
    public PlayerState LocalPlayer { get; private set; }
    public IReadOnlyDictionary<int, PlayerState> RemotePlayers => _remotePlayers;
    public bool IsConnected { get; private set; }
    public bool WasEverConnected { get; private set; }
    public bool IsDisconnected => WasEverConnected && !IsConnected;
    public int LocalPlayerId { get; private set; }
    public int ServerTick { get; private set; }
    public float ServerTimeOfDay { get; private set; }
    public int WorldSeed { get; private set; }
    public bool FlatWorldHint { get; }
    public IReadOnlyCollection<ChunkPosition> DirtyChunks => _dirtyChunks;

    public bool HasDirtyChunks => _dirtyChunks.Count > 0;

    public event Action<ChunkPosition>? ChunkUnloaded;
    public BlockBreakEffects BreakEffects => _breakEffects;
    public ItemEntityWorld ItemEntities => _itemEntities;
    public IReadOnlyDictionary<(int X, int Y, int Z), InventorySlot> FurnaceOutputs => _furnaceOutputs;
    public int ReconnectAttempts => _reconnectAttempts;
    public bool IsReadyToReconnect => DateTime.UtcNow >= _nextReconnectUtc;

    public void SendHello(string playerName)
    {
        LocalPlayer.DisplayName = playerName;
        byte[] packet = NetworkSerializer.WriteClientHello(playerName);
        try
        {
            lock (_udpGate)
            {
                _udp.Send(packet);
            }

            _lastServerPacketUtc = DateTime.UtcNow;
        }
        catch (SocketException)
        {
        }
    }

    public void AttemptReconnect()
    {
        if (!IsReadyToReconnect)
        {
            return;
        }

        _reconnectAttempts++;
        double backoffSeconds = Math.Min(30.0, Math.Pow(2, Math.Min(_reconnectAttempts, 6)) * 0.25);
        _nextReconnectUtc = DateTime.UtcNow.AddSeconds(backoffSeconds);
        SendHello(LocalPlayer.DisplayName);
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
        lock (_udpGate)
        {
            _udp.Send(packet);
        }
    }

    public void SendCraftRequest(string recipeId)
    {
        if (!IsConnected)
        {
            return;
        }

        byte[] packet = NetworkSerializer.WriteCraftRequest(recipeId);
        lock (_udpGate)
        {
            _udp.Send(packet);
        }
    }

    public void ApplyLocalInput(PlayerInput input, float deltaSeconds)
    {
        if (!WasEverConnected && !IsConnected)
        {
            return;
        }

        bool wasOnGround = LocalPlayer.IsOnGround;
        float previousJumpCooldown = LocalPlayer.JumpCooldownSeconds;

        _physics.Simulate(LocalPlayer, _world, input, deltaSeconds);
        UpdateMovementSounds(input, wasOnGround, previousJumpCooldown, deltaSeconds);
    }

    public void SimulateNetworkTick(PlayerInput input)
    {
        if (!IsConnected)
        {
            return;
        }

        PredictBlockInteractions(input);
    }

    public void Poll()
    {
        DrainSocket();
        ProcessInboundPackets(128);
        CheckConnectionTimeout();
    }

    private void CheckConnectionTimeout()
    {
        if (!IsConnected)
        {
            return;
        }

        if (DateTime.UtcNow - _lastServerPacketUtc > DisconnectTimeout)
        {
            IsConnected = false;
        }
    }

    public void ClearDirtyChunks() => _dirtyChunks.Clear();

    public int LoadedChunkCount => _world.LoadedChunkPositions.Count();

    public int PendingChunkCount => _chunkPipeline.QueuedChunkCount;

    public int QueuedNetworkPacketCount => _inboundPackets.QueuedPacketCount;

    private void DrainSocket()
    {
        try
        {
            lock (_udpGate)
            {
                while (_udp.Client.Available > 0)
                {
                    IPEndPoint? remote = null;
                    byte[] data = _udp.Receive(ref remote);
                    _inboundPackets.Enqueue(data);
                    _lastServerPacketUtc = DateTime.UtcNow;
                }
            }
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ProcessInboundPackets(int maxPackets = int.MaxValue)
    {
        int processed = 0;
        while (processed < maxPackets && _inboundPackets.TryDequeue(out byte[]? data))
        {
            HandlePacket(data);
            processed++;
        }
    }

    public void ProcessPendingChunks(int maxChunks = 48)
    {
        _chunkPipeline.ProcessPending(ApplyChunkBlocks, maxChunks);
    }

    public int ExpectedLoadedChunkCount => Math.Max(1, _chunkInterest.WantedCount);

    public void PumpNetwork(double deltaSeconds, int maxChunkApplies = 16)
    {
        DrainSocket();
        ProcessInboundPackets(32);
        ProcessPendingChunks(maxChunkApplies);
    }

    public bool UpdateChunkInterest(bool force = false)
    {
        if (!IsConnected)
        {
            return false;
        }

        return _chunkInterest.UpdateInterest(
            (int)LocalPlayer.Position.X,
            (int)LocalPlayer.Position.Z,
            LocalPlayer.YawRadians,
            force) || force;
    }

    public void TickChunkStreaming(double deltaSeconds = 0, bool force = false)
    {
        if (!IsConnected)
        {
            return;
        }

        ChunkPosition center = ChunkPosition.FromBlock(
            (int)LocalPlayer.Position.X,
            (int)LocalPlayer.Position.Z);
        bool crossedChunk = center != _lastStreamChunkCenter;
        if (crossedChunk)
        {
            _lastStreamChunkCenter = center;
        }

        _chunkStreamAccumulator += deltaSeconds;
        if (!force && !crossedChunk && _chunkStreamAccumulator < ChunkStreamIntervalSeconds)
        {
            return;
        }

        _chunkStreamAccumulator = 0;
        _chunkInterest.ExpireStalePendingRequests(StaleChunkRequestTimeout);
        UpdateChunkInterest(force: force || crossedChunk);

        List<ChunkPosition> requests = _chunkInterest.CollectChunksToRequest(
            GameConstants.MaxClientChunkRequestsPerTick);
        if (requests.Count > 0)
        {
            SendChunkRequests(requests);
        }
    }

    public void ResetChunkInterest(bool clearReceived)
    {
        _chunkInterest.Reset();
        if (clearReceived)
        {
            _receivedChunkSnapshots.Clear();
            _pendingSnapshotBlockChanges.Clear();
        }
    }

    private void SendChunkRequests(IReadOnlyList<ChunkPosition> positions)
    {
        if (!IsConnected || positions.Count == 0)
        {
            return;
        }

        try
        {
            lock (_udpGate)
            {
                _udp.Send(NetworkSerializer.WriteRequestChunks(positions));
            }
        }
        catch (SocketException)
        {
        }
    }

    private void UnloadChunk(ChunkPosition position)
    {
        _world.TryRemoveChunk(position);
        _receivedChunkSnapshots.Remove(position);
        _chunkInterest.MarkUnloaded(position);
        _dirtyChunks.Remove(position);
        _pendingSnapshotBlockChanges.Remove(position);
        ChunkUnloaded?.Invoke(position);
    }

    public void DrainPendingPackets(int maxPackets = 256)
    {
        DrainSocket();
        ProcessInboundPackets(maxPackets);
        ProcessPendingChunks(Math.Min(maxPackets, 48));
        CheckConnectionTimeout();
    }

    public bool TryGetTargetBlock(out BlockPosition target, out Vector3 faceNormal) =>
        _interaction.TryGetTargetBlock(LocalPlayer, _world, out target, out _, out faceNormal);

    public bool TryResolvePlacement(out BlockPosition placement, out bool valid) =>
        _interaction.TryGetPlacementGhost(LocalPlayer, _world, out placement, out valid);

    public void Dispose()
    {
        if (IsConnected)
        {
            byte[] disconnect = [(byte)MessageType.Disconnect];
            try
            {
                lock (_udpGate)
                {
                    _udp.Send(disconnect);
                }
            }
            catch (SocketException)
            {
            }
        }

        _chunkPipeline.Dispose();
        _udp.Dispose();
        _sounds.Dispose();
    }

    private void UpdateMovementSounds(PlayerInput input, bool wasOnGround, float previousJumpCooldown, float deltaSeconds)
    {
        if (previousJumpCooldown <= 0f && LocalPlayer.JumpCooldownSeconds > 0f)
        {
            _sounds.Play(GameSoundEffect.Jump);
        }

        if (!wasOnGround && LocalPlayer.IsOnGround)
        {
            _sounds.PlayFootstep(GetBlockUnderFeet());
            _footstepAccumulator = 0f;
        }

        bool isMoving = MathF.Abs(input.MoveForward) > 0.01f || MathF.Abs(input.MoveRight) > 0.01f;
        if (!LocalPlayer.IsOnGround || !isMoving)
        {
            return;
        }

        float interval = LocalPlayer.IsSprinting ? 0.32f : 0.42f;
        _footstepAccumulator += deltaSeconds;
        if (_footstepAccumulator >= interval)
        {
            _sounds.PlayFootstep(GetBlockUnderFeet());
            _footstepAccumulator = 0f;
        }
    }

    private BlockId GetBlockUnderFeet()
    {
        int blockX = (int)MathF.Floor(LocalPlayer.Position.X);
        int blockY = (int)MathF.Floor(LocalPlayer.Position.Y) - 1;
        int blockZ = (int)MathF.Floor(LocalPlayer.Position.Z);
        return blockY >= 0 ? _world.GetBlock(blockX, blockY, blockZ) : BlockId.Air;
    }

    public ItemPickupResult UpdateEffects(float deltaSeconds)
    {
        _breakEffects.Update(deltaSeconds);
        _itemEntities.Update(_world, deltaSeconds);
        _itemPickup.UpdateMagnet(LocalPlayer, _itemEntities, deltaSeconds);
        ItemPickupResult pickupResult = _itemPickup.TryPickup(LocalPlayer, _itemEntities);
        if ((pickupResult & ItemPickupResult.PickedUp) != 0)
        {
            _sounds.Play(GameSoundEffect.ItemPickup);
        }

        return pickupResult;
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
                if (!_worldReady)
                {
                    return;
                }

                HandleStateDelta(payload);
                break;
            case MessageType.ChunkData:
                HandleChunkData(data[1..].ToArray());
                break;
            case MessageType.BlockChanged:
                if (!_worldReady)
                {
                    return;
                }

                HandleBlockChanged(payload);
                break;
            case MessageType.ItemEntitiesDelta:
                HandleItemEntitiesDelta(payload);
                break;
            case MessageType.FurnaceOutput:
                HandleFurnaceOutput(payload);
                break;
        }
    }

    private void HandleFurnaceOutput(ReadOnlySpan<byte> payload)
    {
        FurnaceStateChange change = NetworkSerializer.ReadFurnaceOutput(payload);
        (int x, int y, int z) key = (change.X, change.Y, change.Z);
        if (!_furnaceOutputs.TryGetValue(key, out InventorySlot? slot))
        {
            slot = new InventorySlot();
            _furnaceOutputs[key] = slot;
        }

        slot.BlockId = change.OutputBlockId;
        slot.ItemId = change.OutputItemId;
        slot.Count = change.OutputCount;
    }

    private void HandleServerWelcome(ReadOnlySpan<byte> payload)
    {
        (int playerId, int tick, Vector3 spawn, int worldSeed, bool flatWorld) = ClientMessageReader.ReadServerWelcome(payload);
        WorldSeed = worldSeed;
        _worldReady = false;
        _world = CreateWorld(worldSeed, flatWorld);
        _receivedChunkSnapshots.Clear();
        _pendingSnapshotBlockChanges.Clear();
        _chunkPipeline.Reset();
        while (_preWelcomeChunkPackets.Count > 0)
        {
            _chunkPipeline.EnqueueRaw(_preWelcomeChunkPackets.Dequeue());
        }

        _worldReady = true;
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
        WasEverConnected = true;
        _lastServerPacketUtc = DateTime.UtcNow;
        _reconnectAttempts = 0;
        _nextReconnectUtc = DateTime.MinValue;
        _pendingBlockPredictions.Clear();
        _breakEffects.Reset();
        _itemEntities.Clear();
        _furnaceOutputs.Clear();
        _remotePlayers.Clear();
        ResetChunkInterest(clearReceived: true);
        UpdateChunkInterest(force: true);
        TickChunkStreaming(force: true);
    }

    public void RequestChunkStreamFromServer(bool fullResync = false)
    {
        if (!IsConnected)
        {
            return;
        }

        if (fullResync)
        {
            foreach (ChunkPosition position in _world.LoadedChunkPositions.ToList())
            {
                UnloadChunk(position);
            }

            ResetChunkInterest(clearReceived: true);
        }

        UpdateChunkInterest(force: true);
        TickChunkStreaming(force: true);
    }

    private void HandleStateDelta(ReadOnlySpan<byte> payload)
    {
        (int tick, float timeOfDay, IReadOnlyList<PlayerStateSnapshot> players) = ClientMessageReader.ReadStateDelta(payload);
        ServerTick = tick;
        ServerTimeOfDay = timeOfDay;

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

    private void HandleChunkData(byte[] payload)
    {
        if (!_worldReady)
        {
            _preWelcomeChunkPackets.Enqueue(payload);
            return;
        }

        _chunkPipeline.EnqueueRaw(payload);
    }

    private void ApplyChunkBlocks(ChunkPosition position, BlockId[] blocks)
    {
        if (_receivedChunkSnapshots.Contains(position))
        {
            return;
        }

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

        _receivedChunkSnapshots.Add(position);
        _chunkInterest.MarkReceived(position);
        ReplayPendingSnapshotBlockChanges(position);
        MarkChunkDirty(position);
    }

    private void HandleItemEntitiesDelta(ReadOnlySpan<byte> payload)
    {
        (int tick, IReadOnlyList<ItemEntitySnapshot> entities) = ClientMessageReader.ReadItemEntitiesDelta(payload);
        ServerTick = tick;
        _itemEntities.ApplyServerSnapshot(entities);
    }

    private void HandleBlockChanged(ReadOnlySpan<byte> payload)
    {
        (int x, int y, int z, BlockId blockId, BlockAxis axis) = NetworkSerializer.ReadBlockChanged(payload);
        var position = (x, y, z);

        if (_pendingBlockPredictions.TryGetValue(position, out PredictedBlockChange prediction))
        {
            if (prediction.PredictedBlock != blockId)
            {
                RollbackPrediction(prediction);
            }

            _pendingBlockPredictions.Remove(position);
        }

        BlockId currentBlock = _world.GetBlock(x, y, z);
        if (currentBlock == blockId)
        {
            return;
        }

        ChunkPosition chunkPosition = ChunkPosition.FromBlock(x, z);
        if (!_receivedChunkSnapshots.Contains(chunkPosition))
        {
            QueuePendingSnapshotBlockChange(chunkPosition, x, y, z, blockId, axis);
        }

        if (!ApplyAuthoritativeBlockChange(x, y, z, blockId, axis))
        {
            return;
        }

        MarkChunkDirty(chunkPosition);
    }

    private void QueuePendingSnapshotBlockChange(ChunkPosition chunkPosition, int x, int y, int z, BlockId blockId, BlockAxis axis)
    {
        if (!_pendingSnapshotBlockChanges.TryGetValue(chunkPosition, out List<PendingServerBlockChange>? pending))
        {
            pending = new List<PendingServerBlockChange>();
            _pendingSnapshotBlockChanges[chunkPosition] = pending;
        }

        pending.RemoveAll(change => change.X == x && change.Y == y && change.Z == z);
        pending.Add(new PendingServerBlockChange(x, y, z, blockId, axis));
    }

    private void ReplayPendingSnapshotBlockChanges(ChunkPosition chunkPosition)
    {
        if (!_pendingSnapshotBlockChanges.TryGetValue(chunkPosition, out List<PendingServerBlockChange>? pending))
        {
            return;
        }

        _pendingSnapshotBlockChanges.Remove(chunkPosition);
        foreach (PendingServerBlockChange change in pending)
        {
            if (_world.GetBlock(change.X, change.Y, change.Z) == change.BlockId)
            {
                continue;
            }

            ApplyAuthoritativeBlockChange(change.X, change.Y, change.Z, change.BlockId, change.Axis);
        }
    }

    private bool ApplyAuthoritativeBlockChange(int x, int y, int z, BlockId blockId, BlockAxis axis)
    {
        if (y < 0 || y >= GameConstants.WorldHeight)
        {
            return false;
        }

        Chunk chunk = _world.GetOrCreateChunk(ChunkPosition.FromBlock(x, z));
        int localX = ((x % GameConstants.ChunkSizeX) + GameConstants.ChunkSizeX) % GameConstants.ChunkSizeX;
        int localZ = ((z % GameConstants.ChunkSizeZ) + GameConstants.ChunkSizeZ) % GameConstants.ChunkSizeZ;
        chunk.SetBlock(localX, y, localZ, blockId, axis);
        return true;
    }

    private readonly record struct PendingServerBlockChange(int X, int Y, int Z, BlockId BlockId, BlockAxis Axis);

    private void PredictLocalPlayer(PlayerInput input)
    {
        if (LocalPlayer.Survival.IsDead)
        {
            return;
        }

        _physics.Simulate(LocalPlayer, _world, input, (float)GameConstants.TickDurationSeconds);
    }

    private void PredictBlockInteractions(PlayerInput input)
    {
        PlayerInput aimInput = input with
        {
            YawRadians = LocalPlayer.YawRadians,
            PitchRadians = LocalPlayer.PitchRadians,
        };

        bool blockBroken = _interaction.UpdateBreaking(
            LocalPlayer,
            _world,
            aimInput,
            (float)GameConstants.TickDurationSeconds,
            out BlockId brokenBlock,
            out int brokenX,
            out int brokenY,
            out int brokenZ);

        if (aimInput.BreakBlock && LocalPlayer.BreakingBlockId != BlockId.Air)
        {
            _breakEffects.OnMiningProgress(
                LocalPlayer.BreakingBlockX,
                LocalPlayer.BreakingBlockY,
                LocalPlayer.BreakingBlockZ,
                LocalPlayer.BreakingBlockId,
                LocalPlayer.BreakProgress);
        }
        else
        {
            _breakEffects.OnMiningProgress(0, 0, 0, BlockId.Air, 0f);
        }

        if (blockBroken && brokenBlock != BlockId.Air)
        {
            RecordPrediction(brokenX, brokenY, brokenZ, brokenBlock, BlockId.Air);
            StackKey drop = _blockRegistry.GetDropStack(brokenBlock);
            if (!drop.IsEmpty)
            {
                _itemEntities.SpawnAtBlock(brokenX, brokenY, brokenZ, drop, predicted: true);
            }
            MarkChunkDirty(ChunkPosition.FromBlock(brokenX, brokenZ));
            _breakEffects.OnBlockBroken(brokenX, brokenY, brokenZ, brokenBlock);
            _sounds.PlayBlockBreak(brokenBlock);
        }

        if (input.RotateBlock)
        {
            _interaction.TryRotateTargetBlock(LocalPlayer, _world);
        }

        if (_survival.TryEatFood(LocalPlayer, _blockRegistry, input))
        {
            return;
        }

        if (!input.PlaceBlock)
        {
            return;
        }

        BlockId placedBlock = LocalPlayer.Inventory.SelectedHotbarSlot.BlockId;
        if (placedBlock == BlockId.Air)
        {
            return;
        }

        if (!_interaction.TryResolvePlacement(LocalPlayer, _world, out BlockPosition placement))
        {
            return;
        }

        if (!BlockInteractionSystem.IsWithinPlacementReach(LocalPlayer, placement))
        {
            return;
        }

        BlockId previousBlock = _world.GetBlock(placement.X, placement.Y, placement.Z);
        if (!_interaction.TryPlaceBlock(LocalPlayer, _world, aimInput))
        {
            return;
        }

        RecordPrediction(placement.X, placement.Y, placement.Z, previousBlock, placedBlock);
        MarkChunkDirty(ChunkPosition.FromBlock(placement.X, placement.Z));
        _sounds.PlayBlockPlace(placedBlock);
    }

    private void RecordPrediction(int x, int y, int z, BlockId previousBlock, BlockId predictedBlock)
    {
        _pendingBlockPredictions[(x, y, z)] = new PredictedBlockChange(x, y, z, previousBlock, predictedBlock);
    }

    private void RollbackPrediction(PredictedBlockChange prediction)
    {
        if (prediction.PredictedBlock == BlockId.Air && prediction.PreviousBlock != BlockId.Air)
        {
            _itemEntities.RemovePredictedAtBlock(
                prediction.X,
                prediction.Y,
                prediction.Z,
                prediction.PreviousBlock);
        }
        else if (prediction.PredictedBlock != BlockId.Air && prediction.PreviousBlock == BlockId.Air)
        {
            LocalPlayer.Inventory.TryAddBlock(prediction.PredictedBlock);
        }
    }

    private readonly record struct PredictedBlockChange(int X, int Y, int Z, BlockId PreviousBlock, BlockId PredictedBlock);

    private void ReconcileLocalPlayer(PlayerStateSnapshot snapshot)
    {
        bool wasDead = LocalPlayer.Survival.IsDead;

        LocalPlayer.Survival.Health = snapshot.Health;
        LocalPlayer.Survival.Oxygen = snapshot.Oxygen;
        LocalPlayer.Survival.Hunger = snapshot.Hunger;
        LocalPlayer.Survival.IsDead = snapshot.IsDead;
        LocalPlayer.Survival.RespawnTicksRemaining = snapshot.RespawnTicksRemaining;

        if (snapshot.IsDead)
        {
            return;
        }

        float distance = Vector3.Distance(LocalPlayer.Position, snapshot.Position);
        if (wasDead || distance > ReconcileDistanceThreshold)
        {
            LocalPlayer.Position = snapshot.Position;
            LocalPlayer.Velocity = snapshot.Velocity;
            LocalPlayer.IsOnGround = snapshot.IsOnGround;
            return;
        }

        if (distance > ReconcileNudgeThreshold)
        {
            LocalPlayer.Position = Vector3.Lerp(LocalPlayer.Position, snapshot.Position, 0.12f);
        }

        if (!LocalPlayer.IsOnGround)
        {
            LocalPlayer.Velocity = new Vector3(LocalPlayer.Velocity.X, snapshot.Velocity.Y, LocalPlayer.Velocity.Z);
        }

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
        player.Survival.IsDead = snapshot.IsDead;
        player.Survival.RespawnTicksRemaining = snapshot.RespawnTicksRemaining;
    }

    private void MarkChunkDirty(ChunkPosition position)
    {
        _dirtyChunks.Add(position);

        if (!_world.TryGetChunk(position, out Chunk chunk))
        {
            return;
        }

        chunk.IsDirty = true;

        foreach (ChunkPosition neighbor in NeighborChunkPositions(position))
        {
            _dirtyChunks.Add(neighbor);
            if (_world.TryGetChunk(neighbor, out Chunk neighborChunk))
            {
                neighborChunk.IsDirty = true;
            }
        }
    }

    private static IEnumerable<ChunkPosition> NeighborChunkPositions(ChunkPosition position)
    {
        yield return new ChunkPosition(position.X + 1, position.Z);
        yield return new ChunkPosition(position.X - 1, position.Z);
        yield return new ChunkPosition(position.X, position.Z + 1);
        yield return new ChunkPosition(position.X, position.Z - 1);
    }

    private GameWorld CreateWorld(int seed, bool flatWorld) =>
        new GameWorld(_blockRegistry, new ClientEmptyWorldGenerator()) { IsFlatWorld = flatWorld };
}
