using System.Net;
using System.Net.Sockets;
using AstroCraft.Core;
using AstroCraft.Core.Discovery;
using AstroCraft.Core.Networking;
using AstroCraft.Core.Players;
using AstroCraft.Core.Server;
using AstroCraft.Core.World;
using AstroCraft.Core.Simulation;

namespace AstroCraft.Server.Hosting;

public sealed class GameServerHost : IDisposable
{
    private readonly GameServer _gameServer;
    private readonly UdpClient _udp;
    private readonly LanDiscoveryService _discovery;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _serverName;
    private readonly int _port;

    public GameServerHost(string serverName, int port, int seed, bool flatWorld, int discoveryPort = GameConstants.DiscoveryPort)
    {
        _serverName = serverName;
        _port = port;
        _gameServer = new GameServer(seed, flatWorld);
        _udp = new UdpClient(port);
        _discovery = new LanDiscoveryService(serverName, port, () => _gameServer.Clients.Count, discoveryPort);
        _discovery.Start();
    }

    public GameServer GameServer => _gameServer;

    public void Start() => _ = Task.Run(RunAsync);

    private async Task RunAsync()
    {
        DateTime nextTick = DateTime.UtcNow;
        while (!_cancellation.IsCancellationRequested)
        {
            await DrainNetworkAsync();
            if (DateTime.UtcNow >= nextTick)
            {
                _gameServer.Tick();
                await BroadcastStateAsync();
                await BroadcastBlockChangesAsync();
                nextTick = nextTick.AddMilliseconds(GameConstants.TickDurationSeconds * 1000);
            }

            await Task.Delay(1);
        }
    }

    private async Task DrainNetworkAsync()
    {
        while (_udp.Available > 0)
        {
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
        return SendWelcomeAndChunksAsync(endPoint, playerId);
    }

    private async Task SendWelcomeAndChunksAsync(IPEndPoint endPoint, int playerId)
    {
        await SendWelcomeAsync(endPoint, playerId);
        await SendNearbyChunksAsync(endPoint);
    }

    private async Task SendWelcomeAsync(IPEndPoint endPoint, int playerId)
    {
        ConnectedClient client = _gameServer.Clients.First(c => c.PlayerId == playerId);
        byte[] welcome = NetworkSerializer.WriteServerWelcome(
            playerId,
            _gameServer.CurrentTick,
            client.Player.Position,
            _gameServer.WorldSeed,
            _gameServer.IsFlatWorld);
        await _udp.SendAsync(welcome, endPoint);
    }

    private async Task SendNearbyChunksAsync(IPEndPoint endPoint)
    {
        foreach (var chunk in _gameServer.World.LoadedChunks)
        {
            byte[] data = NetworkSerializer.WriteChunkData(chunk);
            await _udp.SendAsync(data, endPoint);
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

    private async Task BroadcastStateAsync()
    {
        IReadOnlyList<PlayerState> players = _gameServer.Clients.Select(c => c.Player).ToList();
        byte[] delta = NetworkSerializer.WriteStateDelta(_gameServer.CurrentTick, players);
        foreach (ConnectedClient client in _gameServer.Clients)
        {
            await _udp.SendAsync(delta, client.EndPoint);
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
            byte[] packet = NetworkSerializer.WriteBlockChanged(change.X, change.Y, change.Z, change.BlockId);
            foreach (ConnectedClient client in _gameServer.Clients)
            {
                await _udp.SendAsync(packet, client.EndPoint);
            }
        }

        _gameServer.World.ClearPendingBlockChanges();
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _udp.Dispose();
        _discovery.Dispose();
        _cancellation.Dispose();
    }
}
