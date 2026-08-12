using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;

namespace AstroCraft.Tests;

public class FoodTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;

    public FoodTests(FlatWorldFixture flat) => _flat = flat;

    [Fact]
    public void Apple_IsRegisteredAsEdible()
    {
        BlockDefinition apple = _flat.Registry.Get(BlockId.Apple);

        Assert.True(apple.IsEdible);
        Assert.True(apple.HungerRestore > 0f);
        Assert.True(apple.SaturationRestore > 0f);
    }

    [Fact]
    public void TryEatFood_RestoresHungerAndConsumesItem()
    {
        SurvivalSimulator survival = new();
        PlayerState player = new();
        player.Survival.Hunger = 10f;
        player.Survival.Saturation = 0f;
        player.Inventory.Hotbar[0].BlockId = BlockId.Apple;
        player.Inventory.Hotbar[0].Count = 3;

        PlayerInput eat = new(0f, 0f, 0f, 0f, false, false, false, false, false, 0, UseItem: true);
        bool ate = survival.TryEatFood(player, _flat.Registry, eat);

        Assert.True(ate);
        Assert.Equal(14f, player.Survival.Hunger);
        Assert.Equal(2.4f, player.Survival.Saturation, 1);
        Assert.Equal(2, player.Inventory.Hotbar[0].Count);
    }

    [Fact]
    public void TryEatFood_DoesNotEat_WhenHungerFull()
    {
        SurvivalSimulator survival = new();
        PlayerState player = new();
        player.Inventory.Hotbar[0].BlockId = BlockId.Apple;
        player.Inventory.Hotbar[0].Count = 1;

        PlayerInput eat = new(0f, 0f, 0f, 0f, false, false, false, false, false, 0, UseItem: true);
        bool ate = survival.TryEatFood(player, _flat.Registry, eat);

        Assert.False(ate);
        Assert.Equal(1, player.Inventory.Hotbar[0].Count);
    }

    [Fact]
    public void TryEatFood_DoesNotEat_NonFoodItems()
    {
        SurvivalSimulator survival = new();
        PlayerState player = new();
        player.Survival.Hunger = 5f;
        player.Inventory.Hotbar[0].BlockId = BlockId.Wood;
        player.Inventory.Hotbar[0].Count = 1;

        PlayerInput eat = new(0f, 0f, 0f, 0f, false, false, false, false, false, 0, UseItem: true);
        bool ate = survival.TryEatFood(player, _flat.Registry, eat);

        Assert.False(ate);
        Assert.Equal(5f, player.Survival.Hunger);
    }

    [Fact]
    public void TryPlaceBlock_DoesNotPlaceEdibleItems()
    {
        GameWorld world = _flat.CreateWorld(2);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = new();
        player.ResetToSpawn(new System.Numerics.Vector3(0.5f, 27f, 0.5f));
        player.PitchRadians = -MathF.PI / 2f + 0.05f;
        player.Inventory.Hotbar[0].BlockId = BlockId.Apple;
        player.Inventory.Hotbar[0].Count = 1;

        PlayerInput place = new(0f, 0f, 0f, 0f, false, false, false, false, true, 0);
        bool placed = interaction.TryPlaceBlock(player, world, place);

        Assert.False(placed);
        Assert.Equal(BlockId.Air, world.GetBlock(0, 26, 0));
    }
}
