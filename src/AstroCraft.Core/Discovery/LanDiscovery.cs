using System.Net;
using System.Net.Sockets;
using System.Text;
using AstroCraft.Core.Networking;

namespace AstroCraft.Core.Discovery;

public sealed class LanDiscoveryService : IDisposable
{
    private readonly UdpClient _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _serverName;
    private readonly int _gamePort;
    private readonly Func<int> _playerCountProvider;

    public LanDiscoveryService(string serverName, int gamePort, Func<int> playerCountProvider, int discoveryPort = GameConstants.DiscoveryPort)
    {
        _serverName = serverName;
        _gamePort = gamePort;
        _playerCountProvider = playerCountProvider;
        _listener = new UdpClient(discoveryPort) { EnableBroadcast = true };
    }

    public void Start() => _ = Task.Run(ListenLoopAsync);

    private async Task ListenLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result = await _listener.ReceiveAsync(_cancellation.Token);
                string message = Encoding.UTF8.GetString(result.Buffer);
                if (message != NetworkProtocol.DiscoveryMagic)
                {
                    continue;
                }

                byte[] response = NetworkSerializer.WriteDiscoveryResponse(_serverName, _gamePort, _playerCountProvider());
                await _listener.SendAsync(response, result.RemoteEndPoint, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _listener.Dispose();
        _cancellation.Dispose();
    }
}

public sealed class LanDiscoveryClient : IDisposable
{
    private readonly UdpClient _client = new() { EnableBroadcast = true };

    public async Task<IReadOnlyList<DiscoveredServer>> DiscoverAsync(int timeoutMilliseconds = 2000, int discoveryPort = GameConstants.DiscoveryPort)
    {
        byte[] request = Encoding.UTF8.GetBytes(NetworkProtocol.DiscoveryMagic);
        await _client.SendAsync(request, new IPEndPoint(IPAddress.Broadcast, discoveryPort));

        List<DiscoveredServer> servers = new();
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (_client.Available == 0)
            {
                await Task.Delay(50);
                continue;
            }

            UdpReceiveResult result = await _client.ReceiveAsync();
            if (!Encoding.UTF8.GetString(result.Buffer).StartsWith(NetworkProtocol.AnnounceMagic, StringComparison.Ordinal))
            {
                continue;
            }

            ReadOnlySpan<byte> payload = result.Buffer.AsSpan(NetworkProtocol.AnnounceMagic.Length);
            int nameLength = payload[0];
            string name = Encoding.UTF8.GetString(payload.Slice(1, nameLength));
            int gamePort = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(payload[(1 + nameLength)..]);
            int playerCount = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(payload[(5 + nameLength)..]);
            servers.Add(new DiscoveredServer(name, result.RemoteEndPoint.Address.ToString(), gamePort, playerCount));
        }

        return servers;
    }

    public void Dispose() => _client.Dispose();
}

public readonly record struct DiscoveredServer(string Name, string Address, int Port, int PlayerCount);
