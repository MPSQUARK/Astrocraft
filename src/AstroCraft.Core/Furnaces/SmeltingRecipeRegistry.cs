using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Players;

namespace AstroCraft.Core.Furnaces;

public sealed class SmeltingRecipeRegistry
{
    private readonly Dictionary<StackKey, SmeltingRecipeDefinition> _byInput = new();

    public SmeltingRecipeRegistry(IEnumerable<SmeltingRecipeDefinition> recipes)
    {
        foreach (SmeltingRecipeDefinition recipe in recipes)
        {
            _byInput[recipe.Input] = recipe;
        }
    }

    public IReadOnlyCollection<SmeltingRecipeDefinition> All => _byInput.Values;

    public bool TryGetForInput(InventorySlot slot, out SmeltingRecipeDefinition recipe)
    {
        StackKey key = slot.AsStackKey();
        return _byInput.TryGetValue(key, out recipe!);
    }

    public static SmeltingRecipeRegistry CreateDefault()
    {
        List<SmeltingRecipeDefinition> recipes =
        [
            new SmeltingRecipeDefinition
            {
                Id = "iron_ore",
                Input = StackKey.Block(BlockId.IronOre),
                Output = StackKey.Item(ItemId.IronIngot),
                CookTicks = 200,
            },
        ];

        return new SmeltingRecipeRegistry(recipes);
    }
}
