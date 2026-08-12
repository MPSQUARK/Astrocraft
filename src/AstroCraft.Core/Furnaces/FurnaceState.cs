using AstroCraft.Core.Players;

namespace AstroCraft.Core.Furnaces;

public sealed class FurnaceState
{
    public InventorySlot Input { get; } = new();
    public InventorySlot Fuel { get; } = new();
    public InventorySlot Output { get; } = new();
    public int ProgressTicks { get; set; }
    public int FuelTicksRemaining { get; set; }
}
