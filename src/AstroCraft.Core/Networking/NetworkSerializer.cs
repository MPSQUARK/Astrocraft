using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;

namespace AstroCraft.Core.Networking;

public static class NetworkSerializer
{
    public static byte[] WritePlayerInput(int playerId, PlayerInput input)
    {
        byte[] buffer = new byte[1 + 4 + 8 * 4 + 1 + 1 + 1 + 1 + 1 + 4];
        int offset = 0;
        buffer[offset++] = (byte)MessageType.PlayerInput;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), playerId);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), input.MoveForward);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), input.MoveRight);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), input.LookDeltaX);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), input.LookDeltaY);
        offset += 4;
        buffer[offset++] = input.Jump ? (byte)1 : (byte)0;
        buffer[offset++] = input.Sneak ? (byte)1 : (byte)0;
        buffer[offset++] = input.Sprint ? (byte)1 : (byte)0;
        buffer[offset++] = input.BreakBlock ? (byte)1 : (byte)0;
        buffer[offset++] = input.PlaceBlock ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), input.HotbarSelection);
        return buffer;
    }

    public static PlayerInput ReadPlayerInput(ReadOnlySpan<byte> payload)
    {
        int offset = 0;
        float moveForward = BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]);
        offset += 4;
        float moveRight = BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]);
        offset += 4;
        float lookX = BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]);
        offset += 4;
        float lookY = BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]);
        offset += 4;
        bool jump = payload[offset++] == 1;
        bool sneak = payload[offset++] == 1;
        bool sprint = payload[offset++] == 1;
        bool breakBlock = payload[offset++] == 1;
        bool placeBlock = payload[offset++] == 1;
        int hotbar = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
        return new PlayerInput(moveForward, moveRight, lookX, lookY, jump, sneak, sprint, breakBlock, placeBlock, hotbar);
    }

    public static byte[] WriteClientHello(string playerName)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(playerName);
        byte[] buffer = new byte[2 + nameBytes.Length];
        buffer[0] = (byte)MessageType.ClientHello;
        buffer[1] = (byte)nameBytes.Length;
        nameBytes.CopyTo(buffer.AsSpan(2));
        return buffer;
    }

    public static string ReadClientHello(ReadOnlySpan<byte> payload)
    {
        int length = payload[0];
        return Encoding.UTF8.GetString(payload.Slice(1, length));
    }

    public static byte[] WriteServerWelcome(int playerId, int tick, Vector3 spawnPosition)
    {
        byte[] buffer = new byte[1 + 4 + 4 + 12];
        int offset = 0;
        buffer[offset++] = (byte)MessageType.ServerWelcome;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), playerId);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), tick);
        offset += 4;
        WriteVector3(buffer.AsSpan(offset), spawnPosition);
        return buffer;
    }

    public static byte[] WriteStateDelta(int tick, IReadOnlyList<PlayerState> players)
    {
        int size = 1 + 4 + 2 + players.Count * (4 + 12 + 12 + 4 + 4 + 1 + 4 + 4 + 4);
        byte[] buffer = new byte[size];
        int offset = 0;
        buffer[offset++] = (byte)MessageType.StateDelta;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), tick);
        offset += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)players.Count);
        offset += 2;
        foreach (PlayerState player in players)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), player.PlayerId);
            offset += 4;
            WriteVector3(buffer.AsSpan(offset), player.Position);
            offset += 12;
            WriteVector3(buffer.AsSpan(offset), player.Velocity);
            offset += 12;
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), player.YawRadians);
            offset += 4;
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), player.PitchRadians);
            offset += 4;
            buffer[offset++] = player.IsOnGround ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), player.Survival.Health);
            offset += 4;
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), player.Survival.Oxygen);
            offset += 4;
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), player.Survival.Hunger);
            offset += 4;
        }

        return buffer;
    }

    public static byte[] WriteBlockChanged(int worldX, int worldY, int worldZ, BlockId blockId)
    {
        byte[] buffer = new byte[1 + 4 + 4 + 4 + 2];
        int offset = 0;
        buffer[offset++] = (byte)MessageType.BlockChanged;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), worldX);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), worldY);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), worldZ);
        offset += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)blockId);
        return buffer;
    }

    public static (int X, int Y, int Z, BlockId BlockId) ReadBlockChanged(ReadOnlySpan<byte> payload)
    {
        int x = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int y = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        int z = BinaryPrimitives.ReadInt32LittleEndian(payload[8..]);
        BlockId blockId = (BlockId)BinaryPrimitives.ReadUInt16LittleEndian(payload[12..]);
        return (x, y, z, blockId);
    }

    public static byte[] WriteChunkData(Chunk chunk)
    {
        int blockCount = chunk.Blocks.Length;
        byte[] buffer = new byte[1 + 4 + 4 + blockCount * 2];
        int offset = 0;
        buffer[offset++] = (byte)MessageType.ChunkData;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), chunk.Position.X);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), chunk.Position.Z);
        offset += 4;
        ReadOnlySpan<BlockId> blocks = chunk.Blocks;
        for (int i = 0; i < blockCount; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)blocks[i]);
            offset += 2;
        }

        return buffer;
    }

    public static MessageType ReadMessageType(ReadOnlySpan<byte> data) => (MessageType)data[0];

    public static byte[] WriteDiscoveryResponse(string serverName, int gamePort, int playerCount)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(serverName);
        byte[] buffer = new byte[NetworkProtocol.AnnounceMagic.Length + 1 + 2 + nameBytes.Length + 4 + 4];
        int offset = 0;
        Encoding.UTF8.GetBytes(NetworkProtocol.AnnounceMagic).CopyTo(buffer.AsSpan(offset));
        offset += NetworkProtocol.AnnounceMagic.Length;
        buffer[offset++] = (byte)nameBytes.Length;
        nameBytes.CopyTo(buffer.AsSpan(offset));
        offset += nameBytes.Length;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), gamePort);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), playerCount);
        return buffer;
    }

    private static void WriteVector3(Span<byte> destination, Vector3 value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination, value.X);
        BinaryPrimitives.WriteSingleLittleEndian(destination[4..], value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(destination[8..], value.Z);
    }
}
