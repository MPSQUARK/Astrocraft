using System.Buffers.Binary;
using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Entities;
using AstroCraft.Core.Math;
using AstroCraft.Core.Networking;
using AstroCraft.Core.Players;
using AstroCraft.Core.World;

namespace AstroCraft.Client.Networking;

internal static class ClientMessageReader
{
    public static (int PlayerId, int Tick, Vector3 Spawn, int WorldSeed, bool FlatWorld) ReadServerWelcome(ReadOnlySpan<byte> payload)
    {
        int playerId = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int tick = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        Vector3 spawn = ReadVector3(payload[8..]);
        int worldSeed = BinaryPrimitives.ReadInt32LittleEndian(payload[20..]);
        bool flatWorld = payload[24] == 1;
        return (playerId, tick, spawn, worldSeed, flatWorld);
    }

    public static (int Tick, float TimeOfDay, IReadOnlyList<PlayerStateSnapshot> Players) ReadStateDelta(ReadOnlySpan<byte> payload)
    {
        int tick = BinaryPrimitives.ReadInt32LittleEndian(payload);
        float timeOfDay = BinaryPrimitives.ReadSingleLittleEndian(payload[4..]);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]);
        int offset = 10;
        List<PlayerStateSnapshot> players = new(count);

        for (int i = 0; i < count; i++)
        {
            int playerId = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            offset += 4;
            Vector3 position = ReadVector3(payload[offset..]);
            offset += 12;
            Vector3 velocity = ReadVector3(payload[offset..]);
            offset += 12;
            float yaw = BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]);
            offset += 4;
            float pitch = BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]);
            offset += 4;
            bool onGround = payload[offset++] == 1;
            float health = BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]);
            offset += 4;
            float oxygen = BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]);
            offset += 4;
            float hunger = BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]);
            offset += 4;
            bool isDead = payload[offset++] == 1;
            int respawnTicksRemaining = payload[offset++];

            players.Add(new PlayerStateSnapshot(
                playerId,
                position,
                velocity,
                yaw,
                pitch,
                onGround,
                health,
                oxygen,
                hunger,
                isDead,
                respawnTicksRemaining));
        }

        return (tick, timeOfDay, players);
    }

    public static (int Tick, IReadOnlyList<ItemEntitySnapshot> Entities) ReadItemEntitiesDelta(ReadOnlySpan<byte> payload)
    {
        int tick = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
        int offset = 6;
        List<ItemEntitySnapshot> entities = new(count);

        for (int i = 0; i < count; i++)
        {
            int id = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            offset += 4;
            Vector3 position = ReadVector3(payload[offset..]);
            offset += 12;
            Vector3 velocity = ReadVector3(payload[offset..]);
            offset += 12;
            BlockId blockId = (BlockId)BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            offset += 2;
            ItemId itemId = (ItemId)BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            offset += 2;
            int stackCount = payload[offset++];
            float spin = BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]);
            offset += 4;
            entities.Add(new ItemEntitySnapshot(id, position, velocity, blockId, itemId, stackCount, spin));
        }

        return (tick, entities);
    }

    public static (ChunkPosition Position, BlockId[] Blocks) ReadChunkData(ReadOnlySpan<byte> payload)
    {
        int chunkX = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int chunkZ = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        BlockId[] blocks = ChunkDataCodec.Decode(payload);
        return (new ChunkPosition(chunkX, chunkZ), blocks);
    }

    private static Vector3 ReadVector3(ReadOnlySpan<byte> source) =>
        new(
            BinaryPrimitives.ReadSingleLittleEndian(source),
            BinaryPrimitives.ReadSingleLittleEndian(source[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(source[8..]));
}

internal readonly record struct PlayerStateSnapshot(
    int PlayerId,
    Vector3 Position,
    Vector3 Velocity,
    float YawRadians,
    float PitchRadians,
    bool IsOnGround,
    float Health,
    float Oxygen,
    float Hunger,
    bool IsDead,
    int RespawnTicksRemaining);
