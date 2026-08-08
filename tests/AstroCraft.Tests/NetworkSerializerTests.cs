using AstroCraft.Core.Networking;
using AstroCraft.Core.Simulation;

namespace AstroCraft.Tests;

public class NetworkSerializerTests
{
    [Fact]
    public void PlayerInput_RoundTripsThroughSerializer()
    {
        PlayerInput original = new(1f, -0.5f, 0.1f, -0.2f, true, false, true, true, false, 3);
        byte[] packet = NetworkSerializer.WritePlayerInput(5, original);
        PlayerInput parsed = NetworkSerializer.ReadPlayerInput(packet.AsSpan(5));

        Assert.Equal(original.MoveForward, parsed.MoveForward);
        Assert.Equal(original.MoveRight, parsed.MoveRight);
        Assert.Equal(original.Jump, parsed.Jump);
        Assert.Equal(original.HotbarSelection, parsed.HotbarSelection);
    }

    [Fact]
    public void BlockChanged_RoundTripsThroughSerializer()
    {
        byte[] packet = NetworkSerializer.WriteBlockChanged(10, 20, 30, Core.Blocks.BlockId.Stone);
        (int x, int y, int z, Core.Blocks.BlockId block) = NetworkSerializer.ReadBlockChanged(packet.AsSpan(1));

        Assert.Equal(10, x);
        Assert.Equal(20, y);
        Assert.Equal(30, z);
        Assert.Equal(Core.Blocks.BlockId.Stone, block);
    }
}
