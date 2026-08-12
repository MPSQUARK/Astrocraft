using AstroCraft.Core;
using AstroCraft.Core.Math;
using AstroCraft.Core.Networking;

namespace AstroCraft.Client.Networking;

/// <summary>
/// Tracks which chunks the client wants, has received, and has already requested from the server.
/// </summary>
public sealed class ClientChunkInterestTracker
{
    private readonly HashSet<ChunkPosition> _wanted = new();
    private readonly HashSet<ChunkPosition> _received = new();
    private readonly HashSet<ChunkPosition> _pendingRequest = new();
    private readonly Dictionary<ChunkPosition, DateTime> _pendingRequestUtc = new();
    private readonly List<ChunkPosition> _scratchWanted = new();
    private readonly List<ChunkPosition> _scratchUnload = new();
    private ChunkPosition _lastCenter = new(int.MinValue, int.MinValue);
    private float _lastYaw = float.NaN;

    public IReadOnlyCollection<ChunkPosition> Wanted => _wanted;

    public int WantedCount => _wanted.Count;

    public int ReceivedCount => _received.Count;

    public int PendingRequestCount => _pendingRequest.Count;

    public void Reset()
    {
        _wanted.Clear();
        _received.Clear();
        _pendingRequest.Clear();
        _pendingRequestUtc.Clear();
        _lastCenter = new ChunkPosition(int.MinValue, int.MinValue);
        _lastYaw = float.NaN;
    }

    public void ClearReceived()
    {
        _received.Clear();
        _pendingRequest.Clear();
        _pendingRequestUtc.Clear();
    }

    public void ExpireStalePendingRequests(TimeSpan timeout)
    {
        if (_pendingRequest.Count == 0)
        {
            return;
        }

        DateTime cutoff = DateTime.UtcNow - timeout;
        foreach (KeyValuePair<ChunkPosition, DateTime> entry in _pendingRequestUtc)
        {
            if (entry.Value < cutoff)
            {
                _pendingRequest.Remove(entry.Key);
            }
        }

        List<ChunkPosition> staleKeys = new();
        foreach (ChunkPosition position in _pendingRequestUtc.Keys)
        {
            if (!_pendingRequest.Contains(position))
            {
                staleKeys.Add(position);
            }
        }

        foreach (ChunkPosition position in staleKeys)
        {
            _pendingRequestUtc.Remove(position);
        }
    }

    public bool UpdateInterest(int centerBlockX, int centerBlockZ, float yawRadians, bool force)
    {
        ChunkPosition center = ChunkPosition.FromBlock(centerBlockX, centerBlockZ);
        if (!force
            && center == _lastCenter
            && !float.IsNaN(_lastYaw)
            && MathF.Abs(yawRadians - _lastYaw) < 0.35f)
        {
            return false;
        }

        _lastCenter = center;
        _lastYaw = yawRadians;

        _scratchWanted.Clear();
        ChunkViewSelector.CollectInterestChunks(centerBlockX, centerBlockZ, yawRadians, _scratchWanted);

        _wanted.Clear();
        foreach (ChunkPosition position in _scratchWanted)
        {
            _wanted.Add(position);
        }

        return true;
    }

    public List<ChunkPosition> CollectChunksToRequest(int maxCount)
    {
        List<ChunkPosition> requests = new(maxCount);
        foreach (ChunkPosition position in _wanted)
        {
            if (requests.Count >= maxCount)
            {
                break;
            }

            if (_received.Contains(position) || _pendingRequest.Contains(position))
            {
                continue;
            }

            requests.Add(position);
            _pendingRequest.Add(position);
            _pendingRequestUtc[position] = DateTime.UtcNow;
        }

        return requests;
    }

    public void MarkReceived(ChunkPosition position)
    {
        _received.Add(position);
        _pendingRequest.Remove(position);
        _pendingRequestUtc.Remove(position);
    }

    public IReadOnlyList<ChunkPosition> CollectChunksToUnload()
    {
        _scratchUnload.Clear();
        foreach (ChunkPosition position in _received)
        {
            if (!_wanted.Contains(position))
            {
                _scratchUnload.Add(position);
            }
        }

        return _scratchUnload;
    }

    public void MarkUnloaded(ChunkPosition position)
    {
        _received.Remove(position);
        _pendingRequest.Remove(position);
        _pendingRequestUtc.Remove(position);
    }
}
