using System.Net;
using System.Net.Sockets;
using System.Numerics;
using AstroCraft.Core.Discovery;
using AstroCraft.Core.Entities;
using AstroCraft.Core.Furnaces;
using AstroCraft.Core.Math;
using AstroCraft.Core.Networking;
using AstroCraft.Core.Players;
using AstroCraft.Core.Server;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;

namespace AstroCraft.Core.Hosting;

public sealed class GameServerHost : IDisposable
{
    private readonly GameServer _gameServer;
    private readonly UdpClient _udp;
    private readonly LanDiscoveryService? _discovery;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ManualResetEventSlim _networkLoopReady = new(false);

    public GameServerHost(
        string serverName,
        int port,
        int seed,
        bool flatWorld,
        bool enableDiscovery = true,
        int discoveryPort = GameConstants.DiscoveryPort)
    {
        _gameServer = new GameServer(seed, flatWorld);
        _udp = new UdpClient(port);
        if (enableDiscovery)
        {
            _discovery = new LanDiscoveryService(serverName, port, () => _gameServer.Clients.Count, discoveryPort);
            _discovery.Start();
        }
    }

    public GameServer GameServer => _gameServer;

    public bool IsNetworkReady => _networkLoopReady.IsSet;

    public void Start() => _ = Task.Run(RunAsync);

    public bool WaitUntilReady(TimeSpan timeout)
    {
        return _networkLoopReady.Wait(timeout);
    }

    private async Task RunAsync()
    {
        _networkLoopReady.Set();
        DateTime nextTick = DateTime.UtcNow;
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await DrainNetworkAsync();
                if (DateTime.UtcNow >= nextTick)
                {
                    _gameServer.Tick();
                    await BroadcastStateAsync();
                    await BroadcastItemEntitiesAsync();
                    await BroadcastBlockChangesAsync();
                    await BroadcastFurnaceOutputsAsync();
                    nextTick = nextTick.AddMilliseconds(GameConstants.TickDurationSeconds * 1000);
                }
            }
            catch (Exception) when (!_cancellation.IsCancellationRequested)
            {
                await Task.Delay(1);
            }

            await Task.Delay(1);
        }
    }

    private async Task DrainNetworkAsync()
    {
        const int maxPacketsPerDrain = 32;
        for (int i = 0; i < maxPacketsPerDrain; i++)
        {
            if (_udp.Available <= 0)
            {
                return;
            }

            UdpReceiveResult packet = await _udp.ReceiveAsync();
            await HandlePacketAsync(packet);
        }
    }

    private async Task HandlePacketAsync(UdpReceiveResult packet)
    {
        ReadOnlySpan<byte> data = packet.Buffer;
        if (data.IsEmpty)
        {
            return;
        }

        MessageType type = NetworkSerializer.ReadMessageType(data);
        switch (type)
        {
            case MessageType.ClientHello:
                await HandleHelloAsync(packet.RemoteEndPoint, packet.Buffer[1..].ToArray());
                break;
            case MessageType.PlayerInput:
                HandleInput(packet.RemoteEndPoint, data);
                break;
            case MessageType.Disconnect:
                HandleDisconnect(packet.RemoteEndPoint);
                break;
            case MessageType.RequestChunkStream:
                break;
            case MessageType.RequestChunks:
                await HandleChunkRequestsAsync(packet.RemoteEndPoint, packet.Buffer[1..].ToArray());
                break;
            case MessageType.CraftRequest:
                HandleCraftRequest(packet.RemoteEndPoint, data[1..]);
                break;
        }
    }

    private Task HandleHelloAsync(IPEndPoint endPoint, byte[] payload)
    {
        if (_gameServer.TryGetClientByEndpoint(endPoint, out ConnectedClient? existing))
        {
            return SendWelcomeAsync(endPoint, existing.PlayerId);
        }

        string playerName = NetworkSerializer.ReadClientHello(payload);
        int playerId = _gameServer.ConnectClient(endPoint, playerName);
        return SendWelcomeAsync(endPoint, playerId);
    }

    private async Task SendWelcomeAsync(IPEndPoint endPoint, int playerId)
    {
        ConnectedClient? client = _gameServer.Clients.FirstOrDefault(c => c.PlayerId == playerId);
        if (client is null)
        {
            return;
        }

        byte[] welcome = NetworkSerializer.WriteServerWelcome(
            playerId,
            _gameServer.CurrentTick,
            client.Player.Position,
            _gameServer.WorldSeed,
            _gameServer.IsFlatWorld);
        await TrySendAsync(welcome, endPoint);
    }

    private async Task HandleChunkRequestsAsync(IPEndPoint endPoint, byte[] payload)
    {
        if (payload.Length < 2)
        {
            return;
        }

        ChunkPosition[] requested = NetworkSerializer.ReadRequestChunks(payload);
        if (requested.Length == 0)
        {
            return;
        }

        IReadOnlyList<Chunk> chunks = _gameServer.FulfillChunkRequests(
            requested,
            GameConstants.MaxServerChunkResponsesPerTick);
        foreach (Chunk chunk in chunks)
        {
            byte[] encoded = _gameServer.GetCachedChunkPayload(chunk);
            byte[] data = NetworkSerializer.WriteChunkDataFromEncoded(encoded);
            if (!await TrySendAsync(data, endPoint))
            {
                return;
            }
        }
    }

    private void HandleInput(IPEndPoint endPoint, ReadOnlySpan<byte> data)
    {
        if (!_gameServer.TryGetClientByEndpoint(endPoint, out ConnectedClient? client))
        {
            return;
        }

        PlayerInput input = NetworkSerializer.ReadPlayerInput(data[5..]);
        _gameServer.QueueInput(endPoint, input);
    }

    private void HandleDisconnect(IPEndPoint endPoint)
    {
        if (!_gameServer.TryGetClientByEndpoint(endPoint, out ConnectedClient? client))
        {
            return;
        }

        _gameServer.DisconnectClient(client.PlayerId);
    }

    private void HandleCraftRequest(IPEndPoint endPoint, ReadOnlySpan<byte> payload)
    {
        if (!_gameServer.TryGetClientByEndpoint(endPoint, out ConnectedClient? client))
        {
            return;
        }

        string recipeId = NetworkSerializer.ReadCraftRequest(payload);
        _gameServer.TryCraft(client.PlayerId, recipeId);
    }

    private async Task BroadcastStateAsync()
    {
        IReadOnlyList<PlayerState> players = _gameServer.Clients.Select(c => c.Player).ToList();
        byte[] delta = NetworkSerializer.WriteStateDelta(_gameServer.CurrentTick, _gameServer.TimeOfDay, players);
        foreach (ConnectedClient client in _gameServer.Clients)
        {
            await TrySendAsync(delta, client.EndPoint);
        }
    }

    private async Task BroadcastItemEntitiesAsync()
    {
        foreach (ConnectedClient client in _gameServer.Clients)
        {
            Vector3 center = client.Player.Position;
            List<ItemEntity> nearby = _gameServer.ItemEntities.GetNear(center, GameConstants.ItemEntitySyncRadius);
            byte[] packet = NetworkSerializer.WriteItemEntitiesDelta(_gameServer.CurrentTick, nearby);
            await TrySendAsync(packet, client.EndPoint);
        }
    }

    private async Task<bool> TrySendAsync(byte[] data, IPEndPoint endPoint)
    {
        try
        {
            await _udp.SendAsync(data, endPoint);
            return true;
        }
        catch (SocketException)
        {
            if (_gameServer.TryGetClientByEndpoint(endPoint, out ConnectedClient? client))
            {
                _gameServer.DisconnectClient(client.PlayerId);
            }

            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private async Task BroadcastBlockChangesAsync()
    {
        IReadOnlyList<BlockChange> changes = _gameServer.World.PendingBlockChanges;
        if (changes.Count == 0)
        {
            return;
        }

        foreach (BlockChange change in changes)
        {
            ChunkPosition chunkPosition = ChunkPosition.FromBlock(change.X, change.Z);
            _gameServer.InvalidateChunkPayloadCache(chunkPosition.X, chunkPosition.Z);

            byte[] packet = NetworkSerializer.WriteBlockChanged(change.X, change.Y, change.Z, change.BlockId, change.Axis);
            foreach (ConnectedClient client in _gameServer.Clients)
            {
                await TrySendAsync(packet, client.EndPoint);
            }
        }

        _gameServer.World.ClearPendingBlockChanges();
    }

    private async Task BroadcastFurnaceOutputsAsync()
    {
        IReadOnlyList<FurnaceStateChange> changes = _gameServer.PendingFurnaceChanges;
        if (changes.Count == 0)
        {
            return;
        }

        foreach (FurnaceStateChange change in changes)
        {
            byte[] packet = NetworkSerializer.WriteFurnaceOutput(change);
            foreach (ConnectedClient client in _gameServer.Clients)
            {
                await TrySendAsync(packet, client.EndPoint);
            }
        }

        _gameServer.ClearPendingFurnaceChanges();
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _udp.Dispose();
        _discovery?.Dispose();
        _networkLoopReady.Dispose();
        _cancellation.Dispose();
    }
}
