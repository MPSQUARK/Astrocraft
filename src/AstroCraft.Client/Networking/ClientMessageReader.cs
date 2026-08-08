using System.Buffers.Binary;
using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;
using AstroCraft.Core.Players;
using AstroCraft.Core.World;

namespace AstroCraft.Client.Networking;

internal static class ClientMessageReader
{
    public static (int PlayerId, int Tick, Vector3 Spawn) ReadServerWelcome(ReadOnlySpan<byte> payload)
    {
        int playerId = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int tick = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        Vector3 spawn = ReadVector3(payload[8..]);
        return (playerId, tick, spawn);
    }

    public static (int Tick, IReadOnlyList<PlayerStateSnapshot> Players) ReadStateDelta(ReadOnlySpan<byte> payload)
    {
        int tick = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
        int offset = 6;
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

            players.Add(new PlayerStateSnapshot(
                playerId,
                position,
                velocity,
                yaw,
                pitch,
                onGround,
                health,
                oxygen,
                hunger));
        }

        return (tick, players);
    }

    public static (ChunkPosition Position, BlockId[] Blocks) ReadChunkData(ReadOnlySpan<byte> payload)
    {
        int chunkX = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int chunkZ = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        ReadOnlySpan<byte> blockBytes = payload[8..];
        int blockCount = GameConstants.ChunkSizeX * GameConstants.ChunkSizeY * GameConstants.ChunkSizeZ;
        BlockId[] blocks = new BlockId[blockCount];

        for (int i = 0; i < blockCount; i++)
        {
            blocks[i] = (BlockId)BinaryPrimitives.ReadUInt16LittleEndian(blockBytes[(i * 2)..]);
        }

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
    float Hunger);
