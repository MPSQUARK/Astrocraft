using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Math;
using AstroCraft.Core.Entities;
using AstroCraft.Core.Furnaces;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;

namespace AstroCraft.Core.Networking;

public static class NetworkSerializer
{
    public static byte[] WritePlayerInput(int playerId, PlayerInput input)
    {
        byte[] buffer = new byte[1 + 4 + 10 * 4 + 1 + 1 + 1 + 1 + 1 + 1 + 4 + 4 + 4 + 1];
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
        buffer[offset++] = input.UseItem ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), input.HotbarSelection);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), input.YawRadians);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), input.PitchRadians);
        offset += 4;
        buffer[offset++] = input.RotateBlock ? (byte)1 : (byte)0;
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
        bool useItem = false;
        if (payload.Length - offset >= 13)
        {
            useItem = payload[offset++] == 1;
        }

        int hotbar = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
        offset += 4;
        float yaw = float.NaN;
        float pitch = float.NaN;
        if (payload.Length >= offset + 8)
        {
            yaw = BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]);
            pitch = BinaryPrimitives.ReadSingleLittleEndian(payload[(offset + 4)..]);
            offset += 8;
        }

        bool rotateBlock = payload.Length > offset && payload[offset++] == 1;

        return new PlayerInput(moveForward, moveRight, lookX, lookY, jump, sneak, sprint, breakBlock, placeBlock, hotbar, yaw, pitch, useItem, rotateBlock);
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

    public static byte[] WriteRequestChunkStream() => [(byte)MessageType.RequestChunkStream];

    public static byte[] WriteRequestChunks(IReadOnlyList<ChunkPosition> positions)
    {
        int count = System.Math.Min(positions.Count, GameConstants.MaxChunkRequestsPerPacket);
        byte[] buffer = new byte[1 + 2 + count * 8];
        buffer[0] = (byte)MessageType.RequestChunks;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(1), (ushort)count);
        int offset = 3;
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), positions[i].X);
            offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), positions[i].Z);
            offset += 4;
        }

        return buffer;
    }

    public static ChunkPosition[] ReadRequestChunks(ReadOnlySpan<byte> payload)
    {
        int count = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        ChunkPosition[] positions = new ChunkPosition[count];
        int offset = 2;
        for (int i = 0; i < count; i++)
        {
            int x = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            offset += 4;
            int z = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            offset += 4;
            positions[i] = new ChunkPosition(x, z);
        }

        return positions;
    }

    public static byte[] WriteCraftRequest(string recipeId)
    {
        byte[] recipeBytes = Encoding.UTF8.GetBytes(recipeId);
        byte[] buffer = new byte[2 + recipeBytes.Length];
        buffer[0] = (byte)MessageType.CraftRequest;
        buffer[1] = (byte)recipeBytes.Length;
        recipeBytes.CopyTo(buffer.AsSpan(2));
        return buffer;
    }

    public static string ReadCraftRequest(ReadOnlySpan<byte> payload)
    {
        int length = payload[0];
        return Encoding.UTF8.GetString(payload.Slice(1, length));
    }

    public static string ReadClientHello(ReadOnlySpan<byte> payload)
    {
        int length = payload[0];
        return Encoding.UTF8.GetString(payload.Slice(1, length));
    }

    public static byte[] WriteServerWelcome(int playerId, int tick, Vector3 spawnPosition, int worldSeed, bool flatWorld)
    {
        byte[] buffer = new byte[1 + 4 + 4 + 12 + 4 + 1];
        int offset = 0;
        buffer[offset++] = (byte)MessageType.ServerWelcome;
        WriteServerWelcomePayload(buffer.AsSpan(offset), playerId, tick, spawnPosition, worldSeed, flatWorld);
        return buffer;
    }

    public static (int PlayerId, int Tick, Vector3 Spawn, int WorldSeed, bool FlatWorld) ReadServerWelcome(ReadOnlySpan<byte> payload)
    {
        int playerId = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int tick = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        Vector3 spawn = ReadVector3(payload[8..]);
        int worldSeed = BinaryPrimitives.ReadInt32LittleEndian(payload[20..]);
        bool flatWorld = payload[24] == 1;
        return (playerId, tick, spawn, worldSeed, flatWorld);
    }

    public static byte[] WriteStateDelta(int tick, float timeOfDay, IReadOnlyList<PlayerState> players)
    {
        int size = 1 + 4 + 4 + 2 + players.Count * (4 + 12 + 12 + 4 + 4 + 1 + 4 + 4 + 4 + 1 + 1);
        byte[] buffer = new byte[size];
        int offset = 0;
        buffer[offset++] = (byte)MessageType.StateDelta;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), tick);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), timeOfDay);
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
            buffer[offset++] = player.Survival.IsDead ? (byte)1 : (byte)0;
            buffer[offset++] = (byte)player.Survival.RespawnTicksRemaining;
        }

        return buffer;
    }

    public static byte[] WriteBlockChanged(int worldX, int worldY, int worldZ, BlockId blockId, BlockAxis axis = BlockAxis.Y)
    {
        byte[] buffer = new byte[1 + 4 + 4 + 4 + 2 + 1];
        int offset = 0;
        buffer[offset++] = (byte)MessageType.BlockChanged;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), worldX);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), worldY);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), worldZ);
        offset += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)blockId);
        offset += 2;
        buffer[offset] = (byte)axis;
        return buffer;
    }

    public static (int X, int Y, int Z, BlockId BlockId, BlockAxis Axis) ReadBlockChanged(ReadOnlySpan<byte> payload)
    {
        int x = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int y = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        int z = BinaryPrimitives.ReadInt32LittleEndian(payload[8..]);
        BlockId blockId = (BlockId)BinaryPrimitives.ReadUInt16LittleEndian(payload[12..]);
        BlockAxis axis = payload.Length > 14 ? (BlockAxis)payload[14] : BlockAxis.Y;
        return (x, y, z, blockId, axis);
    }

    public static byte[] WriteChunkData(Chunk chunk)
    {
        byte[] encoded = ChunkDataCodec.Encode(chunk);
        return WriteChunkDataFromEncoded(encoded);
    }

    public static byte[] WriteChunkDataFromEncoded(byte[] encoded)
    {
        byte[] buffer = new byte[encoded.Length + 1];
        buffer[0] = (byte)MessageType.ChunkData;
        encoded.CopyTo(buffer.AsSpan(1));
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

    public static byte[] WriteItemEntitiesDelta(int tick, IReadOnlyList<ItemEntity> entities)
    {
        const int entityBytes = 4 + 12 + 12 + 2 + 2 + 1 + 4;
        byte[] buffer = new byte[1 + 4 + 2 + entities.Count * entityBytes];
        int offset = 0;
        buffer[offset++] = (byte)MessageType.ItemEntitiesDelta;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), tick);
        offset += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)entities.Count);
        offset += 2;
        foreach (ItemEntity entity in entities)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), entity.Id);
            offset += 4;
            WriteVector3(buffer.AsSpan(offset), entity.Position);
            offset += 12;
            WriteVector3(buffer.AsSpan(offset), entity.Velocity);
            offset += 12;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)entity.Stack.BlockId);
            offset += 2;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)entity.Stack.ItemId);
            offset += 2;
            buffer[offset++] = (byte)entity.Stack.Count;
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), entity.SpinRadians);
            offset += 4;
        }

        return buffer;
    }

    public static byte[] WriteFurnaceOutput(FurnaceStateChange change)
    {
        byte[] buffer = new byte[1 + 4 + 4 + 4 + 2 + 2 + 1];
        int offset = 0;
        buffer[offset++] = (byte)MessageType.FurnaceOutput;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), change.X);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), change.Y);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), change.Z);
        offset += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)change.OutputBlockId);
        offset += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)change.OutputItemId);
        offset += 2;
        buffer[offset] = (byte)change.OutputCount;
        return buffer;
    }

    public static FurnaceStateChange ReadFurnaceOutput(ReadOnlySpan<byte> payload)
    {
        int x = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int y = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        int z = BinaryPrimitives.ReadInt32LittleEndian(payload[8..]);
        BlockId outputBlockId = (BlockId)BinaryPrimitives.ReadUInt16LittleEndian(payload[12..]);
        ItemId outputItemId = (ItemId)BinaryPrimitives.ReadUInt16LittleEndian(payload[14..]);
        int outputCount = payload[16];
        return new FurnaceStateChange(x, y, z, outputBlockId, outputItemId, outputCount);
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

    private static void WriteServerWelcomePayload(Span<byte> destination, int playerId, int tick, Vector3 spawnPosition, int worldSeed, bool flatWorld)
    {
        int offset = 0;
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], playerId);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], tick);
        offset += 4;
        WriteVector3(destination[offset..], spawnPosition);
        offset += 12;
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], worldSeed);
        offset += 4;
        destination[offset] = flatWorld ? (byte)1 : (byte)0;
    }

    private static Vector3 ReadVector3(ReadOnlySpan<byte> source) =>
        new(
            BinaryPrimitives.ReadSingleLittleEndian(source),
            BinaryPrimitives.ReadSingleLittleEndian(source[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(source[8..]));

    private static void WriteVector3(Span<byte> destination, Vector3 value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination, value.X);
        BinaryPrimitives.WriteSingleLittleEndian(destination[4..], value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(destination[8..], value.Z);
    }
}
