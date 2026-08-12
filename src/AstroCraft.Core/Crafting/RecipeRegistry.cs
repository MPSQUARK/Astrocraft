using AstroCraft.Core.Blocks;

namespace AstroCraft.Core.Crafting;

public sealed class RecipeRegistry
{
    private readonly Dictionary<string, RecipeDefinition> _byId = new(StringComparer.Ordinal);
    private readonly List<RecipeDefinition> _shaped = new();
    private readonly List<RecipeDefinition> _shapeless = new();

    public RecipeRegistry(IEnumerable<RecipeDefinition> recipes)
    {
        foreach (RecipeDefinition recipe in recipes)
        {
            _byId[recipe.Id] = recipe;
            if (recipe.Kind == RecipeKind.Shaped)
            {
                _shaped.Add(recipe);
            }
            else
            {
                _shapeless.Add(recipe);
            }
        }
    }

    public IReadOnlyCollection<RecipeDefinition> All => _byId.Values;

    public bool TryGetById(string id, out RecipeDefinition? recipe) =>
        _byId.TryGetValue(id, out recipe);

    public bool TryMatchShaped(ReadOnlySpan<StackKey> grid, out RecipeDefinition? recipe)
    {
        foreach (RecipeDefinition candidate in _shaped)
        {
            if (CraftingSystem.MatchesShaped(grid, candidate))
            {
                recipe = candidate;
                return true;
            }
        }

        recipe = null;
        return false;
    }

    public bool TryMatchShapeless(IReadOnlyList<StackKey> ingredients, out RecipeDefinition? recipe)
    {
        foreach (RecipeDefinition candidate in _shapeless)
        {
            if (CraftingSystem.MatchesShapeless(ingredients, candidate))
            {
                recipe = candidate;
                return true;
            }
        }

        recipe = null;
        return false;
    }

    public static RecipeRegistry CreateDefault() => new(EmbeddedRecipes());

    private static IEnumerable<RecipeDefinition> EmbeddedRecipes()
    {
        yield return Shapeless("planks_from_wood", StackKey.Block(BlockId.Wood), StackKey.Block(BlockId.Planks), 4);
        yield return Shapeless("planks_from_birch", StackKey.Block(BlockId.BirchLog), StackKey.Block(BlockId.Planks), 4);
        yield return Shapeless("planks_from_spruce", StackKey.Block(BlockId.SpruceLog), StackKey.Block(BlockId.Planks), 4);
        yield return Shapeless("planks_from_jungle", StackKey.Block(BlockId.JungleLog), StackKey.Block(BlockId.Planks), 4);
        yield return Shapeless("coal_from_ore", [StackKey.Block(BlockId.CoalOre)], StackKey.Item(ItemId.Coal), 1);

        yield return Shaped(
            "sticks",
            width: 1,
            height: 2,
            pattern:
            [
                StackKey.Block(BlockId.Planks),
                StackKey.Block(BlockId.Planks),
            ],
            result: StackKey.Item(ItemId.Stick),
            resultCount: 4);

        yield return Shaped(
            "crafting_table",
            width: 2,
            height: 2,
            pattern:
            [
                StackKey.Block(BlockId.Planks), StackKey.Block(BlockId.Planks),
                StackKey.Block(BlockId.Planks), StackKey.Block(BlockId.Planks),
            ],
            result: StackKey.Block(BlockId.CraftingTable));

        yield return Shaped(
            "wooden_pickaxe",
            width: 3,
            height: 3,
            pattern:
            [
                StackKey.Block(BlockId.Planks), StackKey.Block(BlockId.Planks), StackKey.Block(BlockId.Planks),
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
            ],
            result: StackKey.Item(ItemId.WoodenPickaxe));

        yield return Shaped(
            "stone_pickaxe",
            width: 3,
            height: 3,
            pattern:
            [
                StackKey.Block(BlockId.Stone), StackKey.Block(BlockId.Stone), StackKey.Block(BlockId.Stone),
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
            ],
            result: StackKey.Item(ItemId.StonePickaxe));

        yield return Shapeless(
            "torch",
            [StackKey.Item(ItemId.Coal), StackKey.Item(ItemId.Stick)],
            StackKey.Block(BlockId.Torch),
            4);

        yield return Shaped(
            "furnace",
            width: 3,
            height: 3,
            pattern:
            [
                StackKey.Block(BlockId.Cobblestone), StackKey.Block(BlockId.Cobblestone), StackKey.Block(BlockId.Cobblestone),
                StackKey.Block(BlockId.Cobblestone), StackKey.Empty, StackKey.Block(BlockId.Cobblestone),
                StackKey.Block(BlockId.Cobblestone), StackKey.Block(BlockId.Cobblestone), StackKey.Block(BlockId.Cobblestone),
            ],
            result: StackKey.Block(BlockId.Furnace));

        yield return Shaped(
            "wooden_axe",
            width: 3,
            height: 3,
            pattern:
            [
                StackKey.Block(BlockId.Planks), StackKey.Block(BlockId.Planks), StackKey.Empty,
                StackKey.Block(BlockId.Planks), StackKey.Item(ItemId.Stick), StackKey.Empty,
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
            ],
            result: StackKey.Item(ItemId.WoodenAxe));

        yield return Shaped(
            "stone_axe",
            width: 3,
            height: 3,
            pattern:
            [
                StackKey.Block(BlockId.Stone), StackKey.Block(BlockId.Stone), StackKey.Empty,
                StackKey.Block(BlockId.Stone), StackKey.Item(ItemId.Stick), StackKey.Empty,
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
            ],
            result: StackKey.Item(ItemId.StoneAxe));

        yield return Shaped(
            "wooden_shovel",
            width: 3,
            height: 3,
            pattern:
            [
                StackKey.Empty, StackKey.Block(BlockId.Planks), StackKey.Empty,
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
            ],
            result: StackKey.Item(ItemId.WoodenShovel));

        yield return Shaped(
            "stone_shovel",
            width: 3,
            height: 3,
            pattern:
            [
                StackKey.Empty, StackKey.Block(BlockId.Stone), StackKey.Empty,
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
            ],
            result: StackKey.Item(ItemId.StoneShovel));

        yield return Shaped(
            "iron_pickaxe",
            width: 3,
            height: 3,
            pattern:
            [
                StackKey.Item(ItemId.IronIngot), StackKey.Item(ItemId.IronIngot), StackKey.Item(ItemId.IronIngot),
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
            ],
            result: StackKey.Item(ItemId.IronPickaxe));

        yield return Shaped(
            "iron_axe",
            width: 3,
            height: 3,
            pattern:
            [
                StackKey.Item(ItemId.IronIngot), StackKey.Item(ItemId.IronIngot), StackKey.Empty,
                StackKey.Item(ItemId.IronIngot), StackKey.Item(ItemId.Stick), StackKey.Empty,
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
            ],
            result: StackKey.Item(ItemId.IronAxe));

        yield return Shaped(
            "iron_shovel",
            width: 3,
            height: 3,
            pattern:
            [
                StackKey.Empty, StackKey.Item(ItemId.IronIngot), StackKey.Empty,
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
                StackKey.Empty, StackKey.Item(ItemId.Stick), StackKey.Empty,
            ],
            result: StackKey.Item(ItemId.IronShovel));
    }

    private static RecipeDefinition Shapeless(
        string id,
        StackKey input,
        StackKey result,
        int resultCount = 1) =>
        new()
        {
            Id = id,
            Kind = RecipeKind.Shapeless,
            Ingredients = [input],
            Result = result,
            ResultCount = resultCount,
        };

    private static RecipeDefinition Shapeless(
        string id,
        StackKey[] inputs,
        StackKey result,
        int resultCount = 1) =>
        new()
        {
            Id = id,
            Kind = RecipeKind.Shapeless,
            Ingredients = inputs,
            Result = result,
            ResultCount = resultCount,
        };

    private static RecipeDefinition Shaped(
        string id,
        int width,
        int height,
        StackKey[] pattern,
        StackKey result,
        int resultCount = 1) =>
        new()
        {
            Id = id,
            Kind = RecipeKind.Shaped,
            Width = width,
            Height = height,
            Pattern = pattern,
            Result = result,
            ResultCount = resultCount,
        };
}
