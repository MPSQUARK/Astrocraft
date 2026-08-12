namespace AstroCraft.Core.Networking;

public enum MessageType : byte
{
    ClientHello = 1,
    ServerWelcome = 2,
    PlayerInput = 3,
    StateDelta = 4,
    FullSnapshot = 5,
    BlockChanged = 6,
    ChunkData = 7,
    PlayerJoined = 8,
    PlayerLeft = 9,
    DiscoveryRequest = 10,
    DiscoveryResponse = 11,
    Disconnect = 12,
    ItemEntitiesDelta = 13,
    RequestChunkStream = 14,
    CraftRequest = 15,
    FurnaceOutput = 16,
    RequestChunks = 17,
}

public static class NetworkProtocol
{
    public const string DiscoveryMagic = "ASTROCRAFT_DISCOVER";
    public const string AnnounceMagic = "ASTROCRAFT_ANNOUNCE";
}
