using System.Threading.Channels;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;

namespace AstroCraft.Client.Networking;

/// <summary>
/// Buffers compressed chunk payloads from the network thread and applies them on the main thread for meshing.
/// </summary>
internal sealed class ClientChunkIngestPipeline : IDisposable
{
    private readonly Channel<byte[]> _pendingChunks = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private int _queuedChunkCount;

    public int QueuedChunkCount => Volatile.Read(ref _queuedChunkCount);

    public bool HasPending => QueuedChunkCount > 0;

    public void EnqueueRaw(ReadOnlySpan<byte> payload)
    {
        byte[] copy = payload.ToArray();
        if (_pendingChunks.Writer.TryWrite(copy))
        {
            Interlocked.Increment(ref _queuedChunkCount);
        }
    }

    public int ProcessPending(Action<ChunkPosition, BlockId[]> applyChunk, int maxChunks)
    {
        int applied = 0;
        while (applied < maxChunks && _pendingChunks.Reader.TryRead(out byte[]? payload))
        {
            Interlocked.Decrement(ref _queuedChunkCount);
            (ChunkPosition position, BlockId[] blocks) = ClientMessageReader.ReadChunkData(payload);
            applyChunk(position, blocks);
            applied++;
        }

        return applied;
    }

    public void Reset()
    {
        while (_pendingChunks.Reader.TryRead(out _))
        {
            Interlocked.Decrement(ref _queuedChunkCount);
        }
    }

    public void Dispose()
    {
        _pendingChunks.Writer.TryComplete();
        Reset();
    }
}
