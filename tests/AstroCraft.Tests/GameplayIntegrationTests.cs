using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Furnaces;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;

namespace AstroCraft.Tests;

public class GameplayIntegrationTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;
    private readonly BlockRegistry _blockRegistry = BlockRegistry.CreateDefault();
    private readonly RecipeRegistry _recipeRegistry = RecipeRegistry.CreateDefault();
    private readonly SmeltingRecipeRegistry _smeltingRecipes = SmeltingRecipeRegistry.CreateDefault();
    private readonly FurnaceFuelRegistry _furnaceFuels = FurnaceFuelRegistry.CreateDefault();

    public GameplayIntegrationTests(FlatWorldFixture flat) => _flat = flat;

    /// <summary>T137: mine coal ore -> get coal item -> craft torch recipe -> verify torch in inventory.</summary>
    [Fact]
    public void T137_MineCoalOre_CraftTorch_VerifyTorchInInventory()
    {
        GameWorld world = _flat.CreateWorld();
        BlockInteractionSystem interaction = new(_blockRegistry);
        PlayerState player = CreatePlayerLookingDown();
        PlayerInventory inventory = player.Inventory;
        inventory.TryAddItem(ItemId.Stick, 1);

        world.TrySetBlock(0, 25, 0, BlockId.CoalOre);
        PlayerInput holdBreak = CreateHoldBreakInput();

        bool broken = BreakBlockWithRepeatedUpdates(interaction, player, world, holdBreak);
        Assert.True(broken);
        Assert.Equal(BlockId.Air, world.GetBlock(0, 25, 0));

        StackKey drop = _blockRegistry.GetDropStack(BlockId.CoalOre);
        Assert.Equal(ItemId.Coal, drop.ItemId);
        Assert.True(inventory.TryAddItem(drop.ItemId, 1));

        bool crafted = inventory.TryCraft("torch", _recipeRegistry);
        Assert.True(crafted);

        int torchCount = CountBlockInInventory(inventory, BlockId.Torch);
        Assert.Equal(4, torchCount);
        Assert.False(inventory.TryRemoveItem(ItemId.Coal, 1));
        Assert.False(inventory.TryRemoveItem(ItemId.Stick, 1));
    }

    /// <summary>T138: craft wooden pick -> mine iron ore -> smelt iron ore in FurnaceSystem -> get iron ingot.</summary>
    [Fact]
    public void T138_CraftWoodenPick_MineIronOre_SmeltToIronIngot()
    {
        GameWorld world = _flat.CreateWorld();
        BlockInteractionSystem interaction = new(_blockRegistry);
        PlayerState player = CreatePlayerLookingDown();
        PlayerInventory inventory = player.Inventory;

        inventory.TryAddBlock(BlockId.Planks, 3);
        inventory.TryAddItem(ItemId.Stick, 2);
        Assert.True(inventory.TryCraft("wooden_pickaxe", _recipeRegistry));
        Assert.Equal(ItemId.WoodenPickaxe, inventory.Hotbar[0].ItemId);

        EquipTool(player, ItemId.WoodenPickaxe);
        world.TrySetBlock(0, 25, 0, BlockId.IronOre);
        PlayerInput holdBreak = CreateHoldBreakInput();

        bool broken = BreakBlockWithRepeatedUpdates(interaction, player, world, holdBreak);
        Assert.True(broken);
        Assert.Equal(BlockId.Air, world.GetBlock(0, 25, 0));

        StackKey oreDrop = _blockRegistry.GetDropStack(BlockId.IronOre);
        Assert.Equal(BlockId.IronOre, oreDrop.BlockId);
        Assert.True(inventory.TryAddBlock(oreDrop.BlockId, 1));

        FurnaceState furnace = new();
        furnace.Input.BlockId = BlockId.IronOre;
        furnace.Input.Count = 1;
        furnace.Fuel.ItemId = ItemId.Coal;
        furnace.Fuel.Count = 1;

        for (int tick = 0; tick < 200; tick++)
        {
            FurnaceSystem.Tick(furnace, _smeltingRecipes, _furnaceFuels);
        }

        Assert.True(furnace.Input.IsEmpty);
        Assert.Equal(ItemId.IronIngot, furnace.Output.ItemId);
        Assert.Equal(1, furnace.Output.Count);
        Assert.Equal(0, furnace.ProgressTicks);
    }

    private static PlayerState CreatePlayerLookingDown()
    {
        PlayerState player = new();
        player.ResetToSpawn(new System.Numerics.Vector3(0.5f, 27f, 0.5f));
        player.PitchRadians = -MathF.PI / 2f + 0.05f;
        return player;
    }

    private static PlayerInput CreateHoldBreakInput() =>
        new(0f, 0f, 0f, 0f, false, false, false, true, false, 0);

    private static void EquipTool(PlayerState player, ItemId toolId)
    {
        PlayerInventory inventory = player.Inventory;
        foreach (InventorySlot slot in inventory.Hotbar.Concat(inventory.Storage))
        {
            if (slot.ItemId != toolId)
            {
                continue;
            }

            inventory.SelectedHotbarIndex = Array.IndexOf(inventory.Hotbar, slot);
            slot.Durability = ToolBreakSpeed.GetMaxDurability(toolId);
            return;
        }
    }

    private static bool BreakBlockWithRepeatedUpdates(
        BlockInteractionSystem interaction,
        PlayerState player,
        GameWorld world,
        PlayerInput holdBreak,
        float deltaSeconds = 0.05f,
        int maxTicks = 600)
    {
        for (int tick = 0; tick < maxTicks; tick++)
        {
            if (interaction.UpdateBreaking(
                    player,
                    world,
                    holdBreak,
                    deltaSeconds,
                    out BlockId brokenBlock,
                    out int _,
                    out int _,
                    out int _)
                && brokenBlock != BlockId.Air)
            {
                return true;
            }
        }

        return false;
    }

    private static int CountBlockInInventory(PlayerInventory inventory, BlockId blockId)
    {
        int total = 0;
        foreach (InventorySlot slot in inventory.Hotbar.Concat(inventory.Storage))
        {
            if (slot.BlockId == blockId)
            {
                total += slot.Count;
            }
        }

        return total;
    }
}
