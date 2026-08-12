using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Players;

namespace AstroCraft.Core.Furnaces;

public sealed class FurnaceFuelDefinition
{
    public required StackKey Fuel { get; init; }
    public int BurnTicks { get; init; }
}

public sealed class FurnaceFuelRegistry
{
    private readonly Dictionary<StackKey, FurnaceFuelDefinition> _byFuel = new();

    public FurnaceFuelRegistry(IEnumerable<FurnaceFuelDefinition> fuels)
    {
        foreach (FurnaceFuelDefinition fuel in fuels)
        {
            _byFuel[fuel.Fuel] = fuel;
        }
    }

    public bool TryGetForSlot(InventorySlot slot, out FurnaceFuelDefinition fuel)
    {
        StackKey key = slot.AsStackKey();
        return _byFuel.TryGetValue(key, out fuel!);
    }

    public static FurnaceFuelRegistry CreateDefault()
    {
        List<FurnaceFuelDefinition> fuels =
        [
            new FurnaceFuelDefinition { Fuel = StackKey.Item(ItemId.Coal), BurnTicks = 1600 },
            new FurnaceFuelDefinition { Fuel = StackKey.Block(BlockId.Planks), BurnTicks = 300 },
        ];

        return new FurnaceFuelRegistry(fuels);
    }
}
