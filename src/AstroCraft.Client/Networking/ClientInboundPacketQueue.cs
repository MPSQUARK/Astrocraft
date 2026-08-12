using System.Collections.Concurrent;

namespace AstroCraft.Client.Networking;

/// <summary>
/// Queues inbound UDP payloads on the main/network thread so bursty chunk sends can be processed incrementally.
/// </summary>
internal sealed class ClientInboundPacketQueue
{
    private readonly ConcurrentQueue<byte[]> _packets = new();
    private int _queuedPacketCount;

    public int QueuedPacketCount => Volatile.Read(ref _queuedPacketCount);

    public void Enqueue(byte[] packet)
    {
        _packets.Enqueue(packet);
        Interlocked.Increment(ref _queuedPacketCount);
    }

    public bool TryDequeue(out byte[] packet)
    {
        if (_packets.TryDequeue(out packet!))
        {
            Interlocked.Decrement(ref _queuedPacketCount);
            return true;
        }

        packet = Array.Empty<byte>();
        return false;
    }
}
