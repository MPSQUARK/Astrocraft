using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Furnaces;
using AstroCraft.Core.Networking;
using AstroCraft.Core.Players;
using AstroCraft.Core.Server;

namespace AstroCraft.Tests;

public class FurnaceTests
{
    private readonly SmeltingRecipeRegistry _recipes = SmeltingRecipeRegistry.CreateDefault();
    private readonly FurnaceFuelRegistry _fuels = FurnaceFuelRegistry.CreateDefault();

    [Fact]
    public void FurnaceState_InitializesEmpty()
    {
        FurnaceState state = new();

        Assert.True(state.Input.IsEmpty);
        Assert.True(state.Fuel.IsEmpty);
        Assert.True(state.Output.IsEmpty);
        Assert.Equal(0, state.ProgressTicks);
        Assert.Equal(0, state.FuelTicksRemaining);
    }

    [Fact]
    public void SmeltingRecipe_IronOre_ToIronIngot_200Ticks()
    {
        SmeltingRecipeDefinition recipe = _recipes.All.Single(r => r.Id == "iron_ore");

        Assert.Equal(StackKey.Block(BlockId.IronOre), recipe.Input);
        Assert.Equal(StackKey.Item(ItemId.IronIngot), recipe.Output);
        Assert.Equal(200, recipe.CookTicks);
    }

    [Fact]
    public void FurnaceSystem_ConsumesCoalAsFuel()
    {
        FurnaceState state = new();
        state.Input.BlockId = BlockId.IronOre;
        state.Input.Count = 1;
        state.Fuel.ItemId = ItemId.Coal;
        state.Fuel.Count = 1;

        bool changed = FurnaceSystem.Tick(state, _recipes, _fuels);

        Assert.True(changed);
        Assert.True(state.Fuel.IsEmpty);
        Assert.Equal(1599, state.FuelTicksRemaining);
        Assert.Equal(1, state.ProgressTicks);
    }

    [Fact]
    public void FurnaceSystem_AcceptsPlanksAsFuel()
    {
        FurnaceState state = new();
        state.Input.BlockId = BlockId.IronOre;
        state.Input.Count = 1;
        state.Fuel.BlockId = BlockId.Planks;
        state.Fuel.Count = 1;

        bool changed = FurnaceSystem.Tick(state, _recipes, _fuels);

        Assert.True(changed);
        Assert.True(state.Fuel.IsEmpty);
        Assert.Equal(299, state.FuelTicksRemaining);
    }

    [Fact]
    public void FurnaceSystem_ProducesIronIngot_After200Ticks()
    {
        FurnaceState state = new();
        state.Input.BlockId = BlockId.IronOre;
        state.Input.Count = 1;
        state.FuelTicksRemaining = 200;

        for (int i = 0; i < 200; i++)
        {
            FurnaceSystem.Tick(state, _recipes, _fuels);
        }

        Assert.True(state.Input.IsEmpty);
        Assert.Equal(ItemId.IronIngot, state.Output.ItemId);
        Assert.Equal(1, state.Output.Count);
        Assert.Equal(0, state.ProgressTicks);
    }

    [Fact]
    public void GameServer_TracksFurnaceByPosition()
    {
        GameServer server = new(seed: 1, flatWorld: true);
        server.World.TrySetBlock(5, 25, 5, BlockId.Furnace);
        FurnaceState furnace = server.GetOrCreateFurnace(5, 25, 5);
        furnace.Input.BlockId = BlockId.IronOre;
        furnace.Input.Count = 1;

        Assert.True(server.TryGetFurnace(5, 25, 5, out FurnaceState? found));
        Assert.Same(furnace, found);
        Assert.Equal(BlockId.IronOre, found!.Input.BlockId);
    }

    [Fact]
    public void GameServer_Smelting_EmitsPendingFurnaceChange()
    {
        GameServer server = new(seed: 1, flatWorld: true);
        server.World.TrySetBlock(0, 25, 0, BlockId.Furnace);
        FurnaceState furnace = server.GetOrCreateFurnace(0, 25, 0);
        furnace.Input.BlockId = BlockId.IronOre;
        furnace.Input.Count = 1;
        furnace.FuelTicksRemaining = 200;

        for (int i = 0; i < 200; i++)
        {
            server.Tick();
        }

        Assert.Equal(ItemId.IronIngot, furnace.Output.ItemId);
        Assert.Single(server.PendingFurnaceChanges);
        FurnaceStateChange change = server.PendingFurnaceChanges[0];
        Assert.Equal(0, change.X);
        Assert.Equal(25, change.Y);
        Assert.Equal(0, change.Z);
        Assert.Equal(ItemId.IronIngot, change.OutputItemId);
        Assert.Equal(1, change.OutputCount);
    }

    [Fact]
    public void GameServer_UnregistersFurnaceWhenBroken()
    {
        GameServer server = new(seed: 1, flatWorld: true);
        server.World.TrySetBlock(3, 25, 3, BlockId.Furnace);
        server.GetOrCreateFurnace(3, 25, 3);
        server.World.TrySetBlock(3, 25, 3, BlockId.Air);
        server.Tick();

        Assert.False(server.TryGetFurnace(3, 25, 3, out _));
    }

    [Fact]
    public void NetworkSerializer_FurnaceOutput_RoundTrips()
    {
        FurnaceStateChange original = new(10, 20, 30, BlockId.Air, ItemId.IronIngot, 1);
        byte[] packet = NetworkSerializer.WriteFurnaceOutput(original);
        FurnaceStateChange parsed = NetworkSerializer.ReadFurnaceOutput(packet.AsSpan(1));

        Assert.Equal(original.X, parsed.X);
        Assert.Equal(original.Y, parsed.Y);
        Assert.Equal(original.Z, parsed.Z);
        Assert.Equal(original.OutputBlockId, parsed.OutputBlockId);
        Assert.Equal(original.OutputItemId, parsed.OutputItemId);
        Assert.Equal(original.OutputCount, parsed.OutputCount);
    }
}
