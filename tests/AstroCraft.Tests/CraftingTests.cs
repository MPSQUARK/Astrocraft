using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Players;
using AstroCraft.Core.Server;

namespace AstroCraft.Tests;

public class CraftingTests
{
    private readonly RecipeRegistry _registry = RecipeRegistry.CreateDefault();

    [Fact]
    public void RecipeRegistry_LoadsEmbeddedRecipes()
    {
        Assert.True(_registry.All.Count >= 15);
        Assert.True(_registry.TryGetById("planks_from_wood", out RecipeDefinition? planks));
        Assert.NotNull(planks);
        Assert.Equal(RecipeKind.Shapeless, planks!.Kind);
    }

    [Fact]
    public void Shapeless_PlanksFromWood_MatchesAndConsumes()
    {
        PlayerInventory inventory = new();
        inventory.TryAddBlock(BlockId.Wood, 1);

        bool crafted = inventory.TryCraft("planks_from_wood", _registry);

        Assert.True(crafted);
        InventorySlot planksSlot = inventory.Hotbar.First(s => s.BlockId == BlockId.Planks);
        Assert.Equal(4, planksSlot.Count);
        Assert.False(inventory.TryRemoveBlock(BlockId.Wood, 1));
    }

    [Fact]
    public void Shapeless_PlanksFromWood_FailsWithoutIngredients()
    {
        PlayerInventory inventory = new();

        bool crafted = inventory.TryCraft("planks_from_wood", _registry);

        Assert.False(crafted);
    }

    [Fact]
    public void Shapeless_Torch_MatchesCoalAndStick()
    {
        RecipeDefinition recipe = _registry.All.First(r => r.Id == "torch");
        StackKey[] provided = [StackKey.Item(ItemId.Coal), StackKey.Item(ItemId.Stick)];

        Assert.True(CraftingSystem.MatchesShapeless(provided, recipe));
    }

    [Fact]
    public void Shapeless_Torch_CraftsFourTorches()
    {
        PlayerInventory inventory = new();
        inventory.TryAddItem(ItemId.Coal, 1);
        inventory.TryAddItem(ItemId.Stick, 1);

        bool crafted = inventory.TryCraft("torch", _registry);

        Assert.True(crafted);
        Assert.Equal(BlockId.Torch, inventory.Hotbar[0].BlockId);
        Assert.Equal(4, inventory.Hotbar[0].Count);
    }

    [Fact]
    public void Shaped_Sticks_MatchesVerticalPlanks()
    {
        RecipeDefinition recipe = _registry.All.First(r => r.Id == "sticks");
        StackKey[] grid =
        [
            StackKey.Empty, StackKey.Empty, StackKey.Empty,
            StackKey.Empty, StackKey.Block(BlockId.Planks), StackKey.Empty,
            StackKey.Empty, StackKey.Block(BlockId.Planks), StackKey.Empty,
        ];

        Assert.True(CraftingSystem.MatchesShaped(grid, recipe));
    }

    [Fact]
    public void Shaped_Sticks_DoesNotMatchHorizontalPlanks()
    {
        RecipeDefinition recipe = _registry.All.First(r => r.Id == "sticks");
        StackKey[] grid =
        [
            StackKey.Empty, StackKey.Empty, StackKey.Empty,
            StackKey.Empty, StackKey.Block(BlockId.Planks), StackKey.Block(BlockId.Planks),
            StackKey.Empty, StackKey.Empty, StackKey.Empty,
        ];

        Assert.False(CraftingSystem.MatchesShaped(grid, recipe));
    }

    [Fact]
    public void Shaped_WoodenPickaxe_MatchesPattern()
    {
        RecipeDefinition recipe = _registry.All.First(r => r.Id == "wooden_pickaxe");
        StackKey[] grid =
        [
            StackKey.Block(BlockId.Planks), StackKey.Block(BlockId.Planks), StackKey.Block(BlockId.Planks),
            StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
            StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
        ];

        Assert.True(_registry.TryMatchShaped(grid, out RecipeDefinition? matched));
        Assert.Equal("wooden_pickaxe", matched!.Id);
        Assert.True(CraftingSystem.MatchesShaped(grid, recipe));
    }

    [Fact]
    public void Shaped_CraftingTable_MatchesTwoByTwoPlanks()
    {
        RecipeDefinition recipe = _registry.All.First(r => r.Id == "crafting_table");
        StackKey[] grid =
        [
            StackKey.Block(BlockId.Planks), StackKey.Block(BlockId.Planks), StackKey.Empty,
            StackKey.Block(BlockId.Planks), StackKey.Block(BlockId.Planks), StackKey.Empty,
            StackKey.Empty, StackKey.Empty, StackKey.Empty,
        ];

        Assert.True(CraftingSystem.MatchesShaped(grid, recipe));
    }

    [Fact]
    public void Shaped_StonePickaxe_CraftsFromInventory()
    {
        PlayerInventory inventory = new();
        inventory.TryAddBlock(BlockId.Stone, 3);
        inventory.TryAddItem(ItemId.Stick, 2);

        bool crafted = inventory.TryCraft("stone_pickaxe", _registry);

        Assert.True(crafted);
        Assert.Equal(ItemId.StonePickaxe, inventory.Hotbar[0].ItemId);
        Assert.Equal(1, inventory.Hotbar[0].Count);
        Assert.False(inventory.TryRemoveBlock(BlockId.Stone, 1));
        Assert.False(inventory.TryRemoveItem(ItemId.Stick, 1));
    }

    [Fact]
    public void Shaped_Furnace_RequiresCobblestoneRing()
    {
        RecipeDefinition recipe = _registry.All.First(r => r.Id == "furnace");
        StackKey[] grid =
        [
            StackKey.Block(BlockId.Cobblestone), StackKey.Block(BlockId.Cobblestone), StackKey.Block(BlockId.Cobblestone),
            StackKey.Block(BlockId.Cobblestone), StackKey.Empty, StackKey.Block(BlockId.Cobblestone),
            StackKey.Block(BlockId.Cobblestone), StackKey.Block(BlockId.Cobblestone), StackKey.Block(BlockId.Cobblestone),
        ];

        Assert.True(CraftingSystem.MatchesShaped(grid, recipe));
    }

    [Fact]
    public void GameServer_TryCraft_IsAuthoritative()
    {
        GameServer server = new(seed: 1, flatWorld: true);
        System.Net.IPEndPoint endpoint = new(System.Net.IPAddress.Loopback, 27016);
        int playerId = server.ConnectClient(endpoint, "Crafter");
        ConnectedClient client = server.Clients.First(c => c.PlayerId == playerId);
        client.Player.Inventory.TryAddBlock(BlockId.Wood, 1);

        bool crafted = server.TryCraft(playerId, "planks_from_wood");

        Assert.True(crafted);
        InventorySlot planksSlot = client.Player.Inventory.Hotbar
            .Concat(client.Player.Inventory.Storage)
            .First(s => s.BlockId == BlockId.Planks);
        Assert.Equal(4, planksSlot.Count);
    }

    [Fact]
    public void GameServer_TryCraft_RejectsUnknownRecipe()
    {
        GameServer server = new(seed: 1, flatWorld: true);
        System.Net.IPEndPoint endpoint = new(System.Net.IPAddress.Loopback, 27017);
        int playerId = server.ConnectClient(endpoint, "Crafter");

        bool crafted = server.TryCraft(playerId, "nonexistent_recipe");

        Assert.False(crafted);
    }
}
