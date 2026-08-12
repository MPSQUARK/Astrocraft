using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;

namespace AstroCraft.Tests;

public class ToolBreakSpeedTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;

    public ToolBreakSpeedTests(FlatWorldFixture flat) => _flat = flat;

    [Theory]
    [InlineData(ItemId.None, ToolBreakSpeed.HandMultiplier)]
    [InlineData(ItemId.WoodenPickaxe, ToolBreakSpeed.WoodMultiplier)]
    [InlineData(ItemId.StoneAxe, ToolBreakSpeed.StoneMultiplier)]
    [InlineData(ItemId.IronShovel, ToolBreakSpeed.IronMultiplier)]
    public void GetMultiplier_ReturnsExpectedTierSpeed(ItemId itemId, float expectedMultiplier)
    {
        Assert.Equal(expectedMultiplier, ToolBreakSpeed.GetMultiplier(itemId));
    }

    [Theory]
    [InlineData(ItemId.None, 0.06666667f)]
    [InlineData(ItemId.WoodenPickaxe, 0.13333333f)]
    [InlineData(ItemId.StonePickaxe, 0.26666667f)]
    [InlineData(ItemId.IronPickaxe, 0.4f)]
    public void UpdateBreaking_AppliesToolMultiplierToProgress(ItemId toolId, float expectedProgress)
    {
        GameWorld world = _flat.CreateWorld(2);
        world.TrySetBlock(0, 25, 0, BlockId.Stone);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = CreatePlayerLookingAtGrass();
        EquipTool(player, toolId);

        PlayerInput holdBreak = new(0f, 0f, 0f, 0f, false, false, false, true, false, 0);
        interaction.UpdateBreaking(
            player,
            world,
            holdBreak,
            0.1f,
            out BlockId _,
            out int _,
            out int _,
            out int _);

        Assert.Equal(expectedProgress, player.BreakProgress, precision: 4);
    }

    [Fact]
    public void UpdateBreaking_DecreasesToolDurabilityOnBreak()
    {
        GameWorld world = _flat.CreateWorld(2);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = CreatePlayerLookingAtGrass();
        EquipTool(player, ItemId.WoodenPickaxe);
        int maxDurability = ToolBreakSpeed.GetMaxDurability(ItemId.WoodenPickaxe);
        player.Inventory.Hotbar[0].Durability = maxDurability;

        PlayerInput holdBreak = new(0f, 0f, 0f, 0f, false, false, false, true, false, 0);
        bool broken = BreakBlockWithRepeatedUpdates(interaction, player, world, holdBreak, deltaSeconds: 0.05f);

        Assert.True(broken);
        Assert.Equal(maxDurability - 1, player.Inventory.Hotbar[0].Durability);
        Assert.Equal(1, player.Inventory.Hotbar[0].Count);
    }

    [Fact]
    public void UpdateBreaking_RemovesToolWhenDurabilityReachesZero()
    {
        GameWorld world = _flat.CreateWorld(2);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = CreatePlayerLookingAtGrass();
        EquipTool(player, ItemId.StonePickaxe);
        player.Inventory.Hotbar[0].Durability = 1;

        PlayerInput holdBreak = new(0f, 0f, 0f, 0f, false, false, false, true, false, 0);
        bool broken = BreakBlockWithRepeatedUpdates(interaction, player, world, holdBreak, deltaSeconds: 0.05f);

        Assert.True(broken);
        Assert.True(player.Inventory.Hotbar[0].IsEmpty);
    }

    [Fact]
    public void TryAddItem_InitializesToolDurability()
    {
        PlayerInventory inventory = new();

        bool added = inventory.TryAddItem(ItemId.IronAxe);

        Assert.True(added);
        Assert.Equal(ToolBreakSpeed.GetMaxDurability(ItemId.IronAxe), inventory.Hotbar[0].Durability);
    }

    private static PlayerState CreatePlayerLookingAtGrass()
    {
        PlayerState player = new();
        player.ResetToSpawn(new System.Numerics.Vector3(0.5f, 27f, 0.5f));
        player.PitchRadians = -MathF.PI / 2f + 0.05f;
        return player;
    }

    private static void EquipTool(PlayerState player, ItemId toolId)
    {
        player.Inventory.Hotbar[0].ItemId = toolId;
        player.Inventory.Hotbar[0].BlockId = BlockId.Air;
        player.Inventory.Hotbar[0].Count = 1;
        if (toolId != ItemId.None)
        {
            player.Inventory.Hotbar[0].Durability = ToolBreakSpeed.GetMaxDurability(toolId);
        }
    }

    private static bool BreakBlockWithRepeatedUpdates(
        BlockInteractionSystem interaction,
        PlayerState player,
        GameWorld world,
        PlayerInput holdBreak,
        float deltaSeconds)
    {
        for (int tick = 0; tick < 40; tick++)
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
}
